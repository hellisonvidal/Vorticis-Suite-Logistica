using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Guias;
using Vorticis.Fiscal.Lucro;
using Vorticis.Fiscal.Relatorios;

namespace Vorticis.Fiscal.Desktop.ViewModels;

public partial class ReceitaPresumidaLinha(string codigo, string descricao, decimal valor, decimal irpj, decimal csll) : ObservableObject
{
    public string Codigo { get; } = codigo;
    public string Descricao { get; } = descricao;
    public string Presuncao { get; } = $"IRPJ {irpj:0.##}% · CSLL {csll:0.##}%";
    [ObservableProperty] private string _valorTexto = Numeros.Moeda(valor);
}

/// <summary>Base dos apuradores trimestrais (Presumido e Real): ano e trimestre, situação da apuração salva e provisão.</summary>
public abstract partial class ApuracaoTrimestralViewModel(IContexto contexto) : RelatorioFiscalViewModel(contexto)
{
    public static IReadOnlyList<string> Trimestres { get; } = ["1º trimestre (jan a mar)", "2º trimestre (abr a jun)", "3º trimestre (jul a set)", "4º trimestre (out a dez)"];

    protected long? ApuracaoId;
    protected bool ProvisaoContabilizada;
    protected abstract RegimeLucro Regime { get; }
    protected int AnoCalculado = DateTime.Today.Year;
    protected int TrimestreCalculado = (DateTime.Today.Month - 1) / 3 + 1;

    [ObservableProperty] private int _ano = DateTime.Today.Year;
    [ObservableProperty] private int _trimestre = (DateTime.Today.Month - 1) / 3;
    [ObservableProperty] private string _situacao = "";
    [ObservableProperty] private string _avisosTexto = "";

    public bool PodeApurar => Contexto.Pode(Permissao.FiscalApurar);
    public bool PodeContabilizar => ApuracaoId is not null && !ProvisaoContabilizada;
    public bool PodeDesfazer => ApuracaoId is not null && ProvisaoContabilizada;

    protected override string TituloRelatorio => Regime == RegimeLucro.Real ? "Apuração do Lucro Real" : "Apuração do Lucro Presumido";
    protected override string Parametros => $"{TrimestreCalculado}º trimestre de {AnoCalculado}";
    protected override string NomeArquivo => $"lucro_{(Regime == RegimeLucro.Real ? "real" : "presumido")}_{AnoCalculado}_T{TrimestreCalculado}";

    public override void AtualizarPermissoes()
    {
        base.AtualizarPermissoes();
        OnPropertyChanged(nameof(PodeApurar));
    }

    protected void AtualizarEstado()
    {
        OnPropertyChanged(nameof(PodeContabilizar));
        OnPropertyChanged(nameof(PodeDesfazer));
        Situacao = ApuracaoId is null ? "Apuração ainda não salva." : ProvisaoContabilizada ? "Apuração salva · provisão contabilizada." : "Apuração salva · provisão não contabilizada.";
    }

    partial void OnAnoChanged(int value) => Reiniciar();
    partial void OnTrimestreChanged(int value) => Reiniciar();

    private void Reiniciar()
    {
        ApuracaoId = null;
        ProvisaoContabilizada = false;
        AtualizarEstado();
    }

    protected void MostrarResultadoLucro(ResultadoLucro r)
    {
        (AnoCalculado, TrimestreCalculado) = (r.Ano, r.Trimestre);
        Mostrar(TabelasFiscais.Lucro(r));
        AvisosTexto = string.Join(Environment.NewLine, r.Avisos);
    }

    protected async Task<ApuracaoLucroSalva?> SalvaDoPeriodoAsync(VorticisApp app, long empresa, long usuario) =>
        (await app.Fiscal().Lucro.ListarApuracoesAsync(empresa, Ano, usuario)).Valor?.FirstOrDefault(a => a.Resultado.Regime == Regime && a.Resultado.Trimestre == Trimestre + 1);

