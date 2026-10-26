using Npgsql;
using NpgsqlTypes;
using Vorticis.Fiscal.Guias;

namespace Vorticis.Fiscal.Postgres;

public sealed class RepositorioGuias(NpgsqlDataSource ds) : IRepositorioGuias
{
    private static string DocumentoSql(DocumentoGuia d) => d switch
    {
        DocumentoGuia.Das => "DAS", DocumentoGuia.Darf => "DARF", DocumentoGuia.Gps => "GPS", DocumentoGuia.DctfWeb => "DCTFWEB", _ => "ISS",
    };

    private static DocumentoGuia DocumentoDe(string s) => s switch
    {
        "DAS" => DocumentoGuia.Das, "GPS" => DocumentoGuia.Gps, "DCTFWEB" => DocumentoGuia.DctfWeb, "ISS" => DocumentoGuia.Iss, _ => DocumentoGuia.Darf,
    };

    public async Task<IReadOnlyList<Guia>> ListarAsync(long empresaId, FiltroGuias f, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT id, tributo, descricao, competencia, vencimento, valor, codigo_receita, documento, origem, referencia, situacao = 'PAGA', data_pagamento, valor_pago, observacao
              FROM fiscal.guia
             WHERE empresa_id = @e
               AND (@ci::date IS NULL OR competencia >= @ci) AND (@cf::date IS NULL OR competencia <= @cf)
               AND (@vi::date IS NULL OR vencimento >= @vi) AND (@vf::date IS NULL OR vencimento <= @vf)
               AND (@p::boolean IS NULL OR (situacao = 'PAGA') = @p)
             ORDER BY vencimento, competencia, tributo, id
            """).Com("e", empresaId).Com("ci", NpgsqlDbType.Date, f.CompetenciaInicio).Com("cf", NpgsqlDbType.Date, f.CompetenciaFim)
                .Com("vi", NpgsqlDbType.Date, f.VencimentoInicio).Com("vf", NpgsqlDbType.Date, f.VencimentoFim).Com("p", NpgsqlDbType.Boolean, f.Paga);
        var res = new List<Guia>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new Guia(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetFieldValue<DateOnly>(3), r.GetFieldValue<DateOnly>(4), r.GetDecimal(5), r.Texto(6),
                DocumentoDe(r.GetString(7)), r.GetString(8), r.Texto(9), r.GetBoolean(10), r.DataOuNula(11), r.IsDBNull(12) ? null : r.GetDecimal(12), r.Texto(13)));
        return res;
    }

    public async Task<int> SubstituirGeradasAsync(long empresaId, DateOnly competenciaInicio, DateOnly competenciaFim, IReadOnlyList<NovaGuia> guias, long usuarioId, CancellationToken ct)
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        await Sql.DefinirUsuarioAsync(con, tx, usuarioId, ct);
        await using (var del = new NpgsqlCommand("""
            DELETE FROM fiscal.guia WHERE empresa_id = @e AND origem <> 'MANUAL' AND situacao = 'ABERTA' AND competencia BETWEEN @ini AND @fim
            """, con, tx).Com("e", empresaId).Com("ini", competenciaInicio).Com("fim", competenciaFim))
            await del.ExecuteNonQueryAsync(ct);
        var inseridas = 0;
        foreach (var g in guias)
        {
            await using var ins = new NpgsqlCommand("""
                INSERT INTO fiscal.guia (empresa_id, tributo, descricao, competencia, vencimento, valor, codigo_receita, documento, origem, referencia, observacao, criado_por)
                VALUES (@e, @t, @d, @c, @v, @val, @cod, @doc, @o, @ref, @obs, @u)
                ON CONFLICT DO NOTHING
                """, con, tx).Com("e", empresaId).Com("t", g.Tributo).Com("d", g.Descricao).Com("c", g.Competencia).Com("v", g.Vencimento).Com("val", g.Valor)
                .ComTexto("cod", g.CodigoReceita).Com("doc", DocumentoSql(g.Documento)).Com("o", g.Origem).ComTexto("ref", g.Referencia).ComTexto("obs", g.Observacao).Com("u", usuarioId);
            inseridas += await ins.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return inseridas;
    }

    public Task<long> GravarManualAsync(long empresaId, NovaGuia g, long usuarioId, CancellationToken ct)
        => Sql.TraduzirErrosAsync(async () =>
        {
            await using var cmd = ds.CreateCommand("""
                INSERT INTO fiscal.guia (empresa_id, tributo, descricao, competencia, vencimento, valor, codigo_receita, documento, origem, observacao, criado_por)
                VALUES (@e, @t, @d, @c, @v, @val, @cod, @doc, 'MANUAL', @obs, @u) RETURNING id
                """).Com("e", empresaId).Com("t", g.Tributo).Com("d", g.Descricao).Com("c", g.Competencia).Com("v", g.Vencimento).Com("val", g.Valor)
                    .ComTexto("cod", g.CodigoReceita).Com("doc", DocumentoSql(g.Documento)).ComTexto("obs", g.Observacao).Com("u", usuarioId);
            return (long)(await cmd.ExecuteScalarAsync(ct))!;
        });

    public async Task<bool> MarcarPagaAsync(long empresaId, long id, DateOnly data, decimal valorPago, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("UPDATE fiscal.guia SET situacao = 'PAGA', data_pagamento = @d, valor_pago = @v WHERE id = @i AND empresa_id = @e")
            .Com("d", data).Com("v", valorPago).Com("i", id).Com("e", empresaId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> ReabrirAsync(long empresaId, long id, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("UPDATE fiscal.guia SET situacao = 'ABERTA', data_pagamento = NULL, valor_pago = NULL WHERE id = @i AND empresa_id = @e").Com("i", id).Com("e", empresaId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> ExcluirAsync(long empresaId, long id, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("DELETE FROM fiscal.guia WHERE id = @i AND empresa_id = @e AND origem = 'MANUAL'").Com("i", id).Com("e", empresaId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }
}
