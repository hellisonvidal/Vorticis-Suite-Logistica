using System.Globalization;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Vorticis.Fiscal.Comum;

namespace Vorticis.Fiscal.Postgres;

/// <summary>
/// Parâmetros legais por vigência em core.parametro_legal. Se a chave ainda não existe, o parâmetro embutido é gravado
/// (semente) com a vigência e o fundamento legal que ele próprio declara; novas vigências entram como novas linhas.
/// </summary>
public sealed class ParametrosFiscaisPostgres(NpgsqlDataSource ds) : IParametrosFiscais
{
    public async Task<string> ObterJsonAsync(string chave, DateOnly data, CancellationToken ct = default)
    {
        for (var tentativa = 0; tentativa < 2; tentativa++)
        {
            await using var cmd = ds.CreateCommand("""
                SELECT valor::text FROM core.parametro_legal
                 WHERE chave = @c AND vigencia_inicio <= @d AND (vigencia_fim IS NULL OR vigencia_fim >= @d)
                 ORDER BY vigencia_inicio DESC LIMIT 1
                """).Com("c", chave).Com("d", data);
            if (await cmd.ExecuteScalarAsync(ct) is string json) return json;
            if (tentativa == 0) await SemearAsync(chave, ct);
        }
        return ParametrosEmbutidos.Json(chave);
    }

    private async Task SemearAsync(string chave, CancellationToken ct)
    {
        var json = ParametrosEmbutidos.Json(chave);
        using var doc = JsonDocument.Parse(json);
        var vigencia = DateOnly.ParseExact(doc.RootElement.GetProperty("vigencia_inicio").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var fundamento = doc.RootElement.TryGetProperty("fundamento", out var f) ? f.GetString() ?? chave : chave;
        await using var cmd = ds.CreateCommand("""
            INSERT INTO core.parametro_legal (chave, vigencia_inicio, valor, fundamento_legal) VALUES (@c, @v, @j, @f)
            ON CONFLICT (chave, vigencia_inicio) DO NOTHING
            """).Com("c", chave).Com("v", vigencia).Com("j", NpgsqlDbType.Jsonb, json).Com("f", fundamento.Length > 250 ? fundamento[..250] : fundamento);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
