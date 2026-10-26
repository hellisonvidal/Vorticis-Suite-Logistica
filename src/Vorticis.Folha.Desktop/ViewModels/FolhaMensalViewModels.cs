using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Folha.Cadastros;
using Vorticis.Folha.Regras;
using Vorticis.Folha.Servicos;

namespace Vorticis.Folha.Desktop.ViewModels;

public sealed record EventoLinha(EventoVariavel E)
{
    public string Funcionario => E.FuncionarioNome;
    public string Rubrica => $"{E.RubricaCodigo} – {E.RubricaDescricao}";
    public string Referencia => E.Referencia == 0 ? "" : Formato.Num(E.Referencia);
    public string Valor => E.Valor is { } v ? Formato.Moeda(v) : "";
    public string Observacao => E.Observacao ?? "";
}

/// <summary>Folha › Folha › Eventos variáveis: horas extras 50%/100%, faltas, adicional noturno, comissões, adiantamentos e outros.</summary>
public partial class EventosVariaveisViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public ObservableCollection<EventoLinha> Eventos { get; } = [];
    public ObservableCollection<Funcionario> Funcionarios { get; } = [];
    public ObservableCollection<Rubrica> Rubricas { get; } = [];

    [ObservableProperty] private DateTime? _competencia = FolhaExtensoes.Mes(contexto.PeriodoTrabalho);
    [ObservableProperty] private Funcionario? _funcionario;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PorReferencia), nameof(RotuloQuantidade))] private Rubrica? _rubrica;
    [ObservableProperty] private string _referencia = "";
    [ObservableProperty] private string _valor = "";
    [ObservableProperty] private string _observacao = "";
    [ObservableProperty] private EventoLinha? _selecionado;
    [ObservableProperty] private string _situacao = "";

    public bool PodeLancar => Contexto.Pode(Permissao.FolhaEventosLancar);
    public bool PorReferencia => Rubrica is { } r && ServicoFolha.UsaReferencia(r.Codigo);
    public string RotuloQuantidade => Rubrica?.Codigo is RubricasPadrao.Faltas or RubricasPadrao.DsrFaltas ? "Dias" : "Horas";

    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeLancar));

    partial void OnCompetenciaChanged(DateTime? value) => _ = CarregarAsync();

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var s = app.Folha();
        var competencia = FolhaExtensoes.InicioMes(Competencia);
        var eventos = await s.Folha.ListarEventosAsync(empresa, competencia, sessao.Id);
        var funcionarios = await s.Cadastros.ListarFuncionariosAsync(empresa, sessao.Id);
        var rubricas = await s.Cadastros.ListarRubricasAsync(empresa, sessao.Id);
        var status = await s.Folha.ObterCompetenciaAsync(empresa, competencia, sessao.Id);
        if (!EhAtual(g)) return;
        Eventos.Clear(); foreach (var e in eventos) Eventos.Add(new EventoLinha(e));
        var fid = Funcionario?.Id; var rid = Rubrica?.Id;
        Funcionarios.Clear(); foreach (var f in funcionarios.Where(f => f.Situacao != "DESLIGADO")) Funcionarios.Add(f);
        Rubricas.Clear(); foreach (var r in rubricas.Where(r => r.Ativa && r.Tipo != 'I' && !ServicoFolha.EhAutomatica(r.Codigo))) Rubricas.Add(r);
        Funcionario = Funcionarios.FirstOrDefault(f => f.Id == fid);
        Rubrica = Rubricas.FirstOrDefault(r => r.Id == rid);
        Situacao = status.Fechada ? $"Competência {competencia:MM/yyyy} FECHADA: reabra-a para lançar eventos." : $"Competência {competencia:MM/yyyy} aberta.";
    }

    [RelayCommand]
    private Task LancarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Funcionario is null || Rubrica is null) { Erros.Add("Escolha o funcionário e a rubrica."); return; }
        var referencia = Numeros.LerValor(Referencia) ?? 0;
        var valor = Numeros.LerValor(Valor);
        var r = await app.Folha().Folha.LancarEventoAsync(empresa, Funcionario.Id, FolhaExtensoes.InicioMes(Competencia), Rubrica.Codigo, referencia, valor, Observacao, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Referencia = Valor = Observacao = "";
        await CarregarAsync();
        Mensagem = "Evento lançado. Recalcule a folha mensal para considerá-lo.";
    }, Permissao.FolhaEventosLancar);

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado is null) { Erros.Add("Selecione um evento."); return; }
        var r = await app.Folha().Folha.ExcluirEventoAsync(empresa, FolhaExtensoes.InicioMes(Competencia), Selecionado.E.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = "Evento excluído.";
    }, Permissao.FolhaEventosLancar);
}

