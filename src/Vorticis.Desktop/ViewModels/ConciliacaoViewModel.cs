using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Conciliacao;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Desktop.ViewModels;

public partial class PartidaConciliacaoLinha(PartidaConciliavel p) : ObservableObject
{
    public PartidaConciliavel Partida { get; } = p;
    public string Data => Partida.Data.ToString("dd/MM/yyyy");
    public long Numero => Partida.Numero;
    public string Conta => Partida.ContaCodigo is null ? "" : $"{Partida.ContaCodigo} {Partida.ContaDescricao}";
    public string Documento => Partida.Documento ?? "";
    public string Debito => Partida.DC == IndicadorDC.D ? Formato.Moeda(Partida.Valor) : "";
    public string Credito => Partida.DC == IndicadorDC.C ? Formato.Moeda(Partida.Valor) : "";
    public string Historico => Partida.Historico;
    public string Situacao => Partida.ConciliacaoId is { } c ? $"Conciliada (#{c})" : "Em aberto";
    [ObservableProperty] private bool _selecionada;
}

/// <summary>
/// Lançamentos › Conciliações: conciliação de contas automática pelo nº do documento ou manual. A lista traz todos os
/// lançamentos que têm documento (conciliados ou não, casando ou não), de todas as contas ou só da conta escolhida.
/// </summary>
public partial class ConciliacaoViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    private IReadOnlyList<PartidaConciliavel> _partidas = [];
    private IReadOnlyList<ContaSelecionavel> _todasContas = [];

    public ObservableCollection<PartidaConciliacaoLinha> Linhas { get; } = [];
    public ObservableCollection<ContaSelecionavel> Contas { get; } = [];
    [ObservableProperty] private string _conta = "";
    [ObservableProperty] private string _nomeConta = "";
    [ObservableProperty] private DateTime? _ate = DateTime.Today;
    [ObservableProperty] private bool _mostrarConciliadas = true;
    [ObservableProperty] private bool _somenteComDocumento = true;
    [ObservableProperty] private PartidaConciliacaoLinha? _linhaSelecionada;
    [ObservableProperty] private string _resumo = "";
    [ObservableProperty] private string _filtroConta = "";
    [ObservableProperty] private ContaSelecionavel? _contaSelecionada;

    public async Task CarregarContasAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var plano = await app.PlanoContas.ListarAsync(empresa, sessao.Id);
        _todasContas = plano.Where(c => c.Analitica).Select(c => new ContaSelecionavel(c.Codigo, c.Descricao, c.CodigoReduzido)).ToList();
        FiltrarContas();
        if (Linhas.Count == 0) await ConsultarAsync();   // já abre com todos os lançamentos que têm documento
    }

    partial void OnFiltroContaChanged(string value) => FiltrarContas();

    private void FiltrarContas()
    {
        Contas.Clear();
        foreach (var c in _todasContas.Where(c => LancamentoViewModel.Combina(c, FiltroConta))) Contas.Add(c);
    }

    partial void OnContaSelecionadaChanged(ContaSelecionavel? value)
    {
        if (value is null) return;
        Conta = value.Codigo;
        _ = ConsultarCommand.ExecuteAsync(null);
    }

    partial void OnMostrarConciliadasChanged(bool value) => Exibir();
    partial void OnSomenteComDocumentoChanged(bool value) => _ = ConsultarCommand.ExecuteAsync(null);

    private DateOnly Limite => Data(Ate, DateOnly.FromDateTime(DateTime.Today));

    [RelayCommand]
    private Task ConsultarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var conta = Conta.Trim();
        if (!SomenteComDocumento && conta.Length == 0) { Erros.Add("Informe a conta ou marque \"Somente com documento\" para ver todas as contas."); return; }
        var r = SomenteComDocumento ? await app.Conciliacao.ListarComDocumentoAsync(empresa, conta, Limite, usuario)
                                    : await app.Conciliacao.ListarAsync(empresa, conta, Limite, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); _partidas = []; Exibir(); return; }
        var c = _todasContas.FirstOrDefault(x => x.Codigo == conta || x.Reduzido?.ToString() == conta);
        NomeConta = c is null ? "Todas as contas" : $"{c.Codigo} – {c.Descricao}";
        _partidas = r.Valor;
        Exibir();
    }, Permissao.LancamentoIncluir);

    private void Exibir()
    {
        Linhas.Clear();
        foreach (var p in _partidas.Where(p => MostrarConciliadas || !p.Conciliada)) Linhas.Add(new PartidaConciliacaoLinha(p));
        var abertas = _partidas.Where(p => !p.Conciliada).ToList();
        var saldo = abertas.Sum(p => p.ValorComSinal);
        var docs = abertas.Where(p => !string.IsNullOrWhiteSpace(p.Documento)).Select(p => ServicoConciliacao.Normalizar(p.Documento!)).Distinct().Count();
        Resumo = _partidas.Count == 0 ? "" :
            $"{_partidas.Count} partida(s) listada(s) · {abertas.Count} em aberto ({docs} documento(s)) · saldo em aberto {LinhaBalancete.Formatar(saldo)} · " +
            $"{_partidas.Count - abertas.Count} conciliada(s)";
    }

    [RelayCommand]
    private Task ConciliarAutomaticoAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Conciliacao.ConciliarAutomaticoAsync(empresa, Conta, Limite, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await ConsultarAsync();
        Mensagem = r.Valor.Conciliacoes == 0
            ? "Nenhum documento fechou (débitos = créditos) para conciliar automaticamente."
            : $"{r.Valor.Conciliacoes} documento(s) conciliado(s) automaticamente ({r.Valor.Partidas} partidas)" +
              (r.Valor.DocumentosEmAberto > 0 ? $"; {r.Valor.DocumentosEmAberto} documento(s) seguem em aberto." : ".");
    }, Permissao.LancamentoIncluir);

    [RelayCommand]
    private Task ConciliarSelecionadasAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var ids = Linhas.Where(l => l.Selecionada).Select(l => l.Partida.PartidaId).ToList();
        var r = await app.Conciliacao.ConciliarManualAsync(empresa, Conta, ids, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await ConsultarAsync();
        Mensagem = $"{ids.Count} partida(s) conciliada(s) (conciliação #{r.Valor}).";
    }, Permissao.LancamentoIncluir);

    [RelayCommand]
    private Task DesfazerAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (LinhaSelecionada?.Partida.ConciliacaoId is not { } id) { Erros.Add("Selecione uma partida conciliada para desfazer a conciliação."); return; }
        var r = await app.Conciliacao.DesfazerAsync(empresa, id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await ConsultarAsync();
        Mensagem = $"Conciliação #{id} desfeita.";
    }, Permissao.LancamentoIncluir);
}
