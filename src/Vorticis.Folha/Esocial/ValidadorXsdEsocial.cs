using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace Vorticis.Folha.Esocial;

/// <summary>
/// Validação local dos eventos e dos lotes contra os XSDs oficiais do eSocial embutidos no assembly (leiaute S-1.3, pacote de
/// schemas de 01/07/2026, e pacote de comunicação v1.6). Não substitui o ambiente de produção restrita, mas pega erros de estrutura,
/// ordem de elementos, obrigatoriedade e formato antes do envio.
/// </summary>
public static class ValidadorXsdEsocial
{
    private sealed class ResolvedorEmbutido : XmlResolver
    {
        public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            var nome = Path.GetFileName(absoluteUri.LocalPath);
            return Assembly.GetExecutingAssembly().GetManifestResourceStream("Esocial." + nome)
                   ?? throw new FileNotFoundException($"XSD embutido não encontrado: {nome}");
        }
        public override Uri ResolveUri(Uri? baseUri, string? relativeUri) => new("file:///" + (relativeUri ?? "").TrimStart('/'));
    }

    private static readonly Dictionary<string, XmlSchemaSet> Cache = [];

    private static XmlSchemaSet Conjunto(string arquivoXsd)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(arquivoXsd, out var c)) return c;
            var set = new XmlSchemaSet { XmlResolver = new ResolvedorEmbutido() };
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Esocial." + arquivoXsd)
                          ?? throw new FileNotFoundException($"XSD embutido não encontrado: {arquivoXsd}");
            using var xr = XmlReader.Create(s, new XmlReaderSettings { XmlResolver = new ResolvedorEmbutido(), DtdProcessing = DtdProcessing.Ignore }, "file:///" + arquivoXsd);
            set.Add(null, xr);
            set.Compile();
            return Cache[arquivoXsd] = set;
        }
    }

    /// <summary>Arquivo XSD do evento a partir do nome do elemento raiz do evento (evtAdmissao → evtAdmissao.xsd).</summary>
    public static string? XsdDoEvento(XDocument doc)
        => doc.Root?.Elements().FirstOrDefault() is { } e && e.Name.LocalName.StartsWith("evt", StringComparison.Ordinal) ? e.Name.LocalName + ".xsd" : null;

    /// <summary>Valida um evento (assinado ou não). Devolve a lista de erros; vazia = válido.</summary>
    public static IReadOnlyList<string> ValidarEvento(string xml, bool exigirAssinatura = true)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); } catch (XmlException ex) { return [$"XML malformado: {ex.Message}"]; }
        if (XsdDoEvento(doc) is not { } xsd) return ["Evento sem elemento evtXxx."];
        var erros = Validar(doc, xsd);
        // Antes da assinatura o evento só "falha" por faltar ds:Signature (último elemento obrigatório do leiaute).
        return exigirAssinatura ? erros : erros.Where(m => !m.Contains("'Signature' in namespace 'http://www.w3.org/2000/09/xmldsig#'")).ToList();
    }

    /// <summary>Valida o lote (envioLoteEventos). Os eventos dentro de &lt;evento&gt; são validados separadamente.</summary>
    public static IReadOnlyList<string> ValidarLote(string xml)
        => Validar(XDocument.Parse(xml), "EnvioLoteEventos-v1_1_1.xsd");

    public static IReadOnlyList<string> ValidarConsulta(string xml)
        => Validar(XDocument.Parse(xml), "ConsultaLoteEventos-v1_0_0.xsd");

    private static IReadOnlyList<string> Validar(XDocument doc, string xsd)
    {
        var erros = new List<string>();
        doc.Validate(Conjunto(xsd), (_, e) => erros.Add(e.Message), addSchemaInfo: false);
        return erros;
    }
}
