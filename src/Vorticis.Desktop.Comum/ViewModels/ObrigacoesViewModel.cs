using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Obrigacoes;
using Vorticis.Application.Relatorios;
using Vorticis.Domain.Obrigacoes;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Desktop.Comum.ViewModels;

/// <summary>Linha do calendário: vencimento, situação e cores de destaque (vencidas em vermelho, próximas 7 dias em âmbar).</summary>
public sealed record LinhaObrigacao(ItemObrigacao Item)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    public OcorrenciaObrigacao Ocorrencia => Item.Ocorrencia;
    public string Vencimento => $"{Item.Ocorrencia.Vencimento:dd/MM/yyyy} ({Item.Ocorrencia.Vencimento.ToString("ddd", PtBr)})";
    public string Nome => Item.Ocorrencia.Definicao.Nome;
    public string Orgao => Item.Ocorrencia.Definicao.Orgao;
    public string Modulo => Item.Ocorrencia.Definicao.Personalizada ? $"Personalizada ({Item.Ocorrencia.Definicao.Esfera.ToLowerInvariant()})"
        : Item.Ocorrencia.Definicao.Modulo == ModuloObrigacao.Contabil ? "Contábil" : Item.Ocorrencia.Definicao.Modulo.ToString();
    public string Competencia => Item.Ocorrencia.CompetenciaTexto;
    public string Regra => Item.Ocorrencia.Definicao.RegraTexto;
    public string Situacao => Item.Status switch
    {
        StatusObrigacao.Cumprida => $"Cumprida em {Item.CumpridaEm:dd/MM/yyyy}",
        StatusObrigacao.Atrasada => $"Atrasada ({-Item.DiasParaVencer} dia{(Item.DiasParaVencer == -1 ? "" : "s")})",
        _ => Item.DiasParaVencer == 0 ? "Vence hoje" : Item.DiasParaVencer == 1 ? "Vence amanhã" : Item.DiasParaVencer <= 7 ? $"Vence em {Item.DiasParaVencer} dias" : "Pendente",
    };
    public string Observacao => Item.Observacao ?? Item.Ocorrencia.Definicao.Descricao ?? "";
    public IBrush Fundo => Item.Status == StatusObrigacao.Atrasada ? new SolidColorBrush(Color.Parse("#FDE2E2"))
        : Item.Vencendo7Dias ? new SolidColorBrush(Color.Parse("#FFF1CC")) : Item.Status == StatusObrigacao.Cumprida ? new SolidColorBrush(Color.Parse("#E3F4E8")) : Brushes.Transparent;
    public IBrush Texto => Item.Status == StatusObrigacao.Atrasada ? new SolidColorBrush(Color.Parse("#9B1C1C"))
        : Item.Vencendo7Dias ? new SolidColorBrush(Color.Parse("#8A5A00")) : Item.Status == StatusObrigacao.Cumprida ? new SolidColorBrush(Color.Parse("#17623A")) : new SolidColorBrush(Color.Parse("#2B3445"));
}

