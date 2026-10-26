using Npgsql;
using Vorticis.Application.Sistema;

namespace Vorticis.Infra.Postgres;

/// <summary>Parâmetros gerais em core.configuracao_sistema (chave/valor).</summary>
public sealed class RepositorioConfiguracao(NpgsqlDataSource ds) : IRepositorioConfiguracao
{
    public async Task<IReadOnlyDictionary<string, (string Valor, DateTimeOffset AlteradoEm)>> LerAsync(IEnumerable<string> chaves, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT chave, valor, alterado_em FROM core.configuracao_sistema WHERE chave = ANY(@c)")
                                .Com("c", chaves.ToArray());
        var res = new Dictionary<string, (string, DateTimeOffset)>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res[r.GetString(0)] = (r.GetString(1), r.GetFieldValue<DateTimeOffset>(2));
        return res;
    }

    public async Task GravarAsync(IReadOnlyDictionary<string, string?> valores, long? usuarioId, CancellationToken ct)
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        foreach (var (chave, valor) in valores)
        {
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO core.configuracao_sistema (chave, valor, alterado_por) VALUES (@c, @v, @u)
                ON CONFLICT (chave) DO UPDATE SET valor = EXCLUDED.valor, alterado_em = now(), alterado_por = EXCLUDED.alterado_por
                """, con, tx).Com("c", chave).Com("v", valor ?? "").Com("u", NpgsqlTypes.NpgsqlDbType.Bigint, usuarioId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public Task<int> LimparBancoAsync(long usuarioId, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await Mapeamentos.DefinirUsuarioAsync(con, tx, usuarioId, ct);
            int qtd;
            await using (var cmd = new NpgsqlCommand("SELECT core.fn_limpar_banco()", con, tx) { CommandTimeout = 600 })
            {
                qtd = (int)(await cmd.ExecuteScalarAsync(ct))!;
            }
            await tx.CommitAsync(ct);
            return qtd;
        });
}
