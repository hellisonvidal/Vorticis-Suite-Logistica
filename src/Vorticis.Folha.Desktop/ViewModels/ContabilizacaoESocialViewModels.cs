using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Folha.Servicos;

namespace Vorticis.Folha.Desktop.ViewModels;

public sealed record RegraLinha(RegraContabil R)
{
    public string Chave => R.Chave;
    public string Descricao => R.Descricao;
    public string Debito => R.ContaDebito;
    public string Credito => R.ContaCredito;
    public string CentroCusto => R.UsaCentroCusto ? (R.LadoCentroCusto == 'D' ? "no débito" : "no crédito") : "";
}

public sealed record ContabilizacaoLinha(ContabilizacaoRegistro C)
{
    public string Competencia => C.Competencia.ToString("MM/yyyy");
    public string Tipo => C.Tipo == "FOLHA" ? "Folha" : "Provisões";
    public string Lancamento => C.NumeroLancamento?.ToString() ?? "";
    public string Valor => Formato.Moeda(C.ValorTotal);
    public string Criada => C.CriadaEm.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public string Situacao => C.DesfeitaEm is { } d ? $"Desfeita em {d.ToLocalTime():dd/MM/yyyy HH:mm}" : "Ativa";
}

/// <summary>
/// Cálculos › Folha › Contabilização: regras rubrica → conta de débito/crédito (centro de custo opcional), prévia, lançamento
/// automático da folha (competência fechada) e das provisões no módulo contábil, e desfazer (estorno).
/// </summary>
public partial class ContabilizacaoFolhaViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public ObservableCollection<RegraLinha> Regras { get; } = [];
    public ObservableCollection<string> Previa { get; } = [];
    public ObservableCollection<ContabilizacaoLinha> Contabilizacoes { get; } = [];
    public static IReadOnlyList<Opcao> Lados { get; } = [new("D", "Partida de débito"), new("C", "Partida de crédito")];

    [ObservableProperty] private DateTime? _competencia = FolhaExtensoes.Mes(contexto.PeriodoTrabalho);
    [ObservableProperty] private RegraLinha? _regraSelecionada;
    [ObservableProperty] private ContabilizacaoLinha? _contabilizacaoSelecionada;
    [ObservableProperty] private string _chave = "";
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private string _contaDebito = "";
    [ObservableProperty] private string _contaCredito = "";
    [ObservableProperty] private bool _usaCentroCusto;
    [ObservableProperty] private Opcao? _lado = Lados[0];
    [ObservableProperty] private bool _comProvisoes = true;
    [ObservableProperty] private string _totalPrevia = "";

    public bool PodeParametros => Contexto.Pode(Permissao.FolhaParametrosManter);
    public bool PodeContabilizar => Contexto.Pode(Permissao.FolhaContabilizar);
    public void AtualizarPermissoes() { OnPropertyChanged(nameof(PodeParametros)); OnPropertyChanged(nameof(PodeContabilizar)); }

    private DateOnly Comp => FolhaExtensoes.InicioMes(Competencia);

    partial void OnRegraSelecionadaChanged(RegraLinha? value)
    {
        if (value is null) return;
        (Chave, Descricao, ContaDebito, ContaCredito, UsaCentroCusto) = (value.R.Chave, value.R.Descricao, value.R.ContaDebito, value.R.ContaCredito, value.R.UsaCentroCusto);
        Lado = Lados.First(l => l.Codigo[0] == value.R.LadoCentroCusto);
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var s = app.Folha().Contabilizacao;
        var regras = await s.ListarRegrasAsync(empresa, sessao.Id);
        var registros = await s.ListarAsync(empresa, null, sessao.Id);
        if (!EhAtual(g)) return;
        var chave = RegraSelecionada?.R.Chave;
        Regras.Clear(); foreach (var r in regras) Regras.Add(new RegraLinha(r));
        RegraSelecionada = Regras.FirstOrDefault(r => r.R.Chave == chave);
        Contabilizacoes.Clear(); foreach (var c in registros) Contabilizacoes.Add(new ContabilizacaoLinha(c));
    }

    [RelayCommand]
    private Task GerarSugeridasAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Folha().Contabilizacao.GerarRegrasSugeridasAsync(empresa, ComProvisoes, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = r.Valor == 0 ? "Todas as regras sugeridas já existem." : $"{r.Valor} regra(s) criada(s) sobre o plano de contas modelo; confira as contas com o plano da empresa.";
    }, Permissao.FolhaParametrosManter);

    [RelayCommand]
    private Task GravarRegraAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Folha().Contabilizacao.GravarRegraAsync(empresa, new RegraContabil(0, Chave, Descricao, ContaDebito, ContaCredito, UsaCentroCusto, (Lado?.Codigo ?? "D")[0]), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = "Regra gravada.";
    }, Permissao.FolhaParametrosManter);

    [RelayCommand]
    private Task ExcluirRegraAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (RegraSelecionada is null) { Erros.Add("Selecione uma regra."); return; }
        var r = await app.Folha().Contabilizacao.ExcluirRegraAsync(empresa, RegraSelecionada.R.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        RegraSelecionada = null;
        await CarregarAsync();
        Mensagem = "Regra excluída.";
    }, Permissao.FolhaParametrosManter);

    private Task PreviaDeAsync(string tipo) => ExecutarAsync(async (app, empresa, usuario) =>
    {
        Previa.Clear();
        TotalPrevia = "";
        var r = await app.Folha().Contabilizacao.PreviaAsync(empresa, Comp, tipo, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        foreach (var l in r.Valor.Linhas) Previa.Add(l);
        TotalPrevia = $"Lançamento de {r.Valor.Proposta.Data:dd/MM/yyyy}: {r.Valor.Proposta.Partidas.Count} partidas, total {Formato.Moeda(r.Valor.Total)}";
    }, Permissao.FolhaContabilizar);

    [RelayCommand] private Task PreviaFolhaAsync() => PreviaDeAsync(ServicoContabilizacaoFolha.TipoFolhaMensal);
    [RelayCommand] private Task PreviaProvisoesAsync() => PreviaDeAsync(ServicoContabilizacaoFolha.TipoProvisao);

    private Task ContabilizarDeAsync(string tipo) => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Folha().Contabilizacao.ContabilizarAsync(empresa, Comp, tipo, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        if (r.Valor.Numero == 0) { Mensagem = "Contabilização em lote: a pendência foi registrada. Gere o lançamento em Integrações › Exportar › Contabilização em lote."; return; }
        Mensagem = $"Contabilizado: lançamento nº {r.Valor.Numero} em {Comp.AddMonths(1).AddDays(-1):dd/MM/yyyy}.";
    }, Permissao.FolhaContabilizar);

    [RelayCommand] private Task ContabilizarFolhaAsync() => ContabilizarDeAsync(ServicoContabilizacaoFolha.TipoFolhaMensal);
    [RelayCommand] private Task ContabilizarProvisoesAsync() => ContabilizarDeAsync(ServicoContabilizacaoFolha.TipoProvisao);

    [RelayCommand]
    private async Task DesfazerAsync()
    {
        if (ContabilizacaoSelecionada is not { C.DesfeitaEm: null } sel) { Erros.Clear(); Erros.Add("Selecione uma contabilização ativa."); return; }
        if (!await Contexto.ConfirmarAsync("Desfazer contabilização",
                $"Estornar o lançamento nº {sel.Lancamento} ({sel.Tipo} {sel.Competencia})? Será gerado um lançamento de estorno.", "Desfazer", "Cancelar")) return;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            var r = await app.Folha().Contabilizacao.DesfazerAsync(empresa, sel.C.Competencia, sel.C.Tipo, usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            await CarregarAsync();
            Mensagem = "Contabilização desfeita (lançamento estornado).";
        }, Permissao.FolhaContabilizar);
    }
}

