using Npgsql;
using NpgsqlTypes;
using Vorticis.Application.Contabil;
using Vorticis.Domain.Contabil;

namespace Vorticis.Infra.Postgres;

public sealed class RepositorioPlanoReferencial(NpgsqlDataSource ds) : IRepositorioPlanoReferencial
{
    public async Task<IReadOnlyList<TabelaReferencialImportada>> ListarTabelasAsync(CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT DISTINCT ON (cod_plan_ref, ano_vigencia) cod_plan_ref, ano_vigencia, contas, arquivo, importado_em
              FROM contabil.plano_referencial_importacao
             ORDER BY cod_plan_ref, ano_vigencia, importado_em DESC
            """);
        var res = new List<TabelaReferencialImportada>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new TabelaReferencialImportada(r.GetString(0), r.GetInt16(1), r.GetInt32(2), r.GetString(3), r.GetFieldValue<DateTimeOffset>(4)));
        return res.OrderByDescending(t => t.Ano).ThenBy(t => t.CodPlanRef.Length).ThenBy(t => t.CodPlanRef).ToArray();
    }

    public async Task<IReadOnlyList<ContaReferencial>> ListarContasAsync(string codPlanRef, int ano, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT codigo, descricao, tipo = 'A', nivel, codigo_pai, cod_nat, dt_ini, dt_fim
              FROM contabil.plano_referencial WHERE cod_plan_ref = @p AND ano_vigencia = @a ORDER BY codigo
            """).Com("p", codPlanRef).Com("a", (short)ano);
        var res = new List<ContaReferencial>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new ContaReferencial(r.GetString(0), r.GetString(1), r.GetBoolean(2), r.GetInt16(3), r.IsDBNull(4) ? null : r.GetString(4),
                r.GetString(5), r.IsDBNull(6) ? null : r.GetFieldValue<DateOnly>(6), r.IsDBNull(7) ? null : r.GetFieldValue<DateOnly>(7)));
        return res;
    }

    public Task<IReadOnlyList<string>> GravarTabelaAsync(string codPlanRef, int ano, IReadOnlyList<ContaReferencial> contas, string arquivo, string sha256,
                                                         long usuarioId, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await using (var cmd = new NpgsqlCommand("""
                INSERT INTO contabil.plano_referencial (cod_plan_ref, ano_vigencia, codigo, descricao, tipo, nivel, codigo_pai, cod_nat, dt_ini, dt_fim)
                SELECT @p, @a, t.c, t.d, t.t::contabil.tipo_conta, t.n, t.pai, t.nat, t.ini, t.fim
                  FROM unnest(@cod, @desc, @tipo, @niv, @pai, @nat, @ini, @fim) AS t(c, d, t, n, pai, nat, ini, fim)
                ON CONFLICT (cod_plan_ref, ano_vigencia, codigo) DO UPDATE
                   SET descricao = EXCLUDED.descricao, tipo = EXCLUDED.tipo, nivel = EXCLUDED.nivel,
                       codigo_pai = EXCLUDED.codigo_pai, cod_nat = EXCLUDED.cod_nat, dt_ini = EXCLUDED.dt_ini, dt_fim = EXCLUDED.dt_fim
                """, con, tx)
                .Com("p", codPlanRef).Com("a", (short)ano)
                .Com("cod", contas.Select(c => c.Codigo).ToArray())
                .Com("desc", contas.Select(c => c.Descricao.Length > 400 ? c.Descricao[..400] : c.Descricao).ToArray())
                .Com("tipo", contas.Select(c => c.Analitica ? "A" : "S").ToArray())
                .Com("niv", contas.Select(c => c.Nivel).ToArray())
                .Com("pai", NpgsqlDbType.Array | NpgsqlDbType.Varchar, contas.Select(c => c.CodigoPai).ToArray())
                .Com("nat", contas.Select(c => c.CodNat).ToArray())
                .Com("ini", NpgsqlDbType.Array | NpgsqlDbType.Date, contas.Select(c => c.Inicio).ToArray())
                .Com("fim", NpgsqlDbType.Array | NpgsqlDbType.Date, contas.Select(c => c.Fim).ToArray()))
                await cmd.ExecuteNonQueryAsync(ct);

            // Contas que saíram da tabela: remove as que não estão vinculadas; devolve as vinculadas para revisão.
            var mantidas = new List<string>();
            await using (var cmd = new NpgsqlCommand("""
                WITH removidas AS (
                    SELECT r.id, r.codigo, EXISTS (SELECT 1 FROM contabil.mapeamento_referencial m WHERE m.plano_referencial_id = r.id) AS em_uso
                      FROM contabil.plano_referencial r
                     WHERE r.cod_plan_ref = @p AND r.ano_vigencia = @a AND NOT (r.codigo = ANY(@cod))),
                apagadas AS (DELETE FROM contabil.plano_referencial WHERE id IN (SELECT id FROM removidas WHERE NOT em_uso))
                SELECT codigo FROM removidas WHERE em_uso ORDER BY codigo
                """, con, tx).Com("p", codPlanRef).Com("a", (short)ano).Com("cod", contas.Select(c => c.Codigo).ToArray()))
            await using (var r = await cmd.ExecuteReaderAsync(ct))
                while (await r.ReadAsync(ct)) mantidas.Add(r.GetString(0));

            await using (var cmd = new NpgsqlCommand("""
                INSERT INTO contabil.plano_referencial_importacao (cod_plan_ref, ano_vigencia, arquivo, sha256, contas, importado_por)
                VALUES (@p, @a, @arq, @h, @n, @u)
                """, con, tx).Com("p", codPlanRef).Com("a", (short)ano).Com("arq", arquivo.Length > 260 ? arquivo[..260] : arquivo)
                             .Com("h", sha256).Com("n", contas.Count).Com("u", usuarioId))
                await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return (IReadOnlyList<string>)mantidas;
        });

    public async Task<string?> ObterPlanoDaEmpresaAsync(long empresaId, int ano, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT cod_plan_ref FROM contabil.exercicio WHERE empresa_id = @e AND ano = @a")
                                .Com("e", empresaId).Com("a", (short)ano);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    public async Task DefinirPlanoDaEmpresaAsync(long empresaId, int ano, string codPlanRef, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("UPDATE contabil.exercicio SET cod_plan_ref = @p WHERE empresa_id = @e AND ano = @a")
                                .Com("p", codPlanRef).Com("e", empresaId).Com("a", (short)ano);
        if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new Domain.Comum.ViolacaoRegraException($"Exercício {ano} não cadastrado.");
    }

    public async Task<IReadOnlyList<ContaMapeamento>> ListarMapeamentoAsync(long empresaId, int ano, string codPlanRef, CancellationToken ct)
    {
        // "Movimento no ano": lançamento no ano ou saldo diferente de zero ao fim dele (ambos exigem I051 na ECD).
        await using var cmd = ds.CreateCommand("""
            SELECT c.id, c.codigo, c.descricao, c.cod_nat,
                   EXISTS (SELECT 1 FROM contabil.saldo_mensal s WHERE s.conta_id = c.id
                              AND s.competencia BETWEEN make_date(@a, 1, 1) AND make_date(@a, 12, 1)
                              AND (s.total_debito <> 0 OR s.total_credito <> 0))
                   OR COALESCE((SELECT SUM(s.total_debito - s.total_credito) FROM contabil.saldo_mensal s
                                 WHERE s.conta_id = c.id AND s.competencia <= make_date(@a, 12, 1)), 0) <> 0,
                   r.codigo, r.descricao
              FROM contabil.plano_contas c
              LEFT JOIN contabil.mapeamento_referencial m ON m.conta_id = c.id AND m.centro_custo_id IS NULL AND m.ano_vigencia = @a
              LEFT JOIN contabil.plano_referencial r ON r.id = m.plano_referencial_id AND r.cod_plan_ref = @p
             WHERE c.empresa_id = @e AND c.tipo = 'A'
             ORDER BY c.codigo
            """).Com("e", empresaId).Com("a", (short)ano).Com("p", codPlanRef);
        var res = new List<ContaMapeamento>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new ContaMapeamento(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetBoolean(4),
                                        r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6)));
        return res;
    }

    public Task GravarVinculosAsync(long empresaId, int ano, string codPlanRef, IReadOnlyDictionary<long, string?> vinculos, long usuarioId,
                                    CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await Mapeamentos.DefinirUsuarioAsync(con, tx, usuarioId, ct);
            foreach (var (contaId, codigo) in vinculos)
            {
                await using (var cmd = new NpgsqlCommand("""
                    DELETE FROM contabil.mapeamento_referencial
                     WHERE conta_id = @c AND centro_custo_id IS NULL AND ano_vigencia = @a AND empresa_id = @e
                    """, con, tx).Com("c", contaId).Com("a", (short)ano).Com("e", empresaId))
                    await cmd.ExecuteNonQueryAsync(ct);
                if (codigo is null) continue;
                await using var ins = new NpgsqlCommand("""
                    INSERT INTO contabil.mapeamento_referencial (conta_id, plano_referencial_id, ano_vigencia, empresa_id)
                    SELECT @c, r.id, @a, @e FROM contabil.plano_referencial r
                     WHERE r.cod_plan_ref = @p AND r.ano_vigencia = @a AND r.codigo = @cod
                    """, con, tx).Com("c", contaId).Com("a", (short)ano).Com("e", empresaId).Com("p", codPlanRef).Com("cod", codigo);
                if (await ins.ExecuteNonQueryAsync(ct) != 1)
                    throw new Domain.Comum.ViolacaoRegraException($"Referencial {codigo} não encontrada na tabela {codPlanRef}/{ano}.");
            }
            await tx.CommitAsync(ct);
            return 0;
        });
}
