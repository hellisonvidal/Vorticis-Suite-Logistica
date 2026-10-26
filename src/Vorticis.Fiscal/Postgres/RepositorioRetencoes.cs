using Npgsql;
using NpgsqlTypes;
using Vorticis.Fiscal.Retencoes;

namespace Vorticis.Fiscal.Postgres;

public sealed class RepositorioRetencoes(NpgsqlDataSource ds) : IRepositorioRetencoes
{
    private static string SentidoSql(SentidoRetencao s) => s == SentidoRetencao.Sofrida ? "SOFRIDA" : "EFETUADA";
    private static SentidoRetencao Sentido(string s) => s == "SOFRIDA" ? SentidoRetencao.Sofrida : SentidoRetencao.Efetuada;

    public async Task<IReadOnlyList<Retencao>> ListarAsync(long empresaId, DateOnly inicio, DateOnly fim, SentidoRetencao? sentido, TributoRetido? tributo, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT r.id, r.sentido, r.tributo, r.competencia, r.documento_id, d.numero, r.participante_id, p.nome, r.base_calculo, r.aliquota, r.valor,
                   r.codigo_receita, r.origem, COALESCE(r.descricao, '')
              FROM fiscal.retencao r
              LEFT JOIN fiscal.documento d ON d.id = r.documento_id
              LEFT JOIN core.participante p ON p.id = r.participante_id
             WHERE r.empresa_id = @e AND r.competencia BETWEEN @ini AND @fim
               AND (@s::text IS NULL OR r.sentido = @s) AND (@t::text IS NULL OR r.tributo = @t)
             ORDER BY r.competencia, r.sentido, r.tributo, d.numero, r.id
            """).Com("e", empresaId).Com("ini", new DateOnly(inicio.Year, inicio.Month, 1)).Com("fim", fim)
                .Com("s", NpgsqlDbType.Text, sentido is { } s ? SentidoSql(s) : null).Com("t", NpgsqlDbType.Text, tributo?.ToString().ToUpperInvariant());
        var res = new List<Retencao>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new Retencao(r.GetInt64(0), Sentido(r.GetString(1)), Enum.Parse<TributoRetido>(r.GetString(2)), r.GetFieldValue<DateOnly>(3), r.LongOuNulo(4), r.Texto(5),
                r.LongOuNulo(6), r.Texto(7), r.GetDecimal(8), r.GetDecimal(9), r.GetDecimal(10), r.Texto(11), r.GetString(12), r.Texto(13)));
        return res;
    }

    public async Task<int> RegerarDeNotasAsync(long empresaId, DateOnly inicio, DateOnly fim, IReadOnlyList<NovaRetencao> retencoes, long usuarioId, CancellationToken ct)
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        await Sql.DefinirUsuarioAsync(con, tx, usuarioId, ct);
        await using (var del = new NpgsqlCommand("""
            DELETE FROM fiscal.retencao r USING fiscal.documento d
             WHERE r.documento_id = d.id AND r.empresa_id = @e AND r.origem = 'NOTA' AND d.data_entrada_saida BETWEEN @ini AND @fim
            """, con, tx).Com("e", empresaId).Com("ini", inicio).Com("fim", fim))
            await del.ExecuteNonQueryAsync(ct);
        foreach (var n in retencoes)
        {
            await using var ins = new NpgsqlCommand("""
                INSERT INTO fiscal.retencao (empresa_id, sentido, tributo, competencia, documento_id, participante_id, base_calculo, aliquota, valor, codigo_receita,
                                             origem, descricao, criado_por)
                VALUES (@e, @s, @t, @c, @d, @p, @b, @a, @v, @cod, 'NOTA', @desc, @u)
                """, con, tx).Com("e", empresaId).Com("s", SentidoSql(n.Sentido)).Com("t", n.Tributo.ToString().ToUpperInvariant()).Com("c", n.Competencia)
                .Com("d", NpgsqlDbType.Bigint, n.DocumentoId).Com("p", NpgsqlDbType.Bigint, n.ParticipanteId).Com("b", n.BaseCalculo).Com("a", n.Aliquota)
                .Com("v", n.Valor).ComTexto("cod", n.CodigoReceita).ComTexto("desc", n.Descricao).Com("u", usuarioId);
            await ins.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return retencoes.Count;
    }

    public Task<long> GravarManualAsync(long empresaId, NovaRetencao n, long? participanteId, long usuarioId, CancellationToken ct)
        => Sql.TraduzirErrosAsync(async () =>
        {
            await using var cmd = ds.CreateCommand("""
                INSERT INTO fiscal.retencao (empresa_id, sentido, tributo, competencia, participante_id, base_calculo, aliquota, valor, codigo_receita, origem, descricao, criado_por)
                VALUES (@e, @s, @t, @c, @p, @b, @a, @v, @cod, 'MANUAL', @desc, @u) RETURNING id
                """).Com("e", empresaId).Com("s", SentidoSql(n.Sentido)).Com("t", n.Tributo.ToString().ToUpperInvariant()).Com("c", n.Competencia)
                .Com("p", NpgsqlDbType.Bigint, participanteId).Com("b", n.BaseCalculo).Com("a", n.Aliquota).Com("v", n.Valor).ComTexto("cod", n.CodigoReceita)
                .ComTexto("desc", n.Descricao).Com("u", usuarioId);
            return (long)(await cmd.ExecuteScalarAsync(ct))!;
        });

    public async Task ExcluirAsync(long empresaId, long id, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("DELETE FROM fiscal.retencao WHERE id = @i AND empresa_id = @e").Com("i", id).Com("e", empresaId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<decimal> SomarAsync(long empresaId, SentidoRetencao sentido, TributoRetido tributo, DateOnly inicio, DateOnly fim, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT COALESCE(SUM(valor), 0) FROM fiscal.retencao
             WHERE empresa_id = @e AND sentido = @s AND tributo = @t AND competencia BETWEEN @ini AND @fim
            """).Com("e", empresaId).Com("s", SentidoSql(sentido)).Com("t", tributo.ToString().ToUpperInvariant()).Com("ini", new DateOnly(inicio.Year, inicio.Month, 1)).Com("fim", fim);
        return (decimal)(await cmd.ExecuteScalarAsync(ct))!;
    }
}
