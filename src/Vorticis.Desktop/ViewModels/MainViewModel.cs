namespace Vorticis.Desktop.ViewModels;

/// <summary>Janela principal do Contábil: o casco com o <see cref="ModuloContabil"/> e o acesso tipado às telas do módulo.</summary>
public sealed class MainViewModel() : MainViewModelBase(new ModuloContabil())
{
    public LancamentoViewModel Lancamento => Instancia<LancamentoViewModel>();
    public BalanceteViewModel Balancete => Instancia<BalanceteViewModel>();
    public RazaoViewModel Razao => Instancia<RazaoViewModel>();
    public DiarioViewModel Diario => Instancia<DiarioViewModel>();
    public PeriodosViewModel Periodos => Instancia<PeriodosViewModel>();
    public ReorganizarLancamentosViewModel ReorganizarLancamentos => Instancia<ReorganizarLancamentosViewModel>();
    public ReorganizarSaldosViewModel ReorganizarSaldos => Instancia<ReorganizarSaldosViewModel>();
    public PlanoContasViewModel PlanoContas => Instancia<PlanoContasViewModel>();
    public CentrosCustoViewModel CentrosCusto => Instancia<CentrosCustoViewModel>();
    public SaldosIniciaisViewModel SaldosIniciais => Instancia<SaldosIniciaisViewModel>();
    public EncerramentoViewModel Encerramento => Instancia<EncerramentoViewModel>();
    public BalancoViewModel Balanco => Instancia<BalancoViewModel>();
    public DreViewModel Dre => Instancia<DreViewModel>();
    public LivrosPdfViewModel LivrosPdf => Instancia<LivrosPdfViewModel>();
    public ImportacaoReferencialViewModel ImportacaoReferencial => Instancia<ImportacaoReferencialViewModel>();
    public MapeamentoReferencialViewModel MapeamentoReferencial => Instancia<MapeamentoReferencialViewModel>();
    public ConsultasViewModel Consultas => Instancia<ConsultasViewModel>();
    public ImportacaoPlanoViewModel ImportacaoPlano => Instancia<ImportacaoPlanoViewModel>();
    public ImportacaoPlanilhaViewModel ImportacaoPlanilhas => Instancia<ImportacaoPlanilhaViewModel>();
    public ImportacaoExtratoViewModel ImportacaoExtratos => Instancia<ImportacaoExtratoViewModel>();
    public ImportacaoLancamentosPadraoViewModel ImportacaoLancamentos => Instancia<ImportacaoLancamentosPadraoViewModel>();
    public HistoricosViewModel Historicos => Instancia<HistoricosViewModel>();
    public ExportacaoLancamentosViewModel ExportacaoLancamentos => Instancia<ExportacaoLancamentosViewModel>();
    public DmplViewModel Dmpl => Instancia<DmplViewModel>();
    public DlpaViewModel Dlpa => Instancia<DlpaViewModel>();
    public CentroCustoRelatorioViewModel CentroCustoRelatorio => Instancia<CentroCustoRelatorioViewModel>();
    public RelacaoCustosViewModel RelacaoCustos => Instancia<RelacaoCustosViewModel>();
    public ReceitaCentroViewModel ReceitaCentro => Instancia<ReceitaCentroViewModel>();
    public OrcadoRealizadoViewModel OrcadoRealizado => Instancia<OrcadoRealizadoViewModel>();
    public OrcamentoViewModel Orcamento => Instancia<OrcamentoViewModel>();
    public TermosViewModel Termos => Instancia<TermosViewModel>();
    public BensViewModel Bens => Instancia<BensViewModel>();
    public GruposDepreciacaoViewModel GruposDepreciacao => Instancia<GruposDepreciacaoViewModel>();
    public DepreciacaoViewModel Depreciacao => Instancia<DepreciacaoViewModel>();
    public ConciliacaoViewModel Conciliacao => Instancia<ConciliacaoViewModel>();
    public EcdAssistenteViewModel EcdAssistente => Instancia<EcdAssistenteViewModel>();
    public EcfAssistenteViewModel EcfAssistente => Instancia<EcfAssistenteViewModel>();
    public DiferencasSaldosViewModel DiferencasSaldos => Instancia<DiferencasSaldosViewModel>();
}