public sealed record CalculoLinha(CalculoGravado C)
{
    public string Matricula => C.Matricula;
    public string Funcionario => C.FuncionarioNome;
    public string Tipo => C.Tipo.Descricao();
    public string Proventos => Formato.Moeda(C.TotalProventos);
    public string Descontos => Formato.Moeda(C.TotalDescontos);
    public string Liquido => Formato.Moeda(C.Liquido);
    public string Inss => Formato.Moeda(C.Inss);
    public string Irrf => Formato.Moeda(C.Irrf);
    public string Fgts => Formato.Moeda(C.Fgts);
}

public sealed record ItemLinha(ItemGravado I)
{
    public string Codigo => I.Codigo;
    public string Descricao => I.Descricao;
    public string Referencia => I.Referencia == 0 ? "" : Formato.Num(I.Referencia);
    public string Proventos => I.Tipo == 'P' ? Formato.Moeda(I.Valor) : "";
    public string Descontos => I.Tipo == 'D' ? Formato.Moeda(I.Valor) : "";
    public string Informativo => I.Tipo == 'I' ? Formato.Moeda(I.Valor) : "";
    public string Memoria => I.Memoria;
}

/// <summary>Base das telas que listam os cálculos de uma competência com a memória de cálculo e geram recibos em PDF.</summary>
public abstract partial class CalculosBaseViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public ObservableCollection<CalculoLinha> Calculos { get; } = [];
    public ObservableCollection<ItemLinha> Itens { get; } = [];

    [ObservableProperty] private CalculoLinha? _calculoSelecionado;
    [ObservableProperty] private string _totais = "";

    protected abstract DateOnly CompetenciaConsulta { get; }
    protected abstract TipoFolha? TipoConsulta { get; }

    public bool PodeRelatorios => Contexto.Pode(Permissao.FolhaRelatorios);

    partial void OnCalculoSelecionadoChanged(CalculoLinha? value)
    {
        Itens.Clear();
        if (value is null) return;
        foreach (var i in value.C.Itens) Itens.Add(new ItemLinha(i));
    }

    protected async Task CarregarCalculosAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var lista = await app.Folha().Folha.ListarCalculosAsync(empresa, CompetenciaConsulta, TipoConsulta, sessao.Id, comItens: true);
        if (!EhAtual(g)) return;
        var id = CalculoSelecionado?.C.Id;
        Calculos.Clear(); foreach (var c in lista) Calculos.Add(new CalculoLinha(c));
        CalculoSelecionado = Calculos.FirstOrDefault(c => c.C.Id == id);
        // funcionários com cálculo na competência, para o holerite de um só
        var escolhido = FuncionarioHolerite?.Id;
        FuncionariosHolerite.Clear();
        FuncionariosHolerite.Add(new OpcaoFuncionarioHolerite(null, "Todos os funcionários"));
        foreach (var f in lista.GroupBy(c => c.FuncionarioId).Select(g => g.First()).OrderBy(c => c.FuncionarioNome))
            FuncionariosHolerite.Add(new OpcaoFuncionarioHolerite(f.FuncionarioId, $"{f.Matricula} – {f.FuncionarioNome}"));
        FuncionarioHolerite = FuncionariosHolerite.FirstOrDefault(o => o.Id == escolhido) ?? FuncionariosHolerite[0];
        Totais = lista.Count == 0 ? "Nenhum cálculo na competência."
            : $"{lista.Count} cálculo(s) · proventos {Formato.Moeda(lista.Sum(c => c.TotalProventos))} · descontos {Formato.Moeda(lista.Sum(c => c.TotalDescontos))} · líquido {Formato.Moeda(lista.Sum(c => c.Liquido))} · FGTS {Formato.Moeda(lista.Sum(c => c.Fgts))}";
    }

    [RelayCommand]
    private Task ReciboAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (CalculoSelecionado is null) { Erros.Add("Selecione um cálculo."); return; }
        var r = await app.Folha().Relatorios.ReciboAsync(empresa, CalculoSelecionado.C.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var c = CalculoSelecionado.C;
        Mensagem = "Recibo gerado: " + ArquivosGerados.Salvar($"recibo_{c.Tipo.Sql().ToLowerInvariant()}_{c.Matricula}_{c.Competencia:yyyyMM}.pdf", r.Valor.Conteudo);
    }, Permissao.FolhaRelatorios);

    /// <summary>Holerites: de todos os funcionários da competência ou só do escolhido na lista.</summary>
    public ObservableCollection<OpcaoFuncionarioHolerite> FuncionariosHolerite { get; } = [new(null, "Todos os funcionários")];
    [ObservableProperty] private OpcaoFuncionarioHolerite? _funcionarioHolerite;

    [RelayCommand]
    private Task HoleritesAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var tipo = TipoConsulta ?? TipoFolha.Mensal;
        var funcionario = FuncionarioHolerite?.Id;
        var r = await app.Folha().Relatorios.HoleritesAsync(empresa, CompetenciaConsulta, tipo, usuario, funcionarioId: funcionario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var sufixo = funcionario is null ? "" : "_" + new string([.. FuncionarioHolerite!.Texto.TakeWhile(c => c != ' ')]);
        Mensagem = "Holerites gerados: " + ArquivosGerados.Salvar($"holerites_{tipo.Sql().ToLowerInvariant()}_{CompetenciaConsulta:yyyyMM}{sufixo}.pdf", r.Valor.Conteudo);
    }, Permissao.FolhaRelatorios);

    public void AtualizarPermissoesBase() => OnPropertyChanged(nameof(PodeRelatorios));
}

