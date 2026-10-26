using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Vorticis.Application.Relatorios;
using Vorticis.Application.Sistema;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra.Relatorios;

namespace Vorticis.Infra.Tests;

/// <summary>Certificado padrão (contador/responsável), classificação A1/A3, importação do repositório e assinatura digital de PDF.</summary>
public class CertificadosPadraoTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static X509Certificate2 Cert(string cn, DateTimeOffset? ate = null)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={cn}, O=ICP-Brasil, C=BR", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var c = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), ate ?? DateTimeOffset.UtcNow.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(c.Export(X509ContentType.Pfx, "x"), "x", X509KeyStorageFlags.Exportable);
    }

    [Theory]
    [InlineData(null, true, false, "A1")]
    [InlineData("Microsoft Software Key Storage Provider", true, false, "A1")]
    [InlineData("Microsoft Smart Card Key Storage Provider", true, false, "A3")]
    [InlineData("Microsoft Software Key Storage Provider", false, false, "A3")]
    [InlineData("Microsoft Enhanced RSA and AES Cryptographic Provider", true, true, "A3")]
    public void Classifica_A1_e_A3_pelo_provedor_e_pela_exportabilidade(string? provedor, bool exportavel, bool hardware, string esperado)
        => Assert.Equal(esperado, ServicoCertificados.ClassificarChave(provedor, exportavel, hardware));

    [Fact]
    public void Certificado_de_software_exportavel_e_A1_e_nao_entra_na_lista_de_A3()
    {
        using var c = Cert("EMPRESA:11222333000181");
        Assert.Equal("A1", ServicoCertificados.TipoDoCertificado(c));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Empty(ServicoCertificados.ListarInstaladosNoWindows("A3"));
            Assert.Contains("só está disponível no Windows", ServicoCertificados.AvisoSomenteWindows);
            Assert.False(ServicoCertificados.ExportarInstaladoComoPfx(new string('A', 40), "s").Sucesso);
        }
    }

    [Fact]
    public async Task Exporta_instalado_como_pfx_e_importa_como_A1()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var admin = new SessaoUsuario(await db.AdminAsync(), "admin_teste", "Admin", true, false);
        using var c = Cert("IMPORTADO LTDA:11222333000181");
        Assert.False(ServicoCertificados.ExportarComoPfx(c, "").Sucesso);
        var pfx = ServicoCertificados.ExportarComoPfx(c, "protecao1");
        Assert.True(pfx.Sucesso);
        var r = await db.App.Certificados.ImportarA1Async(admin, pfx.Valor, "protecao1", "Importado do Windows", null, ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        using var aberto = (await db.App.Certificados.AbrirA1Async(admin, r.Valor, "protecao1", ct)).Valor;
        Assert.True(aberto.HasPrivateKey);
        await db.App.Certificados.ExcluirAsync(admin, r.Valor, ct);
    }

    [Fact]
    public async Task Certificado_padrao_um_por_papel_e_escopo_e_nao_vale_vencido()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var svc = db.App.Certificados;
        var admin = new SessaoUsuario(await db.AdminAsync(), "admin_teste", "Admin", true, false);
        var empresa = await db.CriarEmpresaAsync("06990590000123");
        long Id(string cn, long? emp) => svc.ImportarA1Async(admin, ServicoCertificados_Pfx(cn), "s", cn, emp, ct).GetAwaiter().GetResult().Valor;
        var a = Id("CONTADOR A:52998224725", null);
        var b = Id("CONTADOR B:39053344705", null);
        var e = Id("RESPONSAVEL E:11222333000181", empresa);

        Assert.False((await svc.DefinirPadraoAsync(admin with { Administrador = false }, a, PapelAssinatura.Contador, true, ct)).Sucesso);
        Assert.True((await svc.DefinirPadraoAsync(admin, a, PapelAssinatura.Contador, true, ct)).Sucesso);
        Assert.Equal(a, (await svc.ObterPadraoAsync(admin, PapelAssinatura.Contador, empresa, ct)).Valor!.Id);
        Assert.True((await svc.DefinirPadraoAsync(admin, b, PapelAssinatura.Contador, true, ct)).Sucesso);   // troca o padrão geral
        Assert.Equal(b, (await svc.ObterPadraoAsync(admin, PapelAssinatura.Contador, empresa, ct)).Valor!.Id);
        Assert.Single((await svc.ListarAsync(admin, ct)).Valor, x => x.PadraoContador);

        Assert.Null((await svc.ObterPadraoAsync(admin, PapelAssinatura.Responsavel, empresa, ct)).Valor);
        Assert.True((await svc.DefinirPadraoAsync(admin, b, PapelAssinatura.Responsavel, true, ct)).Sucesso);   // geral
        Assert.True((await svc.DefinirPadraoAsync(admin, e, PapelAssinatura.Responsavel, true, ct)).Sucesso);   // da empresa tem precedência
        Assert.Equal(e, (await svc.ObterPadraoAsync(admin, PapelAssinatura.Responsavel, empresa, ct)).Valor!.Id);
        Assert.Equal(b, (await svc.ObterPadraoAsync(admin, PapelAssinatura.Responsavel, null, ct)).Valor!.Id);
        Assert.True((await svc.DefinirPadraoAsync(admin, e, PapelAssinatura.Responsavel, false, ct)).Sucesso);
        Assert.Equal(b, (await svc.ObterPadraoAsync(admin, PapelAssinatura.Responsavel, empresa, ct)).Valor!.Id);

        foreach (var id in new[] { a, b, e }) await svc.ExcluirAsync(admin, id, ct);
    }

    private static byte[] ServicoCertificados_Pfx(string cn)
    {
        using var c = Cert(cn);
        return c.Export(X509ContentType.Pfx, "s");
    }

    [Fact]
    public void Pdf_sai_assinado_com_cms_destacado_valido_sobre_o_intervalo_assinado()
    {
        using var cert = Cert("CONTADOR TESTE:52998224725");
        var doc = new DocumentoRelatorio("Balancete", ["Empresa de Teste"], [new Bloco(Texto: "Conteúdo do relatório.")],
            AssinaturaDigital: new AssinaturaDigitalPdf(cert, "Assinatura do contador", "São Paulo"));
        var pdf = new GeradorPdf().Gerar(doc).Conteudo;
        if (Environment.GetEnvironmentVariable("VORTICIS_PDF_ASSINADO") is { Length: > 0 } saida) File.WriteAllBytes(saida, pdf);   // para conferir com pdfsig
        var texto = Encoding.Latin1.GetString(pdf);

        var br = Regex.Match(texto, @"/ByteRange\s*\[\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\]");
        Assert.True(br.Success);
        var n = br.Groups.Cast<Group>().Skip(1).Select(g => int.Parse(g.Value)).ToArray();
        Assert.Equal(0, n[0]);
        Assert.Equal(pdf.Length, n[2] + n[3]);                                   // o intervalo cobre tudo, menos o /Contents
        var assinado = pdf[..n[1]].Concat(pdf[n[2]..]).ToArray();

        var hex = Regex.Match(texto, @"/Contents\s*<([0-9A-Fa-f]+)>").Groups[1].Value;
        var bytes = Convert.FromHexString(hex);
        AsnDecoder.ReadEncodedValue(bytes, AsnEncodingRules.BER, out _, out var len, out var consumido);
        var cms = new SignedCms(new ContentInfo(assinado), detached: true);
        cms.Decode(bytes[..consumido]);
        cms.CheckSignature(verifySignatureOnly: true);                           // autoassinado: não valida a cadeia
        Assert.Equal(cert.Thumbprint, cms.SignerInfos[0].Certificate!.Thumbprint);
        Assert.Contains(cms.SignerInfos[0].SignedAttributes.Cast<CryptographicAttributeObject>(), a => a.Oid.Value == "1.2.840.113549.1.9.16.2.47");
        Assert.Contains("/adbe.pkcs7.detached", texto);

        // Alterar um byte do conteúdo invalida a assinatura
        assinado[100] ^= 0xFF;
        var adulterado = new SignedCms(new ContentInfo(assinado), detached: true);
        adulterado.Decode(bytes[..consumido]);
        Assert.ThrowsAny<CryptographicException>(() => adulterado.CheckSignature(verifySignatureOnly: true));

        // Sem certificado, o PDF sai sem assinatura
        var semAssinatura = new GeradorPdf().Gerar(doc with { AssinaturaDigital = null }).Conteudo;
        Assert.DoesNotContain("/ByteRange", Encoding.Latin1.GetString(semAssinatura));
    }
}
