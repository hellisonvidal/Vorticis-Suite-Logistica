using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Vorticis.Application.Sistema;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Infra.Tests;

public class CertificadosTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    /// <summary>e-CNPJ de teste no formato ICP-Brasil ("RAZAO SOCIAL:CNPJ"), autoassinado.</summary>
    public static byte[] Pfx(string cn, string senha, DateTimeOffset ate)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={cn}, O=ICP-Brasil, C=BR", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(ate.AddYears(-1), ate);
        return cert.Export(X509ContentType.Pfx, senha);
    }

    [Fact]
    public async Task Importa_A1_confere_senha_e_validade_e_so_o_administrador_mantem()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var admin = new SessaoUsuario(await db.AdminAsync(), "admin_teste", "Admin", true, false);
        var svc = db.App.Certificados;
        var pfx = Pfx("EMPRESA TESTE LTDA:11222333000181", "senha123", DateTimeOffset.UtcNow.AddDays(20));

        Assert.Contains("senha incorreta", ServicoCertificados.LerPfx(pfx, "errada").Erros[0]);
        var lido = ServicoCertificados.LerPfx(pfx, "senha123").Valor;
        Assert.Equal(("EMPRESA TESTE LTDA", "11222333000181", true), (lido.Titular, lido.Documento, lido.TemChavePrivada));

        Assert.Contains("Somente o administrador", (await svc.ImportarA1Async(admin with { Administrador = false }, pfx, "senha123", null, null, ct)).Erros[0]);
        Assert.False((await svc.ListarAsync(admin with { Administrador = false }, ct)).Sucesso);
        var r = await svc.ImportarA1Async(admin, pfx, "senha123", "e-CNPJ da empresa", null, ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        Assert.Contains("já está no repositório", (await svc.ImportarA1Async(admin, pfx, "senha123", null, null, ct)).Erros[0]);
        var vencido = Pfx("ANTIGO:11222333000181", "x", DateTimeOffset.UtcNow.AddDays(-1));
        Assert.Contains("vencido", (await svc.ImportarA1Async(admin, vencido, "x", null, null, ct)).Erros[0]);

        var c = Assert.Single((await svc.ListarAsync(admin, ct)).Valor);
        Assert.Equal(("A1", "e-CNPJ da empresa", "11222333000181"), (c.Tipo, c.Descricao, c.Documento));
        Assert.StartsWith("Vence em", c.Situacao(svc.Agora));                 // aviso de vencimento próximo (30 dias)

        // O arquivo guardado abre com a senha (pedida no uso)
        Assert.False((await svc.AbrirA1Async(admin, c.Id, "errada", ct)).Sucesso);
        using (var aberto = (await svc.AbrirA1Async(admin, c.Id, "senha123", ct)).Valor) Assert.True(aberto.HasPrivateKey);

        // A3: só o registro (sem arquivo)
        var a3 = lido with { Titular = "FULANO DE TAL", Documento = "39053344705", Impressao = new string('A', 40) };
        Assert.True((await svc.RegistrarA3Async(admin, a3, null, null, ct)).Sucesso);
        Assert.Equal(2, (await svc.ListarAsync(admin, ct)).Valor.Count);

        Assert.True((await svc.ExcluirAsync(admin, c.Id, ct)).Sucesso);
        Assert.Equal("A3", Assert.Single((await svc.ListarAsync(admin, ct)).Valor).Tipo);
    }
}
