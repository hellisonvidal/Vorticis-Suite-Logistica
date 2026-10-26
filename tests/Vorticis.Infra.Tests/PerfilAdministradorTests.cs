using Vorticis.Domain.Seguranca;

namespace Vorticis.Infra.Tests;

/// <summary>O perfil Administrador libera todas as funções, mesmo as que surgiram depois que as permissões dele foram gravadas.</summary>
public class PerfilAdministradorTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    [Fact]
    public async Task Perfil_administrador_libera_permissoes_novas_mesmo_sem_registro()
    {
        BancoDeTeste.ExigirBanco();
        var empresa = await db.CriarEmpresaAsync("27865757000102", 2026);
        var id = await db.EscalarAsync<long>(
            "INSERT INTO core.usuario (login, nome, senha_hash, perfil_id) VALUES ('perfil_adm', 'Perfil Adm', 'x', (SELECT id FROM core.perfil WHERE administrador)) RETURNING id");
        await db.EscalarAsync<int>("INSERT INTO core.usuario_empresa (usuario_id, empresa_id, perfil_id) VALUES (@u, @e, (SELECT id FROM core.perfil WHERE administrador)) RETURNING 1",
                                   ("u", id), ("e", empresa));
        // simula um perfil gravado por uma versão antiga, sem a permissão fiscal de digitação
        await db.EscalarAsync<int>("WITH d AS (DELETE FROM core.perfil_permissao WHERE permissao = 'FISCAL_DOCUMENTOS_IMPORTAR' AND perfil_id = (SELECT id FROM core.perfil WHERE administrador) RETURNING 1) SELECT COUNT(*)::int FROM d");
        var p = await db.App.Autorizador.ObterAsync(id, empresa, TestContext.Current.CancellationToken);
        Assert.True(p.Pode(Permissao.FiscalDocumentosImportar));
        Assert.False(p.Pode(Permissao.AdministrarSistema));
    }
}
