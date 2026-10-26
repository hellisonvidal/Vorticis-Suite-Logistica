using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Guias;
using Vorticis.Fiscal.Relatorios;
using Vorticis.Fiscal.Retencoes;

namespace Vorticis.Fiscal.Desktop.ViewModels;

/// <summary>Obrigações › Retenções: IRRF, CSRF (PIS, COFINS, CSLL), INSS e ISS, geradas das notas ou lançadas à mão; sofridas e efetuadas.</summary>
public partial class RetencoesViewModel(IContexto contexto) : RelatorioFiscalViewModel(contexto)
{
    public static IReadOnlyList<string> Sentidos { get; } = ["Todas", "Sofridas (retidas por clientes)", "Efetuadas (retidas de fornecedores)"];
    public static IReadOnlyList<string> Tributos { get; } = ["Todos", "IRRF", "PIS", "COFINS", "CSLL", "INSS", "ISS"];
    public static IReadOnlyList<string> SentidosNova { get; } = ["Efetuada (a recolher)", "Sofrida (a compensar)"];
    public static IReadOnlyList<string> TributosNova { get; } = ["IRRF", "PIS", "COFINS", "CSLL", "INSS", "ISS"];

    private (DateOnly Ini, DateOnly Fim) _periodo;
    private IReadOnlyList<Retencao> _lista = [];

    public ObservableCollection<RetencaoLinha> Linhas { get; } = [];

    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _fim = DateTime.Today;
    [ObservableProperty] private int _sentido;
    [ObservableProperty] private int _tributo;
    // Nova retenção manual
    [ObservableProperty] private DateTime? _novaCompetencia = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private int _novoSentido;
    [ObservableProperty] private int _novoTributo;
    [ObservableProperty] private string _novaBase = "";
    [ObservableProperty] private string _novoValor = "";
    [ObservableProperty] private string _novoCodigo = "";
    [ObservableProperty] private string _novaDescricao = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ExcluirCommand))] private RetencaoLinha? _selecionada;

    public bool PodeManter => Contexto.Pode(Permissao.FiscalRetencoesManter);

    protected override string TituloRelatorio => "Retenções de tributos";
    protected override string Parametros => $"Período: {_periodo.Ini:dd/MM/yyyy} a {_periodo.Fim:dd/MM/yyyy}";
    protected override string NomeArquivo => $"retencoes_{_periodo.Ini:yyyyMMdd}_{_periodo.Fim:yyyyMMdd}";

    public override void AtualizarPermissoes()
    {
        base.AtualizarPermissoes();
        OnPropertyChanged(nameof(PodeManter));
    }

    private (DateOnly Ini, DateOnly Fim) Periodo()
    {
        var ini = Data(Inicio, new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1));
        return (ini, Data(Fim, ini));
    }

    [RelayCommand]
    public Task ConsultarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var (ini, fim) = Periodo();
        var r = await app.Fiscal().Retencoes.ListarAsync(empresa, ini, fim, Sentido switch { 1 => SentidoRetencao.Sofrida, 2 => SentidoRetencao.Efetuada, _ => null },
            Tributo == 0 ? null : Enum.Parse<TributoRetido>(Tributos[Tributo]), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); Limpar(); return; }
        (_periodo, _lista) = ((ini, fim), r.Valor);
        Linhas.Clear();
        foreach (var x in r.Valor) Linhas.Add(new RetencaoLinha(x));
        Mostrar(TabelasFiscais.Retencoes(r.Valor));
        Mensagem = r.Valor.Count == 0 ? "Nenhuma retenção no período. Use Gerar das notas para extraí-las dos documentos importados." : null;
    }, Permissao.FiscalRelatorios);

    [RelayCommand]
    private Task GerarDasNotasAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var (ini, fim) = Periodo();
        var r = await app.Fiscal().Retencoes.GerarDasNotasAsync(empresa, ini, fim, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await ConsultarAsync();
        Mensagem = $"{r.Valor} retenção(ões) gerada(s) das notas do período (as lançadas à mão foram mantidas).";
    }, Permissao.FiscalRetencoesManter);

    [RelayCommand]
    private Task IncluirManualAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var valor = Numeros.LerValor(NovoValor);
        if (valor is null) { Erros.Add("Informe o valor retido."); return; }
        var baseCalculo = Numeros.LerValor(NovaBase) ?? 0m;
        var comp = Data(NovaCompetencia, new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1));
        var nova = new NovaRetencao(NovoSentido == 0 ? SentidoRetencao.Efetuada : SentidoRetencao.Sofrida, Enum.Parse<TributoRetido>(TributosNova[NovoTributo]), comp, null, null, baseCalculo,
            baseCalculo > 0 ? Math.Round(valor.Value / baseCalculo * 100m, 4) : 0m, valor.Value, NovoCodigo, "MANUAL", NovaDescricao);
        var r = await app.Fiscal().Retencoes.GravarManualAsync(empresa, nova, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        (NovaBase, NovoValor, NovoCodigo, NovaDescricao) = ("", "", "", "");
        await ConsultarAsync();
        Mensagem = "Retenção incluída.";
    }, Permissao.FiscalRetencoesManter);

    private bool TemSelecionada() => Selecionada is not null;

    [RelayCommand(CanExecute = nameof(TemSelecionada))]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada is not { } s) return;
        if (!await Contexto.ConfirmarAsync("Excluir retenção", $"Excluir a retenção de {s.Tributo} de {s.Valor}?", "Excluir", "Cancelar")) return;
        var r = await app.Fiscal().Retencoes.ExcluirAsync(empresa, s.Retencao.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await ConsultarAsync();
        Mensagem = "Retenção excluída.";
    }, Permissao.FiscalRetencoesManter);
}

