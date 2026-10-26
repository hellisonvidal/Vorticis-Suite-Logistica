using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;

namespace Vorticis.Folha.Esocial;

/// <summary>
/// Assinatura XMLDSig dos eventos do eSocial, conforme o Manual de Orientação do Desenvolvedor (seção 6.7): formato enveloped, certificado
/// ICP-Brasil A1 ou A3, RSA-SHA256 (xmldsig-more#rsa-sha256), digest SHA-256, transformações enveloped-signature e C14N
/// (xml-c14n-20010315), Reference URI="" (documento inteiro) e KeyInfo apenas com o X509Certificate do usuário final (EndCertOnly).
/// A assinatura entra como último filho do elemento raiz &lt;eSocial&gt;, depois do elemento do evento.
/// </summary>
public static class AssinadorEsocial
{
    public const string UriRsaSha256 = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";
    public const string UriSha256 = "http://www.w3.org/2001/04/xmlenc#sha256";
    public const string UriC14N = "http://www.w3.org/TR/2001/REC-xml-c14n-20010315";

    /// <summary>Assina o evento. Falha se o certificado não tiver chave privada RSA ou se o XML já estiver assinado.</summary>
    public static string Assinar(string xmlEvento, X509Certificate2 certificado)
    {
        var doc = Carregar(xmlEvento);
        if (doc.DocumentElement!.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl).Count > 0)
            throw new InvalidOperationException("O evento já está assinado.");
        using var chave = certificado.GetRSAPrivateKey()
                          ?? throw new InvalidOperationException("O certificado não possui chave privada RSA (ou o token A3 não está conectado).");
        var assinado = new SignedXml(doc) { SigningKey = chave };
        assinado.SignedInfo!.CanonicalizationMethod = UriC14N;
        assinado.SignedInfo.SignatureMethod = UriRsaSha256;
        var referencia = new Reference("") { DigestMethod = UriSha256 };
        referencia.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        referencia.AddTransform(new XmlDsigC14NTransform());
        assinado.AddReference(referencia);
        var info = new KeyInfo();
        info.AddClause(new KeyInfoX509Data(certificado));   // só o certificado do usuário final (sem cadeia)
        assinado.KeyInfo = info;
        assinado.ComputeSignature();
        doc.DocumentElement.AppendChild(doc.ImportNode(assinado.GetXml(), true));
        return Serializar(doc);
    }

    /// <summary>Confere a integridade e a assinatura do evento com o certificado embutido (não valida a cadeia ICP-Brasil).</summary>
    public static bool Verificar(string xmlAssinado, out X509Certificate2? certificado)
    {
        certificado = null;
        var doc = Carregar(xmlAssinado);
        var nos = doc.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl);
        if (nos.Count != 1) return false;
        var s = new SignedXml(doc);
        s.LoadXml((XmlElement)nos[0]!);
        foreach (var clausula in s.KeyInfo.OfType<KeyInfoX509Data>())
            certificado ??= clausula.Certificates?.OfType<X509Certificate2>().FirstOrDefault();
        return certificado is not null && s.CheckSignature(certificado, verifySignatureOnly: true);
    }

    /// <summary>
    /// CPF ou CNPJ (só dígitos) do titular de um certificado ICP-Brasil. Lê primeiro o CN ("NOME:documento") e, se não houver, os campos
    /// otherName do SubjectAltName: OID 2.16.76.1.3.3 (CNPJ) e 2.16.76.1.3.1 (data de nascimento DDMMAAAA + CPF + NIS + RG).
    /// </summary>
    public static string? DocumentoDoCertificado(X509Certificate2 c)
    {
        var cn = c.GetNameInfo(X509NameType.SimpleName, false) ?? "";
        var i = cn.LastIndexOf(':');
        var doc = i >= 0 ? new string(cn[(i + 1)..].Where(char.IsDigit).ToArray()) : "";
        if (doc.Length is 11 or 14) return doc;
        var san = c.Extensions["2.5.29.17"]?.RawData;
        if (san is null) return null;
        // OID DER (06 09 ...) seguido do valor (A0 len 0C/13/16 len texto)
        byte[] oidCnpj = [0x06, 0x09, 0x60, 0x4C, 0x01, 0x03, 0x03], oidCpf = [0x06, 0x09, 0x60, 0x4C, 0x01, 0x03, 0x01];
        if (TextoApos(san, oidCnpj) is { } t14 && t14.Length >= 14) return t14[..14];
        if (TextoApos(san, oidCpf) is { } t11 && t11.Length >= 19) return t11.Substring(8, 11);
        return null;
    }

    private static string? TextoApos(byte[] dados, byte[] oid)
    {
        for (var p = 0; p + oid.Length < dados.Length; p++)
        {
            if (!dados.AsSpan(p, oid.Length).SequenceEqual(oid)) continue;
            var q = p + oid.Length;
            // A0 len (explícito) -> string (0C UTF8, 13 Printable, 16 IA5) len texto
            while (q + 2 < dados.Length && dados[q] is not (0x0C or 0x13 or 0x16 or 0x1E)) q++;
            if (q + 2 >= dados.Length) return null;
            var len = dados[q + 1];
            if (q + 2 + len > dados.Length) return null;
            return System.Text.Encoding.ASCII.GetString(dados, q + 2, len);
        }
        return null;
    }

    private static XmlDocument Carregar(string xml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);
        return doc;
    }

    private static string Serializar(XmlDocument doc)
    {
        // Sem declaração duplicada e sem BOM: o XML segue como texto UTF-8.
        using var sw = new StringWriter();
        using (var xw = XmlWriter.Create(sw, new XmlWriterSettings { OmitXmlDeclaration = false, Encoding = new System.Text.UTF8Encoding(false), Indent = false }))
            doc.Save(xw);
        return sw.ToString().Replace("encoding=\"utf-16\"", "encoding=\"UTF-8\"");
    }
}
