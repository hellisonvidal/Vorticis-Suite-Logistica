using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Contabil.Desktop.Servicos;

namespace Vorticis.Contabil.Desktop.ViewModels;

public sealed partial class RazaoComparativoViewModel : ObservableObject
{
    private readonly IBalanceteServico _servico;

    public RazaoComparativoViewModel(IBalanceteServico servico)
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
    [ObservableProperty] private string? _contaInicial;
    [ObservableProperty] private string? _contaFinal;
    [ObservableProperty] private RazaoComparativoResultado? _resultado;
    [ObservableProperty] private bool _carregando;

    public IAsyncRelayCommand GerarCommand { get; }

    private async Task GerarAsync()
    {
        Carregando = true;
        try
        {
            Resultado = await _servico.GerarRazaoComparativoAsync(AnoBase, MesBase, ContaInicial, ContaFinal, AnosComparacao);
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

public sealed class RazaoComparativoResultado
{
    public int AnoBase { get; init; }
    public int MesBase { get; init; }
    public List<int> AnosComparacao { get; init; } = [];
    public List<LinhaRazaoComparativo> Linhas { get; init; } = [];
}

public sealed class LinhaRazaoComparativo
{
    public string ContaCodigo { get; init; } = "";
    public string ContaNome { get; init; } = "";
    public decimal SaldoAnteriorBase { get; init; }
    public List<LancamentoRazao> LancamentosBase { get; init; } = [];
    public decimal SaldoAtualBase { get; init; }
    public Dictionary<int, (decimal Anterior, List<LancamentoRazao> Lancs, decimal Atual)> Comparacao { get; init; } = [];
}

public sealed class LancamentoRazao
{
    public DateTime Data { get; init; }
    public string Historico { get; init; } = "";
    public string ContaContrapartida { get; init; } = "";
    public decimal Debito { get; init; }
    public decimal Credito { get; init; }
}