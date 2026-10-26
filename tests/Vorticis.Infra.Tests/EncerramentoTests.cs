using Vorticis.Application.Contabil;
using Vorticis.Domain.Contabil;

namespace Vorticis.Infra.Tests;

public class EncerramentoTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private async Task Lancar(long empresa, long adm, DateOnly data, string debito, string credito, decimal valor) =>
        Assert.True((await db.App.Lancamentos.EfetivarAsync(new LancamentoProposto(empresa, data, OrigemLancamento.Manual,
            [new(debito, IndicadorDC.D, valor, "Teste"), new(credito, IndicadorDC.C, valor, "Teste")]), adm, TestContext.Current.CancellationToken)).Sucesso);

    [Fact]
    public async Task Encerra_o_exercicio_transferindo_o_lucro_ao_pl_e_permite_desfazer()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var app = db.App;
        var empresa = await db.CriarEmpresaAsync("11222333000181");
        var adm = await db.AdminAsync();
        var contas = new ContasEncerramento();

        await Lancar(empresa, adm, new(2026, 1, 2), "1.1.01.002", "2.3.01.001", 50_000m);   // capital
        await Lancar(empresa, adm, new(2026, 3, 10), "1.1.01.002", "3.1.01.003", 12_000m);  // serviços
        await Lancar(empresa, adm, new(2026, 4, 5), "4.2.02.001", "1.1.01.002", 3_000m);    // aluguel
        await Lancar(empresa, adm, new(2026, 4, 6), "3.2.01.005", "2.1.04.003", 600m);      // ISS (dedução)

        var previa = await app.Encerramento.PreverAsync(empresa, 2026, contas, adm, ct);
        Assert.True(previa.Sucesso, string.Join("; ", previa.Erros));
        Assert.Equal((12_000m, 3_600m, 8_400m, 3), (previa.Valor.Receitas, previa.Valor.CustosDespesas, previa.Valor.Resultado, previa.Valor.ContasAZerar));

        // Antes de encerrar: BP fecha com o resultado do período destacado
        var bpAntes = (await app.Demonstracoes.BalancoAsync(empresa, new(2026, 12, 31), 3, adm, ct)).Valor;
        Assert.True(bpAntes.Fecha);
        Assert.Equal(8_400m, bpAntes.ResultadoNaoEncerrado);

        var enc = await app.Encerramento.EncerrarAsync(empresa, 2026, contas, adm, ct);
        Assert.True(enc.Sucesso, string.Join("; ", enc.Erros));
        Assert.Equal(2, enc.Valor.Count);

        var bal = (await app.Relatorios.BalanceteAsync(empresa, new(2026, 1, 1), new(2026, 12, 31), adm, ct: ct)).Valor;
        Assert.All(bal.Where(l => l.Analitica && (l.Codigo.StartsWith('3') || l.Codigo.StartsWith('4') || l.Codigo.StartsWith('5'))),
                   l => Assert.Equal(0m, l.SaldoFinal));
        Assert.Equal(-8_400m, bal.Single(l => l.Codigo == "2.3.03.001").SaldoFinal);   // lucros acumulados (credor)
        Assert.True(await db.EscalarAsync<bool>(
            "SELECT bool_and(ind_lcto = 'E' AND data_lancamento = '2026-12-31') FROM contabil.lancamento WHERE origem = 'ENCERRAMENTO'"));

        // Depois: BP fecha sem resultado pendente; DRE do ano continua mostrando o lucro (ignora o encerramento)
        var bpDepois = (await app.Demonstracoes.BalancoAsync(empresa, new(2026, 12, 31), 3, adm, ct)).Valor;
        Assert.True(bpDepois.Fecha);
        Assert.Equal(0m, bpDepois.ResultadoNaoEncerrado);
        Assert.Equal(59_000m, bpDepois.TotalAtivo);          // 50.000 + 12.000 − 3.000
        Assert.Equal(59_000m, bpDepois.TotalPassivoPl);      // 50.000 capital + 600 ISS a recolher + 8.400 lucros
        var dre = (await app.Demonstracoes.DreAsync(empresa, new(2026, 1, 1), new(2026, 12, 31), 3, adm, ct)).Valor;
        Assert.Equal(8_400m, dre.Resultado);

        Assert.Contains("já foi encerrado", (await app.Encerramento.EncerrarAsync(empresa, 2026, contas, adm, ct)).Erros[0]);

        // Desfazer: estorna os dois e permite encerrar de novo
        var desfeito = await app.Encerramento.DesfazerAsync(empresa, 2026, "ajuste de despesa", adm, ct);
        Assert.True(desfeito.Sucesso, string.Join("; ", desfeito.Erros));
        await Lancar(empresa, adm, new(2026, 12, 20), "4.2.02.002", "1.1.01.002", 400m);
        var reencerrado = await app.Encerramento.EncerrarAsync(empresa, 2026, contas, adm, ct);
        Assert.True(reencerrado.Sucesso, string.Join("; ", reencerrado.Erros));
        bal = (await app.Relatorios.BalanceteAsync(empresa, new(2026, 1, 1), new(2026, 12, 31), adm, ct: ct)).Valor;
        Assert.Equal(-8_000m, bal.Single(l => l.Codigo == "2.3.03.001").SaldoFinal);
        Assert.True(await db.EscalarAsync<bool>("SELECT core.fn_verificar_auditoria() IS NULL"));
    }

    [Fact]
    public async Task Prejuizo_vai_para_prejuizos_acumulados_e_contas_invalidas_sao_recusadas()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync("33000167000101");
        var adm = await db.AdminAsync();
        await Lancar(empresa, adm, new(2026, 2, 1), "4.2.02.001", "1.1.01.001", 1_000m);

        Assert.Contains(((await db.App.Encerramento.EncerrarAsync(empresa, 2026, new ContasEncerramento(Apuracao: "1.1.01.001"), adm, ct)).Erros),
                        e => e.Contains("natureza 01"));
        Assert.True((await db.App.Encerramento.EncerrarAsync(empresa, 2026, new ContasEncerramento(), adm, ct)).Sucesso);
        var bal = (await db.App.Relatorios.BalanceteAsync(empresa, new(2026, 1, 1), new(2026, 12, 31), adm, ct: ct)).Valor;
        Assert.Equal(1_000m, bal.Single(l => l.Codigo == "2.3.03.002").SaldoFinal);   // prejuízos acumulados (devedor)
    }
}
