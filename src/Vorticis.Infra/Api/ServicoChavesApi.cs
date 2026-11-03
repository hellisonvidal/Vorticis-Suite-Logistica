using System.Security.Cryptography;
using System.Text;
using Npgsql;
using Vorticis.Domain.Comum;
using Vorticis.Infra.Postgres;

namespace Vorticis.Infra.Api;

/// <summary>Chave de acesso à API (sem o segredo, que só aparece ao ser gerada).</summary>
public sealed record ChaveApi(long Id, long EmpresaId, long UsuarioId, string Usuario, string Nome, string Prefixo, bool Escrita,
                              DateTime CriadaEm, DateTime? RevogadaEm, DateTime? UltimoUso)
{
    public bool Ativa => RevogadaEm is null;
}

/// <summary>Quem está chamando a API: a empresa e o usuário da chave, e se ela pode gravar.</summary>
public sealed record AcessoApi(long ChaveId, long EmpresaId, long UsuarioId, bool Escrita);

/// <summary>
/// Chaves da integração por API: gerar (só administradores), listar, revogar e validar. O segredo tem o formato "vtx_" + 43 caracteres;
/// o banco guarda só o hash SHA-256.
/// </summary>
public sealed class ServicoChavesApi(NpgsqlDataSource ds)
{
    private readonly BancoModulo _db = new(ds);
    public const string Prefixo = "vtx_";

    public static string Hash(string segredo) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(segredo.Trim()))).ToLowerInvariant();

    private Task<bool> AdministradorAsync(long usuario, CancellationToken ct) =>
        _db.EscalarAsync<bool>("SELECT COALESCE((SELECT administrador FROM core.usuario WHERE id = @u), FALSE)", ct, ("u", usuario));

    public async Task<Resultado<IReadOnlyList<ChaveApi>>> ListarAsync(long empresa, long usuario, CancellationToken ct = default)
    {
        if (!await AdministradorAsync(usuario, ct)) return Resultado<IReadOnlyList<ChaveApi>>.Falha("Só administradores gerenciam as chaves da API.");
        return Resultado<IReadOnlyList<ChaveApi>>.Ok(await _db.ListarAsync("""
            SELECT k.id, k.empresa_id, k.usuario_id, u.login, k.nome, k.prefixo, k.escrita, k.criada_em, k.revogada_em, k.ultimo_uso
              FROM core.api_chave k JOIN core.usuario u ON u.id = k.usuario_id
             WHERE k.empresa_id = @e ORDER BY k.revogada_em IS NOT NULL, k.criada_em DESC
            """, r => new ChaveApi(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetBoolean(6),
                                   r.GetDateTime(7), r.IsDBNull(8) ? null : r.GetDateTime(8), r.IsDBNull(9) ? null : r.GetDateTime(9)), ct, ("e", empresa)));
    }

    /// <summary>Gera uma chave nova para a empresa, em nome do usuário informado; devolve o segredo (mostrado uma vez só).</summary>
    public async Task<Resultado<string>> GerarAsync(long empresa, string nome, bool escrita, long usuario, CancellationToken ct = default)
    {
        if (!await AdministradorAsync(usuario, ct)) return Resultado<string>.Falha("Só administradores geram chaves da API.");
        if (string.IsNullOrWhiteSpace(nome)) return Resultado<string>.Falha("Dê um nome à chave (ex.: o sistema que vai usá-la).");
        var segredo = Prefixo + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await _db.ExecutarAsync("""
            INSERT INTO core.api_chave (empresa_id, usuario_id, nome, prefixo, hash, escrita) VALUES (@e, @u, @n, @p, @h, @w)
            """, ct, ("e", empresa), ("u", usuario), ("n", nome.Trim()), ("p", segredo[..12]), ("h", Hash(segredo)), ("w", escrita));
        return Resultado<string>.Ok(segredo);
    }

    public async Task<Resultado> RevogarAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (!await AdministradorAsync(usuario, ct)) return Resultado.Falha("Só administradores revogam chaves da API.");
        var n = await _db.ExecutarAsync("UPDATE core.api_chave SET revogada_em = now() WHERE id = @id AND empresa_id = @e AND revogada_em IS NULL",
                                        ct, ("id", id), ("e", empresa));
        return n == 1 ? Resultado.Ok() : Resultado.Falha("Chave não encontrada ou já revogada.");
    }

    /// <summary>Valida o segredo enviado pelo sistema externo (cabeçalho Authorization: Bearer ...); null se inválido ou revogado.</summary>
    public async Task<AcessoApi?> ValidarAsync(string? segredo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(segredo) || !segredo.Trim().StartsWith(Prefixo, StringComparison.Ordinal)) return null;
        var r = await _db.ListarAsync("""
            UPDATE core.api_chave k SET ultimo_uso = now()
              FROM core.usuario u
             WHERE k.hash = @h AND k.revogada_em IS NULL AND u.id = k.usuario_id AND u.ativo
            RETURNING k.id, k.empresa_id, k.usuario_id, k.escrita
            """, x => new AcessoApi(x.GetInt64(0), x.GetInt64(1), x.GetInt64(2), x.GetBoolean(3)), ct, ("h", Hash(segredo)));
        return r.FirstOrDefault();
    }
}
