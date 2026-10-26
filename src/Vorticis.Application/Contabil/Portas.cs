using Vorticis.Domain.Contabil;

namespace Vorticis.Application.Contabil;

/// <summary>Persistência do módulo contábil. A implementação deve gravar cada operação em uma única transação.</summary>
public interface IRepositorioContabil
{
    Task<PeriodoInfo?> ObterPeriodoAsync(long empresaId, DateOnly data, CancellationToken ct);
    Task<IReadOnlyDictionary<string, ContaInfo>> ObterContasAsync(long empresaId, IEnumerable<string> codigos, CancellationToken ct);
    Task<IReadOnlyDictionary<string, long>> ObterCentrosCustoAsync(long empresaId, IEnumerable<string> codigos, CancellationToken ct);
    Task<IReadOnlyDictionary<string, long>> ObterParticipantesAsync(long empresaId, IEnumerable<string> codigos, CancellationToken ct);
    Task<LancamentoRegistrado?> ObterLancamentoAsync(long empresaId, long lancamentoId, CancellationToken ct);

    /// <summary>
    /// Grava cabeçalho e partidas, atribui o próximo número do exercício e efetiva.
    /// Se <see cref="LancamentoValidado.EstornoDeId"/> estiver preenchido, marca o original como ESTORNADO
    /// na mesma transação. Lança <c>ViolacaoRegraException</c> se o banco rejeitar.
    /// </summary>
    Task<LancamentoEfetivado> GravarEfetivadoAsync(LancamentoValidado lancamento, long? usuarioId, CancellationToken ct);

    /// <summary>Grava vários lançamentos na mesma transação (tudo ou nada), numerados em sequência.</summary>
    /// <param name="chaves">Origem de cada lançamento importado (mesma posição), gravada para evitar duplicidade; null = sem controle.</param>
    Task<IReadOnlyList<LancamentoEfetivado>> GravarEfetivadosAsync(IReadOnlyList<LancamentoValidado> lancamentos, long? usuarioId, CancellationToken ct,
                                                                  IReadOnlyList<string?>? chaves = null);

    /// <summary>Lançamentos efetivados (não estornados) de uma origem no exercício, com as partidas.</summary>
    Task<IReadOnlyList<LancamentoRegistrado>> ListarLancamentosAtivosAsync(long empresaId, long exercicioId, OrigemLancamento origem, CancellationToken ct);

    Task<IReadOnlyList<CompetenciaStatus>> ListarPeriodosAsync(long empresaId, int ano, CancellationToken ct);

    /// <summary>Número do lançamento efetivado (não estornado) com a origem dada no exercício, se houver.</summary>
    Task<long?> ObterNumeroLancamentoAtivoAsync(long empresaId, long exercicioId, OrigemLancamento origem, CancellationToken ct);

    /// <summary>A empresa corrige lançamentos por estorno (true, padrão) ou altera/exclui o próprio lançamento (false).</summary>
    Task<bool> UsaEstornoAsync(long empresaId, CancellationToken ct);

    /// <summary>Dados para decidir se um lançamento gravado pode ser excluído ou alterado (null = não existe na empresa).</summary>
    Task<SituacaoLancamento?> ObterSituacaoLancamentoAsync(long empresaId, long lancamentoId, CancellationToken ct);

    /// <summary>
    /// Exclui o lançamento (e o par estorno/original, se houver): remove as partidas, desfaz o saldo mensal e registra o que foi
    /// excluído na trilha de auditoria, numa só transação. Devolve os lançamentos removidos.
    /// </summary>
    Task<IReadOnlyList<LancamentoEfetivado>> ExcluirLancamentoAsync(long empresaId, long lancamentoId, long? usuarioId, CancellationToken ct);

    /// <summary>Regrava data e partidas no mesmo lançamento e no mesmo número (sem estorno), com saldo e auditoria.</summary>
    Task AlterarLancamentoAsync(long lancamentoId, LancamentoValidado novo, long? usuarioId, CancellationToken ct);

    Task<IReadOnlyList<Inconsistencia>> VerificarInvariantesAsync(long empresaId, DateOnly competencia, CancellationToken ct);
    Task AlterarStatusPeriodoAsync(long empresaId, DateOnly competencia, bool fechar, long? usuarioId, CancellationToken ct);
}

/// <summary>Um lançamento ligado ao consultado por estorno (o original ou o estorno dele).</summary>
public sealed record VinculoLancamento(long Id, long? Numero, OrigemLancamento Origem);

