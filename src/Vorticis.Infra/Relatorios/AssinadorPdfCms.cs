using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using PdfSharp.Pdf.Signatures;

namespace Vorticis.Infra.Relatorios;

/// <summary>
/// Assina o PDF com um certificado digital (A1 ou A3): assinatura CMS/PKCS#7 destacada (adbe.pkcs7.detached, a que o Adobe Reader e o
/// validador do ITI reconhecem) sobre os bytes do arquivo, com SHA-256, cadeia do certificado, horário da assinatura e o atributo
/// ESS signing-certificate-v2 (perfil CAdES-BES usado pelo PAdES básico). A chave privada nunca sai do certificado: no A3 o driver
/// do token faz a assinatura (e pede o PIN).
/// </summary>
public sealed class AssinadorPdfCms(X509Certificate2 certificado) : IDigitalSigner
{
    private const string OidSigningCertificateV2 = "1.2.840.113549.1.9.16.2.47";

    public string CertificateName => certificado.GetNameInfo(X509NameType.SimpleName, false);

    public Task<int> GetSignatureSizeAsync()
    {
        // O tamanho é fixo (RSA + cadeia); assina um conteúdo qualquer só para medir e deixa folga para o horário e a variação da cadeia.
        var tamanho = Assinar([0]).Length;
        return Task.FromResult(tamanho + 1024);
    }

    public Task<byte[]> GetSignatureAsync(Stream stream)
    {
        using var ms = new MemoryStream();
        try { stream.Position = 0; } catch (NotSupportedException) { }
        var buffer = new byte[81920];
        int lidos;
        while ((lidos = stream.Read(buffer, 0, buffer.Length)) > 0) ms.Write(buffer, 0, lidos);   // o fluxo do PDFsharp não permite CopyTo
        return Task.FromResult(Assinar(ms.ToArray()));
    }

    private byte[] Assinar(byte[] conteudo)
    {
        var cms = new SignedCms(new ContentInfo(conteudo), detached: true);
        var assinante = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificado)
        {
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"),   // SHA-256
            IncludeOption = X509IncludeOption.ExcludeRoot,
        };
        assinante.SignedAttributes.Add(new Pkcs9SigningTime());
        assinante.SignedAttributes.Add(new AsnEncodedData(new Oid(OidSigningCertificateV2), SigningCertificateV2()));
        cms.ComputeSignature(assinante, silent: true);
        return cms.Encode();
    }

    /// <summary>SigningCertificateV2 ::= SEQUENCE { certs SEQUENCE OF ESSCertIDv2 }, com o hash SHA-256 do certificado (algoritmo padrão, omitido).</summary>
    private byte[] SigningCertificateV2()
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        using (w.PushSequence())
        using (w.PushSequence())
            w.WriteOctetString(SHA256.HashData(certificado.RawData));
        return w.Encode();
    }
}