    protected void DefinirSalva(ApuracaoLucroSalva? s)
    {
        (ApuracaoId, ProvisaoContabilizada) = (s?.Id, s?.LancamentoId is not null);
        if (s is not null) MostrarResultadoLucro(s.Resultado); else Limpar();
        AtualizarEstado();
    }

    [RelayCommand]
    private Task ContabilizarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Fiscal().Provisoes.ContabilizarLucroAsync(empresa, Regime, Ano, Trimestre + 1, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        ProvisaoContabilizada = true;
        AtualizarEstado();
        Mensagem = "Provisão de IRPJ e CSLL contabilizada.";
    }, Permissao.FiscalApurar);

    [RelayCommand]
    private Task DesfazerAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (ApuracaoId is not { } id) return;
        var r = await app.Fiscal().Provisoes.DesfazerAsync(empresa, TipoApuracao.Lucro, id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        ProvisaoContabilizada = false;
        AtualizarEstado();
        Mensagem = "Provisão estornada.";
    }, Permissao.FiscalApurar);
}

/// <summary>Apuração › Lucro Presumido: IRPJ (15% + adicional de 10%) e CSLL (9%) trimestrais sobre a base presumida de cada atividade.</summary>
public partial class ApuracaoPresumidoViewModel(IContexto contexto) : ApuracaoTrimestralViewModel(contexto)
{
    public ObservableCollection<ReceitaPresumidaLinha> Receitas { get; } = [];
    [ObservableProperty] private string _outrasReceitasTexto = "";
    [ObservableProperty] private string _irrfTexto = "";
    [ObservableProperty] private string _csllRetidaTexto = "";
    [ObservableProperty] private string _outrasDeducoesTexto = "";

    protected override RegimeLucro Regime => RegimeLucro.Presumido;

    private static decimal V(string t) => Numeros.LerValor(t) ?? 0m;

    private EntradaPresumido Entrada() => new(Ano, Trimestre + 1, Receitas.Select(r => new ReceitaPresumida(r.Codigo, V(r.ValorTexto))).Where(r => r.Valor != 0).ToList(),
        V(OutrasReceitasTexto), V(IrrfTexto), V(CsllRetidaTexto), V(OutrasDeducoesTexto));

    [RelayCommand]
    private Task SugerirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var lucro = app.Fiscal().Lucro;
        var r = await lucro.PrepararPresumidoAsync(empresa, Ano, Trimestre + 1, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var p = await lucro.ParametrosAsync(new DateOnly(Ano, 1, 1));
        Receitas.Clear();
        foreach (var a in p.Presuncao)
            Receitas.Add(new ReceitaPresumidaLinha(a.Codigo, a.Descricao, r.Valor.Entrada.Receitas.Where(x => x.Atividade == a.Codigo).Sum(x => x.Valor), a.Irpj, a.Csll));
        (OutrasReceitasTexto, IrrfTexto, CsllRetidaTexto) = (Numeros.Moeda(r.Valor.Entrada.OutrasReceitas), Numeros.Moeda(r.Valor.Entrada.IrrfCompensavel), Numeros.Moeda(r.Valor.Entrada.CsllRetida));
        DefinirSalva(await SalvaDoPeriodoAsync(app, empresa, usuario));
        AvisosTexto = string.Join(Environment.NewLine, r.Valor.Avisos);
        Mensagem = "Receitas sugeridas pelas notas e retenções sofridas do trimestre. Revise e clique em Calcular.";
    }, Permissao.FiscalApurar);

    [RelayCommand]
    private Task CalcularAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Fiscal().Lucro.CalcularPresumidoAsync(empresa, Entrada(), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        MostrarResultadoLucro(r.Valor);
        Mensagem = $"IRPJ a pagar {Numeros.Moeda(r.Valor.IrpjAPagar)} · CSLL a pagar {Numeros.Moeda(r.Valor.CsllAPagar)}. Clique em Salvar para gravar.";
    }, Permissao.FiscalApurar);

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Fiscal().Lucro.SalvarPresumidoAsync(empresa, Entrada(), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        DefinirSalva(r.Valor);
        Mensagem = "Apuração salva.";
    }, Permissao.FiscalApurar);
}

