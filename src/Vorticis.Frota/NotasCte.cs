using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Vorticis.Frota;

/// <summary>
/// NF-e transportada por um CT-e, com o que interessa ao transporte: chave, número, emitente (remetente) e destinatário com os municípios,
/// valor da nota, pesos, volumes e o produto principal (o de maior valor).
/// </summary>
public sealed record NotaTransportada(string Chave, int Numero, int Serie, DateOnly Emissao, string? EmitenteDocumento, string EmitenteNome,
                                      ParadaRota? Origem, string? DestinatarioDocumento, string DestinatarioNome, ParadaRota? Destino,
                                      decimal Valor, decimal PesoBruto, decimal PesoLiquido, int Volumes, string? Produto)
{
    /// <summary>Peso para o frete: o bruto da nota ou, sem ele, o líquido.</summary>
    public decimal Peso => PesoBruto > 0 ? PesoBruto : PesoLiquido;
}

/// <summary>Painel das notas do CT-e: quantidades, pesos, valores e quantos remetentes e destinatários diferentes.</summary>
public sealed record ResumoNotasCte(int Notas, int Volumes, decimal PesoBruto, decimal PesoLiquido, decimal Peso, decimal Valor, int Remetentes, int Destinatarios,
                                    string? Produto, decimal FretePorValor)
{
    public static ResumoNotasCte De(IReadOnlyList<NotaTransportada> notas, decimal frete = 0)
    {
        var valor = notas.Sum(n => n.Valor);
        return new(notas.Count, notas.Sum(n => n.Volumes), notas.Sum(n => n.PesoBruto), notas.Sum(n => n.PesoLiquido), notas.Sum(n => n.Peso), valor,
                   notas.Select(n => n.EmitenteDocumento ?? n.EmitenteNome).Distinct().Count(),
                   notas.Select(n => n.DestinatarioDocumento ?? n.DestinatarioNome).Distinct().Count(),
                   notas.Where(n => n.Produto is not null).GroupBy(n => n.Produto).OrderByDescending(g => g.Sum(n => n.Valor)).FirstOrDefault()?.Key,
                   valor == 0 ? 0 : Math.Round(frete / valor * 100, 2));
    }
}

/// <summary>
/// Lê o XML da NF-e (nfeProc ou NFe, modelo 55) para anexar ao CT-e: sem DTD (proteção contra entidades externas), com os nomes de elemento
/// comparados sem o namespace. Pesos e volumes vêm do grupo transp/vol (somados quando há mais de um).
/// </summary>
public static class LeitorNfeTransporte
{
    public static (NotaTransportada? Nota, string? Erro) Ler(byte[] xml)
    {
        XElement raiz;
        try
        {
            using var ms = new MemoryStream(xml);
            using var leitor = XmlReader.Create(ms, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            raiz = XDocument.Load(leitor).Root ?? throw new XmlException("XML vazio.");
        }
        catch (XmlException ex) { return (null, $"XML inválido: {ex.Message}"); }
        if (raiz.Name.LocalName is not ("nfeProc" or "NFe")) return (null, "O arquivo não é uma NF-e (nfeProc ou NFe).");
        var inf = raiz.DescendantsAndSelf().FirstOrDefault(e => e.Name.LocalName == "infNFe");
        if (inf is null) return (null, "NF-e sem o grupo infNFe.");
        var chave = (inf.Attribute("Id")?.Value ?? "").Replace("NFe", "").Trim();
        if (!RegrasTms.ChaveValida(chave, RegrasTms.ModeloNfe)) return (null, "Chave de acesso da NF-e inválida (44 dígitos, modelo 55).");
        try
        {
            var ide = F(inf, "ide");
            var emit = F(inf, "emit");
            var dest = F(inf, "dest");
            var emissao = DateTime.Parse(V(ide, "dhEmi") ?? V(ide, "dEmi") ?? throw new FormatException("sem data de emissão"), CultureInfo.InvariantCulture,
                                         DateTimeStyles.AdjustToUniversal);
            var vols = F(inf, "transp")?.Elements().Where(e => e.Name.LocalName == "vol").ToList() ?? [];
            var produto = inf.Elements().Where(e => e.Name.LocalName == "det").Select(d => F(d, "prod"))
                             .OrderByDescending(p => N(V(p, "vProd"))).Select(p => V(p, "xProd")).FirstOrDefault();
            return (new NotaTransportada(chave, (int)N(V(ide, "nNF")), (int)N(V(ide, "serie")), DateOnly.FromDateTime(emissao),
                                         V(emit, "CNPJ") ?? V(emit, "CPF"), V(emit, "xNome") ?? "", Parada(F(emit, "enderEmit")),
                                         V(dest, "CNPJ") ?? V(dest, "CPF"), V(dest, "xNome") ?? "", Parada(F(dest, "enderDest")),
                                         N(V(F(F(inf, "total"), "ICMSTot"), "vNF")), vols.Sum(v => N(V(v, "pesoB"))), vols.Sum(v => N(V(v, "pesoL"))),
                                         (int)vols.Sum(v => N(V(v, "qVol"))), produto), null);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException) { return (null, $"Não foi possível ler a NF-e: {ex.Message}"); }
    }

    private static XElement? F(XElement? e, string nome) => e?.Elements().FirstOrDefault(x => x.Name.LocalName == nome);
    private static string? V(XElement? e, string nome) => F(e, nome)?.Value.Trim() is { Length: > 0 } v ? v : null;
    private static decimal N(string? v) => v is null ? 0 : decimal.Parse(v, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static ParadaRota? Parada(XElement? endereco) =>
        V(endereco, "cMun") is { Length: 7 } ibge && V(endereco, "UF") is { } uf ? new ParadaRota(uf, ibge, V(endereco, "xMun") ?? "") : null;
}
