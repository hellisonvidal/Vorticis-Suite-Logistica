using Vorticis.Application.Contabil;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;

namespace Vorticis.Infra.Tests;

public class GerenciaisTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private async Task Lancar(long empresa, long adm, DateOnly data, string conta, IndicadorDC dc, decimal valor, string? centro)
    {
        var contra = dc == IndicadorDC.D ? IndicadorDC.C : IndicadorDC.D;
        var r = await db.App.Lancamentos.EfetivarAsync(new LancamentoProposto(empresa, data, OrigemLancamento.Manual,
            [new(conta, dc, valor, "Teste", centro), new("1.1.01.002", contra, valor, "Teste")]), adm, TestContext.Current.CancellationToken);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
    }

    private async Task<(long Empresa, long Adm)> PrepararAsync(string cnpj)
    {
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync(cnpj);
        var adm = await db.AdminAsync();
        foreach (var (cod, desc, pai) in new[] { ("1", "Administrativo", null), ("1.1", "Financeiro", "1"), ("1.2", "RH", "1"), ("2", "Produção", null) })
            Assert.True((await db.App.PlanoContas.IncluirCentroAsync(empresa, cod, desc, pai, adm, ct: ct)).Sucesso, cod);
        await Lancar(empresa, adm, new(2026, 1, 10), "4.2.02.001", IndicadorDC.D, 1000, "1.1");
        await Lancar(empresa, adm, new(2026, 1, 15), "4.2.02.002", IndicadorDC.D, 500, "1.2");
        await Lancar(empresa, adm, new(2026, 2, 5), "4.2.02.001", IndicadorDC.D, 1200, "1.1");
        await Lancar(empresa, adm, new(2026, 2, 10), "4.1.01.001", IndicadorDC.D, 3000, "2");
        await Lancar(empresa, adm, new(2026, 1, 20), "3.1.01.003", IndicadorDC.C, 10_000, "2");
        await Lancar(empresa, adm, new(2026, 1, 21), "3.1.01.003", IndicadorDC.C, 2_000, "1.1");
        await Lancar(empresa, adm, new(2026, 2, 8), "3.1.01.003", IndicadorDC.C, 8_000, "2");
        await Lancar(empresa, adm, new(2026, 3, 3), "4.2.02.002", IndicadorDC.D, 100, null);
        return (empresa, adm);
    }

    [Fact]
    public async Task Relatorios_por_centro_somam_analiticos_nos_sinteticos_e_conferem_os_valores()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var (empresa, adm) = await PrepararAsync("11222333000181");
        var g = db.App.Gerenciais;

        // 1. Contabilização por centro (só custos e despesas)
        var r1 = await g.PorCentroCustoAsync(empresa, new(new(2026, 1, 1), new(2026, 3, 31), ContaPrefixo: "4"), adm, ct);
        Assert.True(r1.Sucesso, string.Join("; ", r1.Erros));
        decimal D(string cod, TipoLinhaGerencial t = TipoLinhaGerencial.Grupo) => r1.Valor.First(l => l.Tipo == t && l.Codigo == cod).Debitos;
        Assert.Equal(2700m, D("1"));
        Assert.Equal(2200m, D("1.1"));
        Assert.Equal(500m, D("1.2"));
        Assert.Equal(3000m, D("2"));
        Assert.Equal(100m, r1.Valor.Single(l => l.Descricao == Gerenciais.SemCentro).Debitos);
        Assert.Equal(5800m, r1.Valor[^1].Debitos);
        Assert.DoesNotContain(r1.Valor, l => l.Tipo == TipoLinhaGerencial.Item && l.Recuo == 1 && l.Codigo == "4.2.02.001" && l.Debitos == 2700m);   // sintético não repete contas

        var nivel1 = (await g.PorCentroCustoAsync(empresa, new(new(2026, 1, 1), new(2026, 3, 31), ContaPrefixo: "4", NivelMaximo: 1), adm, ct)).Valor;
        Assert.Equal(2200m, nivel1.Single(l => l.Tipo == TipoLinhaGerencial.Item && l.Codigo == "4.2.02.001" && l.Recuo == 1).Debitos);   // 1.000 + 1.200 dos subcentros
        Assert.DoesNotContain(nivel1, l => l.Codigo == "1.1");

        var detalhado = (await g.PorCentroCustoAsync(empresa, new(new(2026, 1, 1), new(2026, 3, 31), CentroId: null, ContaPrefixo: "4.2.02.001", Detalhar: true), adm, ct)).Valor;
        Assert.Equal(2, detalhado.Count(l => l.Tipo == TipoLinhaGerencial.Detalhe));
        Assert.Equal(new DateOnly(2026, 1, 10), detalhado.First(l => l.Tipo == TipoLinhaGerencial.Detalhe).Data);

        // 4. Receita por centro (jan–fev)
        var r4 = (await g.ReceitaPorCentroAsync(empresa, new(new(2026, 1, 1), new(2026, 2, 28)), adm, ct)).Valor;
        Assert.Equal(2, r4.Meses.Count);
        var adm1 = r4.Linhas.Single(l => l.Codigo == "1");
        Assert.Equal((2000m, 2700m, -700m, 10m), (adm1.Receita, adm1.CustosDespesas, adm1.Margem, adm1.Participacao));
        var prod = r4.Linhas.Single(l => l.Codigo == "2");
        Assert.Equal([10_000m, 8_000m], prod.PorMes);
        Assert.Equal((18_000m, 3_000m, 15_000m, 90m), (prod.Receita, prod.CustosDespesas, prod.Margem, prod.Participacao));
        var total = r4.Linhas[^1];
        Assert.Equal((20_000m, 5_700m), (total.Receita, total.CustosDespesas));

        // 3. Relação de custos: fevereiro x período anterior (4 a 31/01)
        var r3 = (await g.RelacaoCustosAsync(empresa, new(new(2026, 2, 1), new(2026, 2, 28), AgrupamentoCustos.PorConta), adm, ct)).Valor;
        var aluguel = r3.Single(l => l.Tipo == TipoLinhaGerencial.Grupo && l.Codigo == "4.2.02.001");
        Assert.Equal((1200m, 1000m, 200m, 20m), (aluguel.Valor, aluguel.Anterior, aluguel.Variacao, aluguel.VariacaoPercentual));
        Assert.Equal(28.57m, aluguel.Participacao);
        Assert.Equal(71.43m, r3.Single(l => l.Codigo == "4.1.01.001").Participacao);
        Assert.Equal((4200m, 1500m), (r3[^1].Valor, r3[^1].Anterior));
        var porCentro = (await g.RelacaoCustosAsync(empresa, new(new(2026, 2, 1), new(2026, 2, 28), Comparar: ComparacaoPeriodo.Nenhuma), adm, ct)).Valor;
        Assert.Equal(1200m, porCentro.Single(l => l.Tipo == TipoLinhaGerencial.Grupo && l.Codigo == "1").Valor);
        Assert.All(porCentro, l => Assert.Null(l.Anterior));
    }

    [Fact]
    public async Task Orcamento_grava_compara_com_o_realizado_copia_com_reajuste_e_le_planilha()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var (empresa, adm) = await PrepararAsync("33000167000101");
        var g = db.App.Gerenciais;
        var plano = (await db.App.Consultas.ObterPlanoAsync(empresa, ct)).ToDictionary(c => c.Codigo);
        var centros = (await db.App.Consultas.ObterCentrosAsync(empresa, ct)).ToDictionary(c => c.Codigo);
        decimal[] Meses(params decimal[] v) => [.. v, .. new decimal[12 - v.Length]];

        // só conta de resultado analítica
        Assert.False((await g.SalvarOrcamentoAsync(empresa, 2026, [new(plano["1.1.01.002"].Id, 0, Meses(10))], adm, ct)).Sucesso);
        var linhas = new LinhaOrcamento[]
        {
            new(plano["4.2.02.001"].Id, centros["1.1"].Id, Meses(1000, 1000)),
            new(plano["3.1.01.003"].Id, centros["2"].Id, Meses(9000)),
        };
        Assert.True((await g.SalvarOrcamentoAsync(empresa, 2026, linhas, adm, ct)).Sucesso);
        Assert.Equal(2, (await g.ListarOrcamentoAsync(empresa, 2026, adm, ct)).Valor.Count);

        var acum = (await g.OrcadoRealizadoAsync(empresa, new(2026, 1, 2), adm, ct)).Valor;
        var aluguel = acum.Single(l => l.Codigo == "4.2.02.001");
        Assert.Equal((2000m, 2200m, 200m, 110m), (aluguel.Orcado, aluguel.Realizado, aluguel.Diferenca, aluguel.Percentual));
        var rec = acum.Single(l => l.Codigo == "3.1.01.003" && l.Centro.StartsWith("2 "));
        Assert.Equal((9000m, 18_000m), (rec.Orcado, rec.Realizado));
        Assert.Equal(0m, acum.Single(l => l.Codigo == "3.1.01.003" && l.Centro.StartsWith("1.1")).Orcado);   // realizado sem orçamento aparece
        Assert.Equal(20_000m, acum.Single(l => l.Descricao == "Total das receitas").Realizado);

        var mensal = (await g.OrcadoRealizadoAsync(empresa, new(2026, 1, 2, Mensal: true, ContaPrefixo: "4.2.02.001"), adm, ct)).Valor;
        Assert.Equal(2, mensal.Count);
        Assert.Equal((1000m, 1200m, 2000m, 2200m), (mensal[1].Orcado, mensal[1].Realizado, mensal[1].AcumOrcado, mensal[1].AcumRealizado));
        var semCentro = (await g.OrcadoRealizadoAsync(empresa, new(2026, 1, 2, IgnorarCentro: true, ContaPrefixo: "3.1"), adm, ct)).Valor;
        Assert.Equal((9000m, 20_000m), (semCentro.Single(l => l.Tipo == TipoLinhaGerencial.Item).Orcado, semCentro.Single(l => l.Tipo == TipoLinhaGerencial.Item).Realizado));

        // cópia do orçamento do ano anterior com reajuste de 10%
        var copia = (await g.CopiarOrcamentoAnteriorAsync(empresa, 2027, 10, adm, ct)).Valor;
        Assert.Equal([1100m, 1100m], copia.Single(l => l.ContaId == plano["4.2.02.001"].Id).Valores.Take(2));
        Assert.Equal(9900m, copia.Single(l => l.ContaId == plano["3.1.01.003"].Id).Valores[0]);
        Assert.False((await g.CopiarOrcamentoAnteriorAsync(empresa, 2026, 0, adm, ct)).Sucesso);   // não há orçamento de 2025

        // realizado do ano anterior com reajuste
        var real = (await g.CopiarRealizadoAnteriorAsync(empresa, 2027, 10, adm, ct)).Valor;
        var l1 = real.Single(l => l.ContaId == plano["4.2.02.001"].Id && l.CentroId == centros["1.1"].Id);
        Assert.Equal([1100m, 1320m], l1.Valores.Take(2));
        Assert.Equal(110m, real.Single(l => l.ContaId == plano["4.2.02.002"].Id && l.CentroId == 0).Valores[2]);   // março, sem centro
        Assert.True((await g.SalvarOrcamentoAsync(empresa, 2027, real, adm, ct)).Sucesso);
        Assert.Equal(real.Count, (await g.ListarOrcamentoAsync(empresa, 2027, adm, ct)).Valor.Count);

        // planilha
        var tabela = TabelaImportada.DeTexto("Conta;Centro de custo;Jan;Fev;Mar\n4.2.02.001;1.2;100,50;200;\n3.1.01.003;;1.000,00;;5\n");
        var lida = (await g.LerPlanilhaAsync(empresa, tabela, adm, ct)).Valor;
        Assert.Equal([100.5m, 200m, 0m], lida[0].Valores.Take(3));
        Assert.Equal((0L, 1000m, 5m), (lida[1].CentroId, lida[1].Valores[0], lida[1].Valores[2]));
        var ruim = await g.LerPlanilhaAsync(empresa, TabelaImportada.DeTexto("Conta;Jan\n9.9.9;10\n1.1.01.002;5\n"), adm, ct);
        Assert.False(ruim.Sucesso);
        Assert.Equal(2, ruim.Erros.Count);
        var longo = (await g.LerPlanilhaAsync(empresa, TabelaImportada.DeTexto("Conta;Mês;Valor\n4.2.02.001;3;50\n4.2.02.001;3;25\n"), adm, ct)).Valor;
        Assert.Equal(75m, longo.Single().Valores[2]);
    }
}