public sealed record LalurLinha(LancamentoLalur Lancamento)
{
    public long Id => Lancamento.Id;
    public string Periodo => $"{Lancamento.Trimestre}º tri/{Lancamento.Ano}";
    public string Tributo => Lancamento.Tributo == "AMBOS" ? "IRPJ e CSLL" : Lancamento.Tributo;
    public string Tipo => Lancamento.Tipo == "ADICAO" ? "Adição" : "Exclusão";
    public string Descricao => Lancamento.Descricao;
    public string Valor => Numeros.Moeda(Lancamento.Valor);
    public string CodigoEcf => Lancamento.CodigoEcf ?? "";
}

public sealed record ParteBLinha(MovimentoParteB Movimento)
{
    public string Tributo => Movimento.Tributo;
    public string Tipo => Movimento.Tipo switch { "SALDO_INICIAL" => "Saldo inicial", "PREJUIZO" => "Prejuízo gerado", _ => "Compensação" };
    public string Periodo => Movimento.Ano is { } a ? $"{Movimento.Trimestre}º tri/{a}" : "";
    public string Valor => Numeros.Moeda(Movimento.Valor);
    public string Descricao => Movimento.Descricao ?? "";
}

/// <summary>Apuração › Lucro Real: resultado contábil do trimestre, LALUR/LACS (adições e exclusões), compensação de prejuízos (30%) e Parte B.</summary>
public partial class ApuracaoRealViewModel(IContexto contexto) : ApuracaoTrimestralViewModel(contexto)
{
    public static IReadOnlyList<string> Tributos { get; } = ["IRPJ e CSLL", "Somente IRPJ", "Somente CSLL"];
    public static IReadOnlyList<string> Tipos { get; } = ["Adição", "Exclusão"];

    public ObservableCollection<LalurLinha> Lalur { get; } = [];
    public ObservableCollection<ParteBLinha> ParteB { get; } = [];

    [ObservableProperty] private string _lucroContabilTexto = "";
    [ObservableProperty] private string _saldoPrejuizoTexto = "";
    [ObservableProperty] private string _saldoBaseNegativaTexto = "";
    [ObservableProperty] private string _irrfTexto = "";
    [ObservableProperty] private string _csllRetidaTexto = "";
    [ObservableProperty] private string _outrasDeducoesTexto = "";
    [ObservableProperty] private string _receitaTexto = "";
    [ObservableProperty] private string _contabilTexto = "";
    // Nova linha do LALUR/LACS
    [ObservableProperty] private int _lalurTributo;
    [ObservableProperty] private int _lalurTipo;
    [ObservableProperty] private string _lalurDescricao = "";
    [ObservableProperty] private string _lalurValor = "";
    [ObservableProperty] private string _lalurCodigoEcf = "";
    // Saldos iniciais da Parte B
    [ObservableProperty] private string _saldoInicialIrpj = "";
    [ObservableProperty] private string _saldoInicialCsll = "";

    [ObservableProperty] private LalurLinha? _lalurSelecionada;

    public bool PodeManterLalur => Contexto.Pode(Permissao.FiscalLalurManter);
    protected override RegimeLucro Regime => RegimeLucro.Real;

    public override void AtualizarPermissoes()
    {
        base.AtualizarPermissoes();
        OnPropertyChanged(nameof(PodeManterLalur));
    }

    private static decimal V(string t) => Numeros.LerValor(t) ?? 0m;

    private EntradaReal Entrada(decimal adIrpj, decimal exIrpj, decimal adCsll, decimal exCsll) =>
        new(Ano, Trimestre + 1, V(LucroContabilTexto), adIrpj, exIrpj, adCsll, exCsll, V(SaldoPrejuizoTexto), V(SaldoBaseNegativaTexto), V(IrrfTexto), V(CsllRetidaTexto), V(OutrasDeducoesTexto), V(ReceitaTexto));

