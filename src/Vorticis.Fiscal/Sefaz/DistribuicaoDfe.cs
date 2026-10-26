using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Vorticis.Fiscal.Sefaz;

/// <summary>Documento devolvido pela distribuição: NF-e completa (procNFe), resumo (resNFe) ou evento.</summary>
public sealed record DocumentoDistribuido(string Nsu, string Schema, string Xml)
{
    public bool NotaCompleta => Schema.StartsWith("procNFe", StringComparison.OrdinalIgnoreCase);
    public bool Resumo => Schema.StartsWith("resNFe", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Retorno do NFeDistribuicaoDFe: 138 = documento localizado; 137 = nenhum documento; demais = rejeição.</summary>
public sealed record RespostaDistribuicao(int CStat, string Motivo, IReadOnlyList<DocumentoDistribuido> Documentos)
{
    public bool Localizado => CStat == 138;
}

/// <summary>Retorno do registro de um evento (manifestação do destinatário): 135/136 = registrado.</summary>
public sealed record RespostaEvento(int CStat, string Motivo)
{
    public bool Registrado => CStat is 135 or 136;
}

public sealed class ErroComunicacaoSefaz(string mensagem, Exception? interna = null) : Exception(mensagem, interna);

public interface IClienteNfe
{
    /// <summary>Consulta uma NF-e pela chave de acesso no Ambiente Nacional, como interessado (destinatário) identificado pelo CNPJ.</summary>
    Task<RespostaDistribuicao> ConsultarChaveAsync(string cnpj, int codigoUf, string chave, CancellationToken ct = default);

    /// <summary>Manifestação do destinatário "Ciência da operação" (210210): libera o download do XML completo.</summary>
    Task<RespostaEvento> ManifestarCienciaAsync(string cnpj, string chave, CancellationToken ct = default);
}

/// <summary>
/// Webservices nacionais da NF-e (Ambiente Nacional, os mesmos usados pelo Portal da NF-e): NFeDistribuicaoDFe (consulta por chave,
/// leiaute 1.01, NT 2014.002) e NFeRecepcaoEvento4 (manifestação do destinatário, NT 2020.001). TLS mútuo com o certificado ICP-Brasil
/// do destinatário; o CNPJ do certificado (ou de um procurador com procuração eletrônica) precisa ser o da empresa.
/// </summary>
public sealed class ClienteNfe(HttpClient http, X509Certificate2? certificado, bool producao = true) : IClienteNfe
{
    public const string NsNfe = "http://www.portalfiscal.inf.br/nfe";
    private const string NsDist = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeDistribuicaoDFe";
    private const string NsEvento = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4";

    private Uri UrlDistribuicao => new(producao
        ? "https://www1.nfe.fazenda.gov.br/NFeDistribuicaoDFe/NFeDistribuicaoDFe.asmx"
        : "https://hom1.nfe.fazenda.gov.br/NFeDistribuicaoDFe/NFeDistribuicaoDFe.asmx");
    private Uri UrlEvento => new(producao
        ? "https://www.nfe.fazenda.gov.br/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx"
        : "https://hom1.nfe.fazenda.gov.br/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx");

    public static ClienteNfe Criar(X509Certificate2 certificado, bool producao = true)
    {
        var handler = new SocketsHttpHandler();
        handler.SslOptions.ClientCertificates = [certificado];
        return new ClienteNfe(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) }, certificado, producao);
    }

    public async Task<RespostaDistribuicao> ConsultarChaveAsync(string cnpj, int codigoUf, string chave, CancellationToken ct = default)
    {
        var resposta = await PostarAsync(UrlDistribuicao, NsDist + "/nfeDistDFeInteresse",
            new XElement(XName.Get("nfeDistDFeInteresse", NsDist), new XElement(XName.Get("nfeDadosMsg", NsDist),
                XElement.Parse(MontarConsultaChave(cnpj, codigoUf, chave, producao)))), ct);
        return LerDistribuicao(resposta);
    }

