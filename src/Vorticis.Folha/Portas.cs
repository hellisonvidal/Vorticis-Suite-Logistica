using Vorticis.Folha.Cadastros;
using Vorticis.Folha.Regras;

namespace Vorticis.Folha;

/// <summary>Persistência dos cadastros da folha. Violações de regra do banco chegam como <c>ViolacaoRegraException</c>.</summary>
public interface IRepositorioCadastrosFolha
{
    Task<IReadOnlyList<Departamento>> ListarDepartamentosAsync(long empresaId, CancellationToken ct);
    Task<long> GravarDepartamentoAsync(long empresaId, Departamento d, CancellationToken ct);
    Task ExcluirDepartamentoAsync(long empresaId, long id, CancellationToken ct);

    Task<IReadOnlyList<Cargo>> ListarCargosAsync(long empresaId, CancellationToken ct);
    Task<long> GravarCargoAsync(long empresaId, Cargo c, CancellationToken ct);
    Task ExcluirCargoAsync(long empresaId, long id, CancellationToken ct);

    Task<IReadOnlyList<Funcionario>> ListarFuncionariosAsync(long empresaId, CancellationToken ct);
    Task<Funcionario?> ObterFuncionarioAsync(long empresaId, long id, CancellationToken ct);
    /// <summary>Grava funcionário e dependentes; mudança de salário ou cargo gera registro de alteração contratual (S-2206).</summary>
    Task<long> GravarFuncionarioAsync(long empresaId, Funcionario f, DateOnly dataAlteracao, long usuarioId, CancellationToken ct);
    Task ExcluirFuncionarioAsync(long empresaId, long id, long usuarioId, CancellationToken ct);
    Task<bool> FuncionarioTemMovimentoAsync(long empresaId, long id, CancellationToken ct);
    Task DefinirDesligamentoAsync(long empresaId, long funcionarioId, DateOnly? data, string? motivo, long usuarioId, CancellationToken ct);

    Task<IReadOnlyList<Rubrica>> ListarRubricasAsync(long empresaId, CancellationToken ct);
    Task<long> GravarRubricaAsync(long empresaId, Rubrica r, CancellationToken ct);
    Task ExcluirRubricaAsync(long empresaId, long id, CancellationToken ct);
    /// <summary>Cria as rubricas padrão que a empresa ainda não tem.</summary>
    Task GarantirRubricasPadraoAsync(long empresaId, CancellationToken ct);

    Task<ParametroEmpresa> ObterParametrosAsync(long empresaId, CancellationToken ct);
    Task GravarParametrosAsync(long empresaId, ParametroEmpresa p, long usuarioId, CancellationToken ct);
    Task<ConfiguracaoFolha> ObterConfiguracaoAsync(long empresaId, CancellationToken ct);
    Task GravarConfiguracaoAsync(long empresaId, ConfiguracaoFolha c, long usuarioId, CancellationToken ct);
    /// <summary>Regime tributário (texto do banco, ex.: SIMPLES_NACIONAL) vigente na data; nulo se não houver.</summary>
    Task<string?> ObterRegimeTributarioAsync(long empresaId, DateOnly data, CancellationToken ct);

    Task<IReadOnlyList<ParametroLegal>> ListarParametrosLegaisAsync(CancellationToken ct);

    Task<IReadOnlyList<Afastamento>> ListarAfastamentosAsync(long empresaId, CancellationToken ct);
    Task<long> GravarAfastamentoAsync(long empresaId, Afastamento a, CancellationToken ct);
    Task ExcluirAfastamentoAsync(long empresaId, long id, CancellationToken ct);
    Task<IReadOnlyList<AlteracaoContratual>> ListarAlteracoesAsync(long empresaId, CancellationToken ct);

    /// <summary>Dados da empresa para o eSocial e os relatórios.</summary>
    Task<EmpresaFolha?> ObterEmpresaAsync(long empresaId, CancellationToken ct);
}

public sealed record EmpresaFolha(long Id, string Cnpj, string RazaoSocial, string CnaePrincipal, string Uf, string CodMunicipioIbge, string? ContatoNome,
                                  string? ContatoCpf, string? ContatoEmail, string? ContatoTelefone);

/// <summary>Persistência do movimento da folha: competências, eventos, cálculos, férias e rescisões.</summary>
public interface IRepositorioMovimentoFolha
{
    Task<StatusCompetencia> ObterCompetenciaAsync(long empresaId, DateOnly competencia, CancellationToken ct);
    /// <summary>Quantos sócios têm pró-labore calculado na competência (empresa só com sócios também fecha a competência).</summary>
    Task<int> ContarProLaboreAsync(long empresaId, DateOnly competencia, CancellationToken ct);
    Task<IReadOnlyList<StatusCompetencia>> ListarCompetenciasAsync(long empresaId, int ano, CancellationToken ct);
    Task AlterarCompetenciaAsync(long empresaId, DateOnly competencia, bool fechar, long usuarioId, CancellationToken ct);

