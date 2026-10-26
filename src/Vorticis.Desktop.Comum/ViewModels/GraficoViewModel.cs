using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Relatorios;
using Vorticis.Desktop.Comum.Modulos;

namespace Vorticis.Desktop.Comum.ViewModels;

/// <summary>Linha da tabela que acompanha o gráfico: a categoria e o valor de cada série (os números exatos, além das cores).</summary>
public sealed record LinhaGrafico(string Categoria, string[] Valores);

/// <summary>Relatórios › Gráficos › (um gráfico): período, o gráfico e a tabela com os mesmos números.</summary>
public partial class GraficoViewModel(IContexto contexto, DefinicaoGrafico definicao) : ModuloViewModel(contexto)
{
    public DefinicaoGrafico Definicao { get; } = definicao;
    public string Titulo => Definicao.Titulo;
    public ObservableCollection<LinhaGrafico> Tabela { get; } = [];
    public ObservableCollection<string> Colunas { get; } = [];
    [ObservableProperty] private DadosGrafico? _dados;
    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-11);
    [ObservableProperty] private DateTime? _fim = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1).AddDays(-1);
    [ObservableProperty] private string _nota = "";

    public Task CarregarAsync() => Dados is null ? GerarAsync() : Task.CompletedTask;

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var (ini, fim) = (Data(Inicio, DateOnly.FromDateTime(DateTime.Today).AddMonths(-11)), Data(Fim, DateOnly.FromDateTime(DateTime.Today)));
        if (fim < ini) { Erros.Add("A data final é anterior à inicial."); return; }
        var d = await Definicao.Gerar(new ContextoGrafico(app, empresa, usuario, ini, fim));
        Colunas.Clear();
        foreach (var s in d.Series) Colunas.Add(s.Nome);
        Tabela.Clear();
        for (var i = 0; i < d.Categorias.Count; i++)
            Tabela.Add(new LinhaGrafico(d.Categorias[i], [.. d.Series.Select(s => Views.GraficoControl.Valor(s.Valores[i], d.Unidade))]));
        if (d.Series.Count > 1)
            Tabela.Add(new LinhaGrafico("TOTAL", [.. d.Series.Select(s => Views.GraficoControl.Valor(s.Valores.Sum(), d.Unidade))]));
        Nota = d.Nota ?? "";
        Dados = d;
    });
}