public sealed record RetencaoLinha(Retencao Retencao)
{
    public string Competencia => Retencao.Competencia.ToString("MM/yyyy");
    public string Sentido => Retencao.Sentido == SentidoRetencao.Sofrida ? "Sofrida" : "Efetuada";
    public string Tributo => Retencao.Tributo.ToString();
    public string Nota => Retencao.DocumentoNumero ?? "";
    public string Participante => Retencao.ParticipanteNome ?? "";
    public string Valor => Numeros.Moeda(Retencao.Valor);
    public string Codigo => Retencao.CodigoReceita ?? "";
    public string Origem => Retencao.Origem == "NOTA" ? "Nota" : "Manual";
}

public sealed record GuiaLinha(Guia Guia)
{
    public string Vencimento => Guia.Vencimento.ToString("dd/MM/yyyy");
    public string Competencia => Guia.Competencia.ToString("MM/yyyy");
    public string Documento => Guia.RotuloDocumento;
    public string Descricao => Guia.Descricao;
    public string Codigo => Guia.CodigoReceita ?? "";
    public string Valor => Numeros.Moeda(Guia.Valor);
    public string Situacao => Guia.Paga ? $"Paga em {Guia.DataPagamento:dd/MM/yyyy}" : "A pagar";
    public string Observacao => Guia.Observacao ?? "";
}

/// <summary>Obrigações › Guias de recolhimento: DAS, DARF (com código de receita), GPS/DCTFWeb (informativo) e ISS, por competência e vencimento.</summary>
public partial class GuiasViewModel(IContexto contexto) : RelatorioFiscalViewModel(contexto)
{
    public static IReadOnlyList<string> Situacoes { get; } = ["Todas", "A pagar", "Pagas"];
    public static IReadOnlyList<string> Documentos { get; } = ["DARF", "DAS", "GPS", "DCTFWeb", "ISS"];

    private IReadOnlyList<Guia> _lista = [];
    private string _descricaoFiltro = "";

    public ObservableCollection<GuiaLinha> Linhas { get; } = [];