public sealed record OpcaoFuncionarioHolerite(long? Id, string Texto)
{
    public override string ToString() => Texto;
}

/// <summary>Folha › Folha › Cálculo da folha mensal: calcula, mostra a memória por rubrica, fecha/reabre a competência e emite holerites.</summary>
public partial class FolhaMensalViewModel(IContexto contexto) : CalculosBaseViewModel(contexto)
{
    [ObservableProperty] private DateTime? _competencia = FolhaExtensoes.Mes(contexto.PeriodoTrabalho);
    [ObservableProperty] private string _situacao = "";
    [ObservableProperty] private bool _fechada;

    protected override DateOnly CompetenciaConsulta => FolhaExtensoes.InicioMes(Competencia);
    protected override TipoFolha? TipoConsulta => null;

    public bool PodeCalcular => Contexto.Pode(Permissao.FolhaCalcular) && !Fechada;
    public bool PodeFechar => Contexto.Pode(Permissao.FolhaFechar) && !Fechada;
    public bool PodeReabrir => Contexto.Pode(Permissao.FolhaReabrir) && Fechada;

    public int AnoCompetencia => (Competencia ?? DateTime.Today).Year;

    partial void OnFechadaChanged(bool value) => AtualizarPermissoes();
    partial void OnCompetenciaChanged(DateTime? value) => _ = CarregarAsync();

