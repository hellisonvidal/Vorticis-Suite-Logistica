using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Relatorios;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Desktop.ViewModels;

public sealed record LinhaBalanceteView(string Codigo, string Descricao, bool Analitica, string SaldoAnterior, string Debitos,
                                        string Creditos, string SaldoFinal, bool SaldoInvertido)
{
    public string CodigoIndentado { get; init; } = Codigo;
    public string Alerta => SaldoInvertido ? "⚠" : "";
}

public partial class BalanceteViewModel(IContexto contexto) : RelatorioViewModel(contexto)
{
    private IReadOnlyList<LinhaBalancete> _ultimo = [];
    private ILookup<long, MovimentoParticipante> _participantes = Array.Empty<MovimentoParticipante>().ToLookup(m => m.ContaId);
    private (DateOnly Ini, DateOnly Fim) _periodo;

    public ObservableCollection<LinhaBalanceteView> Linhas { get; } = [];

    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _fim = DateTime.Today;
    [ObservableProperty] private bool _incluirZeradas;
    /// <summary>Plano com participantes: abaixo de cada conta, uma linha por cliente/fornecedor (a conta mostra a soma).</summary>
    [ObservableProperty] private bool _detalharParticipantes;
    [ObservableProperty] private string _totais = "";

    public bool PodeExportar => PodeExportarPlanilha;

    protected override string TituloRelatorio => "Balancete de verificação";
    protected override string Parametros => $"Período: {_periodo.Ini:dd/MM/yyyy} a {_periodo.Fim:dd/MM/yyyy}";
    protected override string NomeArquivo => $"balancete_{_periodo.Ini:yyyyMMdd}_{_periodo.Fim:yyyyMMdd}";
    protected override bool Paisagem => true;

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var ini = Data(Inicio, DateOnly.FromDateTime(DateTime.Today));
        var r = await app.Relatorios.BalanceteAsync(empresa, ini, Data(Fim, ini), usuario, IncluirZeradas);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        _ultimo = r.Valor;
        _periodo = (ini, Data(Fim, ini));
        _participantes = Array.Empty<MovimentoParticipante>().ToLookup(m => m.ContaId);
        if (DetalharParticipantes)
        {
            var p = await app.Relatorios.ParticipantesBalanceteAsync(empresa, ini, Data(Fim, ini), usuario);
            if (p.Sucesso) _participantes = p.Valor.ToLookup(m => m.ContaId);
        }
        Linhas.Clear();
        foreach (var l in _ultimo)
        {
            Linhas.Add(new LinhaBalanceteView(l.Codigo, l.Descricao, l.Analitica, LinhaBalancete.Formatar(l.SaldoAnterior),
                Numeros.MoedaOuVazio(l.Debitos), Numeros.MoedaOuVazio(l.Creditos), LinhaBalancete.Formatar(l.SaldoFinal), l.SaldoInvertido)
            { CodigoIndentado = new string(' ', (l.Nivel - 1) * 3) + l.Codigo });
            foreach (var m in _participantes[l.ContaId])
                Linhas.Add(new LinhaBalanceteView("", $"   ↳ {m.CodigoParticipante} – {m.NomeParticipante}", false,
                    LinhaBalancete.Formatar(m.SaldoAnterior), Numeros.MoedaOuVazio(m.Debitos), Numeros.MoedaOuVazio(m.Creditos),
                    LinhaBalancete.Formatar(m.SaldoFinal), false) { CodigoIndentado = "" });
        }

