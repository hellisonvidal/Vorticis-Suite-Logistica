using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Vorticis.Application.Sped;

namespace Vorticis.Infra.Sped;

/// <summary>
/// Assinatura digital de arquivos do SPED no formato gravado pelo próprio PVA/PGE do Sped Contábil, para o arquivo ser importado
/// já assinado. O formato foi levantado no PVA da ECD versão 10.4.1 (bibliotecas assinatura.jar e jsignnet.jar e a classe
/// ControleAssinarEscrituracao do contabilpva-nucleo.jar):
/// <list type="bullet">
/// <item>o conteúdo assinado é o hash <b>SHA-1</b> (20 bytes) do arquivo sem assinaturas — do primeiro byte até o CR LF do
/// registro 9999. Todas as assinaturas assinam o mesmo hash, inclusive as acrescentadas depois;</item>
/// <item>cada assinatura é um PKCS#7/CMS SignedData (DER) com o hash encapsulado (id-data), atributo assinado signingTime
/// (UTCTime — o PVA confere se está dentro da validade do certificado) e a cadeia de certificados do signatário;
/// o PVA assina com SHA1withRSA;</item>
/// <item>as assinaturas são gravadas em sequência, logo depois do registro 9999, cada uma precedida da etiqueta ASCII
/// "SBRCAAEPDR" + ordem da assinatura em 2 dígitos (01, 02...) + código de qualificação do assinante em 3 dígitos (J930.COD_ASSIN,
/// ex.: 900 contador), validada pelo PVA com a expressão SBRCAAEPDR([0-9]{2})?([0-9]{3})?. O PVA lê as assinaturas de trás
/// para frente procurando a etiqueta e aceita até 20.000 bytes por assinatura (etiqueta incluída).</item>
/// </list>
/// O PVA ainda grava um atributo assinado informativo (OID 1.3.6.1.4.1.311.88.2.2: versão do PVA e do descritor do leiaute),
/// lido só para exibição e opcional na leitura; não é gravado aqui. Ver docs/05-ecd.md (seção "Assinatura").
/// </summary>
public sealed class AssinadorSpedCms : IAssinadorSped
{
    public const string Etiqueta = "SBRCAAEPDR";
    public const int TamanhoMaximoAssinatura = 20_000;
    private static readonly byte[] EtiquetaBytes = Encoding.ASCII.GetBytes(Etiqueta);
    private static readonly byte[] Registro9999 = Encoding.ASCII.GetBytes("\n|9999|");

    /// <param name="algoritmoHash">Digest da assinatura: SHA-1 (padrão, como o PVA) ou SHA-256.</param>
    public AssinadorSpedCms(HashAlgorithmName? algoritmoHash = null)
    {
        var alg = algoritmoHash ?? HashAlgorithmName.SHA1;
        _oidDigest = alg == HashAlgorithmName.SHA256 ? new Oid("2.16.840.1.101.3.4.2.1")
                   : alg == HashAlgorithmName.SHA1 ? new Oid("1.3.14.3.2.26")
                   : throw new ArgumentException("Use SHA-1 ou SHA-256.", nameof(algoritmoHash));
    }

    private readonly Oid _oidDigest;

