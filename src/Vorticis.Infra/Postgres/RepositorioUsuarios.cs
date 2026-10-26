using Npgsql;
using NpgsqlTypes;
using Vorticis.Application.Seguranca;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Infra.Postgres;

public sealed class RepositorioUsuarios(NpgsqlDataSource ds) : IRepositorioUsuarios
{
    private const string ColunasCredenciais =
        "id, login, nome, senha_hash, ativo, administrador, deve_trocar_senha, tentativas_falhas, bloqueado_ate";

    public async Task<bool> ExisteAlgumAsync(CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT EXISTS (SELECT 1 FROM core.usuario)");
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public Task<CredenciaisUsuario?> ObterPorLoginAsync(string login, CancellationToken ct)
        => ObterCredenciais($"SELECT {ColunasCredenciais} FROM core.usuario WHERE lower(login) = lower(@v)", login, ct);

    public Task<CredenciaisUsuario?> ObterPorIdAsync(long id, CancellationToken ct)
        => ObterCredenciais($"SELECT {ColunasCredenciais} FROM core.usuario WHERE id = @v", id, ct);

    private async Task<CredenciaisUsuario?> ObterCredenciais(string sql, object valor, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand(sql).Com("v", valor);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new CredenciaisUsuario(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetBoolean(4), r.GetBoolean(5),
                                      r.GetBoolean(6), r.GetInt16(7), r.IsDBNull(8) ? null : r.GetFieldValue<DateTimeOffset>(8));
    }

    public async Task<bool> LoginExisteAsync(string login, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT EXISTS (SELECT 1 FROM core.usuario WHERE lower(login) = lower(@l))").Com("l", login);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public Task<long?> CriarAsync(string login, string nome, string senhaHash, bool administrador, long? perfilId, bool deveTrocarSenha,
                                  bool somenteSeNaoHouverUsuarios, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO core.usuario (login, nome, senha_hash, administrador, perfil_id, deve_trocar_senha)
                SELECT @l, @n, @h, @a, @p, @t
                 WHERE NOT @so OR NOT EXISTS (SELECT 1 FROM core.usuario)
                RETURNING id
                """, con, tx)
                .Com("l", login).Com("n", nome).Com("h", senhaHash).Com("a", administrador).Com("t", deveTrocarSenha)
                .Com("p", NpgsqlTypes.NpgsqlDbType.Bigint, perfilId).Com("so", somenteSeNaoHouverUsuarios);
            var id = (long?)await cmd.ExecuteScalarAsync(ct);
            await tx.CommitAsync(ct);
            return id;
        });

    public async Task RegistrarFalhaAsync(long? usuarioId, string login, int tentativas, DateTimeOffset? bloquearAte, string motivo,
                                          string? estacao, CancellationToken ct)
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        if (usuarioId is not null)
            await using (var cmd = new NpgsqlCommand("""
                UPDATE core.usuario SET tentativas_falhas = @t, bloqueado_ate = COALESCE(@b, bloqueado_ate) WHERE id = @id
                """, con, tx).Com("t", (short)tentativas).Com("b", NpgsqlDbType.TimestampTz, bloquearAte).Com("id", usuarioId))
                await cmd.ExecuteNonQueryAsync(ct);
        await RegistrarAcesso(con, tx, usuarioId, login, false, motivo, estacao, ct);
        await tx.CommitAsync(ct);
    }

    public async Task RegistrarSucessoAsync(long usuarioId, string login, string? novoHash, string? estacao, CancellationToken ct)
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        await using (var cmd = new NpgsqlCommand("""
            UPDATE core.usuario
               SET tentativas_falhas = 0, bloqueado_ate = NULL, ultimo_acesso = now(),
                   senha_hash = COALESCE(@h, senha_hash)
             WHERE id = @id
            """, con, tx).Com("h", NpgsqlDbType.Text, novoHash).Com("id", usuarioId))
            await cmd.ExecuteNonQueryAsync(ct);
        await RegistrarAcesso(con, tx, usuarioId, login, true, null, estacao, ct);
        await tx.CommitAsync(ct);
    }

    private static async Task RegistrarAcesso(NpgsqlConnection con, NpgsqlTransaction tx, long? usuarioId, string login, bool sucesso,
                                              string? motivo, string? estacao, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO core.registro_acesso (login, usuario_id, sucesso, motivo, estacao) VALUES (@l, @u, @s, @m, @e)
            """, con, tx)
            .Com("l", login.Length > 60 ? login[..60] : login).Com("u", NpgsqlDbType.Bigint, usuarioId).Com("s", sucesso)
            .ComTexto("m", motivo).ComTexto("e", estacao);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task AlterarSenhaAsync(long usuarioId, string senhaHash, bool deveTrocarSenha, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            UPDATE core.usuario SET senha_hash = @h, deve_trocar_senha = @t, tentativas_falhas = 0, bloqueado_ate = NULL WHERE id = @id
            """).Com("h", senhaHash).Com("t", deveTrocarSenha).Com("id", usuarioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public Task AlterarAsync(long usuarioId, string nome, bool administrador, long perfilId, bool ativo, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var cmd = ds.CreateCommand(
                    "UPDATE core.usuario SET nome = @n, administrador = @a, perfil_id = @p, ativo = @at WHERE id = @id")
                .Com("n", nome).Com("a", administrador).Com("p", perfilId).Com("at", ativo).Com("id", usuarioId);
            return await cmd.ExecuteNonQueryAsync(ct);
        });

    public async Task<PerfilDoUsuario?> ObterPerfilAsync(long perfilId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT id, nome, administrador FROM core.perfil WHERE id = @id").Com("id", perfilId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new PerfilDoUsuario(r.GetInt64(0), r.GetString(1), r.GetBoolean(2)) : null;
    }

    public async Task<long?> IdPerfilAdministradorAsync(CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT id FROM core.perfil WHERE administrador");
        return (long?)await cmd.ExecuteScalarAsync(ct);
    }

    public Task AtribuirPerfilEmTodasEmpresasAsync(long usuarioId, long perfilId, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var cmd = ds.CreateCommand("""
                INSERT INTO core.usuario_empresa (usuario_id, empresa_id, perfil_id)
                SELECT @u, e.id, @p FROM core.empresa e
                ON CONFLICT (usuario_id, empresa_id) DO UPDATE SET perfil_id = EXCLUDED.perfil_id;
                UPDATE core.usuario SET todas_empresas = TRUE WHERE id = @u;
                """).Com("u", usuarioId).Com("p", perfilId);
            return await cmd.ExecuteNonQueryAsync(ct);
        });

    public Task DefinirAcessosAsync(long usuarioId, IReadOnlyList<(long EmpresaId, long? PerfilId)> acessos, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            foreach (var (empresaId, perfilId) in acessos)
                await AplicarAcesso(con, tx, usuarioId, empresaId, perfilId, ct);
            await RecalcularTodasEmpresas(con, tx, usuarioId, ct);
            await tx.CommitAsync(ct);
            return 0;
        });

    private static async Task AplicarAcesso(NpgsqlConnection con, NpgsqlTransaction tx, long usuarioId, long empresaId, long? perfilId, CancellationToken ct)
    {
        await using var cmd = perfilId is null
            ? new NpgsqlCommand("DELETE FROM core.usuario_empresa WHERE usuario_id = @u AND empresa_id = @e", con, tx)
            : new NpgsqlCommand("""
                INSERT INTO core.usuario_empresa (usuario_id, empresa_id, perfil_id) VALUES (@u, @e, @p)
                ON CONFLICT (usuario_id, empresa_id) DO UPDATE SET perfil_id = EXCLUDED.perfil_id
                """, con, tx).Com("p", perfilId.Value);
        cmd.Com("u", usuarioId).Com("e", empresaId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>"Todas as empresas" = tem acesso a todas as cadastradas; só então as empresas novas são liberadas automaticamente.</summary>
    private static async Task RecalcularTodasEmpresas(NpgsqlConnection con, NpgsqlTransaction tx, long usuarioId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            UPDATE core.usuario u
               SET todas_empresas = NOT EXISTS (SELECT 1 FROM core.empresa e
                                                 WHERE NOT EXISTS (SELECT 1 FROM core.usuario_empresa ue
                                                                    WHERE ue.usuario_id = u.id AND ue.empresa_id = e.id))
             WHERE u.id = @u
            """, con, tx).Com("u", usuarioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public Task ExcluirAsync(long usuarioId, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var cmd = ds.CreateCommand("DELETE FROM core.usuario WHERE id = @u").Com("u", usuarioId);
            return await cmd.ExecuteNonQueryAsync(ct);
        });

    public async Task<IReadOnlyList<RegistroAcesso>> ListarRegistroAcessoAsync(int limite, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT ocorrido_em, login, sucesso, motivo, estacao FROM core.registro_acesso ORDER BY id DESC LIMIT @lim
            """).Com("lim", limite);
        var res = new List<RegistroAcesso>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new RegistroAcesso(r.GetFieldValue<DateTimeOffset>(0), r.GetString(1), r.GetBoolean(2),
                                       r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4)));
        return res;
    }

    public async Task<IReadOnlyList<UsuarioResumo>> ListarAsync(CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT u.id, u.login, u.nome, u.administrador, u.ativo, u.ultimo_acesso, u.perfil_id, p.nome
              FROM core.usuario u LEFT JOIN core.perfil p ON p.id = u.perfil_id
             ORDER BY u.nome
            """);
        var res = new List<UsuarioResumo>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new UsuarioResumo(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetBoolean(3), r.GetBoolean(4),
                                      r.IsDBNull(5) ? null : r.GetFieldValue<DateTimeOffset>(5),
                                      r.IsDBNull(6) ? null : r.GetInt64(6), r.IsDBNull(7) ? null : r.GetString(7)));
        return res;
    }

    public Task DefinirAcessoAsync(long usuarioId, long empresaId, long? perfilId, CancellationToken ct)
        => DefinirAcessosAsync(usuarioId, [(empresaId, perfilId)], ct);

    public async Task<IReadOnlyList<AcessoEmpresa>> ListarAcessosAsync(long usuarioId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT e.id, e.razao_social, p.id, p.nome
              FROM core.usuario_empresa ue
              JOIN core.empresa e ON e.id = ue.empresa_id
              JOIN core.perfil p ON p.id = ue.perfil_id
             WHERE ue.usuario_id = @u ORDER BY e.razao_social
            """).Com("u", usuarioId);
        var res = new List<AcessoEmpresa>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(new AcessoEmpresa(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.GetString(3)));
        return res;
    }

    public async Task<IReadOnlyList<EmpresaPermitida>> ListarEmpresasPermitidasAsync(long usuarioId, bool administrador, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT e.id, e.cnpj, e.razao_social, p.nome
              FROM core.empresa e
              LEFT JOIN core.usuario_empresa ue ON ue.empresa_id = e.id AND ue.usuario_id = @u
              LEFT JOIN core.perfil p ON p.id = ue.perfil_id
             WHERE e.ativa AND (@adm OR ue.perfil_id IS NOT NULL)
             ORDER BY e.razao_social
            """).Com("u", usuarioId).Com("adm", administrador);
        var res = new List<EmpresaPermitida>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new EmpresaPermitida(r.GetInt64(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3)));
        return res;
    }

    public async Task<IReadOnlyList<EmpresaPermitida>> ListarEmpresasPermitidasNoModuloAsync(
        long usuarioId, bool administrador, Application.Cadastros.ModulosEmpresa modulo, bool ativas, CancellationToken ct)
    {
        var coluna = Application.Cadastros.ModulosEmpresaExtensoes.Coluna(modulo);
        await using var cmd = ds.CreateCommand($"""
            SELECT e.id, e.cnpj, e.razao_social, p.nome
              FROM core.empresa e
              LEFT JOIN core.usuario_empresa ue ON ue.empresa_id = e.id AND ue.usuario_id = @u
              LEFT JOIN core.perfil p ON p.id = ue.perfil_id
             WHERE e.ativa AND (@adm OR ue.perfil_id IS NOT NULL) AND e.{coluna} = @ativas
             ORDER BY e.razao_social
            """).Com("u", usuarioId).Com("adm", administrador).Com("ativas", ativas);
        var res = new List<EmpresaPermitida>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new EmpresaPermitida(r.GetInt64(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3)));
        return res;
    }
}