    public async Task<RespostaEvento> ManifestarCienciaAsync(string cnpj, string chave, CancellationToken ct = default)
    {
        if (certificado is null) throw new InvalidOperationException("Certificado digital não informado.");
        var evento = AssinarEvento(MontarCiencia(cnpj, chave, DateTimeOffset.Now, producao), certificado);
        var envio = $"<envEvento xmlns=\"{NsNfe}\" versao=\"1.00\"><idLote>{DateTime.Now:yyMMddHHmmss}</idLote>{evento}</envEvento>";
        var resposta = await PostarAsync(UrlEvento, NsEvento + "/nfeRecepcaoEvento",
            new XElement(XName.Get("nfeDadosMsg", NsEvento), XElement.Parse(envio)), ct);
        return LerEvento(resposta);
    }

    // ------------------------------------------------------------------ mensagens (públicas para os testes)

    public static string MontarConsultaChave(string cnpj, int codigoUf, string chave, bool producao)
        => $"<distDFeInt xmlns=\"{NsNfe}\" versao=\"1.01\"><tpAmb>{(producao ? 1 : 2)}</tpAmb><cUFAutor>{codigoUf}</cUFAutor>" +
           $"<CNPJ>{cnpj}</CNPJ><consChNFe><chNFe>{chave}</chNFe></consChNFe></distDFeInt>";

    public static string MontarCiencia(string cnpj, string chave, DateTimeOffset quando, bool producao)
    {
        var id = $"ID210210{chave}01";
        return $"<evento xmlns=\"{NsNfe}\" versao=\"1.00\"><infEvento Id=\"{id}\"><cOrgao>91</cOrgao><tpAmb>{(producao ? 1 : 2)}</tpAmb>" +
               $"<CNPJ>{cnpj}</CNPJ><chNFe>{chave}</chNFe><dhEvento>{quando:yyyy-MM-ddTHH:mm:sszzz}</dhEvento><tpEvento>210210</tpEvento>" +
               "<nSeqEvento>1</nSeqEvento><verEvento>1.00</verEvento><detEvento versao=\"1.00\"><descEvento>Ciencia da Operacao</descEvento>" +
               "</detEvento></infEvento></evento>";
    }