public sealed record EventoEsocialLinha(EventoEsocial E)
{
    public string Tipo => E.Tipo;
    public string Referencia => E.Referencia;
    public string Id => E.IdEvento;
    public string Status => E.Status switch
    {
        "EXPORTADO" => $"Exportado {E.ExportadoEm?.ToLocalTime():dd/MM/yyyy HH:mm}", "ASSINADO" => "Assinado", "ENVIADO" => "Enviado", "PROCESSADO" => "Processado",
        "ERRO" => "Erro/rejeitado", _ => "Gerado",
    };
    public string Gerado => E.GeradoEm.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
}

public sealed partial class TipoEsocialOpcao(string tipo, string descricao, bool periodico) : ObservableObject
{
    public string Tipo { get; } = tipo;
    public string Texto { get; } = $"{tipo} – {descricao}";
    public bool Periodico { get; } = periodico;
    [ObservableProperty] private bool _marcado = true;
}

/// <summary>Folha › Obrigações › eSocial: gera os XMLs do leiaute S-1.3 por competência e acompanha o status (gerado/exportado). Sem assinatura nem transmissão.</summary>
public partial class ESocialViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public ObservableCollection<TipoEsocialOpcao> Tipos { get; } =
        new(EventosEsocial.Todos.Select(t => new TipoEsocialOpcao(t.Tipo, t.Descricao, t.Periodico)));
    public ObservableCollection<EventoEsocialLinha> Eventos { get; } = [];

    [ObservableProperty] private DateTime? _competencia = FolhaExtensoes.Mes(contexto.PeriodoTrabalho);
    [ObservableProperty] private EventoEsocialLinha? _selecionado;
    [ObservableProperty] private string _xml = "";
    [ObservableProperty] private string _pasta = "";

    public bool PodeUsar => Contexto.Pode(Permissao.FolhaESocial);
    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeUsar));

    private DateOnly Comp => FolhaExtensoes.InicioMes(Competencia);

    partial void OnCompetenciaChanged(DateTime? value) { Pasta = PastaPadrao(); _ = CarregarAsync(); }
    partial void OnSelecionadoChanged(EventoEsocialLinha? value) => Xml = value?.E.Xml ?? "";

    private string PastaPadrao() => Path.Combine(Path.GetDirectoryName(ArquivosGerados.Pasta.TrimEnd(Path.DirectorySeparatorChar)) ?? ArquivosGerados.Pasta, "eSocial", $"{Comp:yyyy-MM}");

    public async Task CarregarAsync()
    {
        if (string.IsNullOrWhiteSpace(Pasta)) Pasta = PastaPadrao();
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var lista = await app.Folha().ESocial.ListarAsync(empresa, Comp, sessao.Id);
        if (!EhAtual(g)) return;
        var id = Selecionado?.E.Id;
        Eventos.Clear(); foreach (var e in lista) Eventos.Add(new EventoEsocialLinha(e));
        Selecionado = Eventos.FirstOrDefault(e => e.E.Id == id);
    }

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var tipos = Tipos.Where(t => t.Marcado).Select(t => t.Tipo).ToArray();
        var r = await app.Folha().ESocial.GerarAsync(empresa, Comp, tipos, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        foreach (var a in r.Valor.Avisos) Erros.Add(a);
        Mensagem = $"{r.Valor.Gerados} evento(s) gerado(s)" + (r.Valor.Ignorados > 0 ? $"; {r.Valor.Ignorados} já exportado(s) mantido(s)." : ".");
    }, Permissao.FolhaESocial);

    private async Task ExportarAsync(bool todos)
    {
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            var ids = todos ? Eventos.Select(e => e.E.Id).ToArray() : Selecionado is { } s ? [s.E.Id] : [];
            var r = await app.Folha().ESocial.ExportarAsync(empresa, Comp, ids, string.IsNullOrWhiteSpace(Pasta) ? PastaPadrao() : Pasta, usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            await CarregarAsync();
            Mensagem = $"{r.Valor.Count} arquivo(s) XML gravado(s) em {Path.GetDirectoryName(r.Valor[0])}. Para assinar e transmitir pelo sistema, use Obrigações › eSocial › Transmissão.";
        }, Permissao.FolhaESocial);
    }

    [RelayCommand] private Task ExportarSelecionadoAsync() => ExportarAsync(false);
    [RelayCommand] private Task ExportarTodosAsync() => ExportarAsync(true);
}
