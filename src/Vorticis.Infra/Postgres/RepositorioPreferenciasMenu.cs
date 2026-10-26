using Npgsql;
using Vorticis.Application.Sistema;

namespace Vorticis.Infra.Postgres;

/// <summary>Itens de menu ocultos em core.preferencia_menu (usuario_id nulo = regra do administrador para todos).</summary>
public sealed class RepositorioPreferenciasMenu(NpgsqlDataSource ds) : IRepositorioPreferenciasMenu
{
    public async Task<PreferenciasMenu> ObterAsync(string programa, long usuarioId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT usuario_id IS NULL, chave FROM core.preferencia_menu WHERE programa = @p AND (usuario_id IS NULL OR usuario_id = @u)")
                                .Com("p", programa).Com("u", usuarioId);
        HashSet<string> usuario = [], todos = [];
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) (r.GetBoolean(0) ? todos : usuario).Add(r.GetString(1));
        return new PreferenciasMenu(usuario, todos);
    }

    public async Task SubstituirAsync(string programa, long? usuarioId, IReadOnlyCollection<string> ocultos, CancellationToken ct)
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        await using (var del = new NpgsqlCommand("DELETE FROM core.preferencia_menu WHERE programa = @p AND usuario_id IS NOT DISTINCT FROM @u", con, tx)
                         .Com("p", programa).Com("u", NpgsqlTypes.NpgsqlDbType.Bigint, usuarioId))
            await del.ExecuteNonQueryAsync(ct);
        foreach (var chave in ocultos)
        {
            await using var ins = new NpgsqlCommand("INSERT INTO core.preferencia_menu (programa, usuario_id, chave) VALUES (@p, @u, @c)", con, tx)
                .Com("p", programa).Com("u", NpgsqlTypes.NpgsqlDbType.Bigint, usuarioId).Com("c", chave);
            await ins.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }
}
