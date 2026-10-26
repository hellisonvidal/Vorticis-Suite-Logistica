using Vorticis.Application.Seguranca;
using Vorticis.Application.Sistema;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Application.Contabil;

/// <summary>Situação do exercício para decidir se a numeração pode ser reorganizada.</summary>
public sealed record SituacaoReorganizacao(long ExercicioId, bool Encerrado, IReadOnlyList<DateOnly> PeriodosFechadosComMovimento, bool EcdGerada, int Efetivados);

/// <summary>Resultado de "Reorganizar lançamentos".</summary>
public sealed record ResumoReorganizacao(int Total, int Renumerados);

/// <summary>Conta/mês cujo saldo mensal estava divergente das partidas (antes e depois da correção).</summary>
public sealed record AlteracaoSaldo(string CodigoConta, string DescricaoConta, DateOnly Competencia, decimal DebitoAntes, decimal CreditoAntes,
                                    decimal DebitoDepois, decimal CreditoDepois);

public interface IRepositorioReorganizacao
{
    Task<SituacaoReorganizacao?> ObterSituacaoAsync(long empresaId, int ano, CancellationToken ct);

    /// <summary>Renumera (1..N, por data e número atual) os lançamentos efetivados/estornados do exercício e registra a auditoria.</summary>
    Task<ResumoReorganizacao> ReorganizarLancamentosAsync(long empresaId, long exercicioId, long? usuarioId, CancellationToken ct);

    /// <summary>Recalcula saldo_mensal a partir das partidas do exercício; devolve o que mudou e registra a auditoria.</summary>
    Task<IReadOnlyList<AlteracaoSaldo>> ReorganizarSaldosAsync(long empresaId, int ano, long? usuarioId, CancellationToken ct);
}

/// <summary>
/// Lançamentos › Utilitários. Reorganizar lançamentos: numeração sequencial sem lacunas (exige exercício e períodos abertos e
/// ECD ainda não gerada). Reorganizar saldos: refaz o cache de saldos mensais a partir das partidas efetivadas.
/// Ambos exigem a permissão de fechamento de período e a senha de administrador, e ficam na auditoria.
/// </summary>
public sealed class ServicoReorganizacao(IRepositorioReorganizacao repositorio, ServicoConfiguracoesGerais configuracoes, Autorizador autorizador)
{
    public async Task<Resultado<ResumoReorganizacao>> ReorganizarLancamentosAsync(long empresaId, int ano, string? senhaAdministrador, long usuarioId,
                                                                                  CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<ResumoReorganizacao>(usuarioId, empresaId, Permissao.PeriodoFechar, ct) is { } negado) return negado;
        var sit = await repositorio.ObterSituacaoAsync(empresaId, ano, ct);
        if (sit is null) return Resultado<ResumoReorganizacao>.Falha($"Exercício {ano} não cadastrado.");
        if (sit.Encerrado) return Resultado<ResumoReorganizacao>.Falha($"O exercício {ano} está encerrado: a numeração não pode ser alterada.");
        if (sit.PeriodosFechadosComMovimento.Count > 0)
            return Resultado<ResumoReorganizacao>.Falha("Há períodos fechados com lançamentos (" +
                string.Join(", ", sit.PeriodosFechadosComMovimento.Select(p => p.ToString("MM/yyyy"))) + "). Reabra-os antes de reorganizar.");
        if (sit.EcdGerada) return Resultado<ResumoReorganizacao>.Falha($"Já existe ECD gerada para {ano}: a numeração dos lançamentos não pode mais mudar.");
        if (!await configuracoes.VerificarSenhaAdministradorAsync(senhaAdministrador, ct))
            return Resultado<ResumoReorganizacao>.Falha("Senha de administrador incorreta. Nada foi alterado.");
        return Resultado<ResumoReorganizacao>.Ok(await repositorio.ReorganizarLancamentosAsync(empresaId, sit.ExercicioId, usuarioId, ct));
    }

    public async Task<Resultado<IReadOnlyList<AlteracaoSaldo>>> ReorganizarSaldosAsync(long empresaId, int ano, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<AlteracaoSaldo>>(usuarioId, empresaId, Permissao.PeriodoFechar, ct) is { } negado) return negado;
        if (await repositorio.ObterSituacaoAsync(empresaId, ano, ct) is null) return Resultado<IReadOnlyList<AlteracaoSaldo>>.Falha($"Exercício {ano} não cadastrado.");
        return Resultado<IReadOnlyList<AlteracaoSaldo>>.Ok(await repositorio.ReorganizarSaldosAsync(empresaId, ano, usuarioId, ct));
    }
}