    public void AtualizarPermissoes()
    {
        AtualizarPermissoesBase();
        OnPropertyChanged(nameof(PodeCalcular));
        OnPropertyChanged(nameof(PodeFechar));
        OnPropertyChanged(nameof(PodeReabrir));
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var status = await app.Folha().Folha.ObterCompetenciaAsync(empresa, CompetenciaConsulta, sessao.Id);
        Fechada = status.Fechada;
        Situacao = status.Fechada ? $"Competência {status.Competencia:MM/yyyy} FECHADA em {status.FechadaEm:dd/MM/yyyy HH:mm}" : $"Competência {status.Competencia:MM/yyyy} aberta";
        await CarregarCalculosAsync();
    }

    [RelayCommand]
    private Task CalcularAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Folha().Folha.CalcularMensalAsync(empresa, CompetenciaConsulta, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        foreach (var a in r.Valor.Avisos) Erros.Add(a);
        Mensagem = $"Folha calculada: {r.Valor.Calculados} funcionário(s), líquido {Formato.Moeda(r.Valor.Liquido)}.";
    }, Permissao.FolhaCalcular);

    [RelayCommand]
    private Task FecharAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Folha().Folha.FecharAsync(empresa, CompetenciaConsulta, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        await Contexto.AtualizarSituacaoPeriodoAsync();
        Mensagem = $"Competência {CompetenciaConsulta:MM/yyyy} fechada.";
    }, Permissao.FolhaFechar);

    [RelayCommand]
    private Task ReabrirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Folha().Folha.ReabrirAsync(empresa, CompetenciaConsulta, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        await Contexto.AtualizarSituacaoPeriodoAsync();
        Mensagem = $"Competência {CompetenciaConsulta:MM/yyyy} reaberta.";
    }, Permissao.FolhaReabrir);
}

public sealed record SaldoFeriasLinha(SaldoFerias S)
{
    public string Periodo => $"{S.Periodo.Inicio:dd/MM/yyyy} a {S.Periodo.Fim:dd/MM/yyyy}";
    public string Concessivo => $"até {S.Periodo.FimConcessivo:dd/MM/yyyy}";
    public string Dias => $"{S.DiasUsados} de {S.DiasDireito}";
    public string Situacao => !S.Completo ? "Em curso" : S.Saldo == 0 ? "Quitado" : S.ConcessivoVencido ? "Vencido (dobro)" : "A gozar";
}

public sealed record FeriasLinha(FeriasRegistro F)
{
    public string Funcionario => F.FuncionarioNome;
    public string Periodo => $"{F.AquisitivoInicio:dd/MM/yyyy} a {F.AquisitivoFim:dd/MM/yyyy}";
    public string Gozo => $"{F.GozoInicio:dd/MM/yyyy} ({F.DiasGozo} dias)";
    public string Abono => F.AbonoDias == 0 ? "" : $"{F.AbonoDias} dias";
    public string Pagamento => F.DataPagamento.ToString("dd/MM/yyyy");
    public string Liquido => Formato.Moeda(F.Liquido);
    public string Situacao => F.Status == "CALCULADA" ? "Calculada" : "Cancelada";
}

