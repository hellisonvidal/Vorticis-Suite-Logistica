using Npgsql;
using Vorticis.Application.Conciliacao;

namespace Vorticis.Infra.Postgres;

public sealed class RepositorioConciliacao(NpgsqlDataSource ds) : IRepositorioConciliacao
{
    public async Task<IReadOnlyList<PartidaConciliavel>> ListarAsync(long empresaId, long? contaId, DateOnly ate, bool somenteComDocumento, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT p.id, l.id, l.numero, l.data_lancamento, p.ind_dc::text, p.valor, p.historico, p.num_arquivo, cp.conciliacao_id,
                   p.conta_id, pc.codigo, pc.descricao
              FROM contabil.partida p
              JOIN contabil.lancamento l ON l.id = p.lancamento_id
              JOIN contabil.plano_contas pc ON pc.id = p.conta_id
              LEFT JOIN contabil.conciliacao_partida cp ON cp.partida_id = p.id
             WHERE l.empresa_id = @e AND (@c::bigint IS NULL OR p.conta_id = @c) AND (NOT @doc OR btrim(COALESCE(p.num_arquivo, '')) <> '') AND l.status = 'EFETIVADO' AND l.estorno_de_id IS NULL
               AND l.data_lancamento <= @ate
             ORDER BY pc.codigo, l.data_lancamento, l.numero, p.sequencia
            """).Com("e", empresaId).Com("c", NpgsqlTypes.NpgsqlDbType.Bigint, contaId).Com("doc", somenteComDocumento).Com("ate", ate);
        var res = new List<PartidaConciliavel>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new PartidaConciliavel(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetFieldValue<DateOnly>(3), Mapeamentos.DC(r.GetString(4)),
                r.GetDecimal(5), r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetInt64(8),
                r.GetInt64(9), r.GetString(10), r.GetString(11)));
        return res;
    }

    public Task<long> CriarAsync(long empresaId, long contaId, string? documento, bool automatica, IReadOnlyList<long> partidas, long usuarioId,
                                 CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            long id;
            await using (var cmd = new NpgsqlCommand("""
                INSERT INTO contabil.conciliacao (empresa_id, conta_id, documento, automatica, criado_por) VALUES (@e, @c, @d, @a, @u) RETURNING id
                """, con, tx).Com("e", empresaId).Com("c", contaId).ComTexto("d", documento).Com("a", automatica).Com("u", usuarioId))
                id = (long)(await cmd.ExecuteScalarAsync(ct))!;
            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO contabil.conciliacao_partida (partida_id, conciliacao_id) SELECT unnest(@p), @id", con, tx)
                .Com("p", partidas.ToArray()).Com("id", id))
                await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return id;
        });

    public async Task<bool> DesfazerAsync(long empresaId, long conciliacaoId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("DELETE FROM contabil.conciliacao WHERE id = @id AND empresa_id = @e").Com("id", conciliacaoId).Com("e", empresaId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }
}