    [ObservableProperty] private DateTime? _competenciaInicio = new DateTime(DateTime.Today.Year, 1, 1);
    [ObservableProperty] private DateTime? _competenciaFim = new DateTime(DateTime.Today.Year, 12, 1);
    [ObservableProperty] private int _situacao = 1;
    [ObservableProperty] private DateTime? _dataPagamento = DateTime.Today;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(MarcarPagaCommand), nameof(ReabrirCommand), nameof(ExcluirCommand))] private GuiaLinha? _selecionada;
    // Guia manual
    [ObservableProperty] private string _manualTributo = "";
    [ObservableProperty] private string _manualDescricao = "";
    [ObservableProperty] private DateTime? _manualVencimento = DateTime.Today.AddDays(10);
    [ObservableProperty] private string _manualValor = "";
    [ObservableProperty] private string _manualCodigo = "";
    [ObservableProperty] private int _manualDocumento;

    public bool PodeManter => Contexto.Pode(Permissao.FiscalRetencoesManter);

    protected override string TituloRelatorio => "Guias de recolhimento a pagar";
    protected override string Parametros => _descricaoFiltro;
    protected override string NomeArquivo => $"guias_{DateTime.Today:yyyyMMdd}";

    public override void AtualizarPermissoes()
    {
        base.AtualizarPermissoes();
        OnPropertyChanged(nameof(PodeManter));
    }

    private (DateOnly Ini, DateOnly Fim) Competencias()
    {
        var ini = Data(CompetenciaInicio, new DateOnly(DateTime.Today.Year, 1, 1));
        return (new DateOnly(ini.Year, ini.Month, 1), Data(CompetenciaFim, new DateOnly(ini.Year, 12, 1)));
    }

    [RelayCommand]
    public Task ConsultarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var (ini, fim) = Competencias();
        var r = await app.Fiscal().Guias.ListarAsync(empresa, new FiltroGuias(ini, fim, Paga: Situacao switch { 1 => false, 2 => true, _ => null }), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); Limpar(); return; }
        _lista = r.Valor;
        _descricaoFiltro = $"Competências {ini:MM/yyyy} a {fim:MM/yyyy} · {Situacoes[Situacao]}";
        Linhas.Clear();
        foreach (var g in r.Valor) Linhas.Add(new GuiaLinha(g));
        Mostrar(TabelasFiscais.Guias(r.Valor));
        Mensagem = r.Valor.Count == 0 ? "Nenhuma guia. Use Gerar guias para criá-las das apurações e retenções." : null;
    }, Permissao.FiscalRelatorios);

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var (ini, fim) = Competencias();
        var r = await app.Fiscal().Guias.GerarAsync(empresa, ini, fim, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await ConsultarAsync();
        Mensagem = $"{r.Valor} guia(s) gerada(s) das apurações e retenções efetuadas. Guias pagas foram mantidas.";
    }, Permissao.FiscalRetencoesManter);

    private bool TemSelecionada() => Selecionada is not null;

    [RelayCommand(CanExecute = nameof(TemSelecionada))]
    private Task MarcarPagaAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada is not { } s) return;
        var r = await app.Fiscal().Guias.MarcarPagaAsync(empresa, s.Guia.Id, Data(DataPagamento, DateOnly.FromDateTime(DateTime.Today)), s.Guia.Valor, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await ConsultarAsync();
        Mensagem = "Guia marcada como paga.";
    }, Permissao.FiscalRetencoesManter);

    [RelayCommand(CanExecute = nameof(TemSelecionada))]
    private Task ReabrirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada is not { } s) return;
        var r = await app.Fiscal().Guias.ReabrirAsync(empresa, s.Guia.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await ConsultarAsync();
        Mensagem = "Guia reaberta.";
    }, Permissao.FiscalRetencoesManter);

    [RelayCommand(CanExecute = nameof(TemSelecionada))]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada is not { } s) return;
        var r = await app.Fiscal().Guias.ExcluirAsync(empresa, s.Guia.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await ConsultarAsync();
        Mensagem = "Guia excluída.";
    }, Permissao.FiscalRetencoesManter);

    [RelayCommand]
    private Task IncluirManualAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var valor = Numeros.LerValor(ManualValor);
        if (valor is null) { Erros.Add("Informe o valor."); return; }
        var venc = Data(ManualVencimento, DateOnly.FromDateTime(DateTime.Today));
        var doc = ManualDocumento switch { 1 => DocumentoGuia.Das, 2 => DocumentoGuia.Gps, 3 => DocumentoGuia.DctfWeb, 4 => DocumentoGuia.Iss, _ => DocumentoGuia.Darf };
        var g = new NovaGuia(ManualTributo, ManualDescricao, new DateOnly(venc.Year, venc.Month, 1), venc, valor.Value, string.IsNullOrWhiteSpace(ManualCodigo) ? null : ManualCodigo.Trim(), doc, "MANUAL", null);
        var r = await app.Fiscal().Guias.GravarManualAsync(empresa, g, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        (ManualTributo, ManualDescricao, ManualValor, ManualCodigo) = ("", "", "", "");
        await ConsultarAsync();
        Mensagem = "Guia incluída.";
    }, Permissao.FiscalRetencoesManter);
}

public sealed record ContaTributoLinha(ContaTributo Conta)
{
    public string Tributo => Conta.Tributo;
    public string Despesa => Conta.ContaDespesa;
    public string Passivo => Conta.ContaPassivo;
}

/// <summary>Apuração › Contas dos tributos: contas de despesa e de passivo de cada tributo, usadas na contabilização das provisões.</summary>
public partial class ContasTributosViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public IReadOnlyList<string> Tributos => Vorticis.Fiscal.Guias.ServicoProvisoes.Tributos;
    public ObservableCollection<ContaTributoLinha> Contas { get; } = [];

    [ObservableProperty] private int _tributo;
    [ObservableProperty] private string _despesa = "";
    [ObservableProperty] private string _passivo = "";
    [ObservableProperty] private ContaTributoLinha? _selecionada;

    public bool PodeManter => Contexto.Pode(Permissao.FiscalApurar);

    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeManter));

    partial void OnSelecionadaChanged(ContaTributoLinha? value)
    {
        if (value is null) return;
        (Tributo, Despesa, Passivo) = (Math.Max(0, Tributos.ToList().IndexOf(value.Tributo)), value.Despesa, value.Passivo);
    }

    public Task CarregarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Fiscal().Provisoes.ListarContasAsync(empresa, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Contas.Clear();
        foreach (var c in r.Valor) Contas.Add(new ContaTributoLinha(c));
    });

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Fiscal().Provisoes.GravarContaAsync(empresa, new ContaTributo(Tributos[Tributo], Despesa, Passivo), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = "Contas gravadas.";
    }, Permissao.FiscalApurar);

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada is not { } s) { Erros.Add("Selecione um tributo."); return; }
        var r = await app.Fiscal().Provisoes.ExcluirContaAsync(empresa, s.Tributo, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = "Contas removidas.";
    }, Permissao.FiscalApurar);
}