        var analiticas = _ultimo.Where(l => l.Analitica).ToArray();
        var deb = analiticas.Sum(l => l.Debitos);
        var cred = analiticas.Sum(l => l.Creditos);
        Totais = $"Movimento: débitos {Numeros.Moeda(deb)} | créditos {Numeros.Moeda(cred)}" + (deb == cred ? " ✓ fechado" : " ✗ DIFERENÇA");
        var invertidas = _ultimo.Count(l => l.Analitica && l.SaldoInvertido);
        Mensagem = invertidas > 0 ? $"{invertidas} conta(s) com saldo contrário à natureza (destacadas)." : null;
        TemDados = true;
        AtualizarPermissoesExportacao();
        OnPropertyChanged(nameof(PodeExportar));
    }, Permissao.RelatorioBalancete);

    private static string DC(decimal saldo) => saldo == 0 ? "" : saldo > 0 ? "D" : "C";

    protected override IReadOnlyList<TabelaRelatorio> Tabelas() =>
    [
        new("Balancete", [
                new("Conta", LarguraCm: 3.2), new("Descrição"), new("Tipo", LarguraCm: 1.1),
                new("Saldo anterior", TipoColuna.Valor), new("D/C", LarguraCm: 0.9), new("Débitos", TipoColuna.Valor),
                new("Créditos", TipoColuna.Valor), new("Saldo final", TipoColuna.Valor), new("D/C", LarguraCm: 0.9)],
            _ultimo.SelectMany(l => _participantes[l.ContaId].Select(m => new LinhaRelatorio(
                    ["", $"↳ {m.CodigoParticipante} – {m.NomeParticipante}", "P", Math.Abs(m.SaldoAnterior), DC(m.SaldoAnterior), m.Debitos,
                     m.Creditos, Math.Abs(m.SaldoFinal), DC(m.SaldoFinal)], Recuo: l.Nivel))
                .Prepend(new LinhaRelatorio(
                    [l.Codigo, l.Descricao, l.Analitica ? "A" : "S", Math.Abs(l.SaldoAnterior), DC(l.SaldoAnterior), l.Debitos, l.Creditos,
                     Math.Abs(l.SaldoFinal), DC(l.SaldoFinal)], Destaque: !l.Analitica, Recuo: l.Nivel - 1))).ToList(),
            Totais),
    ];
}

public sealed record LinhaRazaoView(string Data, long Numero, string Historico, string Debito, string Credito, string Saldo, bool Estornado)
{
    public string Situacao => Estornado ? "Estornado" : "";
}

public partial class RazaoViewModel(IContexto contexto) : RelatorioViewModel(contexto)
{
    private Application.Contabil.RazaoConta? _razao;
    private (DateOnly Ini, DateOnly Fim) _periodo;

    public ObservableCollection<LinhaRazaoView> Linhas { get; } = [];

