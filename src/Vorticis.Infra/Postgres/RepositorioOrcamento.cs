using Npgsql;
using Vorticis.Application.Contabil;
using Vorticis.Domain.Contabil;

namespace Vorticis.Infra.Postgres;

public sealed class RepositorioOrcamento(NpgsqlDataSource ds) : IRepositorioOrcamento
{
    public async Task<IReadOnlyList<LinhaOrcamento>> ListarAsync(long empresaId, int ano, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT o.conta_id, COALESCE(o.centro_custo_id, 0), o.mes, o.valor
              FROM contabil.orcamento o JOIN contabil.plano_contas c ON c.id = o.conta_id
             WHERE o.empresa_id = @e AND o.ano = @a ORDER BY c.codigo, 2, o.mes
            """).Com("e", empresaId).Com("a", (short)ano);
        var mapa = new Dictionary<(long, long), decimal[]>();
        var ordem = new List<(long, long)>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var k = (r.GetInt64(0), r.GetInt64(1));
            if (!mapa.TryGetValue(k, out var v)) { mapa[k] = v = new decimal[12]; ordem.Add(k); }
            v[r.GetInt16(2) - 1] = r.GetDecimal(3);
        }
        return ordem.Select(k => new LinhaOrcamento(k.Item1, k.Item2, mapa[k])).ToList();
    }

    public async Task SubstituirAsync(long empresaId, int ano, IReadOnlyList<LinhaOrcamento> linhas, CancellationToken ct)
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        await using (var del = new NpgsqlCommand("DELETE FROM contabil.orcamento WHERE empresa_id = @e AND ano = @a", con, tx))
        {
            del.Parameters.AddWithValue("e", empresaId);
            del.Parameters.AddWithValue("a", (short)ano);
            await del.ExecuteNonQueryAsync(ct);
        }
        foreach (var l in linhas)
            for (var m = 0; m < 12; m++)
            {
                if (l.Valores[m] == 0) continue;
                await using var ins = new NpgsqlCommand(
                    "INSERT INTO contabil.orcamento (empresa_id, ano, mes, conta_id, centro_custo_id, valor) VALUES (@e, @a, @m, @c, @cc, @v)", con, tx);
                ins.Parameters.AddWithValue("e", empresaId);
                ins.Parameters.AddWithValue("a", (short)ano);
                ins.Parameters.AddWithValue("m", (short)(m + 1));
                ins.Parameters.AddWithValue("c", l.ContaId);
                ins.Parameters.AddWithValue("cc", l.CentroId == 0 ? DBNull.Value : l.CentroId);
                ins.Parameters.AddWithValue("v", l.Valores[m]);
                await ins.ExecuteNonQueryAsync(ct);
            }
        await tx.CommitAsync(ct);
    }
}
