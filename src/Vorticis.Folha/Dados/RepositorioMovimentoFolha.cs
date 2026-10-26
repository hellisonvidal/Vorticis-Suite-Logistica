using Npgsql;
using Vorticis.Folha.Cadastros;
using Vorticis.Folha.Regras;

namespace Vorticis.Folha.Dados;

public sealed class RepositorioMovimentoFolha(NpgsqlDataSource ds) : IRepositorioMovimentoFolha
{
    // ------------------------------------------------------------------ competências

    public async Task<StatusCompetencia> ObterCompetenciaAsync(long empresaId, DateOnly competencia, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT status, fechada_em FROM folha.competencia WHERE empresa_id = @e AND competencia = @c")
                                .Com("e", empresaId).ComData("c", competencia);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return new StatusCompetencia(competencia, false, null);
        return new StatusCompetencia(competencia, r.GetString(0) == "FECHADA", r.IsDBNull(1) ? null : r.GetFieldValue<DateTimeOffset>(1));
    }

    public async Task<int> ContarProLaboreAsync(long empresaId, DateOnly competencia, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT COUNT(*)::int FROM folha.pro_labore_calculo WHERE empresa_id = @e AND competencia = @c")
                                .Com("e", empresaId).ComData("c", competencia);
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<IReadOnlyList<StatusCompetencia>> ListarCompetenciasAsync(long empresaId, int ano, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT competencia, status, fechada_em FROM folha.competencia WHERE empresa_id = @e AND EXTRACT(YEAR FROM competencia) = @a ORDER BY competencia
            """).Com("e", empresaId).Com("a", (double)ano);
        var res = new List<StatusCompetencia>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new StatusCompetencia(r.GetFieldValue<DateOnly>(0), r.GetString(1) == "FECHADA", r.IsDBNull(2) ? null : r.GetFieldValue<DateTimeOffset>(2)));
        return res;
    }

    public Task AlterarCompetenciaAsync(long empresaId, DateOnly competencia, bool fechar, long usuarioId, CancellationToken ct) => Sql.TraduzirAsync(async () =>
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        await Sql.DefinirUsuarioAsync(con, tx, usuarioId, ct);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO folha.competencia (empresa_id, competencia, status, fechada_em, fechada_por)
            VALUES (@e, @c, CASE WHEN @f THEN 'FECHADA' ELSE 'ABERTA' END, CASE WHEN @f THEN now() END, CASE WHEN @f THEN @u::bigint END)
            ON CONFLICT (empresa_id, competencia) DO UPDATE SET
                   status = CASE WHEN @f THEN 'FECHADA' ELSE 'ABERTA' END,
                   fechada_em = CASE WHEN @f THEN now() ELSE folha.competencia.fechada_em END,
                   fechada_por = CASE WHEN @f THEN @u::bigint ELSE folha.competencia.fechada_por END,
                   reaberta_em = CASE WHEN @f THEN folha.competencia.reaberta_em ELSE now() END,
                   reaberta_por = CASE WHEN @f THEN folha.competencia.reaberta_por ELSE @u::bigint END
            """, con, tx).Com("e", empresaId).ComData("c", competencia).Com("f", fechar).ComId("u", usuarioId);
        await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return 0;
    });

    // ------------------------------------------------------------------ eventos

    public async Task<IReadOnlyList<EventoVariavel>> ListarEventosAsync(long empresaId, DateOnly competencia, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT v.id, v.funcionario_id, f.nome, v.competencia, ru.codigo, ru.descricao, v.referencia, v.valor, v.observacao
              FROM folha.evento_variavel v JOIN folha.funcionario f ON f.id = v.funcionario_id JOIN folha.rubrica ru ON ru.id = v.rubrica_id
             WHERE v.empresa_id = @e AND v.competencia = @c ORDER BY f.nome, ru.codigo, v.id
            """).Com("e", empresaId).ComData("c", competencia);
        var res = new List<EventoVariavel>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new EventoVariavel(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetFieldValue<DateOnly>(3), r.GetString(4), r.GetString(5),
                                       r.GetDecimal(6), r.Valor(7), r.Texto(8)));
        return res;
    }

    public Task<long> GravarEventoAsync(long empresaId, long funcionarioId, DateOnly competencia, string rubricaCodigo, decimal referencia, decimal? valor,
                                        string? observacao, CancellationToken ct) => Sql.TraduzirAsync(async () =>
    {
        await using var cmd = ds.CreateCommand("""
            INSERT INTO folha.evento_variavel (empresa_id, funcionario_id, competencia, rubrica_id, referencia, valor, observacao)
            SELECT @e, @f, @c, ru.id, @ref, @v, @o FROM folha.rubrica ru WHERE ru.empresa_id = @e AND ru.codigo = @cod RETURNING id
            """).Com("e", empresaId).Com("f", funcionarioId).ComData("c", competencia).Com("cod", rubricaCodigo).Com("ref", referencia)
            .Com("v", NpgsqlTypes.NpgsqlDbType.Numeric, valor).ComTexto("o", observacao);
        return await cmd.ExecuteScalarAsync(ct) as long? ?? throw new Vorticis.Domain.Comum.ViolacaoRegraException($"Rubrica {rubricaCodigo} não cadastrada.");
    });

    public Task ExcluirEventoAsync(long empresaId, long id, CancellationToken ct) => Sql.TraduzirAsync(async () =>
    {
        await using var cmd = ds.CreateCommand("DELETE FROM folha.evento_variavel WHERE id = @id AND empresa_id = @e").Com("id", id).Com("e", empresaId);
        return await cmd.ExecuteNonQueryAsync(ct);
    });

    // ------------------------------------------------------------------ cálculos

    public Task<long> GravarCalculoAsync(long empresaId, NovoCalculo c, long usuarioId, CancellationToken ct) => Sql.TraduzirAsync(async () =>
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        await using (var del = new NpgsqlCommand("""
            DELETE FROM folha.calculo WHERE empresa_id = @e AND funcionario_id = @f AND competencia = @c AND tipo = @t AND COALESCE(origem_id, 0) = COALESCE(@o::bigint, 0)
            """, con, tx).Com("e", empresaId).Com("f", c.FuncionarioId).ComData("c", c.Competencia).Com("t", c.Tipo.Sql()).ComId("o", c.OrigemId))
            await del.ExecuteNonQueryAsync(ct);

        var r = c.Resultado;
        long id;
        await using (var ins = new NpgsqlCommand("""
            INSERT INTO folha.calculo (empresa_id, funcionario_id, competencia, tipo, origem_id, data_pagamento, centro_custo_id, total_proventos, total_descontos,
                   liquido, base_inss, base_irrf, base_fgts, inss, irrf, fgts, salario_familia, enc_inss_patronal, enc_rat, enc_terceiros, calculado_por)
            VALUES (@e, @f, @c, @t, @o, @dp, @cc, @tp, @td, @liq, @bi, @bir, @bf, @inss, @irrf, @fgts, @sf, @ep, @er, @et, @u) RETURNING id
            """, con, tx).Com("e", empresaId).Com("f", c.FuncionarioId).ComData("c", c.Competencia).Com("t", c.Tipo.Sql()).ComId("o", c.OrigemId)
            .ComData("dp", c.DataPagamento).ComId("cc", c.CentroCustoId).Com("tp", r.TotalProventos).Com("td", r.TotalDescontos).Com("liq", r.Liquido)
            .Com("bi", r.BaseInss).Com("bir", r.BaseIrrf).Com("bf", r.BaseFgts).Com("inss", r.Inss).Com("irrf", r.Irrf).Com("fgts", r.Fgts)
            .Com("sf", r.SalarioFamilia).Com("ep", c.Encargos.InssPatronal).Com("er", c.Encargos.Rat).Com("et", c.Encargos.Terceiros).ComId("u", usuarioId))
            id = (long)(await ins.ExecuteScalarAsync(ct))!;

        short seq = 0;
        foreach (var i in r.Itens)
        {
            await using var item = new NpgsqlCommand("""
                INSERT INTO folha.calculo_item (calculo_id, sequencia, rubrica_codigo, descricao, tipo, referencia, valor, memoria) VALUES (@c, @s, @cod, @d, @t, @ref, @v, @m)
                """, con, tx).Com("c", id).Com("s", ++seq).Com("cod", i.Codigo).Com("d", i.Descricao).Com("t", i.Tipo.ToString()).Com("ref", i.Referencia)
                .Com("v", i.Valor).Com("m", i.Memoria.Length > 500 ? i.Memoria[..500] : i.Memoria);
            await item.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return id;
    });

    private const string SelectCalculo = """
        SELECT c.id, c.funcionario_id, f.matricula, f.nome, f.cpf, c.competencia, c.tipo, c.origem_id, c.data_pagamento, c.centro_custo_id, cc.codigo,
               c.total_proventos, c.total_descontos, c.liquido, c.base_inss, c.base_irrf, c.base_fgts, c.inss, c.irrf, c.fgts, c.salario_familia,
               c.enc_inss_patronal, c.enc_rat, c.enc_terceiros
          FROM folha.calculo c JOIN folha.funcionario f ON f.id = c.funcionario_id LEFT JOIN contabil.centro_custo cc ON cc.id = c.centro_custo_id
        """;

    private static CalculoGravado Ler(NpgsqlDataReader r) => new(
        r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetFieldValue<DateOnly>(5), TipoFolhaExtensoes.DeSql(r.GetString(6)),
        r.Id(7), r.Data(8), r.Id(9), r.Texto(10), r.GetDecimal(11), r.GetDecimal(12), r.GetDecimal(13), r.GetDecimal(14), r.GetDecimal(15), r.GetDecimal(16),
        r.GetDecimal(17), r.GetDecimal(18), r.GetDecimal(19), r.GetDecimal(20), r.GetDecimal(21), r.GetDecimal(22), r.GetDecimal(23), []);

    private async Task<Dictionary<long, List<ItemGravado>>> ItensAsync(IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        var res = new Dictionary<long, List<ItemGravado>>();
        if (ids.Count == 0) return res;
        await using var cmd = ds.CreateCommand("""
            SELECT calculo_id, rubrica_codigo, descricao, tipo, referencia, valor, memoria FROM folha.calculo_item WHERE calculo_id = ANY(@ids) ORDER BY calculo_id, sequencia
            """).Com("ids", ids.ToArray());
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            if (!res.TryGetValue(r.GetInt64(0), out var l)) res[r.GetInt64(0)] = l = [];
            l.Add(new ItemGravado(r.GetString(1), r.GetString(2), r.GetString(3)[0], r.GetDecimal(4), r.GetDecimal(5), r.GetString(6)));
        }
        return res;
    }

    public async Task<IReadOnlyList<CalculoGravado>> ListarCalculosAsync(long empresaId, DateOnly competencia, TipoFolha? tipo, bool comItens, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand(SelectCalculo + """
             WHERE c.empresa_id = @e AND c.competencia = @c AND (@t::text IS NULL OR c.tipo = @t) ORDER BY f.nome, c.tipo, c.id
            """).Com("e", empresaId).ComData("c", competencia).ComTexto("t", tipo?.Sql());
        var res = new List<CalculoGravado>();
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) res.Add(Ler(r));
        if (!comItens) return res;
        var itens = await ItensAsync(res.Select(x => x.Id).ToArray(), ct);
        return res.Select(x => itens.TryGetValue(x.Id, out var l) ? x with { Itens = l } : x).ToList();
    }

    public async Task<CalculoGravado?> ObterCalculoAsync(long empresaId, long calculoId, CancellationToken ct)
    {
        CalculoGravado? c = null;
        await using (var cmd = ds.CreateCommand(SelectCalculo + " WHERE c.empresa_id = @e AND c.id = @id").Com("e", empresaId).Com("id", calculoId))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            if (await r.ReadAsync(ct)) c = Ler(r);
        if (c is null) return null;
        var itens = await ItensAsync([c.Id], ct);
        return itens.TryGetValue(c.Id, out var l) ? c with { Itens = l } : c;
    }

    public Task ExcluirCalculosAsync(long empresaId, DateOnly competencia, TipoFolha tipo, long? origemId, CancellationToken ct, long? funcionarioId = null) => Sql.TraduzirAsync(async () =>
    {
        await using var cmd = ds.CreateCommand("""
            DELETE FROM folha.calculo WHERE empresa_id = @e AND competencia = @c AND tipo = @t AND (@o::bigint IS NULL OR origem_id = @o)
               AND (@fn::bigint IS NULL OR funcionario_id = @fn)
            """).Com("e", empresaId).ComData("c", competencia).Com("t", tipo.Sql()).ComId("o", origemId).ComId("fn", funcionarioId);
        return await cmd.ExecuteNonQueryAsync(ct);
    });

    public async Task<(decimal Soma, int Meses)> VariaveisUltimosMesesAsync(long empresaId, long funcionarioId, DateOnly competencia, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT COALESCE(SUM(i.valor) FILTER (WHERE i.tipo = 'P' AND ru.variavel), 0), COUNT(DISTINCT c.competencia)
              FROM folha.calculo c
              JOIN folha.calculo_item i ON i.calculo_id = c.id
              LEFT JOIN folha.rubrica ru ON ru.empresa_id = c.empresa_id AND ru.codigo = i.rubrica_codigo
             WHERE c.empresa_id = @e AND c.funcionario_id = @f AND c.tipo = 'MENSAL' AND c.competencia < @c AND c.competencia >= (@c::date - INTERVAL '12 months')
            """).Com("e", empresaId).Com("f", funcionarioId).ComData("c", competencia);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        return (r.GetDecimal(0), (int)r.GetInt64(1));
    }

    public async Task<decimal> FgtsAcumuladoAsync(long empresaId, long funcionarioId, DateOnly ate, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT COALESCE(SUM(fgts), 0) FROM folha.calculo WHERE empresa_id = @e AND funcionario_id = @f AND competencia <= @c AND tipo <> 'RESCISAO'")
                                .Com("e", empresaId).Com("f", funcionarioId).ComData("c", ate);
        return (decimal)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<decimal> TotalDecimoPrimeiraParcelaAsync(long empresaId, long funcionarioId, int ano, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT COALESCE(SUM(total_proventos), 0) FROM folha.calculo
             WHERE empresa_id = @e AND funcionario_id = @f AND tipo = 'DECIMO_1' AND EXTRACT(YEAR FROM competencia) = @a
            """).Com("e", empresaId).Com("f", funcionarioId).Com("a", (double)ano);
        return (decimal)(await cmd.ExecuteScalarAsync(ct))!;
    }

    // ------------------------------------------------------------------ férias

    private const string SelectFerias = """
        SELECT x.id, x.funcionario_id, f.nome, x.aquisitivo_inicio, x.aquisitivo_fim, x.gozo_inicio, x.dias_gozo, x.abono_dias, x.faltas_injustificadas,
               x.data_pagamento, x.status, COALESCE((SELECT c.liquido FROM folha.calculo c WHERE c.tipo = 'FERIAS' AND c.origem_id = x.id), 0)
          FROM folha.ferias x JOIN folha.funcionario f ON f.id = x.funcionario_id
        """;

    private static FeriasRegistro LerFerias(NpgsqlDataReader r) => new(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetFieldValue<DateOnly>(3),
        r.GetFieldValue<DateOnly>(4), r.GetFieldValue<DateOnly>(5), r.GetInt16(6), r.GetInt16(7), r.GetInt16(8), r.GetFieldValue<DateOnly>(9), r.GetString(10), r.GetDecimal(11));

    public async Task<IReadOnlyList<FeriasRegistro>> ListarFeriasAsync(long empresaId, long? funcionarioId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand(SelectFerias + " WHERE x.empresa_id = @e AND (@f::bigint IS NULL OR x.funcionario_id = @f) ORDER BY x.gozo_inicio DESC")
                                .Com("e", empresaId).ComId("f", funcionarioId);
        var res = new List<FeriasRegistro>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(LerFerias(r));
        return res;
    }

    public async Task<FeriasRegistro?> ObterFeriasAsync(long empresaId, long id, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand(SelectFerias + " WHERE x.empresa_id = @e AND x.id = @id").Com("e", empresaId).Com("id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? LerFerias(r) : null;
    }

    public Task<long> GravarFeriasAsync(long empresaId, long funcionarioId, PeriodoAquisitivo p, DateOnly gozoInicio, int diasGozo, int abonoDias, int faltas,
                                        DateOnly pagamento, CancellationToken ct) => Sql.TraduzirAsync(async () =>
    {
        await using var cmd = ds.CreateCommand("""
            INSERT INTO folha.ferias (empresa_id, funcionario_id, aquisitivo_inicio, aquisitivo_fim, gozo_inicio, dias_gozo, abono_dias, faltas_injustificadas, data_pagamento)
            VALUES (@e, @f, @ai, @af, @g, @d, @a, @fa, @p) RETURNING id
            """).Com("e", empresaId).Com("f", funcionarioId).ComData("ai", p.Inicio).ComData("af", p.Fim).ComData("g", gozoInicio).Com("d", (short)diasGozo)
            .Com("a", (short)abonoDias).Com("fa", (short)faltas).ComData("p", pagamento);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    });

    public Task CancelarFeriasAsync(long empresaId, long id, CancellationToken ct) => Sql.TraduzirAsync(async () =>
    {
        await using var cmd = ds.CreateCommand("UPDATE folha.ferias SET status = 'CANCELADA' WHERE id = @id AND empresa_id = @e").Com("id", id).Com("e", empresaId);
        return await cmd.ExecuteNonQueryAsync(ct);
    });

    // ------------------------------------------------------------------ rescisões

    private const string SelectRescisao = """
        SELECT x.id, x.funcionario_id, f.nome, x.motivo, x.data_desligamento, x.aviso_tipo, x.dias_aviso, x.saldo_fgts, x.data_pagamento, x.status,
               COALESCE((SELECT c.liquido FROM folha.calculo c WHERE c.tipo = 'RESCISAO' AND c.origem_id = x.id), 0),
               COALESCE((SELECT i.valor FROM folha.calculo c JOIN folha.calculo_item i ON i.calculo_id = c.id
                          WHERE c.tipo = 'RESCISAO' AND c.origem_id = x.id AND i.rubrica_codigo = '9005'), 0)
          FROM folha.rescisao x JOIN folha.funcionario f ON f.id = x.funcionario_id
        """;

    private static RescisaoRegistro LerRescisao(NpgsqlDataReader r) => new(r.GetInt64(0), r.GetInt64(1), r.GetString(2), MotivoRescisaoExtensoes.DeSql(r.GetString(3)),
        r.GetFieldValue<DateOnly>(4), MotivoRescisaoExtensoes.AvisoDeSql(r.GetString(5)), r.GetInt16(6), r.GetDecimal(7), r.GetFieldValue<DateOnly>(8), r.GetString(9),
        r.GetDecimal(10), r.GetDecimal(11));

    public async Task<IReadOnlyList<RescisaoRegistro>> ListarRescisoesAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand(SelectRescisao + " WHERE x.empresa_id = @e ORDER BY x.data_desligamento DESC").Com("e", empresaId);
        var res = new List<RescisaoRegistro>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(LerRescisao(r));
        return res;
    }

    public async Task<RescisaoRegistro?> ObterRescisaoAsync(long empresaId, long id, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand(SelectRescisao + " WHERE x.empresa_id = @e AND x.id = @id").Com("e", empresaId).Com("id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? LerRescisao(r) : null;
    }

    public Task<long> GravarRescisaoAsync(long empresaId, long funcionarioId, SolicitacaoRescisao s, int diasAviso, DateOnly pagamento, CancellationToken ct)
        => Sql.TraduzirAsync(async () =>
        {
            await using var cmd = ds.CreateCommand("""
                INSERT INTO folha.rescisao (empresa_id, funcionario_id, motivo, data_desligamento, aviso_tipo, dias_aviso, saldo_fgts, data_pagamento)
                VALUES (@e, @f, @m, @d, @a, @da, @s, @p) RETURNING id
                """).Com("e", empresaId).Com("f", funcionarioId).Com("m", s.Motivo.Sql()).ComData("d", s.DataDesligamento).Com("a", s.Aviso.Sql())
                .Com("da", (short)diasAviso).Com("s", s.SaldoFgts).ComData("p", pagamento);
            return (long)(await cmd.ExecuteScalarAsync(ct))!;
        });

    public Task CancelarRescisaoAsync(long empresaId, long id, CancellationToken ct) => Sql.TraduzirAsync(async () =>
    {
        await using var cmd = ds.CreateCommand("UPDATE folha.rescisao SET status = 'CANCELADA' WHERE id = @id AND empresa_id = @e").Com("id", id).Com("e", empresaId);
        return await cmd.ExecuteNonQueryAsync(ct);
    });
}
