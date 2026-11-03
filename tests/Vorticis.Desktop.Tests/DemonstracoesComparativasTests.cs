using Vorticis.Desktop.ViewModels;
using Vorticis.Domain.Contabil;

namespace Vorticis.Desktop.Tests;

/// <summary>Relatórios contábeis comparativos com anos anteriores (DRE e Balanço).</summary>
public class DemonstracoesComparativasTests
{
    private static LinhaDemonstracao L(string codigo, string descricao, decimal valor, TipoLinhaDemonstracao tipo = TipoLinhaDemonstracao.Conta) =>
        new(codigo, descricao, 1, valor, tipo);

    [Fact]
    public void Junta_os_anos_pela_conta_e_calcula_a_variacao()
    {
        var atual = new[] { L("3.1", "Receita de fretes", 1200m), L("3.3", "Despesas", -500m), L("", "Resultado", 700m, TipoLinhaDemonstracao.Total) };
        var anterior = new[] { L("3.1", "Receita de fretes", 1000m), L("3.2", "Outras receitas", 50m), L("", "Resultado", 1050m, TipoLinhaDemonstracao.Total) };
        var m = ComparativoDemonstracao.Mesclar([atual, anterior]);
        Assert.Equal(["Receita de fretes", "Outras receitas", "Despesas", "Resultado"], m.Select(x => x.Linha.Descricao));
        Assert.Equal([1200m, 0m, -500m, 700m], m.Select(x => x.Linha.Valor));
        Assert.Equal([1000m, 50m, 0m, 1050m], m.Select(x => x.Anteriores[0]));
        Assert.Equal("20,0%", ComparativoDemonstracao.Variacao(1200m, 1000m).Replace('.', ','));
        Assert.Equal("", ComparativoDemonstracao.Variacao(10m, 0m));
        // sem comparar: uma coluna só
        var linhas = new System.Collections.ObjectModel.ObservableCollection<LinhaDemonstracaoView>();
        TabelasDemonstracao.Substituir(linhas, [atual]);
        Assert.All(linhas, l => Assert.Null(l.Anteriores));
        TabelasDemonstracao.Substituir(linhas, [atual, anterior, anterior]);
        var tabela = TabelasDemonstracao.Tabela("DRE", linhas, null, ["2026", "2025", "2024"]);
        Assert.Equal(["Código", "Descrição", "2026", "2025", "2024", "Var. %"], tabela.Colunas.Select(c => c.Titulo));
        Assert.Equal(6, tabela.Linhas[0].Valores.Count);
    }
}
