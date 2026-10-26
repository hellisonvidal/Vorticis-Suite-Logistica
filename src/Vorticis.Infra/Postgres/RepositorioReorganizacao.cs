using Npgsql;
using Vorticis.Application.Contabil;

namespace Vorticis.Infra.Postgres;

public sealed class RepositorioReorganizacao(NpgsqlDataSource ds) : IRepositorioReorganizacao
{
    public async Task<SituacaoReorganizacao?> ObterSituacaoAsync(long empresaId, int ano, CancellationToken ct)
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        long id; bool encerrado;
        await using (var cmd = new NpgsqlCommand("SELECT id, encerrado FROM contabil.exercicio WHERE empresa_id = @e AND ano = @a", con).Com("e", empresaId).Com("a", (short)ano))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) return null;
            (id, encerrado) = (r.GetInt64(0), r.GetBoolean(1));
        }
        var fechados = new List<DateOnly>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT p.competencia FROM contabil.periodo p
             WHERE p.exercicio_id = @x AND p.status = 'FECHADO'
               AND EXISTS (SELECT 1 FROM contabil.lancamento l WHERE l.exercicio_id = p.exercicio_id AND l.status <> 'RASCUNHO'
                              AND date_trunc('month', l.data_lancamento)::date = p.competencia)
             ORDER BY p.competencia
            """, con).Com("x", id))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) fechados.Add(DateOnly.FromDateTime(r.GetDateTime(0)));
        bool ecd; int efetivados;
        await using (var cmd = new NpgsqlCommand("""
            SELECT EXISTS (SELECT 1 FROM contabil.ecd_gerada WHERE empresa_id = @e AND ano = @a),
                   (SELECT COUNT(*) FROM contabil.lancamento WHERE exercicio_id = @x AND status <> 'RASCUNHO')
            """, con).Com("e", empresaId).Com("a", (short)ano).Com("x", id))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            await r.ReadAsync(ct);
            (ecd, efetivados) = (r.GetBoolean(0), (int)r.GetInt64(1));
        }
        return new SituacaoReorganizacao(id, encerrado, fechados, ecd, efetivados);
    }

    public async Task<ResumoReorganizacao> ReorganizarLancamentosAsync(long empresaId, long exercicioId, long? usuarioId, CancellationToken ct)
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        await Mapeamentos.DefinirUsuarioAsync(con, tx, usuarioId, ct);
        await using (var cmd = new NpgsqlCommand("SELECT set_config('vorticis.reorganizando_lancamentos', @e, true)", con, tx).Com("e", empresaId.ToString()))
            await cmd.ExecuteNonQueryAsync(ct);
        // Fase 1 afasta os números (a chave única impede trocas diretas); fase 2 numera 1..N por data e número anterior.
        await using (var cmd = new NpgsqlCommand("""
            CREATE TEMP TABLE _antes ON COMMIT DROP AS
              SELECT id, numero FROM contabil.lancamento WHERE exercicio_id = @x AND status <> 'RASCUNHO';
            UPDATE contabil.lancamento SET numero = numero + 1000000000000 WHERE id IN (SELECT id FROM _antes);
            UPDATE contabil.lancamento l SET numero = n.novo
              FROM (SELECT id, ROW_NUMBER() OVER (ORDER BY data_lancamento, numero, id) AS novo
                      FROM contabil.lancamento WHERE id IN (SELECT id FROM _antes)) n
             WHERE l.id = n.id;
            """, con, tx).Com("x", exercicioId))
            await cmd.ExecuteNonQueryAsync(ct);
        // A Folha guarda uma cópia do número do lançamento (folha.contabilizacao.numero_lancamento): acompanha a renumeração.
        // (O Fiscal guarda só lancamento_id, sem cópia do número.)
        await using (var cmd = new NpgsqlCommand("""
            UPDATE folha.contabilizacao c SET numero_lancamento = l.numero
              FROM contabil.lancamento l
             WHERE l.id = c.lancamento_id AND l.id IN (SELECT id FROM _antes) AND c.numero_lancamento IS DISTINCT FROM l.numero
            """, con, tx))
            await cmd.ExecuteNonQueryAsync(ct);
        int total, alterados;
        await using (var cmd = new NpgsqlCommand("""
            SELECT COUNT(*), COUNT(*) FILTER (WHERE a.numero <> l.numero)
              FROM _antes a JOIN contabil.lancamento l ON l.id = a.id
            """, con, tx))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            await r.ReadAsync(ct);
            (total, alterados) = ((int)r.GetInt64(0), (int)r.GetInt64(1));
        }
        await using (var cmd = new NpgsqlCommand("""
            SELECT core.fn_registrar_evento_auditoria(@e, 'contabil.lancamento', @x, 'REORGANIZA',
                   jsonb_build_object('acao', 'reorganizar_lancamentos', 'exercicio_id', @x, 'total', @t, 'renumerados', @r))
            """, con, tx).Com("e", empresaId).Com("x", exercicioId).Com("t", total).Com("r", alterados))
            await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return new ResumoReorganizacao(total, alterados);
    }

    public async Task<IReadOnlyList<AlteracaoSaldo>> ReorganizarSaldosAsync(long empresaId, int ano, long? usuarioId, CancellationToken ct)
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        await Mapeamentos.DefinirUsuarioAsync(con, tx, usuarioId, ct);
        var alteracoes = new List<AlteracaoSaldo>();
        // Antes/depois por conta e mês; só entram as linhas em que o cache diverge das partidas. O centro de custo soma por conta.
        await using (var cmd = new NpgsqlCommand("""
            WITH ex AS (SELECT data_inicio, data_fim FROM contabil.exercicio WHERE empresa_id = @e AND ano = @a),
            real AS (
                SELECT p.conta_id, COALESCE(p.centro_custo_id, 0) cc, date_trunc('month', l.data_lancamento)::date comp,
                       COALESCE(SUM(p.valor) FILTER (WHERE p.ind_dc = 'D'), 0) deb, COALESCE(SUM(p.valor) FILTER (WHERE p.ind_dc = 'C'), 0) cred
                  FROM contabil.partida p JOIN contabil.lancamento l ON l.id = p.lancamento_id, ex
                 WHERE l.empresa_id = @e AND l.status <> 'RASCUNHO' AND l.data_lancamento BETWEEN ex.data_inicio AND ex.data_fim
                 GROUP BY 1, 2, 3),
            cache AS (
                SELECT s.conta_id, s.centro_custo_id cc, s.competencia comp, s.total_debito deb, s.total_credito cred
                  FROM contabil.saldo_mensal s, ex
                 WHERE s.empresa_id = @e AND s.competencia BETWEEN date_trunc('month', ex.data_inicio)::date AND ex.data_fim)
            SELECT pc.codigo, pc.descricao, COALESCE(r.comp, c.comp),
                   COALESCE(c.deb, 0), COALESCE(c.cred, 0), COALESCE(r.deb, 0), COALESCE(r.cred, 0)
              FROM real r FULL JOIN cache c ON c.conta_id = r.conta_id AND c.cc = r.cc AND c.comp = r.comp
              JOIN contabil.plano_contas pc ON pc.id = COALESCE(r.conta_id, c.conta_id)
             WHERE COALESCE(c.deb, 0) <> COALESCE(r.deb, 0) OR COALESCE(c.cred, 0) <> COALESCE(r.cred, 0)
             ORDER BY pc.codigo, 3
            """, con, tx).Com("e", empresaId).Com("a", (short)ano))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                alteracoes.Add(new AlteracaoSaldo(r.GetString(0), r.GetString(1), DateOnly.FromDateTime(r.GetDateTime(2)),
                                                  r.GetDecimal(3), r.GetDecimal(4), r.GetDecimal(5), r.GetDecimal(6)));
        if (alteracoes.Count > 0)
        {
            // Refaz o cache do exercício inteiro (apaga e regrava a partir das partidas efetivadas).
            await using (var del = new NpgsqlCommand("""
                DELETE FROM contabil.saldo_mensal s USING contabil.exercicio ex
                 WHERE ex.empresa_id = @e AND ex.ano = @a AND s.empresa_id = @e
                   AND s.competencia BETWEEN date_trunc('month', ex.data_inicio)::date AND ex.data_fim
                """, con, tx).Com("e", empresaId).Com("a", (short)ano))
                await del.ExecuteNonQueryAsync(ct);
            await using var cmd = new NpgsqlCommand("""
                WITH ex AS (SELECT data_inicio, data_fim FROM contabil.exercicio WHERE empresa_id = @e AND ano = @a)
                INSERT INTO contabil.saldo_mensal (empresa_id, conta_id, centro_custo_id, competencia, total_debito, total_credito)
                SELECT @e, p.conta_id, COALESCE(p.centro_custo_id, 0), date_trunc('month', l.data_lancamento)::date,
                       COALESCE(SUM(p.valor) FILTER (WHERE p.ind_dc = 'D'), 0), COALESCE(SUM(p.valor) FILTER (WHERE p.ind_dc = 'C'), 0)
                  FROM contabil.partida p JOIN contabil.lancamento l ON l.id = p.lancamento_id, ex
                 WHERE l.empresa_id = @e AND l.status <> 'RASCUNHO' AND l.data_lancamento BETWEEN ex.data_inicio AND ex.data_fim
                 GROUP BY 2, 3, 4
                """, con, tx).Com("e", empresaId).Com("a", (short)ano);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await using (var cmd = new NpgsqlCommand("""
            SELECT core.fn_registrar_evento_auditoria(@e, 'contabil.saldo_mensal', NULL, 'REORGANIZA',
                   jsonb_build_object('acao', 'reorganizar_saldos', 'ano', @a, 'divergencias', @n))
            """, con, tx).Com("e", empresaId).Com("a", ano).Com("n", alteracoes.Count))
            await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return alteracoes;
    }
}
