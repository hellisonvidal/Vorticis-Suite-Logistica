using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Vorticis.Folha.Cadastros;
using Vorticis.Folha.Esocial;

namespace Vorticis.Folha.Tests;

/// <summary>Assinatura XMLDSig dos eventos (certificado autoassinado de teste) e validação contra os XSDs oficiais.</summary>
public class AssinaturaEsocialTests
{
    internal static X509Certificate2 CertificadoTeste(string cn = "EMPRESA DE TESTE LTDA:11222333000181")
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={cn}, O=ICP-Brasil, C=BR", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx, "x"), "x", X509KeyStorageFlags.Exportable);
    }

    internal static string EventoDeTeste(string cnpj = "11222333000181")
    {
        var e = new EmpresaFolha(1, cnpj, "Empresa de Teste LTDA", "4751201", "SP", "3550308", null, null, null, null);
        return GeradoresEsocial.S1299(GeradoresEsocial.GerarId(cnpj, new DateTime(2026, 7, 1, 10, 0, 0), 1), e, ParametroEmpresa.Padrao, new DateOnly(2026, 6, 1), true, true);
    }

    [Fact]
    public void Evento_assinado_valida_no_xsd_e_a_assinatura_confere()
    {
        using var cert = CertificadoTeste();
        var xml = EventoDeTeste();
        Assert.NotEmpty(ValidadorXsdEsocial.ValidarEvento(xml));   // sem assinatura: falta ds:Signature
        Assert.Empty(ValidadorXsdEsocial.ValidarEvento(xml, exigirAssinatura: false));

        var assinado = AssinadorEsocial.Assinar(xml, cert);
        Assert.Empty(ValidadorXsdEsocial.ValidarEvento(assinado));
        Assert.Contains("xmldsig-more#rsa-sha256", assinado);
        Assert.Contains("xmlenc#sha256", assinado);
        Assert.Contains("<Reference URI=\"\">", assinado);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(assinado, "<X509Certificate>"));   // EndCertOnly
        Assert.True(AssinadorEsocial.Verificar(assinado, out var usado));
        Assert.Equal(cert.Thumbprint, usado!.Thumbprint);
        Assert.Equal("11222333000181", AssinadorEsocial.DocumentoDoCertificado(cert));
        Assert.Throws<InvalidOperationException>(() => AssinadorEsocial.Assinar(assinado, cert));   // já assinado
    }

    [Fact]
    public void Alterar_o_conteudo_depois_de_assinar_invalida_a_assinatura()
    {
        using var cert = CertificadoTeste();
        var assinado = AssinadorEsocial.Assinar(EventoDeTeste(), cert);
        Assert.False(AssinadorEsocial.Verificar(assinado.Replace("<perApur>2026-06</perApur>", "<perApur>2026-05</perApur>"), out _));
    }

    [Fact]
    public void Validador_acusa_campo_obrigatorio_faltando()
    {
        var xml = EventoDeTeste().Replace("<evtRemun>S</evtRemun>", "");
        Assert.NotEmpty(ValidadorXsdEsocial.ValidarEvento(xml, exigirAssinatura: false));
    }
}