    public byte[] Assinar(byte[] arquivo, IReadOnlyList<CertificadoAssinante> assinantes)
    {
        var tamanho = TamanhoSemAssinaturas(arquivo);
        var existentes = LerBrutas(arquivo, tamanho);
        var hash = SHA1.HashData(arquivo.AsSpan(0, tamanho));
        using var saida = new MemoryStream();
        saida.Write(arquivo, 0, tamanho);
        foreach (var (_, bruta) in existentes) saida.Write(bruta);   // reescreve as já existentes como estavam
        var ordem = existentes.Count;
        foreach (var a in assinantes)
        {
            if (a.CodigoQualificacao is not { Length: 3 } || !a.CodigoQualificacao.All(char.IsAsciiDigit))
                throw new InvalidOperationException($"Código de qualificação do assinante inválido: {a.CodigoQualificacao}.");
            if (++ordem > 99) throw new InvalidOperationException("O arquivo admite no máximo 99 assinaturas.");
            var cms = new SignedCms(new ContentInfo(hash), detached: false);
            var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, a.Certificado)
            {
                DigestAlgorithm = _oidDigest,
                IncludeOption = X509IncludeOption.WholeChain,
            };
            signer.SignedAttributes.Add(new Pkcs9SigningTime(DateTime.UtcNow));
            cms.ComputeSignature(signer, silent: false);   // A3: o driver do token pode pedir o PIN
            var der = cms.Encode();
            var etiqueta = Encoding.ASCII.GetBytes($"{Etiqueta}{ordem:00}{a.CodigoQualificacao}");
            if (etiqueta.Length + der.Length > TamanhoMaximoAssinatura)
                throw new InvalidOperationException($"A assinatura de {a.Certificado.Subject} ficou com {der.Length} bytes; o PVA aceita até {TamanhoMaximoAssinatura}.");
            saida.Write(etiqueta);
            saida.Write(der);
        }
        return saida.ToArray();
    }

    public IReadOnlyList<AssinaturaSped> LerAssinaturas(byte[] arquivo)
    {
        var tamanho = TamanhoSemAssinaturas(arquivo);
        var hash = SHA1.HashData(arquivo.AsSpan(0, tamanho));
        var res = new List<AssinaturaSped>();
        var ordem = 0;
        foreach (var (etiqueta, bruta) in LerBrutas(arquivo, tamanho))
        {
            ordem++;
            var codigo = etiqueta.Length >= Etiqueta.Length + 5 ? etiqueta[^3..] : "";
            try
            {
                var cms = new SignedCms();
                cms.Decode(bruta.AsSpan(etiqueta.Length));
                var info = cms.SignerInfos[0];
                var cert = info.Certificate;
                DateTimeOffset? em = info.SignedAttributes.Cast<CryptographicAttributeObject>()
                    .SelectMany(a => a.Values.Cast<AsnEncodedData>()).OfType<Pkcs9SigningTime>().Select(t => (DateTimeOffset?)new DateTimeOffset(t.SigningTime, TimeSpan.Zero))
                    .FirstOrDefault();
                string? erro = null;
                try
                {
                    if (!cms.ContentInfo.Content.AsSpan().SequenceEqual(hash)) erro = "O conteúdo assinado não é o hash deste arquivo (arquivo alterado depois de assinado).";
                    else cms.CheckSignature(verifySignatureOnly: true);
                }
                catch (CryptographicException ex) { erro = ex.Message; }
                res.Add(new AssinaturaSped(ordem, etiqueta, codigo, cert?.GetNameInfo(X509NameType.SimpleName, false) ?? "", cert is null ? null : ServicoEcd.DocumentoIcpBrasil(cert),
                                           cert?.Thumbprint ?? "", em, erro is null, erro));
            }
            catch (CryptographicException ex)
            {
                res.Add(new AssinaturaSped(ordem, etiqueta, codigo, "", null, "", null, false, $"Assinatura ilegível: {ex.Message}"));
            }
        }
        return res;
    }

    public int TamanhoSemAssinaturas(byte[] arquivo)
    {
        var span = arquivo.AsSpan();
        var i9999 = span.IndexOf(Registro9999);
        if (i9999 >= 0)
        {
            var fim = span[i9999..].IndexOf("\r\n"u8);
            if (fim >= 0) return i9999 + fim + 2;
        }
        var etiqueta = span.IndexOf(EtiquetaBytes);
        return etiqueta >= 0 ? etiqueta : arquivo.Length;
    }

    /// <summary>Assinaturas brutas (etiqueta + DER) depois da posição <paramref name="inicio"/>.</summary>
    private static List<(string Etiqueta, byte[] Bruta)> LerBrutas(byte[] arquivo, int inicio)
    {
        var res = new List<(string, byte[])>();
        var pos = inicio;
        while (pos + EtiquetaBytes.Length < arquivo.Length && arquivo.AsSpan(pos, EtiquetaBytes.Length).SequenceEqual(EtiquetaBytes))
        {
            var p = pos + EtiquetaBytes.Length;
            while (p < arquivo.Length && p - pos < EtiquetaBytes.Length + 5 && char.IsAsciiDigit((char)arquivo[p])) p++;
            var etiqueta = Encoding.ASCII.GetString(arquivo, pos, p - pos);
            var tamanhoDer = TamanhoDer(arquivo, p);
            if (tamanhoDer <= 0 || p + tamanhoDer > arquivo.Length)
                throw new InvalidOperationException($"Assinatura nº {res.Count + 1} do arquivo está corrompida.");
            res.Add((etiqueta, arquivo[pos..(p + tamanhoDer)]));
            pos = p + tamanhoDer;
        }
        return res;
    }

    /// <summary>Tamanho total (cabeçalho + conteúdo) de uma estrutura DER SEQUENCE que começa em <paramref name="pos"/>.</summary>
    private static int TamanhoDer(byte[] b, int pos)
    {
        if (pos + 2 > b.Length || b[pos] != 0x30) return -1;
        int len = b[pos + 1];
        if (len < 0x80) return 2 + len;
        var n = len & 0x7F;
        if (n is 0 or > 4 || pos + 2 + n > b.Length) return -1;
        long total = 0;
        for (int i = 0; i < n; i++) total = (total << 8) | b[pos + 2 + i];
        return total > int.MaxValue - 6 ? -1 : (int)(2 + n + total);
    }
}

/// <summary>Abertura dos certificados de assinatura (para o Desktop): A1 a partir do .pfx e A3 do repositório do Windows.</summary>
public static class CertificadosSped
{
    /// <summary>A1: abre o .pfx (ou .p12) com a senha. O certificado precisa ter a chave privada.</summary>
    public static X509Certificate2 AbrirPfx(byte[] pfx, string senha)
    {
        var cert = X509CertificateLoader.LoadPkcs12(pfx, senha, OperatingSystem.IsWindows() ? X509KeyStorageFlags.EphemeralKeySet : X509KeyStorageFlags.Exportable);
        if (!cert.HasPrivateKey) { cert.Dispose(); throw new CryptographicException("O arquivo não contém a chave privada do certificado."); }
        return cert;
    }

    /// <summary>A3 (token/cartão) ou A1 instalado: certificado pessoal do usuário no Windows, pela impressão digital (thumbprint).</summary>
    public static X509Certificate2? DoRepositorioWindows(string impressao)
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var achados = store.Certificates.Find(X509FindType.FindByThumbprint, impressao.Replace(" ", ""), validOnly: false);
        return achados.Cast<X509Certificate2>().FirstOrDefault(c => c.HasPrivateKey);
    }
}