    Task<IReadOnlyList<EventoVariavel>> ListarEventosAsync(long empresaId, DateOnly competencia, CancellationToken ct);
    Task<long> GravarEventoAsync(long empresaId, long funcionarioId, DateOnly competencia, string rubricaCodigo, decimal referencia, decimal? valor,
                                 string? observacao, CancellationToken ct);
    Task ExcluirEventoAsync(long empresaId, long id, CancellationToken ct);

    /// <summary>Substitui o cálculo da mesma chave (funcionário, competência, tipo, origem) e grava os itens.</summary>
    Task<long> GravarCalculoAsync(long empresaId, NovoCalculo c, long usuarioId, CancellationToken ct);
    Task<IReadOnlyList<CalculoGravado>> ListarCalculosAsync(long empresaId, DateOnly competencia, TipoFolha? tipo, bool comItens, CancellationToken ct);
    Task<CalculoGravado?> ObterCalculoAsync(long empresaId, long calculoId, CancellationToken ct);
    Task ExcluirCalculosAsync(long empresaId, DateOnly competencia, TipoFolha tipo, long? origemId, CancellationToken ct, long? funcionarioId = null);

    /// <summary>Soma das verbas variáveis (P) do funcionário nas folhas mensais dos 12 meses anteriores a <paramref name="competencia"/> e meses com folha.</summary>
    Task<(decimal Soma, int Meses)> VariaveisUltimosMesesAsync(long empresaId, long funcionarioId, DateOnly competencia, CancellationToken ct);
    /// <summary>Soma do FGTS dos cálculos do funcionário até a competência (saldo estimado da conta vinculada).</summary>
    Task<decimal> FgtsAcumuladoAsync(long empresaId, long funcionarioId, DateOnly ate, CancellationToken ct);
    Task<decimal> TotalDecimoPrimeiraParcelaAsync(long empresaId, long funcionarioId, int ano, CancellationToken ct);

    Task<IReadOnlyList<FeriasRegistro>> ListarFeriasAsync(long empresaId, long? funcionarioId, CancellationToken ct);
    Task<long> GravarFeriasAsync(long empresaId, long funcionarioId, PeriodoAquisitivo periodo, DateOnly gozoInicio, int diasGozo, int abonoDias, int faltas,
                                 DateOnly pagamento, CancellationToken ct);
    Task CancelarFeriasAsync(long empresaId, long id, CancellationToken ct);
    Task<FeriasRegistro?> ObterFeriasAsync(long empresaId, long id, CancellationToken ct);

    Task<IReadOnlyList<RescisaoRegistro>> ListarRescisoesAsync(long empresaId, CancellationToken ct);
    Task<long> GravarRescisaoAsync(long empresaId, long funcionarioId, SolicitacaoRescisao s, int diasAviso, DateOnly pagamento, CancellationToken ct);
    Task CancelarRescisaoAsync(long empresaId, long id, CancellationToken ct);
    Task<RescisaoRegistro?> ObterRescisaoAsync(long empresaId, long id, CancellationToken ct);
}

public sealed record RegraContabil(long Id, string Chave, string Descricao, string ContaDebito, string ContaCredito, bool UsaCentroCusto, char LadoCentroCusto);

public sealed record ProvisaoGravada(long FuncionarioId, string Matricula, string FuncionarioNome, long? CentroCustoId, string? CentroCustoCodigo, decimal Base,
                                     decimal Ferias, decimal Decimo, decimal EncFeriasInss, decimal EncFeriasFgts, decimal EncDecimoInss, decimal EncDecimoFgts)
{
    public decimal Total => Ferias + Decimo + EncFeriasInss + EncFeriasFgts + EncDecimoInss + EncDecimoFgts;
}

public sealed record ContabilizacaoRegistro(long Id, DateOnly Competencia, string Tipo, long LancamentoId, long? NumeroLancamento, decimal ValorTotal,
                                            DateTimeOffset CriadaEm, DateTimeOffset? DesfeitaEm);

public interface IRepositorioContabilizacaoFolha
{
    Task<IReadOnlyList<RegraContabil>> ListarRegrasAsync(long empresaId, CancellationToken ct);
    Task GravarRegraAsync(long empresaId, RegraContabil r, CancellationToken ct);
    Task ExcluirRegraAsync(long empresaId, long id, CancellationToken ct);

    Task SubstituirProvisoesAsync(long empresaId, DateOnly competencia, IReadOnlyList<(long FuncionarioId, long? CentroCustoId, ProvisaoMensal P)> provisoes,
                                  CancellationToken ct);
    Task<IReadOnlyList<ProvisaoGravada>> ListarProvisoesAsync(long empresaId, DateOnly competencia, CancellationToken ct);

