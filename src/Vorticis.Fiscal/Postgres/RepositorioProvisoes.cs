using Npgsql;
using NpgsqlTypes;
using Vorticis.Fiscal.Guias;

namespace Vorticis.Fiscal.Postgres;

public sealed class RepositorioProvisoes(NpgsqlDataSource ds) : IRepositorioProvisoes
{
    public async Task<IReadOnlyList<ContaTributo>> ListarContasAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT t.tributo, d.codigo, p.codigo FROM fiscal.conta_tributo t
              JOIN contabil.plano_contas d ON d.id = t.conta_despesa_id JOIN contabil.plano_contas p ON p.id = t.conta_passivo_id
             WHERE t.empresa_id = @e ORDER BY t.tributo
            """).Com("e", empresaId);
        var res = new List<ContaTributo>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(new ContaTributo(r.GetString(0), r.GetString(1), r.GetString(2)));
        return res;
    }

    public async Task GravarContaAsync(long empresaId, string tributo, long contaDespesaId, long contaPassivoId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            INSERT INTO fiscal.conta_tributo (empresa_id, tributo, conta_despesa_id, conta_passivo_id) VALUES (@e, @t, @d, @p)
            ON CONFLICT (empresa_id, tributo) DO UPDATE SET conta_despesa_id = EXCLUDED.conta_despesa_id, conta_passivo_id = EXCLUDED.conta_passivo_id
            """).Com("e", empresaId).Com("t", tributo).Com("d", contaDespesaId).Com("p", contaPassivoId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ExcluirContaAsync(long empresaId, string tributo, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("DELETE FROM fiscal.conta_tributo WHERE empresa_id = @e AND tributo = @t").Com("e", empresaId).Com("t", tributo);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DefinirLancamentoAsync(long empresaId, TipoApuracao tipo, long apuracaoId, long? lancamentoId, CancellationToken ct)
    {
        var tabela = tipo switch { TipoApuracao.Simples => "fiscal.simples_apuracao", TipoApuracao.Lucro => "fiscal.apuracao_lucro", _ => "fiscal.apuracao_pis_cofins" };
        await using var cmd = ds.CreateCommand($"UPDATE {tabela} SET lancamento_id = @l WHERE id = @i AND empresa_id = @e")
            .Com("l", NpgsqlDbType.Bigint, lancamentoId).Com("i", apuracaoId).Com("e", empresaId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