    [ObservableProperty] private string _conta = "";
    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, 1, 1);
    [ObservableProperty] private DateTime? _fim = DateTime.Today;
    [ObservableProperty] private string _cabecalho = "";

    // Painel de contas analíticas para consultar
    private IReadOnlyList<ContaSelecionavel> _todasContas = [];
    private int _cargaContas;
    public ObservableCollection<ContaSelecionavel> Contas { get; } = [];
    [ObservableProperty] private string _filtroConta = "";
    [ObservableProperty] private ContaSelecionavel? _contaSelecionada;

    public async Task CarregarContasAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = ++_cargaContas;                                         // contador próprio: não interfere na consulta do razão
        var plano = await app.PlanoContas.ListarAsync(empresa, sessao.Id);
        if (g != _cargaContas) return;
        _todasContas = plano.Where(c => c.Analitica).Select(c => new ContaSelecionavel(c.Codigo, c.Descricao, c.CodigoReduzido)).ToList();
        FiltrarContas();
    }

    partial void OnFiltroContaChanged(string value) => FiltrarContas();

    private void FiltrarContas()
    {
        Contas.Clear();
        foreach (var c in _todasContas.Where(c => LancamentoViewModel.Combina(c, FiltroConta))) Contas.Add(c);
    }

    /// <summary>Clicar numa conta da lista já mostra o razão dela no período.</summary>
    partial void OnContaSelecionadaChanged(ContaSelecionavel? value)
    {
        if (value is null) return;
        Conta = value.Codigo;
        _ = GerarCommand.ExecuteAsync(null);
    }

    protected override string TituloRelatorio => "Livro Razão";
    protected override string Parametros => $"Conta {_razao?.Codigo} - {_razao?.Descricao} | Período: {_periodo.Ini:dd/MM/yyyy} a {_periodo.Fim:dd/MM/yyyy}";
    protected override string NomeArquivo => $"razao_{_razao?.Codigo}_{_periodo.Ini:yyyyMMdd}_{_periodo.Fim:yyyyMMdd}";

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        Linhas.Clear();
        Cabecalho = "";
        TemDados = false;
        var ini = Data(Inicio, DateOnly.FromDateTime(DateTime.Today));
        // Aceita também o código reduzido.
        var conta = _todasContas.FirstOrDefault(c => c.Reduzido?.ToString() == Conta.Trim())?.Codigo ?? Conta.Trim();
        var geracao = NovaGeracao();
        var r = await app.Relatorios.RazaoAsync(empresa, conta, ini, Data(Fim, ini), usuario);
        if (!EhAtual(geracao)) return;                                   // outra consulta começou depois desta
        Linhas.Clear();
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var razao = _razao = r.Valor;
        _periodo = (ini, Data(Fim, ini));
        foreach (var l in razao.Linhas)
            Linhas.Add(new LinhaRazaoView(l.Data.ToString("dd/MM/yyyy"), l.Numero, l.Historico,
                l.DC == IndicadorDC.D ? Numeros.Moeda(l.Valor) : "", l.DC == IndicadorDC.C ? Numeros.Moeda(l.Valor) : "",
                LinhaBalancete.Formatar(l.SaldoAcumulado), l.Estornado));
        Cabecalho = $"{razao.Codigo} - {razao.Descricao} | Saldo anterior: {LinhaBalancete.Formatar(razao.SaldoAnterior)} | Saldo final: {LinhaBalancete.Formatar(razao.SaldoFinal)}";
        TemDados = true;
        AtualizarPermissoesExportacao();
    }, Permissao.RelatorioRazao);

    protected override IReadOnlyList<TabelaRelatorio> Tabelas()
    {
        var r = _razao!;
        var linhas = new List<LinhaRelatorio>
        {
            new([null, null, "Saldo anterior", null, null, Math.Abs(r.SaldoAnterior), r.SaldoAnterior == 0 ? "" : r.SaldoAnterior > 0 ? "D" : "C", ""], Destaque: true),
        };
        linhas.AddRange(r.Linhas.Select(l => new LinhaRelatorio(
            [l.Data, l.Numero, l.Historico, l.DC == IndicadorDC.D ? l.Valor : null, l.DC == IndicadorDC.C ? l.Valor : null,
             Math.Abs(l.SaldoAcumulado), l.SaldoAcumulado == 0 ? "" : l.SaldoAcumulado > 0 ? "D" : "C", l.Estornado ? "Estornado" : ""])));
        return
        [
            new($"Razão {r.Codigo}", [
                    new("Data", TipoColuna.Data), new("Nº", TipoColuna.Inteiro), new("Histórico"), new("Débito", TipoColuna.Valor),
                    new("Crédito", TipoColuna.Valor), new("Saldo", TipoColuna.Valor), new("D/C", LarguraCm: 0.9), new("Situação", LarguraCm: 1.8)],
                linhas, Cabecalho),
        ];
    }
}

public sealed record LinhaDiarioView(long LancamentoId, long Numero, string Data, string Conta, string Debito, string Credito,
                                     string Historico, string Situacao);

public partial class DiarioViewModel(IContexto contexto) : RelatorioViewModel(contexto)
{
    private IReadOnlyList<Application.Contabil.LinhaDiario> _ultimo = [];
    private (DateOnly Ini, DateOnly Fim) _periodo;

    public ObservableCollection<LinhaDiarioView> Linhas { get; } = [];

    protected override string TituloRelatorio => "Livro Diário";
    protected override string Parametros => $"Período: {_periodo.Ini:dd/MM/yyyy} a {_periodo.Fim:dd/MM/yyyy}";
    protected override string NomeArquivo => $"diario_{_periodo.Ini:yyyyMMdd}_{_periodo.Fim:yyyyMMdd}";
    protected override bool Paisagem => true;

