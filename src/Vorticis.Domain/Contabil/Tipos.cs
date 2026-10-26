namespace Vorticis.Domain.Contabil;

public enum IndicadorDC { D, C }

/// <summary>ECD I200.IND_LCTO.</summary>
public enum IndicadorLancamento
{
    Normal,         // N
    Encerramento,   // E
    Extemporaneo,   // X
}

/// <summary>Espelha o enum contabil.origem_lancamento do banco.</summary>
public enum OrigemLancamento
{
    Manual, NFe, NFSe, CTe, Extrato, Planilha, Folha,
    Encerramento, Estorno, SaldoInicial, Depreciacao, ApuracaoTributo, Frota,
}

public enum StatusLancamento { Rascunho, Efetivado, Estornado }

public static class IndicadorDCExtensions
{
    public static IndicadorDC Inverso(this IndicadorDC dc) => dc == IndicadorDC.D ? IndicadorDC.C : IndicadorDC.D;

    /// <summary>Convenção interna de sinal: débito positivo, crédito negativo.</summary>
    public static decimal ComSinal(this IndicadorDC dc, decimal valor) => dc == IndicadorDC.D ? valor : -valor;
}

/// <summary>Uma linha de lançamento como chega do usuário ou de um módulo integrador (por códigos).</summary>
public sealed record PartidaProposta(
    string CodigoConta,
    IndicadorDC DC,
    decimal Valor,
    string Historico,
    string? CodigoCentroCusto = null,
    string? CodigoParticipante = null,
    string? NumeroArquivo = null);

/// <summary>Proposta de lançamento — único formato aceito por <c>ServicoLancamento</c>.</summary>
public sealed record LancamentoProposto(
    long EmpresaId,
    DateOnly Data,
    OrigemLancamento Origem,
    IReadOnlyList<PartidaProposta> Partidas,
    IndicadorLancamento Indicador = IndicadorLancamento.Normal,
    DateOnly? DataExtemporanea = null,
    long? DocumentoOrigemId = null,
    long? EstornoDeId = null);

public sealed record ContaInfo(
    long Id,
    string Codigo,
    string Descricao,
    bool Analitica,
    bool Ativa,
    DateOnly DataInclusao,
    bool ExigeCentroCusto,
    bool ExigeParticipante,
    string? CodNat = null);   // ECD I050.COD_NAT (01 Ativo, 02 Passivo, 03 PL, 04 Resultado...)

public sealed record PeriodoInfo(
    long ExercicioId,
    DateOnly InicioExercicio,
    DateOnly FimExercicio,
    bool ExercicioEncerrado,
    bool PeriodoExiste,
    bool PeriodoAberto,
    DateOnly? DataBloqueio = null,
    bool ExigeDocumento = false);

/// <summary>Dados de cadastro resolvidos para validar uma proposta.</summary>
public sealed record ContextoValidacao(
    IReadOnlyDictionary<string, ContaInfo> Contas,
    IReadOnlyDictionary<string, long> CentrosCusto,
    IReadOnlyDictionary<string, long> Participantes,
    PeriodoInfo? Periodo);

public sealed record PartidaValidada(
    short Sequencia,
    long ContaId,
    long? CentroCustoId,
    long? ParticipanteId,
    IndicadorDC DC,
    decimal Valor,
    string Historico,
    string? NumeroArquivo);

/// <summary>Lançamento pronto para gravação: todas as regras de aplicação já foram satisfeitas.</summary>
public sealed record LancamentoValidado(
    long EmpresaId,
    long ExercicioId,
    DateOnly Data,
    OrigemLancamento Origem,
    IndicadorLancamento Indicador,
    DateOnly? DataExtemporanea,
    long? DocumentoOrigemId,
    long? EstornoDeId,
    IReadOnlyList<PartidaValidada> Partidas)
{
    public decimal Total => Partidas.Where(p => p.DC == IndicadorDC.D).Sum(p => p.Valor);
}

public sealed record LancamentoEfetivado(long Id, long Numero);

/// <summary>Lançamento já gravado, com partidas identificadas por código (para estorno e consulta).</summary>
public sealed record LancamentoRegistrado(
    long Id,
    long EmpresaId,
    long? Numero,
    DateOnly Data,
    StatusLancamento Status,
    OrigemLancamento Origem,
    long? EstornoDeId,
    IReadOnlyList<PartidaProposta> Partidas);
