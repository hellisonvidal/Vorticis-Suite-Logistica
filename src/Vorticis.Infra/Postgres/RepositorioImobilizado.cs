using Npgsql;
using Vorticis.Application.Imobilizado;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Imobilizado;

namespace Vorticis.Infra.Postgres;

public sealed class RepositorioImobilizado(NpgsqlDataSource ds) : IRepositorioImobilizado
{
    public async Task<IReadOnlyList<GrupoBem>> ListarGruposAsync(CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT g.id, g.codigo, g.descricao, g.taxa_anual_fiscal, g.deprecia, g.intangivel, g.conta_bem, g.conta_acumulada, g.conta_despesa,
                   g.base_legal, EXISTS (SELECT 1 FROM imobilizado.bem b WHERE b.grupo_id = g.id)
              FROM imobilizado.grupo g ORDER BY g.codigo
            """);
        var res = new List<GrupoBem>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        string? T(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        while (await r.ReadAsync(ct))
            res.Add(new GrupoBem(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetDecimal(3), r.GetBoolean(4), r.GetBoolean(5), T(6), T(7), T(8), T(9),
                                 r.GetBoolean(10)));
        return res;
    }

    public Task<long> GravarGrupoAsync(GrupoBem g, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var cmd = g.Id == 0
                ? ds.CreateCommand("""
                    INSERT INTO imobilizado.grupo (codigo, descricao, taxa_anual_fiscal, deprecia, intangivel, conta_bem, conta_acumulada, conta_despesa, base_legal)
                    VALUES (@c, @d, @t, @dep, @int, @cb, @ca, @cd, @bl) RETURNING id
                    """)
                : ds.CreateCommand("""
                    UPDATE imobilizado.grupo SET codigo = @c, descricao = @d, taxa_anual_fiscal = @t, deprecia = @dep, intangivel = @int,
                           conta_bem = @cb, conta_acumulada = @ca, conta_despesa = @cd, base_legal = @bl
                     WHERE id = @id RETURNING id
                    """);
            cmd.Com("c", g.Codigo).Com("d", g.Descricao).Com("t", g.TaxaAnualFiscal).Com("dep", g.Deprecia).Com("int", g.Intangivel)
               .ComTexto("cb", g.ContaBem).ComTexto("ca", g.ContaAcumulada).ComTexto("cd", g.ContaDespesa).ComTexto("bl", g.BaseLegal);
            if (g.Id != 0) cmd.Com("id", g.Id);
            return (long)(await cmd.ExecuteScalarAsync(ct))!;
        });

    public Task ExcluirGrupoAsync(long id, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var cmd = ds.CreateCommand("DELETE FROM imobilizado.grupo WHERE id = @id").Com("id", id);
            return await cmd.ExecuteNonQueryAsync(ct);
        });

    public async Task<IReadOnlyList<BemImobilizado>> ListarBensAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT b.id, b.codigo, b.descricao, b.grupo_id, b.plaqueta, b.localizacao, b.nota_fiscal, f.codigo,
                   b.data_aquisicao, b.data_inicio_uso, b.valor_aquisicao, b.valor_residual, b.vida_util_meses, b.metodo, b.turnos,
                   b.depreciacao_anterior, cb.codigo, ca.codigo, cd.codigo, b.data_baixa, b.motivo_baixa,
                   COALESCE(d.valor, 0), COALESCE(d.fiscal, 0), d.ultima, b.pro_rata_dia
              FROM imobilizado.bem b
              JOIN contabil.plano_contas cb ON cb.id = b.conta_bem_id
              LEFT JOIN contabil.plano_contas ca ON ca.id = b.conta_acumulada_id
              LEFT JOIN contabil.plano_contas cd ON cd.id = b.conta_despesa_id
              LEFT JOIN core.participante f ON f.id = b.fornecedor_id
              LEFT JOIN LATERAL (
                    SELECT SUM(x.valor) AS valor, SUM(x.valor_fiscal) AS fiscal, MAX(x.competencia) AS ultima
                      FROM imobilizado.depreciacao x JOIN contabil.lancamento l ON l.id = x.lancamento_id
                     WHERE x.bem_id = b.id AND l.status = 'EFETIVADO') d ON TRUE
             WHERE b.empresa_id = @e
            """).Com("e", empresaId);
        var res = new List<BemImobilizado>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        string? T(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        DateOnly? D(int i) => r.IsDBNull(i) ? null : r.GetFieldValue<DateOnly>(i);
        while (await r.ReadAsync(ct))
            res.Add(new BemImobilizado(r.GetInt64(0), r.GetInt32(1), r.GetString(2), r.GetInt64(3), T(4), T(5), T(6), T(7),
                r.GetFieldValue<DateOnly>(8), r.GetFieldValue<DateOnly>(9), r.GetDecimal(10), r.GetDecimal(11), r.GetInt32(12),
                r.GetString(13) == "SOMA_DIGITOS" ? MetodoDepreciacao.SomaDigitos : MetodoDepreciacao.Linear, r.GetInt16(14), r.GetDecimal(15),
                r.GetString(16), T(17), T(18), D(19), T(20), r.GetDecimal(21), r.GetDecimal(22), D(23), r.GetBoolean(24)));
        return res;
    }