    private EntradaReal EntradaDaTela()
    {
        var l = Lalur.Select(x => x.Lancamento).Where(x => x.Trimestre == Trimestre + 1 && x.Ano == Ano).ToList();
        decimal Soma(string tipo, params string[] tributos) => l.Where(x => x.Tipo == tipo && tributos.Contains(x.Tributo)).Sum(x => x.Valor);
        return Entrada(Soma("ADICAO", "IRPJ", "AMBOS"), Soma("EXCLUSAO", "IRPJ", "AMBOS"), Soma("ADICAO", "CSLL", "AMBOS"), Soma("EXCLUSAO", "CSLL", "AMBOS"));
    }

    private async Task RecarregarLalurAsync(VorticisApp app, long empresa, long usuario)
    {
        var r = await app.Fiscal().Lucro.ListarLalurAsync(empresa, Ano, null, usuario);
        Lalur.Clear();
        if (r.Sucesso) foreach (var x in r.Valor) Lalur.Add(new LalurLinha(x));
        ParteB.Clear();
        foreach (var t in new[] { "IRPJ", "CSLL" })
        {
            var b = await app.Fiscal().Lucro.ParteBAsync(empresa, t, usuario);
            if (b.Sucesso) foreach (var m in b.Valor) ParteB.Add(new ParteBLinha(m));
        }
    }

    [RelayCommand]
    private Task PrepararAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        await RecarregarLalurAsync(app, empresa, usuario);
        var r = await app.Fiscal().Lucro.PrepararRealAsync(empresa, Ano, Trimestre + 1, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var e = r.Valor.Entrada;
        (LucroContabilTexto, SaldoPrejuizoTexto, SaldoBaseNegativaTexto, IrrfTexto, CsllRetidaTexto, OutrasDeducoesTexto, ReceitaTexto) =
            (Numeros.Moeda(e.LucroContabil), Numeros.Moeda(e.SaldoPrejuizoIrpj), Numeros.Moeda(e.SaldoBaseNegativaCsll), Numeros.Moeda(e.IrrfCompensavel),
             Numeros.Moeda(e.CsllRetida), "", Numeros.Moeda(e.ReceitaBruta));
        ContabilTexto = $"Resultado contábil do trimestre {Numeros.Moeda(r.Valor.ResultadoContabil)} + despesas de IRPJ/CSLL já lançadas {Numeros.Moeda(r.Valor.AjusteTributosSobreLucro)} = lucro antes dos tributos {Numeros.Moeda(e.LucroContabil)}";
        DefinirSalva(await SalvaDoPeriodoAsync(app, empresa, usuario));
        AvisosTexto = string.Join(Environment.NewLine, r.Valor.Avisos);
        Mensagem = "Dados do trimestre carregados (contabilidade, LALUR/LACS, Parte B e retenções). Revise e clique em Calcular.";
    }, Permissao.FiscalApurar);

    [RelayCommand]
    private Task CalcularAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Fiscal().Lucro.CalcularRealAsync(empresa, EntradaDaTela(), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        MostrarResultadoLucro(r.Valor);
        Mensagem = $"IRPJ a pagar {Numeros.Moeda(r.Valor.IrpjAPagar)} · CSLL a pagar {Numeros.Moeda(r.Valor.CsllAPagar)}. Clique em Salvar para gravar.";
    }, Permissao.FiscalApurar);

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Fiscal().Lucro.SalvarRealAsync(empresa, EntradaDaTela(), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        DefinirSalva(r.Valor);
        await RecarregarLalurAsync(app, empresa, usuario);
        Mensagem = "Apuração salva; prejuízo e compensações gravados na Parte B.";
    }, Permissao.FiscalApurar);

    [RelayCommand]
    private Task AdicionarLalurAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var valor = Numeros.LerValor(LalurValor);
        if (valor is null) { Erros.Add("Informe o valor."); return; }
        var l = new LancamentoLalur(0, Ano, Trimestre + 1, LalurTributo switch { 1 => "IRPJ", 2 => "CSLL", _ => "AMBOS" }, LalurTipo == 0 ? "ADICAO" : "EXCLUSAO", LalurDescricao, valor.Value,
            string.IsNullOrWhiteSpace(LalurCodigoEcf) ? null : LalurCodigoEcf.Trim(), null);
        var r = await app.Fiscal().Lucro.GravarLalurAsync(empresa, l, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        (LalurDescricao, LalurValor, LalurCodigoEcf) = ("", "", "");
        await RecarregarLalurAsync(app, empresa, usuario);
        Mensagem = "Lançamento incluído no LALUR/LACS. Clique em Calcular para refazer a apuração.";
    }, Permissao.FiscalLalurManter);

    [RelayCommand]
    private Task ExcluirLalurAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var linha = LalurSelecionada;
        if (linha is null) { Erros.Add("Selecione a linha do LALUR/LACS."); return; }
        var r = await app.Fiscal().Lucro.ExcluirLalurAsync(empresa, linha.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await RecarregarLalurAsync(app, empresa, usuario);
        Mensagem = "Lançamento excluído.";
    }, Permissao.FiscalLalurManter);

    [RelayCommand]
    private Task DefinirSaldosIniciaisAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        foreach (var (tributo, texto) in new[] { ("IRPJ", SaldoInicialIrpj), ("CSLL", SaldoInicialCsll) })
        {
            if (string.IsNullOrWhiteSpace(texto)) continue;
            var v = Numeros.LerValor(texto);
            if (v is null) { Erros.Add($"Saldo inicial de {tributo} inválido."); return; }
            var r = await app.Fiscal().Lucro.DefinirSaldoInicialAsync(empresa, tributo, v.Value, usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        }
        await RecarregarLalurAsync(app, empresa, usuario);
        Mensagem = "Saldos iniciais da Parte B gravados. Use Preparar para trazê-los à apuração.";
    }, Permissao.FiscalLalurManter);
}

