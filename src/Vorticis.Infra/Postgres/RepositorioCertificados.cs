using Npgsql;
using NpgsqlTypes;
using Vorticis.Application.Sistema;

namespace Vorticis.Infra.Postgres;

public sealed class RepositorioCertificados(NpgsqlDataSource ds) : IRepositorioCertificados
{
    public async Task<IReadOnlyList<CertificadoDigital>> ListarAsync(CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT id, tipo, descricao, titular, documento, emissor, valido_de, valido_ate, impressao, empresa_id, padrao_contador, padrao_responsavel, padrao_procurador
              FROM core.certificado_digital ORDER BY valido_ate
            """);
        var res = new List<CertificadoDigital>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new CertificadoDigital(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
                r.GetString(5), r.GetFieldValue<DateTimeOffset>(6), r.GetFieldValue<DateTimeOffset>(7), r.GetString(8),
                r.IsDBNull(9) ? null : r.GetInt64(9), r.GetBoolean(10), r.GetBoolean(11), r.GetBoolean(12)));
        return res;
    }

    public Task<long> IncluirAsync(CertificadoDigital c, byte[]? arquivo, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var cmd = ds.CreateCommand("""
                INSERT INTO core.certificado_digital (tipo, descricao, titular, documento, emissor, valido_de, valido_ate, impressao, arquivo, empresa_id)
                VALUES (@t, @d, @ti, @doc, @em, @de, @ate, @imp, @arq, @e) RETURNING id
                """).Com("t", c.Tipo).Com("d", c.Descricao).Com("ti", c.Titular).ComTexto("doc", c.Documento).Com("em", c.Emissor)
                    .Com("de", c.ValidoDe).Com("ate", c.ValidoAte).Com("imp", c.Impressao).Com("arq", NpgsqlDbType.Bytea, arquivo)
                    .Com("e", NpgsqlDbType.Bigint, c.EmpresaId);
            return (long)(await cmd.ExecuteScalarAsync(ct))!;
        });

    public async Task<byte[]?> ObterArquivoAsync(long id, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT arquivo FROM core.certificado_digital WHERE id = @id").Com("id", id);
        return await cmd.ExecuteScalarAsync(ct) as byte[];
    }

    public async Task DefinirPadraoAsync(long id, PapelAssinatura papel, bool padrao, CancellationToken ct)
    {
        var coluna = papel switch { PapelAssinatura.Contador => "padrao_contador", PapelAssinatura.Procurador => "padrao_procurador", _ => "padrao_responsavel" };
        await using var con = await ds.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        if (padrao)
        {
            await using var limpa = new NpgsqlCommand($"""
                UPDATE core.certificado_digital SET {coluna} = FALSE
                 WHERE {coluna} AND id <> @id
                   AND COALESCE(empresa_id, 0) = (SELECT COALESCE(empresa_id, 0) FROM core.certificado_digital WHERE id = @id)
                """, con, tx);
            limpa.Parameters.AddWithValue("id", id);
            await limpa.ExecuteNonQueryAsync(ct);
        }
        await using var marca = new NpgsqlCommand($"UPDATE core.certificado_digital SET {coluna} = @p WHERE id = @id", con, tx);
        marca.Parameters.AddWithValue("p", padrao);
        marca.Parameters.AddWithValue("id", id);
        await marca.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    public Task ExcluirAsync(long id, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var cmd = ds.CreateCommand("DELETE FROM core.certificado_digital WHERE id = @id").Com("id", id);
            return await cmd.ExecuteNonQueryAsync(ct);
        });
}
