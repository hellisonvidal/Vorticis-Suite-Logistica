using Npgsql;
using Vorticis.Application.Contabil;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;

namespace Vorticis.Infra.Postgres;

public sealed class RepositorioContabil(NpgsqlDataSource ds) : IRepositorioContabil
{
    public async Task<PeriodoInfo?> ObterPeriodoAsync(long empresaId, DateOnly data, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT e.id, e.data_inicio, e.data_fim, e.encerrado, p.status::text, emp.data_bloqueio, emp.exige_documento
              FROM contabil.exercicio e
              JOIN core.empresa emp ON emp.id = e.empresa_id
              LEFT JOIN contabil.periodo p
                     ON p.exercicio_id = e.id AND p.competencia = date_trunc('month', @d::date)::date
             WHERE e.empresa_id = @e AND @d::date BETWEEN e.data_inicio AND e.data_fim
            """).Com("e", empresaId).Com("d", data);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var status = r.IsDBNull(4) ? null : r.GetString(4);
        return new PeriodoInfo(r.GetInt64(0), r.GetFieldValue<DateOnly>(1), r.GetFieldValue<DateOnly>(2), r.GetBoolean(3),
                               status is not null, status == "ABERTO",
                               r.IsDBNull(5) ? null : r.GetFieldValue<DateOnly>(5), r.GetBoolean(6));
    }

    public async Task<IReadOnlyDictionary<string, ContaInfo>> ObterContasAsync(long empresaId, IEnumerable<string> codigos, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT id, codigo, descricao, tipo = 'A', ativa, data_inclusao, exige_centro_custo, exige_participante, cod_nat
              FROM contabil.plano_contas WHERE empresa_id = @e AND codigo = ANY(@c)
            """).Com("e", empresaId).Com("c", codigos.ToArray());
        var res = new Dictionary<string, ContaInfo>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res[r.GetString(1)] = new ContaInfo(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetBoolean(3), r.GetBoolean(4),
                                                r.GetFieldValue<DateOnly>(5), r.GetBoolean(6), r.GetBoolean(7), r.GetString(8));
        return res;
    }

    public Task<IReadOnlyDictionary<string, long>> ObterCentrosCustoAsync(long empresaId, IEnumerable<string> codigos, CancellationToken ct)
        => CodigosParaIds("contabil.centro_custo", "AND ativo AND tipo = 'A'", empresaId, codigos, ct);

    public Task<IReadOnlyDictionary<string, long>> ObterParticipantesAsync(long empresaId, IEnumerable<string> codigos, CancellationToken ct)
        => CodigosParaIds("core.participante", "", empresaId, codigos, ct);

    private async Task<IReadOnlyDictionary<string, long>> CodigosParaIds(string tabela, string filtro, long empresaId,
                                                                        IEnumerable<string> codigos, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand($"SELECT codigo, id FROM {tabela} WHERE empresa_id = @e AND codigo = ANY(@c) {filtro}")
                                .Com("e", empresaId).Com("c", codigos.ToArray());
        var res = new Dictionary<string, long>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res[r.GetString(0)] = r.GetInt64(1);
        return res;
    }

    public async Task<LancamentoRegistrado?> ObterLancamentoAsync(long empresaId, long lancamentoId, CancellationToken ct)
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        long? numero; DateOnly data; StatusLancamento status; OrigemLancamento origem; long? estornoDe;

        await using (var cmd = new NpgsqlCommand("""
            SELECT numero, data_lancamento, status::text, origem::text, estorno_de_id
              FROM contabil.lancamento WHERE id = @id AND empresa_id = @e
            """, con).Com("id", lancamentoId).Com("e", empresaId))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) return null;
            numero = r.IsDBNull(0) ? null : r.GetInt64(0);
            data = r.GetFieldValue<DateOnly>(1);
            status = Mapeamentos.Status(r.GetString(2));
            origem = Mapeamentos.Origem(r.GetString(3));
            estornoDe = r.IsDBNull(4) ? null : r.GetInt64(4);
        }

        var partidas = new List<PartidaProposta>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT pc.codigo, p.ind_dc::text, p.valor, p.historico, cc.codigo, pa.codigo, p.num_arquivo
              FROM contabil.partida p
              JOIN contabil.plano_contas pc ON pc.id = p.conta_id
              LEFT JOIN contabil.centro_custo cc ON cc.id = p.centro_custo_id
              LEFT JOIN core.participante pa ON pa.id = p.participante_id
             WHERE p.lancamento_id = @id ORDER BY p.sequencia
            """, con).Com("id", lancamentoId))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                partidas.Add(new PartidaProposta(r.GetString(0), Mapeamentos.DC(r.GetString(1)), r.GetDecimal(2), r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6)));

        return new LancamentoRegistrado(lancamentoId, empresaId, numero, data, status, origem, estornoDe, partidas);
    }

    public async Task<LancamentoEfetivado> GravarEfetivadoAsync(LancamentoValidado l, long? usuarioId, CancellationToken ct)
        => (await GravarEfetivadosAsync([l], usuarioId, ct))[0];

    public Task<IReadOnlyList<LancamentoEfetivado>> GravarEfetivadosAsync(IReadOnlyList<LancamentoValidado> lancamentos, long? usuarioId,
                                                                         CancellationToken ct, IReadOnlyList<string?>? chaves = null)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await Mapeamentos.DefinirUsuarioAsync(con, tx, usuarioId, ct);
            var gravados = new List<LancamentoEfetivado>(lancamentos.Count);
            for (var i = 0; i < lancamentos.Count; i++)
            {
                var l = lancamentos[i];
                var gravado = await GravarNaTransacaoAsync(con, tx, l, usuarioId, ct);
                gravados.Add(gravado);
                if (chaves is not null && i < chaves.Count && chaves[i] is { } chave)
                    await using (var cmd = new NpgsqlCommand("""
                        INSERT INTO contabil.lancamento_importado (empresa_id, chave, lancamento_id) VALUES (@e, @c, @l)
                        ON CONFLICT (empresa_id, chave) DO UPDATE SET lancamento_id = EXCLUDED.lancamento_id, importado_em = now()
                        """, con, tx).Com("e", l.EmpresaId).Com("c", chave).Com("l", gravado.Id))
                        await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);   // constraint triggers diferidos (partidas dobradas) rodam aqui
            return (IReadOnlyList<LancamentoEfetivado>)gravados;
        });

    internal static async Task<LancamentoEfetivado> GravarNaTransacaoAsync(NpgsqlConnection con, NpgsqlTransaction tx, LancamentoValidado l,
                                                                          long? usuarioId, CancellationToken ct)
    {
        // Numeração sequencial sem lacunas: o lock na linha do exercício serializa as efetivações.
        await using (var cmd = new NpgsqlCommand(
            "SELECT 1 FROM contabil.exercicio WHERE id = @ex AND empresa_id = @e FOR UPDATE", con, tx)
            .Com("ex", l.ExercicioId).Com("e", l.EmpresaId))
            if (await cmd.ExecuteScalarAsync(ct) is null)
                throw new ViolacaoRegraException("Exercício não pertence à empresa.");

        long numero;
        await using (var cmd = new NpgsqlCommand(
            "SELECT COALESCE(MAX(numero), 0) + 1 FROM contabil.lancamento WHERE exercicio_id = @ex", con, tx)
            .Com("ex", l.ExercicioId))
            numero = (long)(await cmd.ExecuteScalarAsync(ct))!;

        long id;
        await using (var cmd = new NpgsqlCommand("""
            INSERT INTO contabil.lancamento (empresa_id, exercicio_id, data_lancamento, ind_lcto, data_lcto_ext,
                                             origem, documento_origem_id, estorno_de_id, criado_por)
            VALUES (@e, @ex, @d, @ind::contabil.ind_lcto, @dext, @o::contabil.origem_lancamento, @doc, @est, @u)
            RETURNING id
            """, con, tx)
            .Com("e", l.EmpresaId).Com("ex", l.ExercicioId).Com("d", l.Data).Com("ind", l.Indicador.Sql())
            .Com("dext", l.DataExtemporanea).Com("o", l.Origem.Sql()).Com("doc", l.DocumentoOrigemId)
            .Com("est", l.EstornoDeId).Com("u", usuarioId))
            id = (long)(await cmd.ExecuteScalarAsync(ct))!;

        await InserirPartidasAsync(con, tx, id, l, ct);

        await using (var cmd = new NpgsqlCommand("""
            UPDATE contabil.lancamento SET status = 'EFETIVADO', numero = @n, efetivado_em = now() WHERE id = @id
            """, con, tx).Com("n", numero).Com("id", id))
            await cmd.ExecuteNonQueryAsync(ct);

        if (l.EstornoDeId is long original)
        {
            await using var cmd = new NpgsqlCommand("""
                UPDATE contabil.lancamento SET status = 'ESTORNADO'
                 WHERE id = @o AND empresa_id = @e AND status = 'EFETIVADO'
                """, con, tx).Com("o", original).Com("e", l.EmpresaId);
            if (await cmd.ExecuteNonQueryAsync(ct) != 1)
                throw new ViolacaoRegraException("O lançamento original não está mais efetivado (estornado em paralelo?).");
        }

        return new LancamentoEfetivado(id, numero);
    }

    private static async Task InserirPartidasAsync(NpgsqlConnection con, NpgsqlTransaction tx, long id, LancamentoValidado l, CancellationToken ct)
    {
        await using (var cmd = new NpgsqlCommand("""
            INSERT INTO contabil.partida (lancamento_id, sequencia, conta_id, centro_custo_id, participante_id,
                                          ind_dc, valor, historico, num_arquivo)
            SELECT @id, t.s, t.c, t.cc, t.pa, t.d::contabil.indicador_dc, t.v, t.h, t.a
              FROM unnest(@seq, @conta, @cc, @part, @dc, @valor, @hist, @arq) AS t(s, c, cc, pa, d, v, h, a)
            """, con, tx)
            .Com("id", id)
            .Com("seq", l.Partidas.Select(p => p.Sequencia).ToArray())
            .Com("conta", l.Partidas.Select(p => p.ContaId).ToArray())
            .Com("cc", l.Partidas.Select(p => p.CentroCustoId).ToArray())
            .Com("part", l.Partidas.Select(p => p.ParticipanteId).ToArray())
            .Com("dc", l.Partidas.Select(p => p.DC.ToString()).ToArray())
            .Com("valor", l.Partidas.Select(p => p.Valor).ToArray())
            .Com("hist", l.Partidas.Select(p => p.Historico).ToArray())
            .Com("arq", l.Partidas.Select(p => p.NumeroArquivo).ToArray()))
            await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> UsaEstornoAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT usa_estorno FROM core.empresa WHERE id = @e").Com("e", empresaId);
        return await cmd.ExecuteScalarAsync(ct) is not bool b || b;
    }

    public async Task<SituacaoLancamento?> ObterSituacaoLancamentoAsync(long empresaId, long lancamentoId, CancellationToken ct)
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        return await SituacaoAsync(con, null, empresaId, lancamentoId, ct);
    }

    private static async Task<SituacaoLancamento?> SituacaoAsync(NpgsqlConnection con, NpgsqlTransaction? tx, long empresaId, long lancamentoId, CancellationToken ct)
    {
        long exercicioId; int ano; long? numero, estornoDe; DateOnly data; string status, origem, ind;
        bool encerrado, periodoAberto, ecd; DateOnly? bloqueio;
        await using (var cmd = new NpgsqlCommand("""
            SELECT l.exercicio_id, ex.ano, l.numero, l.data_lancamento, l.status::text, l.origem::text, l.ind_lcto::text, l.estorno_de_id,
                   ex.encerrado, COALESCE(p.status = 'ABERTO', FALSE),
                   EXISTS (SELECT 1 FROM contabil.ecd_gerada g WHERE g.empresa_id = l.empresa_id AND g.ano = ex.ano), emp.data_bloqueio
              FROM contabil.lancamento l
              JOIN contabil.exercicio ex ON ex.id = l.exercicio_id
              JOIN core.empresa emp ON emp.id = l.empresa_id
              LEFT JOIN contabil.periodo p ON p.exercicio_id = l.exercicio_id AND p.competencia = date_trunc('month', l.data_lancamento)::date
             WHERE l.id = @id AND l.empresa_id = @e
            """, con, tx).Com("id", lancamentoId).Com("e", empresaId))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) return null;
            exercicioId = r.GetInt64(0); ano = r.GetInt16(1);
            numero = r.IsDBNull(2) ? null : r.GetInt64(2); data = r.GetFieldValue<DateOnly>(3);
            (status, origem, ind) = (r.GetString(4), r.GetString(5), r.GetString(6));
            estornoDe = r.IsDBNull(7) ? null : r.GetInt64(7);
            (encerrado, periodoAberto, ecd) = (r.GetBoolean(8), r.GetBoolean(9), r.GetBoolean(10));
            bloqueio = r.IsDBNull(11) ? null : r.GetFieldValue<DateOnly>(11);
        }
        var vinculados = new List<VinculoLancamento>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT id, numero, origem::text FROM contabil.lancamento
             WHERE empresa_id = @e AND (id = (SELECT estorno_de_id FROM contabil.lancamento WHERE id = @id) OR estorno_de_id = @id)
             ORDER BY id
            """, con, tx).Com("id", lancamentoId).Com("e", empresaId))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                vinculados.Add(new VinculoLancamento(r.GetInt64(0), r.IsDBNull(1) ? null : r.GetInt64(1), Mapeamentos.Origem(r.GetString(2))));
        bool conciliado;
        await using (var cmd = new NpgsqlCommand("""
            SELECT EXISTS (SELECT 1 FROM contabil.conciliacao_partida cp JOIN contabil.partida p ON p.id = cp.partida_id
                            WHERE p.lancamento_id = @id OR p.lancamento_id IN (SELECT id FROM contabil.lancamento WHERE estorno_de_id = @id OR id = @est))
            """, con, tx).Com("id", lancamentoId).Com("est", estornoDe))
            conciliado = (bool)(await cmd.ExecuteScalarAsync(ct))!;
        var indicador = ind switch { "E" => IndicadorLancamento.Encerramento, "X" => IndicadorLancamento.Extemporaneo, _ => IndicadorLancamento.Normal };
        return new SituacaoLancamento(lancamentoId, empresaId, exercicioId, ano, numero, data, Mapeamentos.Status(status), Mapeamentos.Origem(origem),
                                      indicador, estornoDe, encerrado, periodoAberto, ecd, bloqueio, conciliado, vinculados);
    }

    private static async Task DeclararManutencaoAsync(NpgsqlConnection con, NpgsqlTransaction tx, long empresaId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT set_config('vorticis.mantendo_lancamentos', @e, true)", con, tx).Com("e", empresaId.ToString());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task AjustarSaldoAsync(NpgsqlConnection con, NpgsqlTransaction tx, long id, int sinal, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT contabil.fn_ajusta_saldo_lancamento(@id, @s)", con, tx).Com("id", id).Com("s", sinal);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task AuditarAsync(NpgsqlConnection con, NpgsqlTransaction tx, long empresaId, long id, string operacao, string sqlJson, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            $"SELECT core.fn_registrar_evento_auditoria(@e, 'contabil.lancamento', @id, @op, {sqlJson})", con, tx)
            .Com("e", empresaId).Com("id", id).Com("op", operacao);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public Task<IReadOnlyList<LancamentoEfetivado>> ExcluirLancamentoAsync(long empresaId, long lancamentoId, long? usuarioId, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await Mapeamentos.DefinirUsuarioAsync(con, tx, usuarioId, ct);
            await DeclararManutencaoAsync(con, tx, empresaId, ct);
            var sit = await SituacaoAsync(con, tx, empresaId, lancamentoId, ct) ?? throw new ViolacaoRegraException("Lançamento não encontrado.");
            await using (var cmd = new NpgsqlCommand("SELECT 1 FROM contabil.exercicio WHERE id = @x FOR UPDATE", con, tx).Com("x", sit.ExercicioId))
                await cmd.ExecuteNonQueryAsync(ct);
            // O estorno é removido antes do original (chave estrangeira estorno_de_id).
            var ids = new List<(long Id, long? Numero)> { (sit.Id, sit.Numero) };
            ids.AddRange(sit.Vinculados.Select(v => (v.Id, v.Numero)));
            var ordem = new List<(long Id, long? Numero)>();
            foreach (var x in ids)
            {
                await using var cmd = new NpgsqlCommand("SELECT estorno_de_id IS NOT NULL FROM contabil.lancamento WHERE id = @id", con, tx).Com("id", x.Id);
                if ((bool)(await cmd.ExecuteScalarAsync(ct))!) ordem.Insert(0, x); else ordem.Add(x);
            }
            foreach (var (id, numero) in ordem)
            {
                await AuditarAsync(con, tx, empresaId, id, "EXCLUI",
                    "jsonb_build_object('acao', 'excluir_lancamento', 'lancamento', contabil.fn_retrato_lancamento(@id))", ct);
                await AjustarSaldoAsync(con, tx, id, -1, ct);
                await using (var cmd = new NpgsqlCommand("DELETE FROM contabil.partida WHERE lancamento_id = @id", con, tx).Com("id", id))
                    await cmd.ExecuteNonQueryAsync(ct);
                await using (var cmd = new NpgsqlCommand("DELETE FROM contabil.lancamento WHERE id = @id", con, tx).Com("id", id))
                    await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            return (IReadOnlyList<LancamentoEfetivado>)ordem.Select(x => new LancamentoEfetivado(x.Id, x.Numero ?? 0)).ToList();
        });

    public Task AlterarLancamentoAsync(long lancamentoId, LancamentoValidado novo, long? usuarioId, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await Mapeamentos.DefinirUsuarioAsync(con, tx, usuarioId, ct);
            await DeclararManutencaoAsync(con, tx, novo.EmpresaId, ct);
            await using (var cmd = new NpgsqlCommand("SELECT 1 FROM contabil.exercicio WHERE id = @x FOR UPDATE", con, tx).Com("x", novo.ExercicioId))
                await cmd.ExecuteNonQueryAsync(ct);
            string antes;
            await using (var cmd = new NpgsqlCommand("SELECT contabil.fn_retrato_lancamento(@id)::text", con, tx).Com("id", lancamentoId))
                antes = (string)(await cmd.ExecuteScalarAsync(ct) ?? throw new ViolacaoRegraException("Lançamento não encontrado."));
            await AjustarSaldoAsync(con, tx, lancamentoId, -1, ct);
            await using (var cmd = new NpgsqlCommand("DELETE FROM contabil.partida WHERE lancamento_id = @id", con, tx).Com("id", lancamentoId))
                await cmd.ExecuteNonQueryAsync(ct);
            await using (var cmd = new NpgsqlCommand("""
                UPDATE contabil.lancamento SET data_lancamento = @d, ind_lcto = @ind::contabil.ind_lcto, data_lcto_ext = @dext
                 WHERE id = @id AND empresa_id = @e
                """, con, tx)
                .Com("d", novo.Data).Com("ind", novo.Indicador.Sql()).Com("dext", novo.DataExtemporanea).Com("id", lancamentoId).Com("e", novo.EmpresaId))
                if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new ViolacaoRegraException("Lançamento não encontrado.");
            await InserirPartidasAsync(con, tx, lancamentoId, novo, ct);
            await AjustarSaldoAsync(con, tx, lancamentoId, 1, ct);
            await using (var cmd = new NpgsqlCommand("""
                SELECT core.fn_registrar_evento_auditoria(@e, 'contabil.lancamento', @id, 'ALTERA',
                       jsonb_build_object('acao', 'alterar_lancamento', 'antes', @antes::jsonb, 'depois', contabil.fn_retrato_lancamento(@id)))
                """, con, tx).Com("e", novo.EmpresaId).Com("id", lancamentoId).Com("antes", antes))
                await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);   // partidas dobradas (diferido) conferem aqui
            return 0;
        });

    public async Task<IReadOnlyList<LancamentoRegistrado>> ListarLancamentosAtivosAsync(long empresaId, long exercicioId, OrigemLancamento origem,
                                                                                       CancellationToken ct)
    {
        var ids = new List<long>();
        await using (var cmd = ds.CreateCommand("""
            SELECT id FROM contabil.lancamento
             WHERE empresa_id = @e AND exercicio_id = @ex AND origem = @o::contabil.origem_lancamento AND status = 'EFETIVADO'
             ORDER BY numero
            """).Com("e", empresaId).Com("ex", exercicioId).Com("o", origem.Sql()))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) ids.Add(r.GetInt64(0));
        var res = new List<LancamentoRegistrado>();
        foreach (var id in ids)
            if (await ObterLancamentoAsync(empresaId, id, ct) is { } l) res.Add(l);
        return res;
    }

    public async Task<IReadOnlyList<CompetenciaStatus>> ListarPeriodosAsync(long empresaId, int ano, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT p.competencia, p.status = 'ABERTO'
              FROM contabil.periodo p JOIN contabil.exercicio e ON e.id = p.exercicio_id
             WHERE e.empresa_id = @e AND e.ano = @a ORDER BY p.competencia
            """).Com("e", empresaId).Com("a", (short)ano);
        var res = new List<CompetenciaStatus>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(new CompetenciaStatus(r.GetFieldValue<DateOnly>(0), r.GetBoolean(1)));
        return res;
    }

    public async Task<long?> ObterNumeroLancamentoAtivoAsync(long empresaId, long exercicioId, OrigemLancamento origem, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT MIN(numero) FROM contabil.lancamento
             WHERE empresa_id = @e AND exercicio_id = @ex AND origem = @o::contabil.origem_lancamento AND status = 'EFETIVADO'
            """).Com("e", empresaId).Com("ex", exercicioId).Com("o", origem.Sql());
        return await cmd.ExecuteScalarAsync(ct) is long n ? n : null;
    }

    public async Task<IReadOnlyList<Inconsistencia>> VerificarInvariantesAsync(long empresaId, DateOnly competencia, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT codigo, descricao FROM contabil.fn_verificar_invariantes(@e, @c)")
                                .Com("e", empresaId).Com("c", competencia);
        var res = new List<Inconsistencia>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(new Inconsistencia(r.GetString(0), r.GetString(1)));
        return res;
    }

    public async Task AlterarStatusPeriodoAsync(long empresaId, DateOnly competencia, bool fechar, long? usuarioId, CancellationToken ct)
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        await Mapeamentos.DefinirUsuarioAsync(con, tx, usuarioId, ct);
        await using (var cmd = new NpgsqlCommand("""
            UPDATE contabil.periodo p
               SET status = CASE WHEN @f THEN 'FECHADO' ELSE 'ABERTO' END::contabil.status_periodo,
                   fechado_em = CASE WHEN @f THEN now() END,
                   fechado_por = CASE WHEN @f THEN @u::bigint END
              FROM contabil.exercicio e
             WHERE e.id = p.exercicio_id AND e.empresa_id = @e AND p.competencia = @c
            """, con, tx).Com("f", fechar).Com("u", usuarioId).Com("e", empresaId).Com("c", competencia))
            if (await cmd.ExecuteNonQueryAsync(ct) != 1)
                throw new ViolacaoRegraException($"Período {competencia:MM/yyyy} não encontrado.");
        await tx.CommitAsync(ct);
    }
}
