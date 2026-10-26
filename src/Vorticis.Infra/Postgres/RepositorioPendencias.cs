using Npgsql;
using Vorticis.Application.Contabil;

namespace Vorticis.Infra.Postgres;

/// <summary>Tipo de contabilização da empresa, lotes e pendências de contabilização (contabil.contabilizacao_config, lote_contabilizacao, pendencia_contabilizacao).</summary>
public sealed class RepositorioPendencias(NpgsqlDataSource ds) : IRepositorioPendencias
{
    private const string Colunas = "id, empresa_id, origem, tipo, referencia, periodo, descricao, valor, situacao, lote_id, lancamento_id, mensagem, criado_em";

    public async Task<ModoContabilizacao> ModoAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT modo FROM contabil.contabilizacao_config WHERE empresa_id = @e").Com("e", empresaId);
        return await cmd.ExecuteScalarAsync(ct) is "LOTE" ? ModoContabilizacao.Lote : ModoContabilizacao.Online;
    }

    public async Task GravarModoAsync(long empresaId, ModoContabilizacao modo, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            INSERT INTO contabil.contabilizacao_config (empresa_id, modo) VALUES (@e, @m)
            ON CONFLICT (empresa_id) DO UPDATE SET modo = @m, atualizado_em = now()
            """).Com("e", empresaId).Com("m", modo == ModoContabilizacao.Lote ? "LOTE" : "ONLINE");
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> RegistrarAsync(long empresaId, NovaPendencia p, long usuarioId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            INSERT INTO contabil.pendencia_contabilizacao (empresa_id, origem, tipo, referencia, periodo, descricao, valor, criado_por)
            VALUES (@e, @o, @t, @r, @p, @d, @v, @u)
            ON CONFLICT (empresa_id, origem, tipo, referencia) WHERE situacao = 'PENDENTE' DO NOTHING
            """).Com("e", empresaId).Com("o", p.Origem).Com("t", p.Tipo).Com("r", p.Referencia).Com("p", p.Periodo).Com("d", Limitar(p.Descricao, 250))
                .Com("v", p.Valor).Com("u", usuarioId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    private static string Limitar(string s, int max) => s.Length <= max ? s : s[..max];

    private static PendenciaContabil Ler(NpgsqlDataReader r) => new(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetString(4),
        DateOnly.FromDateTime(r.GetDateTime(5)), r.GetString(6), r.GetDecimal(7), r.GetString(8), r.IsDBNull(9) ? null : r.GetInt64(9),
        r.IsDBNull(10) ? null : r.GetInt64(10), r.IsDBNull(11) ? null : r.GetString(11), r.GetDateTime(12));

    public async Task<IReadOnlyList<PendenciaContabil>> ListarAsync(long empresaId, FiltroPendencias f, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand($"""
            SELECT {Colunas} FROM contabil.pendencia_contabilizacao
             WHERE empresa_id = @e AND (@o::text IS NULL OR origem = @o) AND (@i::date IS NULL OR periodo >= @i) AND (@f::date IS NULL OR periodo <= @f)
               AND (@s::text IS NULL OR situacao = @s) AND (@l::bigint IS NULL OR lote_id = @l)
             ORDER BY periodo, origem, id
            """).Com("e", empresaId).Com("o", NpgsqlTypes.NpgsqlDbType.Text, f.Origem).Com("i", NpgsqlTypes.NpgsqlDbType.Date, f.Inicio)
                .Com("f", NpgsqlTypes.NpgsqlDbType.Date, f.Fim).Com("s", NpgsqlTypes.NpgsqlDbType.Text, f.Situacao).Com("l", NpgsqlTypes.NpgsqlDbType.Bigint, f.LoteId);
        var res = new List<PendenciaContabil>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(Ler(r));
        return res;
    }

    public async Task<PendenciaContabil?> ObterAsync(long empresaId, long id, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand($"SELECT {Colunas} FROM contabil.pendencia_contabilizacao WHERE id = @i AND empresa_id = @e").Com("i", id).Com("e", empresaId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Ler(r) : null;
    }

    public async Task<long> CriarLoteAsync(long empresaId, long usuarioId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("INSERT INTO contabil.lote_contabilizacao (empresa_id, criado_por) VALUES (@e, @u) RETURNING id").Com("e", empresaId).Com("u", usuarioId);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task MarcarAsync(long id, string situacao, long? loteId, long? lancamentoId, string? mensagem, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            UPDATE contabil.pendencia_contabilizacao SET situacao = @s, lote_id = COALESCE(@l, lote_id), lancamento_id = @n, mensagem = @m WHERE id = @i
            """).Com("s", situacao).Com("l", NpgsqlTypes.NpgsqlDbType.Bigint, loteId).Com("n", NpgsqlTypes.NpgsqlDbType.Bigint, lancamentoId)
                .Com("m", NpgsqlTypes.NpgsqlDbType.Varchar, mensagem is { Length: > 500 } ? mensagem[..500] : mensagem).Com("i", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<LoteContabil>> ListarLotesAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT l.id, l.criado_em, COUNT(p.id) FILTER (WHERE p.situacao = 'CONTABILIZADA'), COALESCE(SUM(p.valor) FILTER (WHERE p.situacao = 'CONTABILIZADA'), 0), l.desfeito_em
              FROM contabil.lote_contabilizacao l LEFT JOIN contabil.pendencia_contabilizacao p ON p.lote_id = l.id
             WHERE l.empresa_id = @e GROUP BY l.id ORDER BY l.id DESC
            """).Com("e", empresaId);
        var res = new List<LoteContabil>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(new LoteContabil(r.GetInt64(0), r.GetDateTime(1), (int)r.GetInt64(2), r.GetDecimal(3), r.IsDBNull(4) ? null : r.GetDateTime(4)));
        return res;
    }

    public async Task<bool> LoteDesfeitoAsync(long empresaId, long loteId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT desfeito_em IS NOT NULL FROM contabil.lote_contabilizacao WHERE id = @l AND empresa_id = @e").Com("l", loteId).Com("e", empresaId);
        return await cmd.ExecuteScalarAsync(ct) is true;
    }

    public async Task MarcarLoteDesfeitoAsync(long loteId, long usuarioId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("UPDATE contabil.lote_contabilizacao SET desfeito_em = now(), desfeito_por = @u WHERE id = @l").Com("u", usuarioId).Com("l", loteId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