/// <summary>Folha › Folha › Férias: períodos aquisitivos, 1/3 constitucional, abono pecuniário e médias simples das verbas variáveis.</summary>
public partial class FeriasViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public ObservableCollection<Funcionario> Funcionarios { get; } = [];
    public ObservableCollection<SaldoFeriasLinha> Saldos { get; } = [];
    public ObservableCollection<FeriasLinha> Registros { get; } = [];

    [ObservableProperty] private Funcionario? _funcionario;
    [ObservableProperty] private SaldoFeriasLinha? _saldoSelecionado;
    [ObservableProperty] private FeriasLinha? _registroSelecionado;
    [ObservableProperty] private DateTime? _gozoInicio = DateTime.Today.AddDays(15);
    [ObservableProperty] private decimal? _diasGozo = 30;
    [ObservableProperty] private decimal? _abonoDias = 0;
    [ObservableProperty] private decimal? _faltas = 0;
    [ObservableProperty] private DateTime? _pagamento = DateTime.Today.AddDays(12);

    public bool PodeCalcular => Contexto.Pode(Permissao.FolhaCalcular);
    public bool PodeRelatorios => Contexto.Pode(Permissao.FolhaRelatorios);
    public void AtualizarPermissoes() { OnPropertyChanged(nameof(PodeCalcular)); OnPropertyChanged(nameof(PodeRelatorios)); }

    partial void OnFuncionarioChanged(Funcionario? value) => _ = CarregarSaldosAsync();

    partial void OnSaldoSelecionadoChanged(SaldoFeriasLinha? value)
    {
        if (value is { S.Saldo: > 0 }) { DiasGozo = value.S.Saldo; AbonoDias = 0; }
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var s = app.Folha();
        var funcionarios = await s.Cadastros.ListarFuncionariosAsync(empresa, sessao.Id);
        var registros = await s.Folha.ListarFeriasAsync(empresa, null, sessao.Id);
        if (!EhAtual(g)) return;
        var id = Funcionario?.Id;
        Funcionarios.Clear(); foreach (var f in funcionarios.Where(f => f.Situacao != "DESLIGADO")) Funcionarios.Add(f);
        Funcionario = Funcionarios.FirstOrDefault(f => f.Id == id);
        Registros.Clear(); foreach (var r in registros) Registros.Add(new FeriasLinha(r));
    }

    private async Task CarregarSaldosAsync()
    {
        Saldos.Clear();
        if (Funcionario is null || Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var saldos = await app.Folha().Folha.ListarSaldosFeriasAsync(empresa, Funcionario.Id, DateOnly.FromDateTime(DateTime.Today), sessao.Id);
        Saldos.Clear(); foreach (var x in saldos) Saldos.Add(new SaldoFeriasLinha(x));
        SaldoSelecionado = Saldos.LastOrDefault(x => x.S.Completo && x.S.Saldo > 0);
    }

    [RelayCommand]
    private Task CalcularAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Funcionario is null || SaldoSelecionado is null) { Erros.Add("Escolha o funcionário e o período aquisitivo."); return; }
        if (GozoInicio is null || Pagamento is null) { Erros.Add("Informe o início do gozo e a data de pagamento."); return; }
        var r = await app.Folha().Folha.CalcularFeriasAsync(empresa, Funcionario.Id, SaldoSelecionado.S.Periodo.Inicio, DateOnly.FromDateTime(GozoInicio.Value), (int)(DiasGozo ?? 0),
            (int)(AbonoDias ?? 0), (int)(Faltas ?? 0), DateOnly.FromDateTime(Pagamento.Value), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        await CarregarSaldosAsync();
        Mensagem = "Férias calculadas. Use \"Recibo\" para emitir o recibo em PDF.";
    }, Permissao.FolhaCalcular);

    [RelayCommand]
    private Task CancelarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (RegistroSelecionado is null) { Erros.Add("Selecione as férias."); return; }
        var r = await app.Folha().Folha.CancelarFeriasAsync(empresa, RegistroSelecionado.F.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        await CarregarSaldosAsync();
        Mensagem = "Férias canceladas.";
    }, Permissao.FolhaCalcular);

    [RelayCommand]
    private Task ReciboAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (RegistroSelecionado is not { } reg) { Erros.Add("Selecione as férias."); return; }
        var calculos = await app.Folha().Folha.ListarCalculosAsync(empresa, new DateOnly(reg.F.DataPagamento.Year, reg.F.DataPagamento.Month, 1), TipoFolha.Ferias, usuario);
        var calc = calculos.FirstOrDefault(c => c.OrigemId == reg.F.Id);
        if (calc is null) { Erros.Add("As férias selecionadas não têm cálculo (canceladas?)."); return; }
        var r = await app.Folha().Relatorios.ReciboAsync(empresa, calc.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Mensagem = "Recibo gerado: " + ArquivosGerados.Salvar($"recibo_ferias_{calc.Matricula}_{reg.F.GozoInicio:yyyyMMdd}.pdf", r.Valor.Conteudo);
    }, Permissao.FolhaRelatorios);
}

