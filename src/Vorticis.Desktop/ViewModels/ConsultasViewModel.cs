using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Contabil;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Desktop.ViewModels;

public sealed record SaldoContaLinha(LinhaBalancete Linha)
{
    public string Codigo => new string(' ', (Linha.Nivel - 1) * 3) + Linha.Codigo;
    public string Descricao => Linha.Descricao;
    public string Saldo => LinhaBalancete.Formatar(Linha.SaldoFinal);
    public string MovimentoAno => Linha.Debitos == 0 && Linha.Creditos == 0 ? "" : $"D {Numeros.Moeda(Linha.Debitos)} · C {Numeros.Moeda(Linha.Creditos)}";
}

public sealed record LancamentoConsultaLinha(LinhaDiario Linha)
{
    public long Numero => Linha.Numero;
    public string Data => Linha.Data.ToString("dd/MM/yyyy");
    public string Conta => $"{Linha.CodigoConta} {Linha.DescricaoConta}";
    public string Debito => Linha.DC == IndicadorDC.D ? Numeros.Moeda(Linha.Valor) : "";
    public string Credito => Linha.DC == IndicadorDC.C ? Numeros.Moeda(Linha.Valor) : "";
    public string Historico => Linha.Historico;
    public string Situacao => Linha.Status == StatusLancamento.Estornado ? "Estornado" : "";
}

/// <summary>
/// Consultas: respostas rápidas sobre saldos e lançamentos, sem gerar relatórios. Saldos vêm do balancete
/// (início do ano até a data) e lançamentos do diário do período, filtrados na tela.
/// </summary>
public partial class ConsultasViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public const int AbaSaldos = 0;
    public const int AbaLancamentos = 1;

    private IReadOnlyList<LinhaBalancete> _saldos = [];
    private IReadOnlyList<LinhaDiario> _lancamentos = [];

    [ObservableProperty] private int _abaSelecionada;

    // saldos
    public ObservableCollection<SaldoContaLinha> Saldos { get; } = [];
    [ObservableProperty] private DateTime? _dataSaldo = DateTime.Today;
    [ObservableProperty] private string _filtroConta = "";
    [ObservableProperty] private bool _somenteComSaldo = true;
    [ObservableProperty] private string _resumoSaldos = "";

    // lançamentos
    public ObservableCollection<LancamentoConsultaLinha> Lancamentos { get; } = [];
    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _fim = DateTime.Today;
    [ObservableProperty] private string _contaLancamento = "";
    [ObservableProperty] private string _textoHistorico = "";
    [ObservableProperty] private string _valor = "";
    [ObservableProperty] private string _numero = "";
    [ObservableProperty] private string _resumoLancamentos = "";

    public bool PodeSaldos => Contexto.Pode(Permissao.RelatorioBalancete);
    public bool PodeLancamentos => Contexto.Pode(Permissao.RelatorioDiario) || Contexto.Pode(Permissao.RelatorioRazao);

    public void AtualizarPermissoes()
    {
        OnPropertyChanged(nameof(PodeSaldos));
        OnPropertyChanged(nameof(PodeLancamentos));
        if (!PodeSaldos && PodeLancamentos) AbaSelecionada = AbaLancamentos;
    }

    [RelayCommand]
    private Task ConsultarSaldosAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var data = Data(DataSaldo, DateOnly.FromDateTime(DateTime.Today));
        var r = await app.Relatorios.BalanceteAsync(empresa, new DateOnly(data.Year, 1, 1), data, usuario, incluirZeradas: true);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        _saldos = r.Valor;
        FiltrarSaldos();
    }, Permissao.RelatorioBalancete);

    partial void OnFiltroContaChanged(string value) => FiltrarSaldos();
    partial void OnSomenteComSaldoChanged(bool value) => FiltrarSaldos();

    private void FiltrarSaldos()
    {
        var f = FiltroConta.Trim();
        Saldos.Clear();
        foreach (var l in _saldos.Where(l => (!SomenteComSaldo || l.SaldoFinal != 0 || l.Debitos != 0 || l.Creditos != 0)
                                             && (f.Length == 0 || l.Codigo.StartsWith(f, StringComparison.Ordinal)
                                                 || l.Descricao.Contains(f, StringComparison.CurrentCultureIgnoreCase))))
            Saldos.Add(new SaldoContaLinha(l));
        var data = Data(DataSaldo, DateOnly.FromDateTime(DateTime.Today));
        ResumoSaldos = _saldos.Count == 0 ? "" : $"{Saldos.Count} conta(s) · saldos em {data:dd/MM/yyyy} · movimento desde 01/01/{data.Year}";
    }

    [RelayCommand]
    private Task ConsultarLancamentosAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var ini = Data(Inicio, DateOnly.FromDateTime(DateTime.Today));
        var r = await app.Relatorios.DiarioAsync(empresa, ini, Data(Fim, ini), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        _lancamentos = r.Valor;
        FiltrarLancamentos();
    });

    partial void OnContaLancamentoChanged(string value) => FiltrarLancamentos();
    partial void OnTextoHistoricoChanged(string value) => FiltrarLancamentos();
    partial void OnValorChanged(string value) => FiltrarLancamentos();
    partial void OnNumeroChanged(string value) => FiltrarLancamentos();

    private void FiltrarLancamentos()
    {
        var conta = ContaLancamento.Trim();
        var texto = TextoHistorico.Trim();
        var valor = Numeros.LerValor(Valor);
        long? numero = long.TryParse(Numero.Trim(), out var n) ? n : null;
        // Filtra por lançamento: se uma partida atende, mostra o lançamento inteiro (as contrapartidas ajudam a entender).
        var ids = _lancamentos.Where(l =>
                (conta.Length == 0 || l.CodigoConta.StartsWith(conta, StringComparison.Ordinal)
                                   || l.DescricaoConta.Contains(conta, StringComparison.CurrentCultureIgnoreCase))
                && (texto.Length == 0 || l.Historico.Contains(texto, StringComparison.CurrentCultureIgnoreCase))
                && (valor is null || l.Valor == valor)
                && (numero is null || l.Numero == numero))
            .Select(l => l.LancamentoId).ToHashSet();
        Lancamentos.Clear();
        foreach (var l in _lancamentos.Where(l => ids.Contains(l.LancamentoId))) Lancamentos.Add(new LancamentoConsultaLinha(l));
        var deb = Lancamentos.Where(l => l.Linha.DC == IndicadorDC.D).Sum(l => l.Linha.Valor);
        var cred = Lancamentos.Where(l => l.Linha.DC == IndicadorDC.C).Sum(l => l.Linha.Valor);
        ResumoLancamentos = _lancamentos.Count == 0 && Lancamentos.Count == 0 ? ""
            : $"{ids.Count} lançamento(s) · débitos {Numeros.Moeda(deb)} · créditos {Numeros.Moeda(cred)}";
    }
}