    protected override IReadOnlyList<TabelaRelatorio> Tabelas() =>
    [
        new("Diário", [
                new("Nº", TipoColuna.Inteiro), new("Data", TipoColuna.Data), new("Conta", LarguraCm: 2.6), new("Descrição da conta", LarguraCm: 6),
                new("Débito", TipoColuna.Valor), new("Crédito", TipoColuna.Valor), new("Histórico"), new("Situação", LarguraCm: 1.8)],
            _ultimo.Select(l => new LinhaRelatorio(
                [l.Numero, l.Data, l.CodigoConta, l.DescricaoConta, l.DC == IndicadorDC.D ? l.Valor : null,
                 l.DC == IndicadorDC.C ? l.Valor : null, l.Historico, l.Status == StatusLancamento.Estornado ? "Estornado" : ""])).ToList()),
    ];

    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _fim = DateTime.Today;
    [ObservableProperty] private LinhaDiarioView? _selecionada;

    public void AtualizarPermissoes() { }

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var ini = Data(Inicio, DateOnly.FromDateTime(DateTime.Today));
        var r = await app.Relatorios.DiarioAsync(empresa, ini, Data(Fim, ini), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        (_ultimo, _periodo) = (r.Valor, (ini, Data(Fim, ini)));
        TemDados = true;
        AtualizarPermissoesExportacao();
        Linhas.Clear();
        foreach (var l in r.Valor)
            Linhas.Add(new LinhaDiarioView(l.LancamentoId, l.Numero, l.Data.ToString("dd/MM/yyyy"), $"{l.CodigoConta} {l.DescricaoConta}",
                l.DC == IndicadorDC.D ? Numeros.Moeda(l.Valor) : "", l.DC == IndicadorDC.C ? Numeros.Moeda(l.Valor) : "",
                l.Historico, l.Status == StatusLancamento.Estornado ? "Estornado" : ""));
    });
}

public sealed record PeriodoView(DateOnly Competencia, bool Aberto)
{
    public string Rotulo => $"{Competencia:MM/yyyy}  —  {(Aberto ? "Aberto" : "Fechado")}";
}

public partial class PeriodosViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public ObservableCollection<PeriodoView> Periodos { get; } = [];

    [ObservableProperty] private decimal? _ano = DateTime.Today.Year;
    [ObservableProperty] private PeriodoView? _selecionado;

    public bool PodeFechar => Contexto.Pode(Permissao.PeriodoFechar);
    public bool PodeReabrir => Contexto.Pode(Permissao.PeriodoReabrir);

    [RelayCommand]
    public async Task CarregarAsync()
    {
        OnPropertyChanged(nameof(PodeFechar));
        OnPropertyChanged(nameof(PodeReabrir));
        var geracao = NovaGeracao();
        IReadOnlyList<Application.Contabil.CompetenciaStatus> lista = [];
        if (Contexto.App is { } app && Contexto.EmpresaId is { } empresa && Contexto.Sessao is { } sessao)
            lista = await app.Periodos.ListarAsync(empresa, (int)(Ano ?? DateTime.Today.Year), sessao.Id);
        if (!EhAtual(geracao)) return;
        Periodos.Clear();
        foreach (var p in lista) Periodos.Add(new PeriodoView(p.Competencia, p.Aberto));
    }

    [RelayCommand]
    private Task FecharAsync() => Alterar(fechar: true);

    [RelayCommand]
    private Task ReabrirAsync() => Alterar(fechar: false);

    private Task Alterar(bool fechar) => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado is null) { Erros.Add("Selecione uma competência."); return; }
        var competencia = Selecionado.Competencia;
        var r = fechar
            ? await app.Periodos.FecharAsync(empresa, competencia, usuario)
            : await app.Periodos.ReabrirAsync(empresa, competencia, usuario);
        await CarregarAsync();
        MostrarResultado(r, $"Período {competencia:MM/yyyy} {(fechar ? "fechado" : "reaberto")}.");
    }, fechar ? Permissao.PeriodoFechar : Permissao.PeriodoReabrir);
}
