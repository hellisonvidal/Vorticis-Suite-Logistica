using System.Text.Json;
using Npgsql;
using Vorticis.Infra.Postgres;

namespace Vorticis.Infra.Importacao;

/// <summary>Guarda o de-para (campo → coluna da planilha) de cada cadastro da empresa, para repetir a importação sem refazer o layout.</summary>
public sealed class ServicoLayoutsPlanilha(NpgsqlDataSource ds)
{
    private readonly BancoModulo _db = new(ds);

    public async Task<Dictionary<string, string>?> LerAsync(long empresa, string cadastro, CancellationToken ct = default)
    {
        var json = await _db.EscalarAsync<string>("SELECT mapeamento::text FROM core.layout_planilha WHERE empresa_id = @e AND cadastro = @c", ct, ("e", empresa), ("c", cadastro));
        return json is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(json);
    }

    public Task GravarAsync(long empresa, string cadastro, IReadOnlyDictionary<string, string> mapa, CancellationToken ct = default) => _db.ExecutarAsync("""
        INSERT INTO core.layout_planilha (empresa_id, cadastro, mapeamento) VALUES (@e, @c, @m::jsonb)
        ON CONFLICT (empresa_id, cadastro) DO UPDATE SET mapeamento = EXCLUDED.mapeamento, atualizado_em = now()
        """, ct, ("e", empresa), ("c", cadastro), ("m", JsonSerializer.Serialize(mapa.Where(m => !string.IsNullOrWhiteSpace(m.Value)).ToDictionary())));
}
