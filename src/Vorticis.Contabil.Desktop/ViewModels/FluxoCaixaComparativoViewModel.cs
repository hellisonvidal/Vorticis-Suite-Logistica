using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Contabil.Desktop.Servicos;

namespace Vorticis.Contabil.Desktop.ViewModels;

public sealed partial class FluxoCaixaComparativoViewModel : ObservableObject
{
    private readonly IBalanceteServico _servico;

    public FluxoCaixaComparativoViewModel(IBalanceteServico servico)
    {
        _servico = servico;
        AnoBase = DateTime.Now.Year;
        AnosComparacao = [AnoBase - 1, AnoBase - 2];
        GerarCommand = new AsyncRelayCommand(GerarAsync);
    }

    [ObservableProperty] private int _anoBase;
    [ObservableProperty] private ObservableCollection<int> _anosComparacao = [];
    [ObservableProperty] private FluxoCaixaComparativoResultado? _resultado;
    [ObservableProperty] private bool _carregando;

    public IAsyncRelayCommand GerarCommand { get; }

    private async Task GerarAsync()
    {
        Carregando = true;
        try
        {
            Resultado = await _servico.GerarFluxoCaixaComparativoAsync(AnoBase, AnosComparacao);
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

public sealed class FluxoCaixaComparativoResultado
{
    public int AnoBase { get; init; }
    public List<int> AnosComparacao { get; init; } = [];
    public List<LinhaFluxoCaixaComparativo> Linhas { get; init; } = [];
    public decimal SaldoInicialBase { get; init; }
    public Dictionary<int, decimal> SaldosIniciaisComparacao { get; init; } = [];
    public decimal SaldoFinalBase { get; init; }
    public Dictionary<int, decimal> SaldosFinaisComparacao { get; init; } = [];
}

public sealed class LinhaFluxoCaixaComparativo
{
    public string Grupo { get; init; } = "";
    public string Descricao { get; init; } = "";
    public decimal ValorBase { get; init; }
    public Dictionary<int, decimal> ValoresComparacao { get; init; } = [];
    public Dictionary<int, decimal> VariacaoPercentual { get; init; } = [];
    public bool EhTotalizador { get; init; }
    public int Nivel { get; init; }
}