/// <summary>Apuração › PIS e COFINS: cumulativo (0,65% e 3%) ou não cumulativo (1,65% e 7,6%, com créditos sobre as entradas).</summary>
public partial class ApuracaoPisCofinsViewModel(IContexto contexto) : RelatorioFiscalViewModel(contexto)
{
    public static IReadOnlyList<string> Regimes { get; } = ["Cumulativo (Lucro Presumido)", "Não cumulativo (Lucro Real)"];

    private long? _apuracaoId;
    private bool _provisaoContabilizada;
    private DateOnly _competencia = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [ObservableProperty] private DateTime? _competenciaData = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private int _regime;
    [ObservableProperty] private bool _excluirIcms;
    [ObservableProperty] private string _receitaTexto = "";
    [ObservableProperty] private string _baseCreditosTexto = "";
    [ObservableProperty] private string _pisRetidoTexto = "";
    [ObservableProperty] private string _cofinsRetidoTexto = "";
    [ObservableProperty] private string _situacao = "";
    [ObservableProperty] private string _avisosTexto = "";

    public bool PodeApurar => Contexto.Pode(Permissao.FiscalApurar);
    public bool PodeContabilizar => _apuracaoId is not null && !_provisaoContabilizada;
    public bool PodeDesfazer => _apuracaoId is not null && _provisaoContabilizada;

    protected override string TituloRelatorio => "Apuração de PIS e COFINS";
    protected override string Parametros => $"Competência {_competencia:MM/yyyy} · {Regimes[Regime]}";
    protected override string NomeArquivo => $"pis_cofins_{_competencia:yyyyMM}";

    public override void AtualizarPermissoes()
    {
        base.AtualizarPermissoes();
        OnPropertyChanged(nameof(PodeApurar));
    }

