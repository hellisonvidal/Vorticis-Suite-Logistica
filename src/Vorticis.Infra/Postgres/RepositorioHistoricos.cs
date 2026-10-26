using Npgsql;
using Vorticis.Application.Contabil;

namespace Vorticis.Infra.Postgres;

public sealed class RepositorioHistoricos(NpgsqlDataSource ds) : IRepositorioHistoricos
{
    public async Task<IReadOnlyList<HistoricoPadrao>> ListarAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT id, codigo, descricao FROM contabil.historico_padrao WHERE empresa_id = @e").Com("e", empresaId);
        var res = new List<HistoricoPadrao>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(new HistoricoPadrao(r.GetInt64(0), r.GetString(1), r.GetString(2)));
        return res;
    }

    public Task<long> IncluirAsync(long empresaId, string codigo, string descricao, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var cmd = ds.CreateCommand(
                "INSERT INTO contabil.historico_padrao (empresa_id, codigo, descricao) VALUES (@e, @c, @d) RETURNING id")
                .Com("e", empresaId).Com("c", codigo).Com("d", descricao);
            return (long)(await cmd.ExecuteScalarAsync(ct))!;
        });

    public async Task AlterarAsync(long id, string descricao, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("UPDATE contabil.historico_padrao SET descricao = @d WHERE id = @id").Com("d", descricao).Com("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ExcluirAsync(long id, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("DELETE FROM contabil.historico_padrao WHERE id = @id").Com("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