/// <summary>Folha › Folha › 13º salário: 1ª parcela (novembro) e 2ª parcela (dezembro) com INSS e IRRF sobre o valor integral.</summary>
public partial class DecimoTerceiroViewModel(IContexto contexto) : CalculosBaseViewModel(contexto)
{
    [ObservableProperty] private decimal? _ano = DateTime.Today.Year;
    [ObservableProperty] private bool _segundaParcela;

    private int AnoInt => (int)(Ano ?? DateTime.Today.Year);
    protected override DateOnly CompetenciaConsulta => new(AnoInt, SegundaParcela ? 12 : 11, 1);
    protected override TipoFolha? TipoConsulta => SegundaParcela ? TipoFolha.Decimo2 : TipoFolha.Decimo1;

    public bool PodeCalcular => Contexto.Pode(Permissao.FolhaCalcular);
    public void AtualizarPermissoes() { AtualizarPermissoesBase(); OnPropertyChanged(nameof(PodeCalcular)); }

    partial void OnAnoChanged(decimal? value) => _ = CarregarCalculosAsync();
    partial void OnSegundaParcelaChanged(bool value) => _ = CarregarCalculosAsync();

    public Task CarregarAsync() => CarregarCalculosAsync();

    [RelayCommand]
    private Task CalcularAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Folha().Folha.CalcularDecimoAsync(empresa, AnoInt, SegundaParcela ? 2 : 1, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarCalculosAsync();
        foreach (var a in r.Valor.Avisos) Erros.Add(a);
        Mensagem = $"13º salário ({(SegundaParcela ? "2ª" : "1ª")} parcela) calculado: {r.Valor.Calculados} funcionário(s), líquido {Formato.Moeda(r.Valor.Liquido)}.";
    }, Permissao.FolhaCalcular);
}

public sealed record RescisaoLinha(RescisaoRegistro R)
{
    public string Funcionario => R.FuncionarioNome;
    public string Motivo => R.Motivo.Descricao();
    public string Desligamento => R.Desligamento.ToString("dd/MM/yyyy");
    public string Aviso => R.Aviso switch { TipoAviso.Indenizado => $"Indenizado ({R.DiasAviso} dias)", TipoAviso.Trabalhado => "Trabalhado", TipoAviso.NaoCumprido => "Não cumprido", _ => "Dispensado" };
    public string Liquido => Formato.Moeda(R.Liquido);
    public string Multa => R.MultaFgts == 0 ? "" : Formato.Moeda(R.MultaFgts);
    public string Situacao => R.Status == "CALCULADA" ? "Calculada" : "Cancelada";
}

