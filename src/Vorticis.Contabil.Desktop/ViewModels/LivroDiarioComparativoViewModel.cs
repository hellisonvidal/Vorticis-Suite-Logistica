using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Contabil.Desktop.Servicos;

namespace Vorticis.Contabil.Desktop.ViewModels;

public sealed partial class LivroDiarioComparativoViewModel : ObservableObject
{
    private readonly IBalanceteServico _servico;

    public LivroDiarioComparativoViewModel(IBalanceteServico servico)
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
    [ObservableProperty] private DateTime? _dataInicial;
    [ObservableProperty] private DateTime? _dataFinal;
    [ObservableProperty] private LivroDiarioComparativoResultado? _resultado;
    [ObservableProperty] private bool _carregando;

    public IAsyncRelayCommand GerarCommand { get; }

    private async Task GerarAsync()
    {
        Carregando = true;
        try
        {
            Resultado = await _servico.GerarLivroDiarioComparativoAsync(AnoBase, MesBase, DataInicial, DataFinal, AnosComparacao);
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

public sealed class LivroDiarioComparativoResultado
{
    public int AnoBase { get; init; }
    public int MesBase { get; init; }
    public List<int> AnosComparacao { get; init; } = [];
    public List<LinhaLivroDiarioComparativo> Linhas { get; init; } = [];
}

public sealed class LinhaLivroDiarioComparativo
{
    public DateTime Data { get; init; }
    public string Lancamento { get; init; } = "";
    public string Historico { get; init; } = "";
    public Dictionary<string, (decimal Debito, decimal Credito)> ContasBase { get; init; } = [];
    public Dictionary<int, Dictionary<string, (decimal Debito, decimal Credito)>> Comparacao { get; init; } = [];
}