using Vorticis.Domain.Contabil;
using Vorticis.Infra.Migracoes;

namespace Vorticis.Infra.Tests;

public class FluxoContabilTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static PartidaProposta D(string c, decimal v, string h = "Teste") => new(c, IndicadorDC.D, v, h);
    private static PartidaProposta C(string c, decimal v, string h = "Teste") => new(c, IndicadorDC.C, v, h);

    private async Task<Domain.Comum.Resultado<LancamentoEfetivado>> Lancar(long empresa, DateOnly data, params PartidaProposta[] p)
        => await db.App.Lancamentos.EfetivarAsync(new LancamentoProposto(empresa, data, OrigemLancamento.Manual, p), await db.AdminAsync(),
                                                  TestContext.Current.CancellationToken);

    [Fact]
    public async Task Migracoes_sao_idempotentes()
    {
        BancoDeTeste.ExigirBanco();
        var segunda = await db.App.Migrador.AplicarAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(segunda);
        Assert.All(segunda, m => Assert.False(m.Nova));
        Assert.Equal(Migrador.ScriptsEmbutidos().Count, segunda.Count);
    }

    [Fact]
    public async Task Cadastro_de_empresa_cria_exercicio_periodos_e_plano_modelo()
    {
        BancoDeTeste.ExigirBanco();
        var empresa = await db.CriarEmpresaAsync("11222333000181");

        Assert.Equal(12L, await db.EscalarAsync<long>(
            "SELECT COUNT(*) FROM contabil.periodo p JOIN contabil.exercicio e ON e.id = p.exercicio_id WHERE e.empresa_id = @e", ("e", empresa)));
        var plano = await db.App.Consultas.ObterPlanoAsync(empresa, TestContext.Current.CancellationToken);
        Assert.True(plano.Count > 100);
        Assert.False(plano.Single(c => c.Codigo == "1.1.01").Analitica);
        Assert.True(plano.Single(c => c.Codigo == "1.1.01.001").Analitica);
        Assert.Equal(IndicadorDC.C, plano.Single(c => c.Codigo == "1.2.03.007").NaturezaSaldo);   // depreciação acumulada

        var duplicada = await db.App.Empresas.CadastrarAsync(new("11.222.333/0001-81", "Outra", null, "2062", "4751201", "SP", "3550308",
            new(2020, 1, 1), Application.Cadastros.RegimeTributario.SimplesNacional, 2026), TestContext.Current.CancellationToken);
        Assert.Contains(duplicada.Erros, e => e.Contains("Já existe"));
    }

    [Fact]
    public async Task Efetiva_numera_sequencialmente_atualiza_saldos_e_balancete_fecha()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync("19131243000197");

        var l1 = await Lancar(empresa, new(2026, 1, 5), D("1.1.01.002", 50_000m, "Integralização de capital"), C("2.3.01.001", 50_000m, "Integralização de capital"));
        var l2 = await Lancar(empresa, new(2026, 2, 10), D("1.1.02.001", 1_200m), C("3.1.01.003", 1_200m));
        var l3 = await Lancar(empresa, new(2026, 2, 15), D("4.2.02.001", 800m), D("4.2.02.002", 150.37m), C("1.1.01.002", 950.37m));
        Assert.True(l1.Sucesso && l2.Sucesso && l3.Sucesso, string.Join("; ", l1.Erros.Concat(l2.Erros).Concat(l3.Erros)));
        Assert.Equal([1L, 2L, 3L], new[] { l1.Valor.Numero, l2.Valor.Numero, l3.Valor.Numero });

        var bal = (await db.App.Relatorios.BalanceteAsync(empresa, new(2026, 2, 1), new(2026, 2, 28), await db.AdminAsync(), ct: ct)).Valor;
        var banco = bal.Single(l => l.Codigo == "1.1.01.002");
        Assert.Equal((50_000m, 0m, 950.37m, 49_049.63m), (banco.SaldoAnterior, banco.Debitos, banco.Creditos, banco.SaldoFinal));
        var analiticas = bal.Where(l => l.Analitica).ToArray();
        Assert.Equal(analiticas.Sum(l => l.Debitos), analiticas.Sum(l => l.Creditos));
        Assert.Equal(0m, analiticas.Sum(l => l.SaldoFinal));
        Assert.Equal(49_049.63m + 1_200m, bal.Single(l => l.Codigo == "1").SaldoFinal);

        // Balancete com início no meio do mês usa saldo_mensal + partidas dos dias anteriores.
        var meio = (await db.App.Relatorios.BalanceteAsync(empresa, new(2026, 2, 12), new(2026, 2, 28), await db.AdminAsync(), ct: ct)).Valor;
        Assert.Equal(1_200m, meio.Single(l => l.Codigo == "1.1.02.001").SaldoAnterior);

        var razao = (await db.App.Relatorios.RazaoAsync(empresa, "1.1.01.002", new(2026, 1, 1), new(2026, 12, 31), await db.AdminAsync(), ct)).Valor;
        Assert.NotNull(razao);
        Assert.Equal([50_000m, 49_049.63m], razao.Linhas.Select(l => l.SaldoAcumulado));

        var diario = (await db.App.Relatorios.DiarioAsync(empresa, new(2026, 1, 1), new(2026, 12, 31), await db.AdminAsync(), ct)).Valor;
        Assert.Equal(7, diario.Count);
    }

    [Fact]
    public async Task Rejeita_desbalanceado_sem_gravar_nem_consumir_numero()
    {
        BancoDeTeste.ExigirBanco();
        var empresa = await db.CriarEmpresaAsync("33000167000101");

        var ruim = await Lancar(empresa, new(2026, 3, 1), D("1.1.01.001", 10m), C("3.1.01.001", 9m));
        Assert.False(ruim.Sucesso);
        var bom = await Lancar(empresa, new(2026, 3, 1), D("1.1.01.001", 10m), C("3.1.01.001", 10m));
        Assert.Equal(1L, bom.Valor.Numero);
    }

    [Fact]
    public async Task Estorno_zera_saldo_mantem_os_dois_no_diario_e_nao_repete()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync("60701190000104");
        var original = await Lancar(empresa, new(2026, 4, 2), D("1.1.01.001", 300m, "Venda"), C("3.1.01.001", 300m, "Venda"));

        var estorno = await db.App.Lancamentos.EstornarAsync(empresa, original.Valor.Id, new(2026, 4, 3), "lançado em duplicidade", await db.AdminAsync(), ct);
        Assert.True(estorno.Sucesso, string.Join("; ", estorno.Erros));

        var bal = (await db.App.Relatorios.BalanceteAsync(empresa, new(2026, 4, 1), new(2026, 4, 30), await db.AdminAsync(), ct: ct)).Valor;
        Assert.Equal(0m, bal.Single(l => l.Codigo == "1.1.01.001").SaldoFinal);
        var diario = (await db.App.Relatorios.DiarioAsync(empresa, new(2026, 4, 1), new(2026, 4, 30), await db.AdminAsync(), ct)).Valor;
        Assert.Contains(diario, l => l.Status == StatusLancamento.Estornado);

        var denovo = await db.App.Lancamentos.EstornarAsync(empresa, original.Valor.Id, new(2026, 4, 3), "de novo", await db.AdminAsync(), ct);
        Assert.Contains(denovo.Erros, e => e.Contains("já foi estornado"));
    }

    [Fact]
    public async Task Alteracao_estorna_o_original_e_grava_o_corrigido_na_mesma_transacao()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync("42424242000152");
        var admin = await db.AdminAsync();
        var original = await Lancar(empresa, new(2026, 5, 4), D("1.1.01.001", 300m, "Venda"), C("3.1.01.001", 300m, "Venda"));
        var lido = await db.App.Lancamentos.ObterAsync(empresa, original.Valor.Id, admin, ct);
        Assert.Equal(300m, lido.Valor.Partidas[0].Valor);

        var corrigido = new LancamentoProposto(empresa, new(2026, 5, 4), OrigemLancamento.Manual, [D("1.1.01.001", 350m, "Venda"), C("3.1.01.001", 350m, "Venda")]);
        var invalido = corrigido with { Partidas = [D("1.1.01.001", 350m), C("3.1.01.001", 340m)] };
        Assert.False((await db.App.Lancamentos.SubstituirAsync(original.Valor.Id, invalido, admin, ct)).Sucesso);   // nada muda
        var r = await db.App.Lancamentos.SubstituirAsync(original.Valor.Id, corrigido, admin, ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        Assert.Equal([2L, 3L], r.Valor.Select(x => x.Numero));

        var bal = (await db.App.Relatorios.BalanceteAsync(empresa, new(2026, 5, 1), new(2026, 5, 31), admin, ct: ct)).Valor;
        Assert.Equal(350m, bal.Single(l => l.Codigo == "1.1.01.001").SaldoFinal);
        var diario = (await db.App.Relatorios.DiarioAsync(empresa, new(2026, 5, 1), new(2026, 5, 31), admin, ct)).Valor;
        Assert.Equal(StatusLancamento.Estornado, diario.First(l => l.Numero == 1).Status);
        var denovo = await db.App.Lancamentos.SubstituirAsync(original.Valor.Id, corrigido, admin, ct);
        Assert.Contains(denovo.Erros, e => e.Contains("já foi estornado"));
    }

    [Fact]
    public async Task Fechamento_em_ordem_bloqueia_lancamentos_e_reabertura_em_ordem_inversa()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync("45997418000153");
        await Lancar(empresa, new(2026, 1, 20), D("1.1.01.001", 10m), C("3.1.01.001", 10m));

        Assert.Contains((await db.App.Periodos.FecharAsync(empresa, new(2026, 2, 1), await db.AdminAsync(), ct)).Erros, e => e.Contains("01/2026"));
        Assert.True((await db.App.Periodos.FecharAsync(empresa, new(2026, 1, 1), await db.AdminAsync(), ct)).Sucesso);
        Assert.True((await db.App.Periodos.FecharAsync(empresa, new(2026, 2, 1), await db.AdminAsync(), ct)).Sucesso);

        var bloqueado = await Lancar(empresa, new(2026, 1, 25), D("1.1.01.001", 1m), C("3.1.01.001", 1m));
        Assert.Contains(bloqueado.Erros, e => e.Contains("está fechado"));

        Assert.Contains((await db.App.Periodos.ReabrirAsync(empresa, new(2026, 1, 1), await db.AdminAsync(), ct)).Erros, e => e.Contains("02/2026"));
        Assert.True((await db.App.Periodos.ReabrirAsync(empresa, new(2026, 2, 1), await db.AdminAsync(), ct)).Sucesso);
        Assert.True((await db.App.Periodos.ReabrirAsync(empresa, new(2026, 1, 1), await db.AdminAsync(), ct)).Sucesso);
    }

    [Fact]
    public async Task Trilha_de_auditoria_e_integra_e_registra_efetivacao_com_partidas()
    {
        BancoDeTeste.ExigirBanco();
        var empresa = await db.CriarEmpresaAsync("07526557000100");
        await Lancar(empresa, new(2026, 5, 5), D("1.1.01.001", 77m), C("3.1.01.001", 77m));

        Assert.True(await db.EscalarAsync<bool>("SELECT core.fn_verificar_auditoria() IS NULL"));
        Assert.True(await db.EscalarAsync<bool>("""
            SELECT jsonb_array_length(dados_depois->'partidas') = 2 FROM core.auditoria
             WHERE tabela = 'contabil.lancamento' AND empresa_id = @e ORDER BY id DESC LIMIT 1
            """, ("e", empresa)));
        Assert.True(await db.EscalarAsync<bool>("SELECT COUNT(*) > 100 FROM core.auditoria WHERE tabela = 'contabil.plano_contas' AND empresa_id = @e", ("e", empresa)));
    }

    [Fact]
    public async Task Efetivacoes_paralelas_numeram_sem_lacunas_nem_duplicidade()
    {
        BancoDeTeste.ExigirBanco();
        var empresa = await db.CriarEmpresaAsync("12ABC34501DE35");   // CNPJ alfanumérico

        var tarefas = Enumerable.Range(1, 40).Select(i =>
            Lancar(empresa, new(2026, 6, 1 + i % 28), D("1.1.01.001", i), C("3.1.01.001", i)));
        var resultados = await Task.WhenAll(tarefas);

        Assert.All(resultados, r => Assert.True(r.Sucesso, string.Join("; ", r.Erros)));
        Assert.Equal(Enumerable.Range(1, 40).Select(i => (long)i), resultados.Select(r => r.Valor.Numero).Order());
    }
}
