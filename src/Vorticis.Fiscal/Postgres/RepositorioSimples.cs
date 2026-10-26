using Npgsql;
using NpgsqlTypes;
using Vorticis.Fiscal.Simples;

namespace Vorticis.Fiscal.Postgres;

public sealed class RepositorioSimples(NpgsqlDataSource ds) : IRepositorioSimples
{
    public async Task<IReadOnlyList<BaseMensal>> ListarBaseMensalAsync(long empresaId, DateOnly de, DateOnly ate, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT competencia, receita_bruta, folha, origem_receita, origem_folha FROM fiscal.base_mensal
             WHERE empresa_id = @e AND competencia BETWEEN @de AND @ate ORDER BY competencia
            """).Com("e", empresaId).Com("de", de).Com("ate", ate);
        var res = new List<BaseMensal>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new BaseMensal(r.GetFieldValue<DateOnly>(0), r.IsDBNull(1) ? null : r.GetDecimal(1), r.IsDBNull(2) ? null : r.GetDecimal(2), r.GetString(3), r.GetString(4)));
        return res;
    }

    public async Task GravarBaseMensalAsync(long empresaId, BaseMensal b, long usuarioId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            INSERT INTO fiscal.base_mensal (empresa_id, competencia, receita_bruta, folha, origem_receita, origem_folha, alterado_por)
            VALUES (@e, @c, @r, @f, 'MANUAL', 'MANUAL', @u)
            ON CONFLICT (empresa_id, competencia) DO UPDATE
               SET receita_bruta = EXCLUDED.receita_bruta, folha = EXCLUDED.folha, origem_receita = 'MANUAL', origem_folha = 'MANUAL',
                   alterado_em = now(), alterado_por = EXCLUDED.alterado_por
            """).Com("e", empresaId).Com("c", b.Competencia).Com("r", NpgsqlDbType.Numeric, b.Receita).Com("f", NpgsqlDbType.Numeric, b.Folha)
                .Com("u", NpgsqlDbType.Bigint, usuarioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ExcluirBaseMensalAsync(long empresaId, DateOnly competencia, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("DELETE FROM fiscal.base_mensal WHERE empresa_id = @e AND competencia = @c").Com("e", empresaId).Com("c", competencia);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<long> GravarApuracaoAsync(long empresaId, ResultadoSimples r, long usuarioId, CancellationToken ct)
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        await Sql.DefinirUsuarioAsync(con, tx, usuarioId, ct);
        await using (var del = new NpgsqlCommand("DELETE FROM fiscal.simples_apuracao WHERE empresa_id = @e AND competencia = @c", con, tx).Com("e", empresaId).Com("c", r.Competencia))
            await del.ExecuteNonQueryAsync(ct);
        long id;
        await using (var ins = new NpgsqlCommand("""
            INSERT INTO fiscal.simples_apuracao (empresa_id, competencia, rbt12, folha_12m, fator_r, receita_mes, valor_das, vencimento, resultado, criado_por)
            VALUES (@e, @c, @rbt, @fol, @fr, @rec, @das, @venc, @res, @u) RETURNING id
            """, con, tx).Com("e", empresaId).Com("c", r.Competencia).Com("rbt", r.Rbt12).Com("fol", r.Folha12).Com("fr", r.FatorR).Com("rec", r.ReceitaMes)
                .Com("das", r.ValorDas).Com("venc", r.Vencimento).Com("res", NpgsqlDbType.Jsonb, JsonFiscal.Serializar(r)).Com("u", usuarioId))
            id = (long)(await ins.ExecuteScalarAsync(ct))!;
        // A receita do mês passa a compor o RBT12 das competências seguintes (a folha informada à mão é preservada).
        await using (var base_ = new NpgsqlCommand("""
            INSERT INTO fiscal.base_mensal (empresa_id, competencia, receita_bruta, origem_receita, alterado_por) VALUES (@e, @c, @r, 'APURACAO', @u)
            ON CONFLICT (empresa_id, competencia) DO UPDATE SET receita_bruta = EXCLUDED.receita_bruta, origem_receita = 'APURACAO', alterado_em = now(), alterado_por = EXCLUDED.alterado_por
            """, con, tx).Com("e", empresaId).Com("c", r.Competencia).Com("r", Math.Max(0m, r.ReceitaMes)).Com("u", usuarioId))
            await base_.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return id;
    }

    private const string Colunas = "id, competencia, valor_das, vencimento, lancamento_id, resultado::text";

    private static ApuracaoSimplesSalva Ler(NpgsqlDataReader r) =>
        new(r.GetInt64(0), r.GetFieldValue<DateOnly>(1), r.GetDecimal(2), r.GetFieldValue<DateOnly>(3), r.LongOuNulo(4), JsonFiscal.Ler<ResultadoSimples>(r.GetString(5)));

    public async Task<IReadOnlyList<ApuracaoSimplesSalva>> ListarApuracoesAsync(long empresaId, int ano, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand($"SELECT {Colunas} FROM fiscal.simples_apuracao WHERE empresa_id = @e AND (@a = 0 OR extract(year FROM competencia) = @a) ORDER BY competencia")
            .Com("e", empresaId).Com("a", ano);
        var res = new List<ApuracaoSimplesSalva>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(Ler(r));
        return res;
    }

    public async Task<ApuracaoSimplesSalva?> ObterApuracaoAsync(long empresaId, DateOnly competencia, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand($"SELECT {Colunas} FROM fiscal.simples_apuracao WHERE empresa_id = @e AND competencia = @c").Com("e", empresaId).Com("c", competencia);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Ler(r) : null;
    }

    public async Task ExcluirApuracaoAsync(long empresaId, long id, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("DELETE FROM fiscal.simples_apuracao WHERE id = @i AND empresa_id = @e").Com("i", id).Com("e", empresaId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