    public Task<long> GravarBemAsync(long empresaId, BemImobilizado b, long contaBemId, long? contaAcumuladaId, long? contaDespesaId, long? fornecedorId,
                                     CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var cmd = b.Id == 0
                ? ds.CreateCommand("""
                    INSERT INTO imobilizado.bem (empresa_id, codigo, descricao, grupo_id, plaqueta, localizacao, nota_fiscal, fornecedor_id, data_aquisicao,
                                                 data_inicio_uso, valor_aquisicao, valor_residual, vida_util_meses, metodo, turnos, depreciacao_anterior,
                                                 conta_bem_id, conta_acumulada_id, conta_despesa_id, data_baixa, motivo_baixa, pro_rata_dia)
                    VALUES (@e, @cod, @d, @g, @pl, @loc, @nf, @f, @aq, @uso, @v, @res, @vida, @met, @tur, @ant, @cb, @ca, @cd, @bx, @mbx, @prd) RETURNING id
                    """)
                : ds.CreateCommand("""
                    UPDATE imobilizado.bem SET codigo = @cod, descricao = @d, grupo_id = @g, plaqueta = @pl, localizacao = @loc, nota_fiscal = @nf,
                           fornecedor_id = @f, data_aquisicao = @aq, data_inicio_uso = @uso, valor_aquisicao = @v, valor_residual = @res,
                           vida_util_meses = @vida, metodo = @met, turnos = @tur, depreciacao_anterior = @ant, conta_bem_id = @cb,
                           conta_acumulada_id = @ca, conta_despesa_id = @cd, data_baixa = @bx, motivo_baixa = @mbx, pro_rata_dia = @prd
                     WHERE id = @id AND empresa_id = @e RETURNING id
                    """);
            cmd.Com("e", empresaId).Com("cod", b.Codigo).Com("d", b.Descricao).Com("g", b.GrupoId).ComTexto("pl", b.Plaqueta)
               .ComTexto("loc", b.Localizacao).ComTexto("nf", b.NotaFiscal).Com("f", NpgsqlTypes.NpgsqlDbType.Bigint, fornecedorId)
               .Com("aq", b.DataAquisicao).Com("uso", b.InicioUso).Com("v", b.Valor).Com("res", b.Residual).Com("vida", b.VidaUtilMeses)
               .Com("met", b.Metodo == MetodoDepreciacao.SomaDigitos ? "SOMA_DIGITOS" : "LINEAR").Com("tur", (short)b.Turnos)
               .Com("ant", b.DepreciacaoAnterior).Com("cb", contaBemId).Com("ca", NpgsqlTypes.NpgsqlDbType.Bigint, contaAcumuladaId)
               .Com("cd", NpgsqlTypes.NpgsqlDbType.Bigint, contaDespesaId).Com("bx", NpgsqlTypes.NpgsqlDbType.Date, b.DataBaixa)
               .ComTexto("mbx", b.MotivoBaixa).Com("prd", b.ProRataDia);
            if (b.Id != 0) cmd.Com("id", b.Id);
            return (long)(await cmd.ExecuteScalarAsync(ct))!;
        });

    public Task ExcluirBemAsync(long id, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var cmd = ds.CreateCommand("DELETE FROM imobilizado.bem WHERE id = @id").Com("id", id);
            return await cmd.ExecuteNonQueryAsync(ct);
        });

    public Task<LancamentoEfetivado> LancarDepreciacaoAsync(LancamentoValidado lancamento, IReadOnlyList<QuotaDepreciacao> quotas, long usuarioId,
                                                            CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await Mapeamentos.DefinirUsuarioAsync(con, tx, usuarioId, ct);
            var bens = quotas.Select(q => q.BemId).ToArray();
            var competencia = quotas[0].Competencia;
            // Quotas de lançamentos estornados não contam: saem para permitir o novo cálculo.
            await using (var cmd = new NpgsqlCommand("""
                DELETE FROM imobilizado.depreciacao d USING contabil.lancamento l
                 WHERE l.id = d.lancamento_id AND l.status <> 'EFETIVADO' AND d.bem_id = ANY(@b) AND d.competencia = @c
                """, con, tx).Com("b", bens).Com("c", competencia))
                await cmd.ExecuteNonQueryAsync(ct);
            var efetivado = await RepositorioContabil.GravarNaTransacaoAsync(con, tx, lancamento, usuarioId, ct);
            await using (var cmd = new NpgsqlCommand("""
                INSERT INTO imobilizado.depreciacao (bem_id, competencia, valor, valor_fiscal, lancamento_id)
                SELECT b, @c, v, f, @l FROM unnest(@b, @v, @f) AS t(b, v, f)
                """, con, tx).Com("c", competencia).Com("l", efetivado.Id).Com("b", bens)
                             .Com("v", quotas.Select(q => q.Contabil).ToArray()).Com("f", quotas.Select(q => q.Fiscal).ToArray()))
                await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return efetivado;
        });

    public async Task<IReadOnlyList<long>> LancamentosDaCompetenciaAsync(long empresaId, DateOnly competencia, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT DISTINCT l.id FROM imobilizado.depreciacao d
              JOIN imobilizado.bem b ON b.id = d.bem_id
              JOIN contabil.lancamento l ON l.id = d.lancamento_id
             WHERE b.empresa_id = @e AND d.competencia = @c AND l.status = 'EFETIVADO'
            """).Com("e", empresaId).Com("c", competencia);
        var res = new List<long>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(r.GetInt64(0));
        return res;
    }
}