    Task<IReadOnlyList<ContabilizacaoRegistro>> ListarContabilizacoesAsync(long empresaId, DateOnly? competencia, CancellationToken ct);
    Task<ContabilizacaoRegistro?> ObterContabilizacaoAtivaAsync(long empresaId, DateOnly competencia, string tipo, CancellationToken ct);
    Task<long> RegistrarContabilizacaoAsync(long empresaId, DateOnly competencia, string tipo, long lancamentoId, long numero, decimal valor, long usuarioId,
                                            CancellationToken ct);
    Task MarcarDesfeitaAsync(long empresaId, long id, long? estornoId, long usuarioId, CancellationToken ct);
}

public sealed record EventoEsocial(long Id, string Tipo, DateOnly? Competencia, string Referencia, string IdEvento, string Xml, string Status,
                                   DateTimeOffset GeradoEm, DateTimeOffset? ExportadoEm, string? XmlAssinado = null, int? Ambiente = null, long? LoteId = null,
                                   string? Recibo = null, int? CodigoResposta = null, string? OcorrenciasJson = null, DateTimeOffset? AssinadoEm = null,
                                   DateTimeOffset? ProcessadoEm = null);

/// <summary>Lote enviado ao eSocial (um por grupo e por até 50 eventos).</summary>
public sealed record LoteEsocial(long Id, int Ambiente, int Grupo, string TransmissorPapel, int TransmissorTpInsc, string TransmissorNrInsc, string? Protocolo,
                                 string Status, int? CodigoResposta, string? DescricaoResposta, string? OcorrenciasJson, DateTimeOffset EnviadoEm,
                                 DateTimeOffset? ConsultadoEm, int QtdEventos, string? XmlEnvio = null, string? XmlRetornoEnvio = null, string? XmlRetornoConsulta = null);

public sealed record NovoLoteEsocial(int Ambiente, int Grupo, string TransmissorPapel, int TransmissorTpInsc, string TransmissorNrInsc, string? Protocolo,
                                     string Status, int? CodigoResposta, string? DescricaoResposta, string? OcorrenciasJson, string XmlEnvio, string? XmlRetornoEnvio);

/// <summary>Persistência da assinatura e da transmissão dos eventos do eSocial.</summary>
public interface IRepositorioTransmissaoEsocial
{
    Task MarcarAssinadoAsync(long empresaId, long eventoId, string xmlAssinado, int ambiente, long usuarioId, CancellationToken ct);
    /// <summary>Registra o motivo de o evento não poder ser assinado/enviado (status volta/permanece GERADO).</summary>
    Task RegistrarFalhaPreparoAsync(long empresaId, long eventoId, string ocorrenciasJson, CancellationToken ct);
    Task<long> CriarLoteAsync(long empresaId, NovoLoteEsocial lote, long usuarioId, CancellationToken ct);
    /// <summary>Liga os eventos ao lote com o status informado (ENVIADO, ou ERRO se o lote foi recusado).</summary>
    Task VincularEventosAsync(long empresaId, IReadOnlyList<long> eventoIds, long loteId, string status, string? ocorrenciasJson, int? codigo, CancellationToken ct);
    Task AtualizarLoteAsync(long empresaId, long loteId, string status, int? codigo, string? descricao, string? ocorrenciasJson, string xmlRetornoConsulta, CancellationToken ct);
    Task RegistrarResultadoEventoAsync(long empresaId, long loteId, string idEvento, string status, string? recibo, int codigo, string? ocorrenciasJson, CancellationToken ct);
    /// <summary>Eventos do lote ainda sem resultado (status ENVIADO) passam a ERRO com o motivo informado.</summary>
    Task RejeitarEventosPendentesDoLoteAsync(long empresaId, long loteId, int codigo, string? ocorrenciasJson, CancellationToken ct);
    Task<IReadOnlyList<LoteEsocial>> ListarLotesAsync(long empresaId, int limite, CancellationToken ct);
    Task<LoteEsocial?> ObterLoteAsync(long empresaId, long loteId, CancellationToken ct);
}

public interface IRepositorioESocial
{
    Task<IReadOnlyList<EventoEsocial>> ListarAsync(long empresaId, DateOnly? competencia, CancellationToken ct);
    Task<long> GravarAsync(long empresaId, string tipo, DateOnly? competencia, string referencia, string idEvento, string xml, long usuarioId, CancellationToken ct);
    Task MarcarExportadosAsync(long empresaId, IReadOnlyList<long> ids, CancellationToken ct);
    Task<int> SequencialDoDiaAsync(long empresaId, string prefixoId, CancellationToken ct);
    Task ExcluirNaoExportadosAsync(long empresaId, string tipo, DateOnly? competencia, string referencia, CancellationToken ct);
}
