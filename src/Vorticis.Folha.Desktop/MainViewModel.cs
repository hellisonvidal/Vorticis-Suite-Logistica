using Vorticis.Folha;
using Vorticis.Folha.Desktop.ViewModels;

namespace Vorticis.Folha.Desktop;

/// <summary>Janela principal do módulo Folha: o casco com o <see cref="ModuloFolha"/> e o acesso tipado às telas do módulo.</summary>
public sealed class MainViewModel() : MainViewModelBase(new ModuloFolha())
{
    public FuncionariosViewModel Funcionarios => Instancia<FuncionariosViewModel>();
    public CargosViewModel Cargos => Instancia<CargosViewModel>();
    public DepartamentosViewModel Departamentos => Instancia<DepartamentosViewModel>();
    public RubricasViewModel Rubricas => Instancia<RubricasViewModel>();
    public ParametrosFolhaViewModel Parametros => Instancia<ParametrosFolhaViewModel>();
    public EventosVariaveisViewModel Eventos => Instancia<EventosVariaveisViewModel>();
    public FolhaMensalViewModel FolhaMensal => Instancia<FolhaMensalViewModel>();
    public FeriasViewModel Ferias => Instancia<FeriasViewModel>();
    public DecimoTerceiroViewModel Decimo => Instancia<DecimoTerceiroViewModel>();
    public RescisoesViewModel Rescisoes => Instancia<RescisoesViewModel>();
    public ContabilizacaoFolhaViewModel Contabilizacao => Instancia<ContabilizacaoFolhaViewModel>();
    public ESocialViewModel ESocial => Instancia<ESocialViewModel>();
    public AssistenteEsocialViewModel AssistenteEsocial => Instancia<AssistenteEsocialViewModel>();
    public TransmissaoEsocialViewModel Transmissao => Instancia<TransmissaoEsocialViewModel>();
    public ResumoFolhaViewModel Resumo => Instancia<ResumoFolhaViewModel>();
    public EncargosFolhaViewModel Encargos => Instancia<EncargosFolhaViewModel>();
    public ProvisoesFolhaViewModel Provisoes => Instancia<ProvisoesFolhaViewModel>();
    public ProLaboreViewModel ProLabore => Instancia<ProLaboreViewModel>();

    /// <summary>Situação da competência escolhida ao lado da empresa (folha.competencia).</summary>
    protected override async Task<(bool Aberto, string Situacao)?> ConsultarPeriodoAsync(DateOnly periodo)
    {
        if (App is not { } app || EmpresaId is not { } empresa || Sessao is not { } sessao) return null;
        var s = await ServicosFolha.De(app).Folha.ObterCompetenciaAsync(empresa, periodo, sessao.Id);
        return (!s.Fechada, s.Fechada ? "FECHADA" : "ABERTA");
    }

    /// <summary>Competências fechadas do ano em uso e dos dois anteriores (lista ao lado da empresa).</summary>
    protected override async Task<IReadOnlyCollection<DateOnly>> ListarPeriodosFechadosAsync()
    {
        if (App is not { } app || EmpresaId is not { } empresa || Sessao is not { } sessao) return [];
        var fechadas = new HashSet<DateOnly>();
        foreach (var ano in new[] { DateTime.Today.Year - 2, DateTime.Today.Year - 1, DateTime.Today.Year, PeriodoTrabalho.Year }.Distinct())
            foreach (var c in await ServicosFolha.De(app).Folha.ListarCompetenciasAsync(empresa, ano, sessao.Id))
                if (c.Fechada) fechadas.Add(c.Competencia);
        return fechadas;
    }

    /// <summary>Leva a nova competência às telas que trabalham por competência.</summary>
    protected override async Task AoAlterarPeriodoAsync(DateOnly periodo)
    {
        Eventos.Competencia = FolhaMensal.Competencia = periodo.ToDateTime(TimeOnly.MinValue);
        await ProLabore.CarregarAsync();
    }
}