    /// <summary>Assinatura XMLDSig do evento (padrão NF-e: referência ao Id de infEvento, RSA-SHA1, C14N, enveloped).</summary>
    public static string AssinarEvento(string xmlEvento, X509Certificate2 certificado)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xmlEvento);
        var inf = (XmlElement)doc.GetElementsByTagName("infEvento")[0]!;
        using var chave = certificado.GetRSAPrivateKey() ?? throw new InvalidOperationException("O certificado não possui chave privada RSA.");
        var assinado = new SignedXml(doc) { SigningKey = chave };
        assinado.SignedInfo!.CanonicalizationMethod = SignedXml.XmlDsigC14NTransformUrl;
        assinado.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA1Url;
        var referencia = new Reference("#" + inf.GetAttribute("Id")) { DigestMethod = SignedXml.XmlDsigSHA1Url };
        referencia.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        referencia.AddTransform(new XmlDsigC14NTransform());
        assinado.AddReference(referencia);
        var info = new KeyInfo();
        info.AddClause(new KeyInfoX509Data(certificado));
        assinado.KeyInfo = info;
        assinado.ComputeSignature();
        doc.DocumentElement!.AppendChild(doc.ImportNode(assinado.GetXml(), true));
        return doc.DocumentElement.OuterXml;
    }

    /// <summary>Lê o retDistDFeInt (dentro ou fora do envelope SOAP) e descompacta os docZip (base64 + gzip).</summary>
    public static RespostaDistribuicao LerDistribuicao(string xml)
    {
        var ret = XDocument.Parse(xml).Descendants().FirstOrDefault(e => e.Name.LocalName == "retDistDFeInt")
                  ?? throw new ErroComunicacaoSefaz("Resposta da SEFAZ sem retDistDFeInt.");
        var docs = ret.Descendants().Where(e => e.Name.LocalName == "docZip").Select(d =>
        {
            using var zip = new GZipStream(new MemoryStream(Convert.FromBase64String(d.Value.Trim())), CompressionMode.Decompress);
            using var leitor = new StreamReader(zip, Encoding.UTF8);
            return new DocumentoDistribuido((string?)d.Attribute("NSU") ?? "", (string?)d.Attribute("schema") ?? "", leitor.ReadToEnd());
        }).ToList();
        return new RespostaDistribuicao(Num(ret, "cStat"), Texto(ret, "xMotivo"), docs);
    }

    public static RespostaEvento LerEvento(string xml)
    {
        var raiz = XDocument.Parse(xml);
        var inf = raiz.Descendants().FirstOrDefault(e => e.Name.LocalName == "infEvento");
        var lote = raiz.Descendants().FirstOrDefault(e => e.Name.LocalName == "retEnvEvento")
                   ?? throw new ErroComunicacaoSefaz("Resposta da SEFAZ sem retEnvEvento.");
        return inf is not null ? new RespostaEvento(Num(inf, "cStat"), Texto(inf, "xMotivo")) : new RespostaEvento(Num(lote, "cStat"), Texto(lote, "xMotivo"));
    }

    private static int Num(XElement e, string nome) => int.TryParse(Texto(e, nome), out var n) ? n : 0;
    private static string Texto(XElement e, string nome) => e.Elements().FirstOrDefault(x => x.Name.LocalName == nome)?.Value ?? "";

    private async Task<string> PostarAsync(Uri url, string acao, XElement corpo, CancellationToken ct)
    {
        XNamespace soap = "http://www.w3.org/2003/05/soap-envelope";
        var envelope = new XElement(soap + "Envelope", new XAttribute(XNamespace.Xmlns + "soap12", soap), new XElement(soap + "Body", corpo));
        using var conteudo = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8);
        conteudo.Headers.ContentType = MediaTypeHeaderValue.Parse($"application/soap+xml; charset=utf-8; action=\"{acao}\"");
        try
        {
            using var r = await http.PostAsync(url, conteudo, ct);
            var texto = await r.Content.ReadAsStringAsync(ct);
            if (!r.IsSuccessStatusCode && !texto.Contains("Envelope", StringComparison.Ordinal))
                throw new ErroComunicacaoSefaz($"A SEFAZ respondeu HTTP {(int)r.StatusCode} ({r.ReasonPhrase}).");
            return texto;
        }
        catch (HttpRequestException ex) { throw new ErroComunicacaoSefaz($"Sem comunicação com a SEFAZ: {ex.Message}", ex); }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw new ErroComunicacaoSefaz("A SEFAZ não respondeu a tempo. Tente de novo.", ex); }
    }
}

/// <summary>Códigos IBGE das UFs (cUFAutor da distribuição).</summary>
public static class CodigosUf
{
    private static readonly Dictionary<string, int> Mapa = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RO"] = 11, ["AC"] = 12, ["AM"] = 13, ["RR"] = 14, ["PA"] = 15, ["AP"] = 16, ["TO"] = 17, ["MA"] = 21, ["PI"] = 22, ["CE"] = 23,
        ["RN"] = 24, ["PB"] = 25, ["PE"] = 26, ["AL"] = 27, ["SE"] = 28, ["BA"] = 29, ["MG"] = 31, ["ES"] = 32, ["RJ"] = 33, ["SP"] = 35,
        ["PR"] = 41, ["SC"] = 42, ["RS"] = 43, ["MS"] = 50, ["MT"] = 51, ["GO"] = 52, ["DF"] = 53,
    };

    public static int? De(string? uf) => uf is not null && Mapa.TryGetValue(uf.Trim(), out var c) ? c : null;
}
