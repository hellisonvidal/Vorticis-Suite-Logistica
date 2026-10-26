using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal;
using Vorticis.Fiscal.Documentos;
using Vorticis.Fiscal.Duplicatas;

namespace Vorticis.Erp.Desktop.ViewModels;

public sealed record NotaMonitorLinha(NotaMonitorada N)
{
    public string Data => N.Documento.DataLivro.ToString("dd/MM/yyyy");
    public string Operacao => N.Documento.Operacao == TipoOperacao.Entrada ? "Entrada" : "Saída";
    public string Nota => $"{N.Documento.Modelo.Codigo()} {N.Documento.Serie}/{N.Documento.Numero}";
    public string Participante => N.Documento.NomeParticipante ?? "";
    public string Valor => Formato.Moeda(N.Documento.Totais.Total);
    public string Situacao => N.Documento.Situacao.ToString();
    public string Contabilizada => N.Documento.Contabilizado ? "Sim" : "Não";
    public string Cobranca => N.Duplicatas == 0 ? "—" : $"{N.Duplicatas} parc. · aberto {Formato.Moeda(N.SaldoAberto)}";
    public string Alerta => N.Alerta;
}

/// <summary>Monitores › Monitor de notas fiscais: notas do período com o que pede atenção (cancelada, sem contabilização, sem cobrança, cobrança vencida).</summary>
public partial class MonitorNotasViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public static IReadOnlyList<string> Operacoes { get; } = ["Entradas e saídas", "Somente entradas", "Somente saídas"];

    public ObservableCollection<NotaMonitorLinha> Linhas { get; } = [];
    public ObservableCollection<LinhaPainel> Indicadores { get; } = [];
    private IReadOnlyList<NotaMonitorada> _todas = [];

    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _fim = DateTime.Today;
    [ObservableProperty] private int _operacao;
    [ObservableProperty] private bool _somentePendencias;
    [ObservableProperty] private string _atualizadoEm = "";

    partial void OnSomentePendenciasChanged(bool value) => Filtrar();

    private void Filtrar()
    {
        Linhas.Clear();
        foreach (var n in _todas.Where(n => !SomentePendencias || n.TemAlerta)) Linhas.Add(new NotaMonitorLinha(n));
    }

    [RelayCommand]
    public Task AtualizarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var ini = Ext.Dia(Inicio);
        var fim = Fim is null ? DateOnly.FromDateTime(DateTime.Today) : Ext.Dia(Fim);
        var op = Operacao switch { 1 => TipoOperacao.Entrada, 2 => (TipoOperacao?)TipoOperacao.Saida, _ => null };
        var fiscal = ServicosFiscais.De(app);
        var docs = await fiscal.Documentos.ConsultarAsync(empresa, new FiltroDocumentos(ini, fim, op, Limite: 20000), usuario);
        if (!docs.Sucesso) { MostrarResultado(docs, ""); return; }
        var dups = await fiscal.Duplicatas.ConsultarAsync(empresa, new FiltroDuplicatas(Limite: 100000), usuario);
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var (notas, r) = Monitores.Notas(docs.Valor, dups.Sucesso ? dups.Valor : [], hoje);
        _todas = notas;
        Filtrar();
        Indicadores.Clear();
        foreach (var l in new LinhaPainel[]
        {
            new("Notas no período", $"{r.Total} ({r.Entradas} entradas, {r.Saidas} saídas)"),
            new("Vendas (saídas autorizadas)", Formato.Moeda(r.ValorSaidas)), new("Compras (entradas autorizadas)", Formato.Moeda(r.ValorEntradas)),
            new("Canceladas ou denegadas", r.Canceladas.ToString()), new("Não contabilizadas", r.NaoContabilizadas.ToString()),
            new("Sem duplicatas", r.SemCobranca.ToString()), new("Com cobrança vencida", r.ComCobrancaVencida.ToString()),
        }) Indicadores.Add(l);
        AtualizadoEm = $"Atualizado às {DateTime.Now:HH:mm:ss}";
    }, Permissao.FiscalDocumentosConsultar);
}

public sealed record CobrancaMonitorLinha(CobrancaMonitorada C)
{
    public string Vencimento => C.Duplicata.Vencimento.ToString("dd/MM/yyyy");
    public string Cliente => C.Duplicata.ParticipanteNome ?? "";
    public string Documento => $"{C.Duplicata.NumeroDocumento}/{C.Duplicata.Parcela}";
    public string Saldo => Formato.Moeda(C.Duplicata.Saldo);
    public string Faixa => C.Faixa;
    public string Prazo => C.Dias < 0 ? $"{-C.Dias} dia(s) de atraso" : C.Dias == 0 ? "hoje" : $"em {C.Dias} dia(s)";
    public bool Vencida => C.Vencida;
}

/// <summary>Monitores › Monitor de cobranças: contas a receber em aberto por faixa de vencimento, com os maiores atrasos.</summary>
public partial class MonitorCobrancasViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public ObservableCollection<CobrancaMonitorLinha> Linhas { get; } = [];
    public ObservableCollection<LinhaPainel> Indicadores { get; } = [];
    public ObservableCollection<LinhaPainel> Atrasos { get; } = [];
    private IReadOnlyList<CobrancaMonitorada> _todas = [];

    [ObservableProperty] private bool _somenteVencidas;
    [ObservableProperty] private string _cliente = "";
    [ObservableProperty] private string _atualizadoEm = "";

    partial void OnSomenteVencidasChanged(bool value) => Filtrar();
    partial void OnClienteChanged(string value) => Filtrar();

    private void Filtrar()
    {
        var f = Cliente.Trim();
        Linhas.Clear();
        foreach (var c in _todas.Where(c => (!SomenteVencidas || c.Vencida) && (f.Length == 0 || (c.Duplicata.ParticipanteNome ?? "").Contains(f, StringComparison.CurrentCultureIgnoreCase))))
            Linhas.Add(new CobrancaMonitorLinha(c));
    }

    [RelayCommand]
    public Task AtualizarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var dups = await ServicosFiscais.De(app).Duplicatas.ConsultarAsync(empresa, new FiltroDuplicatas(TipoDuplicata.Receber, SomenteAbertas: true, Limite: 100000), usuario);
        if (!dups.Sucesso) { MostrarResultado(dups, ""); return; }
        var (cobrancas, r) = Monitores.Cobrancas(dups.Valor, DateOnly.FromDateTime(DateTime.Today));
        _todas = cobrancas;
        Filtrar();
        Indicadores.Clear();
        foreach (var l in new LinhaPainel[]
        {
            new("A receber em aberto", $"{Formato.Moeda(r.Aberto)} ({r.Titulos} título(s), {r.Clientes} cliente(s))"),
            new("Vencido", Formato.Moeda(r.Vencido)), new("Vence hoje", Formato.Moeda(r.VenceHoje)),
            new("Vence nos próximos 7 dias", Formato.Moeda(r.Proximos7Dias)), new("A vencer depois", Formato.Moeda(r.Depois)),
        }) Indicadores.Add(l);
        Atrasos.Clear();
        foreach (var (c, v) in r.MaioresAtrasos) Atrasos.Add(new LinhaPainel(c, Formato.Moeda(v)));
        AtualizadoEm = $"Atualizado às {DateTime.Now:HH:mm:ss}";
    }, Permissao.FiscalDocumentosConsultar);
}