/// <summary>Situação de um lançamento gravado para as regras de exclusão e alteração.</summary>
public sealed record SituacaoLancamento(
    long Id, long EmpresaId, long ExercicioId, int Ano, long? Numero, DateOnly Data, StatusLancamento Status, OrigemLancamento Origem,
    IndicadorLancamento Indicador, long? EstornoDeId, bool ExercicioEncerrado, bool PeriodoAberto, bool EcdGerada, DateOnly? DataBloqueio,
    bool Conciliado, IReadOnlyList<VinculoLancamento> Vinculados);

public sealed record CompetenciaStatus(DateOnly Competencia, bool Aberto);

public sealed record Inconsistencia(string Codigo, string Descricao);

/// <summary>Consultas de leitura (relatórios). Não passam pelas regras de escrita.</summary>
public interface IConsultasContabeis
{
    Task<IReadOnlyList<ContaPlano>> ObterPlanoAsync(long empresaId, CancellationToken ct);

    /// <summary>Saldo anterior a <paramref name="inicio"/> e movimento no intervalo, por conta analítica.</summary>
    /// <param name="excluirEncerramento">Ignora lançamentos de encerramento (IND_LCTO = E) no movimento do intervalo — usado na DRE.</param>
    Task<IReadOnlyList<MovimentoConta>> ObterMovimentosAsync(long empresaId, DateOnly inicio, DateOnly fim, CancellationToken ct,
                                                             bool excluirEncerramento = false);

    Task<RazaoConta?> ObterRazaoAsync(long empresaId, string codigoConta, DateOnly inicio, DateOnly fim, CancellationToken ct);
    /// <summary>Partidas dos lançamentos do período (para as mutações do PL), marcando as de encerramento.</summary>
    Task<IReadOnlyList<PartidaMutacao>> ObterPartidasAsync(long empresaId, DateOnly inicio, DateOnly fim, CancellationToken ct);
    /// <summary>Saldo anterior e movimento por conta e participante (só partidas com participante).</summary>
    Task<IReadOnlyList<MovimentoParticipante>> ObterMovimentosParticipantesAsync(long empresaId, DateOnly inicio, DateOnly fim, CancellationToken ct);
    Task<IReadOnlyList<LinhaDiario>> ObterDiarioAsync(long empresaId, DateOnly inicio, DateOnly fim, CancellationToken ct);

    /// <summary>Centros de custo da empresa (para somar analíticos nos sintéticos).</summary>
    Task<IReadOnlyList<CentroPlano>> ObterCentrosAsync(long empresaId, CancellationToken ct);

    /// <summary>Movimento por conta, centro de custo (0 = sem centro) e mês, sem rascunhos nem lançamentos de encerramento.</summary>
    Task<IReadOnlyList<MovimentoCentro>> ObterMovimentosCentroAsync(long empresaId, DateOnly inicio, DateOnly fim, CancellationToken ct);

    /// <summary>Partidas do período com os dados do lançamento, para detalhar o relatório por centro de custo.</summary>
    Task<IReadOnlyList<DetalheCentro>> ObterDetalhesCentroAsync(long empresaId, DateOnly inicio, DateOnly fim, CancellationToken ct);
}

/// <summary>Persistência do orçamento anual.</summary>
public interface IRepositorioOrcamento
{
    Task<IReadOnlyList<LinhaOrcamento>> ListarAsync(long empresaId, int ano, CancellationToken ct);
    /// <summary>Substitui todo o orçamento do ano (numa transação).</summary>
    Task SubstituirAsync(long empresaId, int ano, IReadOnlyList<LinhaOrcamento> linhas, CancellationToken ct);
}

public sealed record LinhaRazao(DateOnly Data, long Numero, string Historico, IndicadorDC DC, decimal Valor, decimal SaldoAcumulado, bool Estornado);

public sealed record RazaoConta(string Codigo, string Descricao, decimal SaldoAnterior, IReadOnlyList<LinhaRazao> Linhas)
{
    public decimal SaldoFinal => Linhas.Count == 0 ? SaldoAnterior : Linhas[^1].SaldoAcumulado;
}

public sealed record LinhaDiario(long LancamentoId, long Numero, DateOnly Data, short Sequencia, string CodigoConta, string DescricaoConta,
                                 IndicadorDC DC, decimal Valor, string Historico, StatusLancamento Status, string? Documento = null,
                                 string? CentroCusto = null, string? Participante = null);
