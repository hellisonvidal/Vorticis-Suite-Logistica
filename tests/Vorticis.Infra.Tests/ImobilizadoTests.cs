using Vorticis.Application.Imobilizado;
using Vorticis.Domain.Imobilizado;

namespace Vorticis.Infra.Tests;

public class ImobilizadoTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    [Fact]
    public async Task Cadastro_calculo_lancamento_e_estorno_da_depreciacao()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var svc = db.App.Imobilizado;
        var empresa = await db.CriarEmpresaAsync("61695227000193");
        var adm = await db.AdminAsync();
        var grupos = await svc.ListarGruposAsync(ct);
        var moveis = grupos.Single(g => g.Codigo == "04");
        Assert.Equal((10m, 120), (moveis.TaxaAnualFiscal, moveis.VidaUtilFiscalMeses));
        var terreno = grupos.Single(g => g.Codigo == "12");
        Assert.False(terreno.Deprecia);

        BemImobilizado Novo(string desc, GrupoBem g, decimal valor, int vida) =>
            new(0, 0, desc, g.Id, null, null, null, null, new DateOnly(2026, 1, 10), new DateOnly(2026, 2, 1), valor, 0, vida,
                MetodoDepreciacao.Linear, 1, 0, g.ContaBem!, g.ContaAcumulada, g.ContaDespesa, null, null);
        var mesa = await svc.GravarBemAsync(empresa, Novo("Mesa de reunião", moveis, 6_000m, 120), adm, ct);
        Assert.True(mesa.Sucesso, string.Join("; ", mesa.Erros));
        var lote = await svc.GravarBemAsync(empresa, Novo("Terreno da sede", terreno, 200_000m, 0), adm, ct);
        Assert.True(lote.Sucesso, string.Join("; ", lote.Erros));
        Assert.Contains("vida útil", (await svc.GravarBemAsync(empresa, Novo("Sem vida", moveis, 100m, 0), adm, ct)).Erros[0]);
        Assert.Equal([1, 2], (await svc.ListarBensAsync(empresa, adm, ct)).Select(b => b.Codigo));

        var calc = await svc.CalcularAsync(empresa, new DateOnly(2026, 2, 1), adm, ct);
        Assert.Equal(50m, calc.Valor.Single(i => i.Bem.Codigo == 1).Quota!.Contabil);
        Assert.StartsWith("Não deprecia", calc.Valor.Single(i => i.Bem.Codigo == 2).Situacao);

        var lanc = await svc.LancarAsync(empresa, new DateOnly(2026, 2, 1), adm, ct);
        Assert.True(lanc.Sucesso, string.Join("; ", lanc.Erros));
        Assert.Equal("DEPRECIACAO", await db.EscalarAsync<string>("SELECT origem::text FROM contabil.lancamento WHERE id = @l", ("l", lanc.Valor.Id)));
        Assert.Equal(new DateOnly(2026, 2, 28), await db.EscalarAsync<DateOnly>("SELECT data_lancamento FROM contabil.lancamento WHERE id = @l", ("l", lanc.Valor.Id)));
        Assert.Contains("Nenhuma depreciação", (await svc.LancarAsync(empresa, new DateOnly(2026, 2, 1), adm, ct)).Erros[0]);
        var bem = (await svc.ListarBensAsync(empresa, adm, ct)).Single(b => b.Codigo == 1);
        Assert.Equal((50m, 5_950m, new DateOnly(2026, 2, 1)), (bem.DepreciacaoLancada, bem.ValorContabil, bem.UltimaCompetencia));
        Assert.Contains("já tem depreciação lançada", (await svc.GravarBemAsync(empresa, bem with { Valor = 7_000m }, adm, ct)).Erros[0]);
        Assert.Contains("registre a baixa", (await svc.ExcluirBemAsync(empresa, bem.Id, adm, ct)).Erros[0]);

        Assert.True((await svc.LancarAsync(empresa, new DateOnly(2026, 3, 1), adm, ct)).Sucesso);
        Assert.Contains("posteriores", (await svc.EstornarAsync(empresa, new DateOnly(2026, 2, 1), adm, ct)).Erros[0]);
        var est = await svc.EstornarAsync(empresa, new DateOnly(2026, 3, 1), adm, ct);
        Assert.True(est.Sucesso, string.Join("; ", est.Erros));
        bem = (await svc.ListarBensAsync(empresa, adm, ct)).Single(b => b.Codigo == 1);
        Assert.Equal((50m, new DateOnly(2026, 2, 1)), (bem.DepreciacaoLancada, bem.UltimaCompetencia));
        Assert.True((await svc.LancarAsync(empresa, new DateOnly(2026, 3, 1), adm, ct)).Sucesso);   // relançar depois do estorno

        // Relatórios › Depreciação: posição da última depreciação lançada (fev e mar = 100 da mesa)
        var bens = await svc.ListarBensAsync(empresa, adm, ct);
        var relacao = TabelasImobilizado.RelacaoBens(bens, grupos);
        Assert.Equal(3, relacao.Linhas.Count);
        Assert.Equal(206_000m, relacao.Linhas[^1].Valores[8]);
        var depreciado = TabelasImobilizado.ValorDepreciado(bens, grupos, moveis.Id);
        Assert.Equal(2, depreciado.Linhas.Count);
        Assert.Equal((100m, 1.67m, "03/2026"), ((decimal)depreciado.Linhas[0].Valores[6]!, (decimal)depreciado.Linhas[0].Valores[7]!, (string)depreciado.Linhas[0].Valores[8]!));
        var residual = TabelasImobilizado.ValorResidual(bens, grupos);
        Assert.Equal(5_900m, residual.Linhas.Single(l => Equals(l.Valores[0], 1)).Valores[6]);              // saldo a depreciar
        Assert.Equal(0m, residual.Linhas.Single(l => Equals(l.Valores[0], 2)).Valores[3]);                 // terreno não deprecia
        var porGrupo = TabelasImobilizado.PorGrupo(bens, grupos);
        Assert.Equal(3, porGrupo.Linhas.Count);                                                             // móveis, terrenos e total
        Assert.Equal(205_900m, porGrupo.Linhas[^1].Valores[6]);
        foreach (var t in new[] { relacao, depreciado, residual, porGrupo }) Assert.All(t.Linhas, l => Assert.Equal(t.Colunas.Count, l.Valores.Count));

        // Grupo em uso não é excluído; empresa excluída leva o imobilizado junto
        Assert.Contains("tem bens", (await svc.ExcluirGrupoAsync(empresa, moveis.Id, adm, ct)).Erros[0]);
        Assert.True((await db.App.Empresas.ExcluirAsync(empresa, ct)).Sucesso);
    }
}
