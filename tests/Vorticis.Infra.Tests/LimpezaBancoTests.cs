using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Infra.Tests;

public class LimpezaBancoTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    [Fact]
    public async Task Limpar_banco_exige_administrador_e_confirmacao_e_mantem_usuarios()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("11444777000161");
        await db.App.Lancamentos.EfetivarAsync(new LancamentoProposto(empresa, new(2026, 2, 1), OrigemLancamento.Manual,
            [new("1.1.01.001", IndicadorDC.D, 50m, "x"), new("2.3.01.001", IndicadorDC.C, 50m, "x")]), adm, ct);
        var svc = db.App.ConfiguracoesGerais;
        var admin = new SessaoUsuario(adm, "admin_teste", "Admin", true, false);

        Assert.Contains("Somente o administrador", (await svc.LimparBancoAsync(admin with { Administrador = false }, "LIMPAR", "VORTICIS", ct)).Erros[0]);
        Assert.Contains("digite LIMPAR", (await svc.LimparBancoAsync(admin, "limpar", "VORTICIS", ct)).Erros[0]);
        Assert.Contains("Senha de administrador incorreta", (await svc.LimparBancoAsync(admin, "LIMPAR", "vorticis", ct)).Erros[0]);

        // A senha de administrador pode ser trocada; a padrão deixa de valer
        Assert.True(await svc.UsandoSenhaPadraoAsync(ct));
        Assert.True((await svc.AlterarSenhaAdministradorAsync(admin, "VORTICIS", "Nova#2026", "Nova#2026", ct)).Sucesso);
        Assert.False(await svc.VerificarSenhaAdministradorAsync("VORTICIS", ct));
        var a3 = new Application.Sistema.DadosCertificado("X", null, "AC", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1), new string('B', 40), true);
        Assert.True((await db.App.Certificados.RegistrarA3Async(admin, a3, null, null, ct)).Sucesso);
        var r = await svc.LimparBancoAsync(admin, "LIMPAR", "Nova#2026", ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        Assert.Equal(1, r.Valor);
        Assert.Equal(0L, await db.EscalarAsync<long>("SELECT count(*) FROM core.empresa"));
        Assert.Equal(0L, await db.EscalarAsync<long>("SELECT count(*) FROM contabil.lancamento"));
        Assert.Equal(0L, await db.EscalarAsync<long>("SELECT count(*) FROM core.certificado_digital"));
        // Banco como numa instalação nova: nenhum usuário; só vale o usuário de implantação (vorticis/vorticis)
        Assert.Equal(0L, await db.EscalarAsync<long>("SELECT count(*) FROM core.usuario"));
        Assert.True(await db.App.Usuarios.ImplantacaoAtivaAsync(ct));
        Assert.False((await db.App.Usuarios.AutenticarAsync("ADMIN", "VORTICIS", ct)).Sucesso);
        var login = await db.App.Usuarios.AutenticarAsync("vorticis", "vorticis", ct);
        Assert.True(login.Sucesso, string.Join("; ", login.Erros));
        Assert.True(login.Valor.Implantacao);
        Assert.True(await db.App.Perfis.ConfiguracaoInicialPendenteAsync(ct));       // os perfis voltam a ser configurados no primeiro uso
        Assert.True(await svc.VerificarSenhaAdministradorAsync("VORTICIS", ct));    // senha de administrador volta à padrão
        Assert.True(await db.EscalarAsync<long>("SELECT count(*) FROM imobilizado.grupo") > 0);
    }
}
