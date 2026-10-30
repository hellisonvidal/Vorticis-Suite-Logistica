using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Contabil.Desktop.Servicos;

namespace Vorticis.Contabil.Desktop.ViewModels;

public sealed partial class LivroCaixaComparativoViewModel : ObservableObject
{
    private readonly IBalanceteServico _servico;

    public LivroCaixaComparativoViewModel(IBalanceteServico servico)
    {
        _servico = servico;
        AnoBase = DateTime.Now.Year;
        AnosComparacao = [AnoBase - 1, AnoBase - 2];
        GerarCommand = new AsyncRelayCommand(GerarAsync);
    }

    [ObservableProperty] private int _anoBase;
    [ObservableProperty] private ObservableCollection<int> _anosComparacao = [];
    [ObservableProperty] private string? _contaCaixa;
    [ObservableProperty] private LivroCaixaComparativoResultado? _resultado;
    [ObservableProperty] private bool _carregando;

    public IAsyncRelayCommand GerarCommand { get; }

    private async Task GerarAsync()
    {
        Carregando = true;
        try
        {
            Resultado = await _servico.GerarLivroCaixaComparativoAsync(AnoBase, ContaCaixa, AnosComparacao);
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

public sealed class LivroCaixaComparativoResultado
{
    public int AnoBase { get; init; }
    public List<int> AnosComparacao { get; init; } = [];
    public string ContaCaixa { get; init; } = "";
    public List<LinhaLivroCaixaComparativo> Linhas { get; init; } = [];
    public decimal SaldoInicialBase { get; init; }
    public Dictionary<int, decimal> SaldosIniciaisComparacao { get; init; } = [];
    public decimal SaldoFinalBase { get; init; }
    public Dictionary<int, decimal> SaldosFinaisComparacao { get; init; } = [];
}

public sealed class LinhaLivroCaixaComparativo
{
    public DateTime Data { get; init; }
    public string Historico { get; init; } = "";
    public string ContaContrapartida { get; init; } = "";
    public decimal EntradaBase { get; init; }
    public decimal SaidaBase { get; init; }
    public decimal SaldoBase { get; init; }
    public Dictionary<int, (decimal Entrada, decimal Saida, decimal Saldo)> Comparacao { get; init; } = [];
}