using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Contabil.Desktop.Servicos;

namespace Vorticis.Contabil.Desktop.ViewModels;

public sealed partial class BalanceteComparativoViewModel : ObservableObject
{
    private readonly IBalanceteServico _servico;

    public BalanceteComparativoViewModel(IBalanceteServico servico)
    {
        _servico = servico;
        AnoBase = DateTime.Now.Year;
        MesBase = DateTime.Now.Month;
        AnosComparacao = [AnoBase - 1, AnoBase - 2];
        GerarCommand = new AsyncRelayCommand(GerarAsync);
    }

    [ObservableProperty] private int _anoBase;
    [ObservableProperty] private int _mesBase;
    [ObservableProperty] private ObservableCollection<int> _anosComparacao = [];
    [ObservableProperty] private bool _incluirContasZeradas;
    [ObservableProperty] private bool _mostrarVariacao = true;
    [ObservableProperty] private BalanceteComparativoResultado? _resultado;
    [ObservableProperty] private bool _carregando;

    public IAsyncRelayCommand GerarCommand { get; }

    private async Task GerarAsync()
    {
        Carregando = true;
        try
        {
            Resultado = await _servico.GerarBalanceteComparativoAsync(AnoBase, MesBase, AnosComparacao, IncluirContasZeradas, MostrarVariacao);
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

public sealed class BalanceteComparativoResultado
{
    public int AnoBase { get; init; }
    public int MesBase { get; init; }
    public List<int> AnosComparacao { get; init; } = [];
    public List<LinhaBalanceteComparativo> Linhas { get; init; } = [];
}

public sealed class LinhaBalanceteComparativo
{
    public string ContaCodigo { get; init; } = "";
    public string ContaNome { get; init; } = "";
    public decimal SaldoInicialBase { get; init; }
    public decimal DebitosBase { get; init; }
    public decimal CreditosBase { get; init; }
    public decimal SaldoFinalBase { get; init; }
    public Dictionary<int, (decimal Inicial, decimal Debitos, decimal Creditos, decimal Final)> Comparacao { get; init; } = [];
    public Dictionary<int, decimal> VariacaoSaldoFinal { get; init; } = [];
    public bool EhTotalizador { get; init; }
    public int Nivel { get; init; }
}