/// <summary>Folha › Folha › Rescisões: motivos principais, aviso prévio proporcional (Lei 12.506), férias, 13º e multa do FGTS; TRCT simplificado.</summary>
public partial class RescisoesViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public static IReadOnlyList<Opcao> Motivos { get; } = Enum.GetValues<MotivoRescisao>().Select(m => new Opcao(m.Sql(), m.Descricao())).ToArray();
    public static IReadOnlyList<Opcao> Avisos { get; } =
        [new("INDENIZADO", "Aviso prévio indenizado"), new("TRABALHADO", "Aviso prévio trabalhado"), new("NAO_CUMPRIDO", "Aviso não cumprido pelo empregado"), new("DISPENSADO", "Sem aviso (dispensado/não se aplica)")];

    public ObservableCollection<Funcionario> Funcionarios { get; } = [];
    public ObservableCollection<RescisaoLinha> Registros { get; } = [];

    [ObservableProperty] private Funcionario? _funcionario;
    [ObservableProperty] private Opcao? _motivo = Motivos[0];
    [ObservableProperty] private Opcao? _aviso = Avisos[0];
    [ObservableProperty] private DateTime? _desligamento = DateTime.Today;
    [ObservableProperty] private DateTime? _pagamento = DateTime.Today.AddDays(10);
    [ObservableProperty] private string _saldoFgts = "";
    [ObservableProperty] private RescisaoLinha? _selecionada;

    public bool PodeCalcular => Contexto.Pode(Permissao.FolhaCalcular);
    public void AtualizarPermissoes() { OnPropertyChanged(nameof(PodeCalcular)); }

    partial void OnMotivoChanged(Opcao? value)
    {
        Aviso = value?.Codigo switch
        {
            "PEDIDO_DEMISSAO" => Avisos[1],
            "JUSTA_CAUSA" or "TERMINO_CONTRATO" => Avisos[3],
            _ => Avisos[0],
        };
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var s = app.Folha();
        var funcionarios = await s.Cadastros.ListarFuncionariosAsync(empresa, sessao.Id);
        var registros = await s.Folha.ListarRescisoesAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        var id = Funcionario?.Id;
        Funcionarios.Clear(); foreach (var f in funcionarios.Where(f => f.Situacao != "DESLIGADO")) Funcionarios.Add(f);
        Funcionario = Funcionarios.FirstOrDefault(f => f.Id == id);
        Registros.Clear(); foreach (var r in registros) Registros.Add(new RescisaoLinha(r));
    }

    [RelayCommand]
    private Task EstimarFgtsAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Funcionario is null || Desligamento is null) { Erros.Add("Escolha o funcionário e a data do desligamento."); return; }
        var saldo = await app.Folha().Folha.SaldoFgtsEstimadoAsync(empresa, Funcionario.Id, new DateOnly(Desligamento.Value.Year, Desligamento.Value.Month, 1).AddMonths(-1), usuario);
        SaldoFgts = saldo.ToString("N2", Formato.PtBr);
        Mensagem = "Saldo estimado pelos depósitos calculados na folha; confira com o extrato da conta vinculada.";
    }, Permissao.FolhaCalcular);

    [RelayCommand]
    private Task CalcularAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Funcionario is null || Desligamento is null || Pagamento is null) { Erros.Add("Informe o funcionário, o desligamento e a data de pagamento."); return; }
        var motivo = MotivoRescisaoExtensoes.DeSql(Motivo?.Codigo ?? "SEM_JUSTA_CAUSA");
        var aviso = MotivoRescisaoExtensoes.AvisoDeSql(Aviso?.Codigo ?? "DISPENSADO");
        decimal? fgts = string.IsNullOrWhiteSpace(SaldoFgts) ? null : Numeros.LerValor(SaldoFgts);
        if (!string.IsNullOrWhiteSpace(SaldoFgts) && fgts is null) { Erros.Add("Saldo do FGTS inválido."); return; }
        var r = await app.Folha().Folha.CalcularRescisaoAsync(empresa, Funcionario.Id, motivo, DateOnly.FromDateTime(Desligamento.Value), aviso, fgts, DateOnly.FromDateTime(Pagamento.Value), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = "Rescisão calculada. Use \"TRCT\" para emitir o termo simplificado em PDF.";
    }, Permissao.FolhaCalcular);

    [RelayCommand]
    private Task CancelarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada is null) { Erros.Add("Selecione a rescisão."); return; }
        var r = await app.Folha().Folha.CancelarRescisaoAsync(empresa, Selecionada.R.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = "Rescisão cancelada; o funcionário voltou à situação Ativo.";
    }, Permissao.FolhaCalcular);

    [RelayCommand]
    private Task TrctAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada is not { } sel) { Erros.Add("Selecione a rescisão."); return; }
        var calculos = await app.Folha().Folha.ListarCalculosAsync(empresa, new DateOnly(sel.R.Desligamento.Year, sel.R.Desligamento.Month, 1), TipoFolha.Rescisao, usuario);
        var calc = calculos.FirstOrDefault(c => c.OrigemId == sel.R.Id);
        if (calc is null) { Erros.Add("A rescisão selecionada não tem cálculo (cancelada?)."); return; }
        var r = await app.Folha().Relatorios.ReciboAsync(empresa, calc.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Mensagem = "TRCT gerado: " + ArquivosGerados.Salvar($"trct_{calc.Matricula}_{sel.R.Desligamento:yyyyMMdd}.pdf", r.Valor.Conteudo);
    }, Permissao.FolhaRelatorios);
}
