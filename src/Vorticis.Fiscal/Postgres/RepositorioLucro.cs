using Npgsql;
using NpgsqlTypes;
using Vorticis.Fiscal.Lucro;
using Vorticis.Fiscal.Simples;

namespace Vorticis.Fiscal.Postgres;

public sealed class RepositorioLucro(NpgsqlDataSource ds) : IRepositorioLucro
{
    // ------------------------------------------------------------------ LALUR / LACS

    public async Task<IReadOnlyList<LancamentoLalur>> ListarLalurAsync(long empresaId, int ano, int? trimestre, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT l.id, l.ano, l.trimestre, l.tributo, l.tipo, l.descricao, l.valor, l.codigo_ecf, c.codigo
              FROM fiscal.lalur_lancamento l LEFT JOIN contabil.plano_contas c ON c.id = l.conta_id
             WHERE l.empresa_id = @e AND l.ano = @a AND (@t::int IS NULL OR l.trimestre = @t)
             ORDER BY l.ano, l.trimestre, l.tipo, l.id
            """).Com("e", empresaId).Com("a", (short)ano).Com("t", NpgsqlDbType.Integer, trimestre);
        var res = new List<LancamentoLalur>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new LancamentoLalur(r.GetInt64(0), r.GetInt16(1), r.GetInt16(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetDecimal(6), r.Texto(7), r.Texto(8)));
        return res;
    }

    public Task<long> GravarLalurAsync(long empresaId, LancamentoLalur l, long? contaId, long usuarioId, CancellationToken ct)
        => Sql.TraduzirErrosAsync(async () =>
        {
            await using var cmd = ds.CreateCommand("""
                INSERT INTO fiscal.lalur_lancamento (empresa_id, ano, trimestre, tributo, tipo, descricao, valor, codigo_ecf, conta_id, criado_por)
                VALUES (@e, @a, @t, @tr, @tp, @d, @v, @cod, @c, @u) RETURNING id
                """).Com("e", empresaId).Com("a", (short)l.Ano).Com("t", (short)l.Trimestre).Com("tr", l.Tributo).Com("tp", l.Tipo).Com("d", l.Descricao)
                    .Com("v", l.Valor).ComTexto("cod", l.CodigoEcf).Com("c", NpgsqlDbType.Bigint, contaId).Com("u", usuarioId);
            return (long)(await cmd.ExecuteScalarAsync(ct))!;
        });

    public async Task ExcluirLalurAsync(long empresaId, long id, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("DELETE FROM fiscal.lalur_lancamento WHERE id = @i AND empresa_id = @e").Com("i", id).Com("e", empresaId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<(decimal AdicoesIrpj, decimal ExclusoesIrpj, decimal AdicoesCsll, decimal ExclusoesCsll)> SomarLalurAsync(long empresaId, int ano, int trimestre, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT COALESCE(SUM(valor) FILTER (WHERE tipo = 'ADICAO'  AND tributo IN ('IRPJ', 'AMBOS')), 0),
                   COALESCE(SUM(valor) FILTER (WHERE tipo = 'EXCLUSAO' AND tributo IN ('IRPJ', 'AMBOS')), 0),
                   COALESCE(SUM(valor) FILTER (WHERE tipo = 'ADICAO'  AND tributo IN ('CSLL', 'AMBOS')), 0),
                   COALESCE(SUM(valor) FILTER (WHERE tipo = 'EXCLUSAO' AND tributo IN ('CSLL', 'AMBOS')), 0)
              FROM fiscal.lalur_lancamento WHERE empresa_id = @e AND ano = @a AND trimestre = @t
            """).Com("e", empresaId).Com("a", (short)ano).Com("t", (short)trimestre);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        return (r.GetDecimal(0), r.GetDecimal(1), r.GetDecimal(2), r.GetDecimal(3));
    }

    // ------------------------------------------------------------------ Parte B

    public async Task<decimal> SaldoParteBAsync(long empresaId, string tributo, int ano, int trimestre, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT COALESCE(SUM(valor), 0) FROM fiscal.lalur_parte_b
             WHERE empresa_id = @e AND tributo = @t AND (tipo = 'SALDO_INICIAL' OR (ano * 4 + trimestre) < (@a * 4 + @q))
            """).Com("e", empresaId).Com("t", tributo).Com("a", ano).Com("q", trimestre);
        return Math.Max(0, (decimal)(await cmd.ExecuteScalarAsync(ct))!);
    }

    public async Task<IReadOnlyList<MovimentoParteB>> ListarParteBAsync(long empresaId, string tributo, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT id, tributo, tipo, ano, trimestre, valor, descricao FROM fiscal.lalur_parte_b
             WHERE empresa_id = @e AND tributo = @t ORDER BY COALESCE(ano, 0), COALESCE(trimestre, 0), id
            """).Com("e", empresaId).Com("t", tributo);
        var res = new List<MovimentoParteB>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new MovimentoParteB(r.GetInt64(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetInt16(3), r.IsDBNull(4) ? null : r.GetInt16(4), r.GetDecimal(5), r.Texto(6)));
        return res;
    }

    public async Task DefinirSaldoInicialAsync(long empresaId, string tributo, decimal valor, long usuarioId, CancellationToken ct)
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        await Sql.DefinirUsuarioAsync(con, tx, usuarioId, ct);
        await using (var del = new NpgsqlCommand("DELETE FROM fiscal.lalur_parte_b WHERE empresa_id = @e AND tributo = @t AND tipo = 'SALDO_INICIAL'", con, tx).Com("e", empresaId).Com("t", tributo))
            await del.ExecuteNonQueryAsync(ct);
        if (valor > 0)
            await using (var ins = new NpgsqlCommand("INSERT INTO fiscal.lalur_parte_b (empresa_id, tributo, tipo, valor, descricao) VALUES (@e, @t, 'SALDO_INICIAL', @v, 'Saldo inicial')", con, tx)
                             .Com("e", empresaId).Com("t", tributo).Com("v", valor))
                await ins.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ------------------------------------------------------------------ contabilidade

    public async Task<(decimal Resultado, decimal DespesaTributosLucro)> ResultadoContabilAsync(long empresaId, DateOnly inicio, DateOnly fim, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT COALESCE(SUM(CASE p.ind_dc WHEN 'C' THEN p.valor ELSE -p.valor END) FILTER (WHERE c.cod_nat = '04'), 0),
                   COALESCE(SUM(CASE p.ind_dc WHEN 'D' THEN p.valor ELSE -p.valor END)
                            FILTER (WHERE c.id IN (SELECT conta_despesa_id FROM fiscal.conta_tributo WHERE empresa_id = @e AND tributo IN ('IRPJ', 'CSLL'))), 0)
              FROM contabil.partida p
              JOIN contabil.lancamento l ON l.id = p.lancamento_id
              JOIN contabil.plano_contas c ON c.id = p.conta_id
             WHERE l.empresa_id = @e AND l.status <> 'RASCUNHO' AND l.ind_lcto <> 'E' AND l.data_lancamento BETWEEN @ini AND @fim
            """).Com("e", empresaId).Com("ini", inicio).Com("fim", fim);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        return (r.GetDecimal(0), r.GetDecimal(1));
    }

    // ------------------------------------------------------------------ apuração IRPJ/CSLL

    public async Task<long> GravarApuracaoAsync(long empresaId, ResultadoLucro r, long usuarioId, CancellationToken ct)
    {
        var regime = r.Regime == RegimeLucro.Real ? "REAL" : "PRESUMIDO";
        await using var con = await ds.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        await Sql.DefinirUsuarioAsync(con, tx, usuarioId, ct);
        await using (var del = new NpgsqlCommand("DELETE FROM fiscal.apuracao_lucro WHERE empresa_id = @e AND regime = @r AND ano = @a AND trimestre = @t", con, tx)
                         .Com("e", empresaId).Com("r", regime).Com("a", (short)r.Ano).Com("t", (short)r.Trimestre))
            await del.ExecuteNonQueryAsync(ct);   // o razão da Parte B desta apuração sai junto (ON DELETE CASCADE)
        long id;
        await using (var ins = new NpgsqlCommand("""
            INSERT INTO fiscal.apuracao_lucro (empresa_id, regime, ano, trimestre, periodo_inicio, periodo_fim, receita_bruta, lucro_contabil, adicoes_irpj, exclusoes_irpj,
                adicoes_csll, exclusoes_csll, compensacao_irpj, compensacao_csll, prejuizo_irpj, prejuizo_csll, base_irpj, base_csll, irpj_normal, irpj_adicional,
                irpj_deducoes, irpj_a_pagar, csll_devida, csll_deducoes, csll_a_pagar, vencimento, resultado, criado_por)
            VALUES (@e, @reg, @a, @t, @ini, @fim, @rec, @ll, @adi, @exi, @adc, @exc, @coi, @coc, @pi, @pc, @bi, @bc, @inor, @ia, @idd, @ip, @cd, @cded, @cp, @venc, @res, @u)
            RETURNING id
            """, con, tx).Com("e", empresaId).Com("reg", regime).Com("a", (short)r.Ano).Com("t", (short)r.Trimestre).Com("ini", r.Inicio).Com("fim", r.Fim)
                .Com("rec", r.ReceitaBruta).Com("ll", NpgsqlDbType.Numeric, r.LucroContabil).Com("adi", r.AdicoesIrpj).Com("exi", r.ExclusoesIrpj).Com("adc", r.AdicoesCsll)
                .Com("exc", r.ExclusoesCsll).Com("coi", r.CompensacaoIrpj).Com("coc", r.CompensacaoCsll).Com("pi", r.PrejuizoIrpj).Com("pc", r.PrejuizoCsll)
                .Com("bi", r.BaseIrpj).Com("bc", r.BaseCsll).Com("inor", r.IrpjNormal).Com("ia", r.IrpjAdicional).Com("idd", r.IrpjDeducoes).Com("ip", r.IrpjAPagar)
                .Com("cd", r.CsllDevida).Com("cded", r.CsllDeducoes).Com("cp", r.CsllAPagar).Com("venc", r.Vencimento)
                .Com("res", NpgsqlDbType.Jsonb, JsonFiscal.Serializar(r)).Com("u", usuarioId))
            id = (long)(await ins.ExecuteScalarAsync(ct))!;

        if (r.Regime == RegimeLucro.Real)
        {
            async Task Parte(string tributo, string tipo, decimal valor, string descricao)
            {
                if (valor == 0) return;
                await using var b = new NpgsqlCommand("""
                    INSERT INTO fiscal.lalur_parte_b (empresa_id, tributo, tipo, ano, trimestre, valor, apuracao_id, descricao) VALUES (@e, @t, @tp, @a, @q, @v, @ap, @d)
                    """, con, tx).Com("e", empresaId).Com("t", tributo).Com("tp", tipo).Com("a", (short)r.Ano).Com("q", (short)r.Trimestre).Com("v", valor).Com("ap", id).Com("d", descricao);
                await b.ExecuteNonQueryAsync(ct);
            }
            await Parte("IRPJ", "PREJUIZO", r.PrejuizoIrpj, $"Prejuízo fiscal do {r.Trimestre}º tri/{r.Ano}");
            await Parte("CSLL", "PREJUIZO", r.PrejuizoCsll, $"Base negativa da CSLL do {r.Trimestre}º tri/{r.Ano}");
            await Parte("IRPJ", "COMPENSACAO", -r.CompensacaoIrpj, $"Compensação no {r.Trimestre}º tri/{r.Ano}");
            await Parte("CSLL", "COMPENSACAO", -r.CompensacaoCsll, $"Compensação no {r.Trimestre}º tri/{r.Ano}");
        }
        await tx.CommitAsync(ct);
        return id;
    }

    private const string ColunasApuracao = "id, resultado::text, lancamento_id";

    private static ApuracaoLucroSalva LerApuracao(NpgsqlDataReader r) => new(r.GetInt64(0), JsonFiscal.Ler<ResultadoLucro>(r.GetString(1)), r.LongOuNulo(2));

    public async Task<IReadOnlyList<ApuracaoLucroSalva>> ListarApuracoesAsync(long empresaId, int ano, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand($"SELECT {ColunasApuracao} FROM fiscal.apuracao_lucro WHERE empresa_id = @e AND (@a = 0 OR ano = @a) ORDER BY ano, trimestre, regime")
            .Com("e", empresaId).Com("a", ano);
        var res = new List<ApuracaoLucroSalva>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(LerApuracao(r));
        return res;
    }

    public async Task<ApuracaoLucroSalva?> ObterApuracaoAsync(long empresaId, RegimeLucro regime, int ano, int trimestre, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand($"SELECT {ColunasApuracao} FROM fiscal.apuracao_lucro WHERE empresa_id = @e AND regime = @r AND ano = @a AND trimestre = @t")
            .Com("e", empresaId).Com("r", regime == RegimeLucro.Real ? "REAL" : "PRESUMIDO").Com("a", (short)ano).Com("t", (short)trimestre);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? LerApuracao(r) : null;
    }

    public async Task ExcluirApuracaoAsync(long empresaId, long id, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("DELETE FROM fiscal.apuracao_lucro WHERE id = @i AND empresa_id = @e").Com("i", id).Com("e", empresaId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ------------------------------------------------------------------ PIS e COFINS

    public async Task<long> GravarPisCofinsAsync(long empresaId, ResultadoPisCofins r, long usuarioId, CancellationToken ct)
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        await Sql.DefinirUsuarioAsync(con, tx, usuarioId, ct);
        await using (var del = new NpgsqlCommand("DELETE FROM fiscal.apuracao_pis_cofins WHERE empresa_id = @e AND competencia = @c", con, tx).Com("e", empresaId).Com("c", r.Competencia))
            await del.ExecuteNonQueryAsync(ct);
        long id;
        await using (var ins = new NpgsqlCommand("""
            INSERT INTO fiscal.apuracao_pis_cofins (empresa_id, competencia, regime, receita_base, pis_debito, cofins_debito, base_credito, pis_credito, cofins_credito,
                pis_retido, cofins_retido, pis_a_pagar, cofins_a_pagar, vencimento, resultado, criado_por)
            VALUES (@e, @c, @reg, @rb, @pd, @cd, @bc, @pc, @cc, @pr, @cr, @pp, @cp, @venc, @res, @u) RETURNING id
            """, con, tx).Com("e", empresaId).Com("c", r.Competencia).Com("reg", r.Regime == RegimePisCofins.Cumulativo ? "CUMULATIVO" : "NAO_CUMULATIVO")
                .Com("rb", r.ReceitaBase).Com("pd", r.PisDebito).Com("cd", r.CofinsDebito).Com("bc", r.BaseCreditos).Com("pc", r.PisCredito).Com("cc", r.CofinsCredito)
                .Com("pr", r.PisRetido).Com("cr", r.CofinsRetido).Com("pp", r.PisAPagar).Com("cp", r.CofinsAPagar).Com("venc", r.Vencimento)
                .Com("res", NpgsqlDbType.Jsonb, JsonFiscal.Serializar(r)).Com("u", usuarioId))
            id = (long)(await ins.ExecuteScalarAsync(ct))!;
        await tx.CommitAsync(ct);
        return id;
    }

    private static ApuracaoPisCofinsSalva LerPisCofins(NpgsqlDataReader r) => new(r.GetInt64(0), JsonFiscal.Ler<ResultadoPisCofins>(r.GetString(1)), r.LongOuNulo(2));

    public async Task<IReadOnlyList<ApuracaoPisCofinsSalva>> ListarPisCofinsAsync(long empresaId, int ano, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand($"SELECT {ColunasApuracao} FROM fiscal.apuracao_pis_cofins WHERE empresa_id = @e AND (@a = 0 OR extract(year FROM competencia) = @a) ORDER BY competencia")
            .Com("e", empresaId).Com("a", ano);
        var res = new List<ApuracaoPisCofinsSalva>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(LerPisCofins(r));
        return res;
    }

    public async Task<ApuracaoPisCofinsSalva?> ObterPisCofinsAsync(long empresaId, DateOnly competencia, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand($"SELECT {ColunasApuracao} FROM fiscal.apuracao_pis_cofins WHERE empresa_id = @e AND competencia = @c").Com("e", empresaId).Com("c", competencia);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? LerPisCofins(r) : null;
    }

    public async Task ExcluirPisCofinsAsync(long empresaId, long id, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("DELETE FROM fiscal.apuracao_pis_cofins WHERE id = @i AND empresa_id = @e").Com("i", id).Com("e", empresaId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
