using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Contabil.Desktop.Servicos;
using Vorticis.Domain.Contabil.Relatorios;

namespace Vorticis.Contabil.Desktop.ViewModels;

public sealed partial class BalancoComparativoViewModel : ObservableObject
{
    private readonly IBalanceteServico _servico;

    public BalancoComparativoViewModel(IBalanceteServico servico)
    {
        _servico = servico;
        AnoBase = DateTime.Now.Year;
        AnosComparacao = [AnoBase - 1, AnoBase - 2];
        GerarCommand = new AsyncRelayCommand(GerarAsync);
    }

    [ObservableProperty] private int _anoBase;
    [ObservableProperty] private ObservableCollection<int> _anosComparacao = [];
    [ObservableProperty] private bool _incluirContasZeradas;
    [ObservableProperty] private bool _mostrarPercentualVariacao = true;
    [ObservableProperty] private BalancoComparativoResultado? _resultado;
    [ObservableProperty] private bool _carregando;

    public IAsyncRelayCommand GerarCommand { get; }

    private async Task GerarAsync()
    {
        Carregando = true;
        try
        {
            Resultado = await _servico.GerarBalancoComparativoAsync(AnoBase, AnosComparacao, IncluirContasZeradas, MostrarPercentualVariacao);
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

public sealed class BalancoComparativoResultado
{
    public int AnoBase { get; init; }
    public List<int> AnosComparacao { get; init; } = [];
    public List<LinhaBalancoComparativo> Linhas { get; init; } = [];
}

public sealed class LinhaBalancoComparativo
{
    public string ContaCodigo { get; init; } = "";
    public string ContaNome { get; init; } = "";
    public decimal ValorBase { get; init; }
    public Dictionary<int, decimal> ValoresComparacao { get; init; } = [];
    public Dictionary<int, decimal> VariacaoPercentual { get; init; } = [];
    public bool EhTotalizador { get; init; }
    public int Nivel { get; init; }
}