using Npgsql;
using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Infra.Postgres;

namespace Vorticis.Infra.Cadastros;

/// <summary>Empresa do grupo (matriz ou filial).</summary>
public sealed record EmpresaGrupo(long Id, string Cnpj, string RazaoSocial, long? MatrizId)
{
    public bool EhMatriz => MatrizId is null;
    public string Rotulo => $"{(EhMatriz ? "Matriz" : "Filial")} {Domain.Cadastros.Cnpj.Formatar(Cnpj)} – {RazaoSocial}";
    public override string ToString() => Rotulo;
}

/// <summary>
/// Matriz e filiais: a filial é uma empresa com escrituração própria ligada à matriz (mesma raiz de CNPJ). Ligar e desligar exige ser
/// administrador da empresa; consultar o grupo, ter acesso a ela.
/// </summary>
public sealed class ServicoFiliais(NpgsqlDataSource ds, Autorizador autorizador)
{
    private readonly BancoModulo _db = new(ds);

    /// <summary>A matriz e as filiais do grupo da empresa (a matriz primeiro); só a própria empresa se ela não tem grupo.</summary>
    public async Task<List<EmpresaGrupo>> GrupoAsync(long empresa, long usuario, CancellationToken ct = default)
    {
        if (!(await autorizador.ObterAsync(usuario, empresa, ct)).TemAcesso) return [];
        return await _db.ListarAsync("""
            WITH m AS (SELECT COALESCE(matriz_id, id) AS id FROM core.empresa WHERE id = @e)
            SELECT e.id, e.cnpj, e.razao_social, e.matriz_id FROM core.empresa e, m
             WHERE e.id = m.id OR e.matriz_id = m.id ORDER BY e.matriz_id NULLS FIRST, e.cnpj
            """, r => new EmpresaGrupo(r.GetInt64(0), r.GetString(1).Trim(), r.GetString(2), r.Id(3)), ct, ("e", empresa));
    }

    /// <summary>Empresas que podem ser a matriz desta: mesma raiz de CNPJ, que não são filiais de outra.</summary>
    public async Task<List<EmpresaGrupo>> MatrizesPossiveisAsync(long empresa, long usuario, CancellationToken ct = default)
    {
        if (!(await autorizador.ObterAsync(usuario, empresa, ct)).TemAcesso) return [];
        return await _db.ListarAsync("""
            SELECT o.id, o.cnpj, o.razao_social, o.matriz_id FROM core.empresa e JOIN core.empresa o ON left(o.cnpj, 8) = left(e.cnpj, 8) AND o.id <> e.id
             WHERE e.id = @e AND o.matriz_id IS NULL ORDER BY o.cnpj
            """, r => new EmpresaGrupo(r.GetInt64(0), r.GetString(1).Trim(), r.GetString(2), r.Id(3)), ct, ("e", empresa));
    }

    /// <summary>Liga a empresa como filial da matriz (ou desliga, com matriz nula). As regras ficam no banco (mesma raiz, sem filial de filial).</summary>
    public async Task<Resultado> DefinirMatrizAsync(long empresa, long? matriz, long usuario, CancellationToken ct = default)
    {
        if (!(await autorizador.ObterAsync(usuario, empresa, ct)).Administrador) return Resultado.Falha("Só o administrador pode ligar ou desligar filiais.");
        try
        {
            await _db.ExecutarAsync("UPDATE core.empresa SET matriz_id = @m WHERE id = @e", ct, ("m", matriz), ("e", empresa));
            return Resultado.Ok();
        }
        catch (ViolacaoRegraException ex) { return Resultado.Falha(ex.Message.StartsWith("new row", StringComparison.Ordinal) ? "A empresa não pode ser filial de si mesma." : ex.Message); }
    }
}
