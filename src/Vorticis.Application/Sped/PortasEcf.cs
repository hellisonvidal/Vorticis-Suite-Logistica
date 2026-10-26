using Vorticis.Domain.Sped;

namespace Vorticis.Application.Sped;

/// <summary>Dados contábeis e fiscais lidos para a ECF do ano-calendário (sem os signatários, que vêm do cadastro de responsáveis).</summary>
/// <param name="Regime">Regime tributário vigente no ano (core.empresa_regime): LUCRO_REAL, LUCRO_PRESUMIDO, SIMPLES_NACIONAL...; nulo = sem regime cadastrado.</param>
public sealed record DadosContabeisEcf(
    EmpresaEcf Empresa,
    string? Regime,
    bool RegimeCaixa,
    long ExercicioId,
    DateOnly InicioExercicio,
    DateOnly FimExercicio,
    string? CodPlanRef,
    IReadOnlyList<ContaEcd> Plano,
    IReadOnlyList<SaldoAberturaEcd> SaldosAbertura,
    IReadOnlyList<LancamentoEcd> Lancamentos,
    IReadOnlyList<ReferencialEcf> Referencial,
    IReadOnlyList<ApuracaoEcf> Apuracoes,
    IReadOnlyList<LancamentoLalurEcf> Lalur,
    IReadOnlyList<MovimentoParteBEcf> ParteB,
    IReadOnlyList<SocioEcf> Socios,
    EcdDaEcf? Ecd,
    IReadOnlyList<DateOnly> PeriodosAbertos);

/// <summary>ECF gerada, para o histórico (contabil.ecf_gerada).</summary>
public sealed record EcfRegistrada(long Id, int Ano, DateOnly Inicio, DateOnly Fim, string FormaTributacao, string HashSha1, int Linhas, int Assinaturas,
                                   DateTimeOffset GeradoEm);

/// <summary>Consultas de leitura para a ECF (Infra, SQL direto).</summary>
public interface IConsultasEcf
{
    /// <summary>Carrega o ano-calendário <paramref name="ano"/> da empresa; null se o exercício não existe.</summary>
    Task<DadosContabeisEcf?> CarregarAsync(long empresaId, int ano, CancellationToken ct);
    Task<long> RegistrarAsync(long empresaId, int ano, DateOnly inicio, DateOnly fim, string formaTributacao, string hashSha1, int linhas, int assinaturas,
                              long? usuarioId, CancellationToken ct);
    Task<IReadOnlyList<EcfRegistrada>> ListarAsync(long empresaId, CancellationToken ct);
}