/// <summary>
/// Obrigações › Calendário de obrigações da empresa ativa: lista ou visão mensal, com vencimento calculado, situação (pendente, cumprida,
/// atrasada), baixa com data e observação, obrigações personalizadas e exportação (PDF/Excel/CSV) pela barra comum.
/// </summary>
public partial class ObrigacoesViewModel(MainViewModelBase main) : RelatorioViewModel(main)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static IReadOnlyList<string> Visoes { get; } = ["Visão mensal", "Lista (atrasadas e próximos 90 dias)"];
    public static IReadOnlyList<string> Esferas { get; } = ["Federal", "Estadual", "Municipal", "Outra"];
    public static IReadOnlyList<string> Periodicidades { get; } = ["Mensal", "Trimestral", "Anual"];
    public static IReadOnlyList<string> Regras { get; } = ["Dia fixo", "N-ésimo dia útil", "Último dia útil"];
    public static IReadOnlyList<string> Ajustes { get; } = ["Antecipa para o dia útil anterior", "Posterga para o próximo dia útil", "Mantém a data"];

    public ObservableCollection<LinhaObrigacao> Linhas { get; } = [];
    public ObservableCollection<ObrigacaoPersonalizada> Personalizadas { get; } = [];

    [ObservableProperty] private int _visao;
    [ObservableProperty] private DateOnly _mes = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    // filtros de módulo: por padrão só o módulo atual
    [ObservableProperty] private bool _verContabil = ModuloDe(main) == ModuloObrigacao.Contabil;
    [ObservableProperty] private bool _verFiscal = ModuloDe(main) == ModuloObrigacao.Fiscal;
    [ObservableProperty] private bool _verFolha = ModuloDe(main) == ModuloObrigacao.Folha;
    [ObservableProperty] private bool _ocultarCumpridas;
    [ObservableProperty] private LinhaObrigacao? _selecionada;
    [ObservableProperty] private DateTime? _dataCumprimento = DateTime.Today;
    [ObservableProperty] private string _observacaoBaixa = "";
    [ObservableProperty] private string _resumo = "";

    // formulário de obrigação personalizada
    [ObservableProperty] private ObrigacaoPersonalizada? _personalizadaSelecionada;
    [ObservableProperty] private long _pId;
    [ObservableProperty] private string _pNome = "";
    [ObservableProperty] private string _pOrgao = "";
    [ObservableProperty] private int _pEsfera = 2;
    [ObservableProperty] private int _pPeriodicidade;
    [ObservableProperty] private int _pRegra;
    [ObservableProperty] private decimal? _pDia = 10;
    [ObservableProperty] private decimal? _pMesesApos = 1;
    [ObservableProperty] private decimal? _pMesVencimento = 1;
    [ObservableProperty] private decimal? _pAnosApos = 0;
    [ObservableProperty] private int _pAjuste = 1;
    [ObservableProperty] private string _pObservacao = "";

    public bool PodeManter => Contexto.Pode(Permissao.ObrigacoesManter);
    public string TituloMes => PtBr.TextInfo.ToTitleCase(Mes.ToString("MMMM 'de' yyyy", PtBr));
    public bool VisaoMensal => Visao == 0;
    public bool PEhAnual => PPeriodicidade == 2;
    public bool PEhDiaFixo => PRegra == 0;
    public bool PUsaDia => PRegra != 2;
    public string NomeModulo => main.Modulo.NomeCurto;

    /// <summary>Módulo do casco em execução (null se desconhecido).</summary>
    public static ModuloObrigacao? ModuloDe(MainViewModelBase m) => m.Modulo.NomeCurto.ToLowerInvariant() switch
    {
        var n when n.StartsWith("cont") => ModuloObrigacao.Contabil,
        var n when n.StartsWith("fisc") => ModuloObrigacao.Fiscal,
        var n when n.StartsWith("folha") => ModuloObrigacao.Folha,
        _ => null,
    };

    /// <summary>Módulos marcados; null (sem filtro) quando nenhum ou módulo desconhecido e nada marcado.</summary>
    private IReadOnlyList<ModuloObrigacao>? ModulosSelecionados
    {
        get
        {
            var l = new List<ModuloObrigacao>();
            if (VerContabil) l.Add(ModuloObrigacao.Contabil);
            if (VerFiscal) l.Add(ModuloObrigacao.Fiscal);
            if (VerFolha) l.Add(ModuloObrigacao.Folha);
            return l.Count == 3 || (l.Count == 0 && ModuloDe(main) is null) ? null : l;
        }
    }

    private string TextoModulos => ModulosSelecionados is not { } l ? "todos os módulos"
        : l.Count == 0 ? "nenhum módulo (somente personalizadas)" : "módulo(s) " + string.Join(", ", l.Select(m => m == ModuloObrigacao.Contabil ? "Contábil" : m.ToString()));

    protected override string TituloRelatorio => "Calendário de obrigações";
    protected override string Parametros => $"{(VisaoMensal ? $"Vencimentos em {TituloMes}" : "Atrasadas e próximos 90 dias")} — {TextoModulos}";
    protected override string NomeArquivo => $"calendario_obrigacoes_{(VisaoMensal ? Mes.ToString("yyyy-MM") : DateTime.Today.ToString("yyyy-MM-dd"))}";
    protected override bool Paisagem => true;

    protected override IReadOnlyList<TabelaRelatorio> Tabelas() =>
    [
        new TabelaRelatorio("Obrigações", [new("Vencimento", TipoColuna.Data), new("Obrigação", TipoColuna.Texto, 6), new("Órgão"), new("Módulo"),
                new("Competência"), new("Situação"), new("Cumprida em", TipoColuna.Data), new("Observação", TipoColuna.Texto, 5)],
            Linhas.Select(l => new LinhaRelatorio([l.Ocorrencia.Vencimento, l.Nome, l.Orgao, l.Modulo, l.Competencia, l.Situacao, l.Item.CumpridaEm, l.Observacao],
                l.Item.Status == StatusObrigacao.Atrasada)).ToList(), Resumo),
    ];

    public void AtualizarPermissoes()
    {
        AtualizarPermissoesExportacao();
        OnPropertyChanged(nameof(PodeManter));
        OnPropertyChanged(nameof(NomeModulo));
    }

    partial void OnVisaoChanged(int value) { OnPropertyChanged(nameof(VisaoMensal)); _ = CarregarAsync(); }
    partial void OnMesChanged(DateOnly value) { OnPropertyChanged(nameof(TituloMes)); _ = CarregarAsync(); }
    partial void OnVerContabilChanged(bool value) => _ = CarregarAsync();
    partial void OnVerFiscalChanged(bool value) => _ = CarregarAsync();
    partial void OnVerFolhaChanged(bool value) => _ = CarregarAsync();
    partial void OnOcultarCumpridasChanged(bool value) => _ = CarregarAsync();
    partial void OnPPeriodicidadeChanged(int value) => OnPropertyChanged(nameof(PEhAnual));
    partial void OnPRegraChanged(int value) { OnPropertyChanged(nameof(PEhDiaFixo)); OnPropertyChanged(nameof(PUsaDia)); }

    partial void OnSelecionadaChanged(LinhaObrigacao? value)
    {
        DataCumprimento = value?.Item.CumpridaEm?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today;
        ObservacaoBaixa = value?.Item.Status == StatusObrigacao.Cumprida ? value.Item.Observacao ?? "" : "";
    }

    partial void OnPersonalizadaSelecionadaChanged(ObrigacaoPersonalizada? value)
    {
        if (value is not { } o) return;
        (PId, PNome, POrgao, PObservacao) = (o.Id, o.Nome, o.Orgao, o.Observacao ?? "");
        PEsfera = Math.Max(0, Esferas.ToList().FindIndex(e => e.Equals(o.Esfera, StringComparison.OrdinalIgnoreCase)));
        (PPeriodicidade, PRegra, PAjuste) = ((int)o.Periodicidade, (int)o.Regra, (int)o.Ajuste switch { 0 => 0, 1 => 1, _ => 2 });
        (PDia, PMesesApos, PMesVencimento, PAnosApos) = (o.Dia, o.MesesApos, o.MesVencimento, o.AnosApos);
    }

    [RelayCommand] private void MesAnterior() => Mes = Mes.AddMonths(-1);
    [RelayCommand] private void MesSeguinte() => Mes = Mes.AddMonths(1);
    [RelayCommand] private void MesAtual() => Mes = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);

    public async Task CarregarAsync()
    {
        AtualizarPermissoes();
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var (de, ate) = VisaoMensal ? (Mes, Mes.AddMonths(1).AddDays(-1)) : (hoje.AddDays(-180), hoje.AddDays(90));
        try
        {
            var r = await app.Obrigacoes.ListarAsync(sessao.Id, empresa, de, ate, hoje, ModulosSelecionados);
            var p = await app.Obrigacoes.ListarPersonalizadasAsync(sessao.Id, empresa);
            if (!EhAtual(g)) return;
            Erros.Clear();
            if (!r.Sucesso) { foreach (var e in r.Erros) Erros.Add(e); Linhas.Clear(); TemDados = false; return; }
            var itens = r.Valor.Where(i => !(OcultarCumpridas && i.Status == StatusObrigacao.Cumprida) && (VisaoMensal || i.Status != StatusObrigacao.Cumprida)).ToList();
            var chave = Selecionada is { } s ? (s.Ocorrencia.Definicao.Codigo, s.Ocorrencia.Competencia) : default;
            Linhas.Clear();
            foreach (var i in itens) Linhas.Add(new LinhaObrigacao(i));
            Selecionada = Linhas.FirstOrDefault(l => (l.Ocorrencia.Definicao.Codigo, l.Ocorrencia.Competencia) == chave);
            if (p.Sucesso)
            {
                var sel = PersonalizadaSelecionada?.Id;
                Personalizadas.Clear();
                foreach (var o in p.Valor) Personalizadas.Add(o);
                PersonalizadaSelecionada = Personalizadas.FirstOrDefault(o => o.Id == sel);
            }
            var atrasadas = itens.Count(i => i.Status == StatusObrigacao.Atrasada);
            var proximas = itens.Count(i => i.Vencendo7Dias);
            Resumo = $"{itens.Count} obrigação(ões) — {atrasadas} atrasada(s), {proximas} vencendo nos próximos 7 dias, {itens.Count(i => i.Status == StatusObrigacao.Cumprida)} cumprida(s).";
            TemDados = Linhas.Count > 0;
            AtualizarPermissoesExportacao();
        }
        catch (Exception ex)
        {
            Erros.Add($"Não foi possível carregar o calendário: {ex.Message}");
        }
    }

    [RelayCommand]
    private Task MarcarCumpridaAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada is not { } l) { Erros.Add("Selecione uma obrigação."); return; }
        if (DataCumprimento is not { } d) { Erros.Add("Informe a data do cumprimento."); return; }
        var r = await app.Obrigacoes.MarcarCumpridaAsync(usuario, empresa, l.Ocorrencia.Definicao.Codigo, l.Ocorrencia.Competencia.Chave, DateOnly.FromDateTime(d), ObservacaoBaixa);
        MostrarResultado(r, "Obrigação marcada como cumprida.");
        if (r.Sucesso) { await CarregarAsync(); main.Inicio.Atualizar(); }
    }, Permissao.ObrigacoesManter);

    [RelayCommand]
    private Task DesfazerAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada is not { } l) { Erros.Add("Selecione uma obrigação."); return; }
        var r = await app.Obrigacoes.DesfazerCumprimentoAsync(usuario, empresa, l.Ocorrencia.Definicao.Codigo, l.Ocorrencia.Competencia.Chave);
        MostrarResultado(r, "Cumprimento desfeito.");
        if (r.Sucesso) { await CarregarAsync(); main.Inicio.Atualizar(); }
    }, Permissao.ObrigacoesManter);

    [RelayCommand]
    private void NovaPersonalizada()
    {
        PersonalizadaSelecionada = null;
        (PId, PNome, POrgao, PObservacao) = (0, "", "", "");
        (PEsfera, PPeriodicidade, PRegra, PAjuste, PDia, PMesesApos, PMesVencimento, PAnosApos) = (2, 0, 0, 1, 10, 1, 1, 0);
    }

    [RelayCommand]
    private Task SalvarPersonalizadaAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var o = new ObrigacaoPersonalizada(PId, PNome, POrgao, Esferas[PEsfera], (Periodicidade)PPeriodicidade, (TipoRegra)PRegra, (int)(PDia ?? 0),
            (int)(PMesesApos ?? 0), (int)(PMesVencimento ?? 1), (int)(PAnosApos ?? 0), PAjuste switch { 0 => AjusteNaoUtil.Antecipa, 1 => AjusteNaoUtil.Posterga, _ => AjusteNaoUtil.Nenhum },
            PObservacao);
        var r = await app.Obrigacoes.SalvarPersonalizadaAsync(usuario, empresa, o);
        MostrarResultado(r, "Obrigação personalizada salva.");
        if (r.Sucesso) { PId = r.Valor; PersonalizadaSelecionada = null; await CarregarAsync(); PersonalizadaSelecionada = Personalizadas.FirstOrDefault(x => x.Id == r.Valor); }
    }, Permissao.ObrigacoesManter);

    [RelayCommand]
    private async Task ExcluirPersonalizadaAsync()
    {
        if (PersonalizadaSelecionada is not { } o) { Erros.Clear(); Erros.Add("Selecione a obrigação personalizada a excluir."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir obrigação", $"Excluir \"{o.Nome}\" e as baixas já registradas dela?")) return;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            var r = await app.Obrigacoes.ExcluirPersonalizadaAsync(usuario, empresa, o.Id);
            MostrarResultado(r, "Obrigação excluída.");
            if (r.Sucesso) { NovaPersonalizada(); await CarregarAsync(); }
        }, Permissao.ObrigacoesManter);
    }
}
