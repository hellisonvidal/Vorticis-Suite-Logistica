using Npgsql;
using Vorticis.Application.Seguranca;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Infra.Postgres;

public sealed class RepositorioPerfis(NpgsqlDataSource ds) : IRepositorioPerfis, IRepositorioPermissoes
{
    public async Task<PermissoesEfetivas> ObterAsync(long usuarioId, long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT u.administrador, p.nome, COALESCE(array_agg(pp.permissao) FILTER (WHERE pp.permissao IS NOT NULL), '{}'), COALESCE(p.administrador, FALSE)
              FROM core.usuario u
              LEFT JOIN core.usuario_empresa ue ON ue.usuario_id = u.id AND ue.empresa_id = @e
              LEFT JOIN core.perfil p ON p.id = ue.perfil_id
              LEFT JOIN core.perfil_permissao pp ON pp.perfil_id = p.id
             WHERE u.id = @u AND u.ativo
             GROUP BY u.administrador, p.nome, p.administrador
            """).Com("u", usuarioId).Com("e", empresaId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return PermissoesEfetivas.Nenhuma;
        // Perfil Administrador: todas as funções, inclusive as criadas por versões posteriores ao cadastro do perfil.
        var liberadas = r.GetBoolean(3)
            ? Permissoes.Catalogo.Select(d => d.Permissao).Where(p => p != Permissao.AdministrarSistema).ToHashSet()
            : Permissoes.DeCodigos(r.GetFieldValue<string[]>(2));
        return new PermissoesEfetivas(r.GetBoolean(0), liberadas, r.IsDBNull(1) ? null : r.GetString(1));
    }

    public async Task<IReadOnlyList<PerfilAcesso>> ListarAsync(CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT p.id, p.nome, p.descricao, p.padrao,
                   COALESCE((SELECT array_agg(permissao) FROM core.perfil_permissao WHERE perfil_id = p.id), '{}'),
                   (SELECT COUNT(*) FROM core.usuario u
                     WHERE u.perfil_id = p.id OR EXISTS (SELECT 1 FROM core.usuario_empresa ue WHERE ue.usuario_id = u.id AND ue.perfil_id = p.id))::int,
                   p.administrador
              FROM core.perfil p ORDER BY p.administrador DESC, p.padrao DESC, p.nome
            """);
        var res = new List<PerfilAcesso>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new PerfilAcesso(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetBoolean(3),
                                     Permissoes.DeCodigos(r.GetFieldValue<string[]>(4)), r.GetInt32(5), r.GetBoolean(6)));
        return res;
    }

    public async Task<bool> NomeEmUsoAsync(string nome, long? excetoId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT EXISTS (SELECT 1 FROM core.perfil WHERE lower(nome) = lower(@n) AND id <> COALESCE(@id, 0))")
                                .Com("n", nome).Com("id", NpgsqlTypes.NpgsqlDbType.Bigint, excetoId);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public Task<long> CriarAsync(string nome, string? descricao, IReadOnlySet<Permissao> permissoes, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            long id;
            await using (var cmd = new NpgsqlCommand("INSERT INTO core.perfil (nome, descricao) VALUES (@n, @d) RETURNING id", con, tx)
                             .Com("n", nome).ComTexto("d", descricao))
                id = (long)(await cmd.ExecuteScalarAsync(ct))!;
            await GravarPermissoes(con, tx, id, permissoes, ct);
            await tx.CommitAsync(ct);
            return id;
        });

    public Task AtualizarAsync(long id, string nome, string? descricao, IReadOnlySet<Permissao> permissoes, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await using (var cmd = new NpgsqlCommand("UPDATE core.perfil SET nome = @n, descricao = @d WHERE id = @id", con, tx)
                             .Com("n", nome).ComTexto("d", descricao).Com("id", id))
                if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new Domain.Comum.ViolacaoRegraException("Perfil não encontrado.");
            await GravarPermissoes(con, tx, id, permissoes, ct);
            await tx.CommitAsync(ct);
            return 0;
        });

    /// <summary>Grava só a diferença (inclusões e exclusões), para a auditoria registrar exatamente o que mudou.</summary>
    private static async Task GravarPermissoes(NpgsqlConnection con, NpgsqlTransaction tx, long perfilId, IReadOnlySet<Permissao> permissoes,
                                               CancellationToken ct)
    {
        var codigos = permissoes.Select(p => p.Codigo()).ToArray();
        await using (var cmd = new NpgsqlCommand(
            "DELETE FROM core.perfil_permissao WHERE perfil_id = @p AND NOT (permissao = ANY(@c))", con, tx).Com("p", perfilId).Com("c", codigos))
            await cmd.ExecuteNonQueryAsync(ct);
        await using (var cmd = new NpgsqlCommand("""
            INSERT INTO core.perfil_permissao (perfil_id, permissao) SELECT @p, unnest(@c::varchar[]) ON CONFLICT DO NOTHING
            """, con, tx).Com("p", perfilId).Com("c", codigos))
            await cmd.ExecuteNonQueryAsync(ct);
    }

    public Task ExcluirAsync(long id, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var cmd = ds.CreateCommand("DELETE FROM core.perfil WHERE id = @id").Com("id", id);
            return await cmd.ExecuteNonQueryAsync(ct);
        });

    public async Task<bool> ConfiguracaoInicialConcluidaAsync(CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM core.configuracao_sistema WHERE chave = 'perfis_configurados' AND valor = 'S')");
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task ConcluirConfiguracaoInicialAsync(long administradorId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            INSERT INTO core.configuracao_sistema (chave, valor, alterado_por) VALUES ('perfis_configurados', 'S', @u)
            ON CONFLICT (chave) DO UPDATE SET valor = 'S', alterado_em = now(), alterado_por = @u
            """).Com("u", administradorId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
