using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Contabil.Desktop.Servicos;

namespace Vorticis.Contabil.Desktop.ViewModels;

public sealed partial class DreComparativoViewModel : ObservableObject
{
    private readonly IBalanceteServico _servico;

    public DreComparativoViewModel(IBalanceteServico servico)
    {
        _servico = servico;
        AnoBase = DateTime.Now.Year;
        AnosComparacao = [AnoBase - 1, AnoBase - 2];
        GerarCommand = new AsyncRelayCommand(GerarAsync);
    }

    [ObservableProperty] private int _anoBase;
    [ObservableProperty] private ObservableCollection<int> _anosComparacao = [];
    [ObservableProperty] private bool _mostrarPercentualReceita = true;
    [ObservableProperty] private bool _mostrarVariacao = true;
    [ObservableProperty] private DreComparativoResultado? _resultado;
    [ObservableProperty] private bool _carregando;

    public IAsyncRelayCommand GerarCommand { get; }

    private async Task GerarAsync()
    {
        Carregando = true;
        try
        {
            Resultado = await _servico.GerarDreComparativoAsync(AnoBase, AnosComparacao, MostrarPercentualReceita, MostrarVariacao);
        }
        finally { Carregando = false; }
    }

    [RelayCommand]
    private void AdicionarAno() => AnosComparacao.Add(AnosComparacao.Min() - 1);

    [RelayCommand]
    private void RemoverAno(int ano)
    {
        if (AnosComparacao.Count > 1) AnosComparacao.Remove(ano);
    }
}

public sealed class DreComparativoResultado
{
    public int AnoBase { get; init; }
    public List<int> AnosComparacao { get; init; } = [];
    public List<LinhaDreComparativo> Linhas { get; init; } = [];
    public decimal ReceitaBase { get; init; }
    public Dictionary<int, decimal> ReceitasComparacao { get; init; } = [];
    public decimal ResultadoBase { get; init; }
    public Dictionary<int, decimal> ResultadosComparacao { get; init; } = [];
}

public sealed class LinhaDreComparativo
{
    public string ContaCodigo { get; init; } = "";
    public string ContaNome { get; init; } = "";
    public decimal ValorBase { get; init; }
    public Dictionary<int, decimal> ValoresComparacao { get; init; } = [];
    public Dictionary<int, decimal> PercentualReceitaBase { get; init; } = [];
    public Dictionary<int, Dictionary<int, decimal>> PercentualReceitaComparacao { get; init; } = [];
    public Dictionary<int, decimal> VariacaoPercentual { get; init; } = [];
    public bool EhTotalizador { get; init; }
    public int Nivel { get; init; }
}