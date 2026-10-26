using Vorticis.Application.Cadastros;
using Vorticis.Application.Contabil;
using Vorticis.Domain.Contabil;

namespace Vorticis.Infra.Tests;

public class CadastrosAuxiliaresTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    [Fact]
    public async Task Reduzido_sequencial_nas_analiticas_e_editavel_sem_repetir()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var svc = db.App.PlanoContas;
        var empresa = await db.CriarEmpresaAsync("27865757000102");
        var adm = await db.AdminAsync();
        var plano = await svc.ListarAsync(empresa, adm, ct);
        Assert.All(plano.Where(c => !c.Analitica), c => Assert.Null(c.CodigoReduzido));
        var reduzidos = plano.Where(c => c.Analitica).Select(c => c.CodigoReduzido!.Value).ToList();
        Assert.Equal(Enumerable.Range(1, reduzidos.Count), reduzidos.Order());

        var nova = await svc.IncluirAsync(empresa, new NovaConta("1.1.01.004", "Caixa filial", "01", IndicadorDC.D, false, false, new DateOnly(2026, 1, 1)), adm, ct);
        var conta = (await svc.ListarAsync(empresa, adm, ct)).Single(c => c.Id == nova.Valor);
        Assert.Equal(reduzidos.Max() + 1, conta.CodigoReduzido);
        Assert.Contains("já é da conta", (await svc.AlterarReduzidoAsync(empresa, conta.Id, 1, adm, ct)).Erros[0]);
        Assert.True((await svc.AlterarReduzidoAsync(empresa, conta.Id, 900, adm, ct)).Sucesso);
        Assert.Equal(900, (await svc.ListarAsync(empresa, adm, ct)).Single(c => c.Id == conta.Id).CodigoReduzido);
        var comReduzido = await svc.IncluirAsync(empresa, new NovaConta("1.1.01.005", "Caixa 2", "01", IndicadorDC.D, false, false, new DateOnly(2026, 1, 1), 900), adm, ct);
        Assert.Contains("já está em uso", comReduzido.Erros[0]);

        // Analítica que vira sintética perde o reduzido
        Assert.True((await svc.IncluirAsync(empresa, new NovaConta("1.1.01.004.01", "Sub", "01", IndicadorDC.D, false, false, new DateOnly(2026, 1, 1)), adm, ct)).Sucesso);
        Assert.Null((await svc.ListarAsync(empresa, adm, ct)).Single(c => c.Id == conta.Id).CodigoReduzido);
    }

    [Fact]
    public async Task Historicos_e_participantes_com_codigo_sequencial()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync("42938862000100");
        var adm = await db.AdminAsync();

        var h1 = await db.App.Historicos.IncluirAsync(empresa, null, "Pagamento NF", adm, ct);
        var h2 = await db.App.Historicos.IncluirAsync(empresa, "", "Recebimento de", adm, ct);
        Assert.True(h1.Sucesso && h2.Sucesso);
        Assert.Equal(["1", "2"], (await db.App.Historicos.ListarAsync(empresa, adm, ct)).Select(h => h.Codigo));
        Assert.Contains("Já existe", (await db.App.Historicos.IncluirAsync(empresa, "2", "x", adm, ct)).Erros[0]);
        Assert.True((await db.App.Historicos.AlterarAsync(empresa, h1.Valor, "Pagamento da NF", adm, ct)).Sucesso);
        Assert.True((await db.App.Historicos.ExcluirAsync(empresa, h2.Valor, adm, ct)).Sucesso);
        Assert.Equal("Pagamento da NF", Assert.Single(await db.App.Historicos.ListarAsync(empresa, adm, ct)).Descricao);

        var svc = db.App.Participantes;
        Participante Novo(string nome, string? doc, bool cli, bool forn, string? conta = null) =>
            new(0, "", nome, null, doc, null, "SP", null, null, null, null, cli, forn, conta, true);
        var cliente = await svc.GravarAsync(empresa, Novo("Cliente A", "11.222.333/0001-81", true, false, "1.1.02.001"), adm, ct);
        Assert.True(cliente.Sucesso, string.Join("; ", cliente.Erros));
        Assert.Contains("CPF ou CNPJ inválido.", (await svc.GravarAsync(empresa, Novo("X", "123", true, false), adm, ct)).Erros);
        Assert.Contains("já está cadastrado", (await svc.GravarAsync(empresa, Novo("Y", "11222333000181", false, true), adm, ct)).Erros[0]);
        Assert.Contains("cliente, fornecedor", (await svc.GravarAsync(empresa, Novo("Z", null, false, false), adm, ct)).Erros[0]);
        var forn = await svc.GravarAsync(empresa, Novo("Fornecedor B", "529.982.247-25", false, true), adm, ct);
        Assert.True(forn.Sucesso, string.Join("; ", forn.Erros));
        var lista = await svc.ListarAsync(empresa, adm, ct);
        Assert.Equal(["1", "2"], lista.Select(p => p.Codigo));
        Assert.Equal(("11222333000181", "1.1.02.001"), (lista[0].CpfCnpj, lista[0].CodigoConta));
        Assert.True((await svc.ExcluirAsync(empresa, forn.Valor, adm, ct)).Sucesso);
        // Excluir a empresa leva junto os cadastros auxiliares
        Assert.True((await db.App.Empresas.ExcluirAsync(empresa, ct)).Sucesso);
    }

    [Fact]
    public async Task Plano_com_participantes_exige_o_participante_na_conta_e_detalha_o_balancete()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync("51515151000113");
        var adm = await db.AdminAsync();
        var svc = db.App.Participantes;
        var cliente = new Participante(0, "", "Cliente Alfa", null, "11.222.333/0001-81", null, "SP", null, null, null, null, true, false,
                                       "1.1.02.001", true, ControleConta: true);
        Assert.Contains("não usa o plano de contas com participantes", (await svc.GravarAsync(empresa, cliente, adm, ct)).Erros[0]);
        Assert.True((await svc.DefinirUsaParticipantesAsync(empresa, true, adm, ct)).Sucesso);
        Assert.True(await svc.UsaParticipantesAsync(empresa, ct));
        Assert.Contains("informe a conta contábil", (await svc.GravarAsync(empresa, cliente with { CodigoConta = null }, adm, ct)).Erros[0]);
        var r = await svc.GravarAsync(empresa, cliente, adm, ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        Assert.True((await db.App.PlanoContas.ListarAsync(empresa, adm, ct)).Single(c => c.Codigo == "1.1.02.001").ExigeParticipante);

        LancamentoProposto Venda(string? part) => new(empresa, new(2026, 3, 10), OrigemLancamento.Manual,
            [new("1.1.02.001", IndicadorDC.D, 300m, "Venda a prazo", null, part), new("3.1.01.001", IndicadorDC.C, 300m, "Venda a prazo")]);
        Assert.Contains("exige participante", string.Join(" ", (await db.App.Lancamentos.EfetivarAsync(Venda(null), adm, ct)).Erros));
        Assert.True((await db.App.Lancamentos.EfetivarAsync(Venda("1"), adm, ct)).Sucesso);

        // Balancete: a conta mostra a soma; o detalhamento traz o saldo de cada participante
        var bal = (await db.App.Relatorios.BalanceteAsync(empresa, new(2026, 3, 1), new(2026, 3, 31), adm, ct: ct)).Valor;
        Assert.Equal(300m, bal.Single(l => l.Codigo == "1.1.02.001").SaldoFinal);
        var det = Assert.Single((await db.App.Relatorios.ParticipantesBalanceteAsync(empresa, new(2026, 3, 1), new(2026, 3, 31), adm, ct)).Valor);
        Assert.Equal(("1", "Cliente Alfa", 300m), (det.CodigoParticipante, det.NomeParticipante, det.SaldoFinal));

        // Desligar o plano com participantes libera as contas
        Assert.True((await svc.DefinirUsaParticipantesAsync(empresa, false, adm, ct)).Sucesso);
        Assert.False((await db.App.PlanoContas.ListarAsync(empresa, adm, ct)).Single(c => c.Codigo == "1.1.02.001").ExigeParticipante);
    }
}
