using Npgsql;
using Vorticis.Application.Contabil;
using Vorticis.Domain.Contabil;

namespace Vorticis.Infra.Postgres;

public sealed class ConsultasContabeis(NpgsqlDataSource ds) : IConsultasContabeis
{
    public async Task<IReadOnlyList<ContaPlano>> ObterPlanoAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT id, codigo, descricao, nivel, tipo = 'A', conta_pai_id, natureza_saldo::text, cod_nat
              FROM contabil.plano_contas WHERE empresa_id = @e ORDER BY codigo
            """).Com("e", empresaId);
        var res = new List<ContaPlano>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new ContaPlano(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt16(3), r.GetBoolean(4),
                                   r.IsDBNull(5) ? null : r.GetInt64(5), Mapeamentos.DC(r.GetString(6)), r.GetString(7)));
        return res;
    }

    /// <summary>
    /// Saldo anterior = meses completos anteriores (saldo_mensal) + dias do mês inicial antes de <paramref name="inicio"/>
    /// (partidas). Movimento do intervalo vem das partidas. Inclui lançamentos estornados e seus estornos,
    /// que se anulam — ambos constam do Diário.
    /// </summary>
    public async Task<IReadOnlyList<MovimentoConta>> ObterMovimentosAsync(long empresaId, DateOnly inicio, DateOnly fim, CancellationToken ct,
                                                                          bool excluirEncerramento = false)
    {
        await using var cmd = ds.CreateCommand("""
            WITH mes_ini AS (SELECT date_trunc('month', @ini::date)::date AS m),
            anterior_meses AS (
                SELECT s.conta_id, SUM(s.total_debito - s.total_credito) AS saldo
                  FROM contabil.saldo_mensal s, mes_ini
                 WHERE s.empresa_id = @e AND s.competencia < mes_ini.m
                 GROUP BY s.conta_id),
            partidas AS (
                SELECT p.conta_id, l.data_lancamento AS d, p.ind_dc, p.valor
                  FROM contabil.partida p
                  JOIN contabil.lancamento l ON l.id = p.lancamento_id, mes_ini
                 WHERE l.empresa_id = @e AND l.status <> 'RASCUNHO'
                   AND l.data_lancamento BETWEEN mes_ini.m AND @fim::date
                   AND NOT (@semE AND l.ind_lcto = 'E')),
            agregado AS (
                SELECT conta_id,
                       SUM(CASE WHEN d < @ini::date THEN CASE ind_dc WHEN 'D' THEN valor ELSE -valor END ELSE 0 END) AS ant_dias,
                       SUM(CASE WHEN d >= @ini::date AND ind_dc = 'D' THEN valor ELSE 0 END) AS deb,
                       SUM(CASE WHEN d >= @ini::date AND ind_dc = 'C' THEN valor ELSE 0 END) AS cred
                  FROM partidas GROUP BY conta_id)
            SELECT COALESCE(a.conta_id, m.conta_id),
                   COALESCE(m.saldo, 0) + COALESCE(a.ant_dias, 0),
                   COALESCE(a.deb, 0), COALESCE(a.cred, 0)
              FROM agregado a FULL JOIN anterior_meses m ON m.conta_id = a.conta_id
            """).Com("e", empresaId).Com("ini", inicio).Com("fim", fim).Com("semE", excluirEncerramento);
        var res = new List<MovimentoConta>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new MovimentoConta(r.GetInt64(0), r.GetDecimal(1), r.GetDecimal(2), r.GetDecimal(3)));
        return res;
    }

    public async Task<IReadOnlyList<PartidaMutacao>> ObterPartidasAsync(long empresaId, DateOnly inicio, DateOnly fim, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT l.id, p.conta_id, p.ind_dc::text, p.valor, l.ind_lcto = 'E', l.origem = 'SALDO_INICIAL'
              FROM contabil.lancamento l
              JOIN contabil.partida p ON p.lancamento_id = l.id
             WHERE l.empresa_id = @e AND l.status <> 'RASCUNHO' AND l.data_lancamento BETWEEN @ini AND @fim
            """).Com("e", empresaId).Com("ini", inicio).Com("fim", fim);
        var res = new List<PartidaMutacao>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new PartidaMutacao(r.GetInt64(0), r.GetInt64(1), Mapeamentos.DC(r.GetString(2)), r.GetDecimal(3), r.GetBoolean(4), r.GetBoolean(5)));
        return res;
    }

    public async Task<IReadOnlyList<MovimentoParticipante>> ObterMovimentosParticipantesAsync(long empresaId, DateOnly inicio, DateOnly fim,
                                                                                            CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT p.conta_id, pa.codigo, pa.nome,
                   SUM(CASE WHEN l.data_lancamento < @ini THEN CASE p.ind_dc WHEN 'D' THEN p.valor ELSE -p.valor END ELSE 0 END),
                   SUM(CASE WHEN l.data_lancamento >= @ini AND p.ind_dc = 'D' THEN p.valor ELSE 0 END),
                   SUM(CASE WHEN l.data_lancamento >= @ini AND p.ind_dc = 'C' THEN p.valor ELSE 0 END)
              FROM contabil.partida p
              JOIN contabil.lancamento l ON l.id = p.lancamento_id
              JOIN core.participante pa ON pa.id = p.participante_id
             WHERE l.empresa_id = @e AND l.status <> 'RASCUNHO' AND l.data_lancamento <= @fim
             GROUP BY p.conta_id, pa.codigo, pa.nome
             ORDER BY pa.nome
            """).Com("e", empresaId).Com("ini", inicio).Com("fim", fim);
        var res = new List<MovimentoParticipante>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new MovimentoParticipante(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetDecimal(3), r.GetDecimal(4), r.GetDecimal(5)));
        return res;
    }

    public async Task<RazaoConta?> ObterRazaoAsync(long empresaId, string codigoConta, DateOnly inicio, DateOnly fim, CancellationToken ct)
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        long contaId; string descricao; bool analitica;
        await using (var cmd = new NpgsqlCommand(
            "SELECT id, descricao, tipo = 'A' FROM contabil.plano_contas WHERE empresa_id = @e AND codigo = @c", con)
            .Com("e", empresaId).Com("c", codigoConta))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) return null;
            (contaId, descricao, analitica) = (r.GetInt64(0), r.GetString(1), r.GetBoolean(2));
        }
        if (!analitica) throw new ArgumentException($"O Razão é emitido por conta analítica; {codigoConta} é sintética.");

        decimal saldoAnterior;
        await using (var cmd = new NpgsqlCommand("""
            SELECT COALESCE((SELECT SUM(total_debito - total_credito) FROM contabil.saldo_mensal
                              WHERE empresa_id = @e AND conta_id = @c AND competencia < date_trunc('month', @ini::date)), 0)
                 + COALESCE((SELECT SUM(CASE p.ind_dc WHEN 'D' THEN p.valor ELSE -p.valor END)
                               FROM contabil.partida p JOIN contabil.lancamento l ON l.id = p.lancamento_id
                              WHERE l.empresa_id = @e AND p.conta_id = @c AND l.status <> 'RASCUNHO'
                                AND l.data_lancamento >= date_trunc('month', @ini::date) AND l.data_lancamento < @ini::date), 0)
            """, con).Com("e", empresaId).Com("c", contaId).Com("ini", inicio))
            saldoAnterior = (decimal)(await cmd.ExecuteScalarAsync(ct))!;

        var linhas = new List<LinhaRazao>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT l.data_lancamento, l.numero, p.historico, p.ind_dc::text, p.valor,
                   @ant + SUM(CASE p.ind_dc WHEN 'D' THEN p.valor ELSE -p.valor END)
                          OVER (ORDER BY l.data_lancamento, l.numero, p.sequencia),
                   l.status = 'ESTORNADO'
              FROM contabil.partida p JOIN contabil.lancamento l ON l.id = p.lancamento_id
             WHERE l.empresa_id = @e AND p.conta_id = @c AND l.status <> 'RASCUNHO'
               AND l.data_lancamento BETWEEN @ini AND @fim
             ORDER BY l.data_lancamento, l.numero, p.sequencia
            """, con).Com("ant", saldoAnterior).Com("e", empresaId).Com("c", contaId).Com("ini", inicio).Com("fim", fim))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                linhas.Add(new LinhaRazao(r.GetFieldValue<DateOnly>(0), r.GetInt64(1), r.GetString(2), Mapeamentos.DC(r.GetString(3)),
                                          r.GetDecimal(4), r.GetDecimal(5), r.GetBoolean(6)));

        return new RazaoConta(codigoConta, descricao, saldoAnterior, linhas);
    }

    public async Task<IReadOnlyList<LinhaDiario>> ObterDiarioAsync(long empresaId, DateOnly inicio, DateOnly fim, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT l.id, l.numero, l.data_lancamento, p.sequencia, pc.codigo, pc.descricao, p.ind_dc::text, p.valor, p.historico, l.status::text,
                   p.num_arquivo, cc.codigo, pa.codigo
              FROM contabil.lancamento l
              JOIN contabil.partida p ON p.lancamento_id = l.id
              JOIN contabil.plano_contas pc ON pc.id = p.conta_id
              LEFT JOIN contabil.centro_custo cc ON cc.id = p.centro_custo_id
              LEFT JOIN core.participante pa ON pa.id = p.participante_id
             WHERE l.empresa_id = @e AND l.status <> 'RASCUNHO' AND l.data_lancamento BETWEEN @ini AND @fim
             ORDER BY l.data_lancamento, l.numero, p.sequencia
            """).Com("e", empresaId).Com("ini", inicio).Com("fim", fim);
        var res = new List<LinhaDiario>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new LinhaDiario(r.GetInt64(0), r.GetInt64(1), r.GetFieldValue<DateOnly>(2), r.GetInt16(3), r.GetString(4), r.GetString(5),
                                    Mapeamentos.DC(r.GetString(6)), r.GetDecimal(7), r.GetString(8), Mapeamentos.Status(r.GetString(9)),
                                    r.IsDBNull(10) ? null : r.GetString(10), r.IsDBNull(11) ? null : r.GetString(11),
                                    r.IsDBNull(12) ? null : r.GetString(12)));
        return res;
    }

    public async Task<IReadOnlyList<CentroPlano>> ObterCentrosAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT id, codigo, descricao, centro_pai_id, ativo, tipo = 'A' FROM contabil.centro_custo WHERE empresa_id = @e ORDER BY codigo")
            .Com("e", empresaId);
        var res = new List<CentroPlano>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new CentroPlano(r.GetInt64(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetInt64(3), r.GetBoolean(4), r.GetBoolean(5)));
        return res;
    }

    public async Task<IReadOnlyList<MovimentoCentro>> ObterMovimentosCentroAsync(long empresaId, DateOnly inicio, DateOnly fim, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT p.conta_id, COALESCE(p.centro_custo_id, 0), date_trunc('month', l.data_lancamento)::date,
                   SUM(CASE WHEN p.ind_dc = 'D' THEN p.valor ELSE 0 END), SUM(CASE WHEN p.ind_dc = 'C' THEN p.valor ELSE 0 END)
              FROM contabil.partida p
              JOIN contabil.lancamento l ON l.id = p.lancamento_id
             WHERE l.empresa_id = @e AND l.status <> 'RASCUNHO' AND l.ind_lcto IS DISTINCT FROM 'E'
               AND l.data_lancamento BETWEEN @ini AND @fim
             GROUP BY 1, 2, 3
            """).Com("e", empresaId).Com("ini", inicio).Com("fim", fim);
        var res = new List<MovimentoCentro>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new MovimentoCentro(r.GetInt64(0), r.GetInt64(1), r.GetFieldValue<DateOnly>(2), r.GetDecimal(3), r.GetDecimal(4)));
        return res;
    }

    public async Task<IReadOnlyList<DetalheCentro>> ObterDetalhesCentroAsync(long empresaId, DateOnly inicio, DateOnly fim, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT p.conta_id, COALESCE(p.centro_custo_id, 0), l.data_lancamento, COALESCE(l.numero, 0), p.ind_dc::text, p.valor, COALESCE(p.historico, '')
              FROM contabil.partida p
              JOIN contabil.lancamento l ON l.id = p.lancamento_id
             WHERE l.empresa_id = @e AND l.status <> 'RASCUNHO' AND l.ind_lcto IS DISTINCT FROM 'E'
               AND l.data_lancamento BETWEEN @ini AND @fim
            """).Com("e", empresaId).Com("ini", inicio).Com("fim", fim);
        var res = new List<DetalheCentro>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new DetalheCentro(r.GetInt64(0), r.GetInt64(1), r.GetFieldValue<DateOnly>(2), r.GetInt64(3), Mapeamentos.DC(r.GetString(4)), r.GetDecimal(5), r.GetString(6)));
        return res;
    }
}