    private DateOnly Competencia() => Data(CompetenciaData, _competencia) is var d ? new DateOnly(d.Year, d.Month, 1) : _competencia;
    private RegimePisCofins RegimeEscolhido() => Regime == 0 ? RegimePisCofins.Cumulativo : RegimePisCofins.NaoCumulativo;
    private static decimal V(string t) => Numeros.LerValor(t) ?? 0m;
    private EntradaPisCofins Entrada() => new(Competencia(), RegimeEscolhido(), V(ReceitaTexto), V(BaseCreditosTexto), V(PisRetidoTexto), V(CofinsRetidoTexto));

    private void AtualizarEstado()
    {
        OnPropertyChanged(nameof(PodeContabilizar));
        OnPropertyChanged(nameof(PodeDesfazer));
        Situacao = _apuracaoId is null ? "Apuração ainda não salva." : _provisaoContabilizada ? "Apuração salva · provisão contabilizada." : "Apuração salva · provisão não contabilizada.";
    }

    partial void OnCompetenciaDataChanged(DateTime? value)
    {
        _apuracaoId = null;
        _provisaoContabilizada = false;
        AtualizarEstado();
    }

    private void MostrarCalculo(ResultadoPisCofins r)
    {
        _competencia = r.Competencia;
        Mostrar(TabelasFiscais.PisCofins(r));
        AvisosTexto = string.Join(Environment.NewLine, r.Avisos);
    }

    [RelayCommand]
    private Task SugerirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Fiscal().Lucro.PrepararPisCofinsAsync(empresa, Competencia(), RegimeEscolhido(), ExcluirIcms, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var e = r.Valor.Entrada;
        (ReceitaTexto, BaseCreditosTexto, PisRetidoTexto, CofinsRetidoTexto) = (Numeros.Moeda(e.ReceitaBase), Numeros.Moeda(e.BaseCreditos), Numeros.Moeda(e.PisRetido), Numeros.Moeda(e.CofinsRetido));
        var salva = (await app.Fiscal().Lucro.ListarPisCofinsAsync(empresa, Competencia().Year, usuario)).Valor?.FirstOrDefault(a => a.Resultado.Competencia == Competencia());
        (_apuracaoId, _provisaoContabilizada) = (salva?.Id, salva?.LancamentoId is not null);
        if (salva is not null) MostrarCalculo(salva.Resultado); else Limpar();
        AtualizarEstado();
        AvisosTexto = string.Join(Environment.NewLine, r.Valor.Avisos);
        Mensagem = "Valores sugeridos pelas notas e retenções do mês. Revise e clique em Calcular.";
    }, Permissao.FiscalApurar);

    [RelayCommand]
    private Task CalcularAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Fiscal().Lucro.CalcularPisCofinsAsync(empresa, Entrada(), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        MostrarCalculo(r.Valor);
        Mensagem = $"PIS a pagar {Numeros.Moeda(r.Valor.PisAPagar)} · COFINS a pagar {Numeros.Moeda(r.Valor.CofinsAPagar)}. Clique em Salvar para gravar.";
    }, Permissao.FiscalApurar);

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Fiscal().Lucro.SalvarPisCofinsAsync(empresa, Entrada(), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        (_apuracaoId, _provisaoContabilizada) = (r.Valor.Id, r.Valor.LancamentoId is not null);
        MostrarCalculo(r.Valor.Resultado);
        AtualizarEstado();
        Mensagem = "Apuração salva.";
    }, Permissao.FiscalApurar);

    [RelayCommand]
    private Task ContabilizarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Fiscal().Provisoes.ContabilizarPisCofinsAsync(empresa, Competencia(), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        _provisaoContabilizada = true;
        AtualizarEstado();
        Mensagem = "Provisão de PIS e COFINS contabilizada.";
    }, Permissao.FiscalApurar);

    [RelayCommand]
    private Task DesfazerAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (_apuracaoId is not { } id) return;
        var r = await app.Fiscal().Provisoes.DesfazerAsync(empresa, TipoApuracao.PisCofins, id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        _provisaoContabilizada = false;
        AtualizarEstado();
        Mensagem = "Provisão estornada.";
    }, Permissao.FiscalApurar);
}
