using System.Xml.Linq;

namespace Vorticis.Folha.Esocial;

/// <summary>Quem transmite o lote (ideTransmissor): CNPJ (1) ou CPF (2) do certificado usado na conexão TLS.</summary>
public sealed record TransmissorEsocial(string Papel, int TpInsc, string NrInsc);

/// <summary>Montagem do lote de eventos (EnvioLoteEventos v1_1_1): até 50 eventos assinados, todos do mesmo grupo.</summary>
public static class MontadorLoteEsocial
{
    public const string Namespace = "http://www.esocial.gov.br/schema/lote/eventos/envio/v1_1_1";
    public const int MaximoEventosPorLote = 50;

    /// <summary>Grupo do lote: 1 = eventos de tabelas, 2 = não periódicos, 3 = periódicos.</summary>
    public static int Grupo(string tipo) => tipo switch
    {
        "S-1000" or "S-1005" or "S-1010" or "S-1020" or "S-1030" or "S-1035" or "S-1040" or "S-1050" or "S-1060" or "S-1070" or "S-1080" => 1,
        "S-1200" or "S-1202" or "S-1207" or "S-1210" or "S-1260" or "S-1270" or "S-1280" or "S-1298" or "S-1299" => 3,
        _ => 2,
    };

    public static string NomeGrupo(int grupo) => grupo switch { 1 => "Tabelas", 2 => "Não periódicos", _ => "Periódicos" };

    /// <summary>ideEmpregador do lote: raiz do CNPJ (8 dígitos) do empregador pessoa jurídica.</summary>
    public static string Montar(string cnpjEmpregador, TransmissorEsocial transmissor, int grupo, IReadOnlyList<(string IdEvento, string XmlAssinado)> eventos)
    {
        if (eventos.Count is 0 or > MaximoEventosPorLote) throw new ArgumentException($"O lote deve ter de 1 a {MaximoEventosPorLote} eventos.", nameof(eventos));
        XNamespace ns = Namespace;
        var raiz = new string(cnpjEmpregador.Where(char.IsDigit).ToArray())[..8];
        var doc = new XElement(ns + "eSocial", new XElement(ns + "envioLoteEventos", new XAttribute("grupo", grupo),
            new XElement(ns + "ideEmpregador", new XElement(ns + "tpInsc", 1), new XElement(ns + "nrInsc", raiz)),
            new XElement(ns + "ideTransmissor", new XElement(ns + "tpInsc", transmissor.TpInsc), new XElement(ns + "nrInsc", transmissor.NrInsc)),
            new XElement(ns + "eventos", eventos.Select(e => new XElement(ns + "evento", new XAttribute("Id", e.IdEvento),
                XElement.Parse(e.XmlAssinado, LoadOptions.PreserveWhitespace))))));   // preserva os espaços: a assinatura cobre o texto exato
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + doc.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>Divide em lotes de até 50, por grupo, na ordem 1 → 2 → 3.</summary>
    public static IEnumerable<(int Grupo, IReadOnlyList<T> Itens)> Dividir<T>(IEnumerable<T> itens, Func<T, int> grupo)
    {
        foreach (var g in itens.GroupBy(grupo).OrderBy(g => g.Key))
            foreach (var parte in g.Chunk(MaximoEventosPorLote))
                yield return (g.Key, parte);
    }
}
