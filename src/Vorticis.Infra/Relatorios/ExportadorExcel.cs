using System.IO.Compression;
using System.Xml.Linq;
using MiniExcelLibs;
using Vorticis.Application.Relatorios;

namespace Vorticis.Infra.Relatorios;

/// <summary>
/// Planilha .xlsx com uma aba por tabela; valores numéricos e datas como números/datas do Excel. As colunas de valor (R$) saem formatadas
/// como moeda, com duas casas decimais (continuam números: dá para somar e filtrar).
/// </summary>
public static class ExportadorExcel
{
    public static byte[] Gerar(IReadOnlyList<TabelaRelatorio> tabelas)
    {
        var abas = new Dictionary<string, object>();
        foreach (var t in tabelas)
        {
            var titulos = TitulosUnicos(t.Colunas);
            var linhas = t.Linhas.Select(l =>
            {
                var d = new Dictionary<string, object?>();
                for (var i = 0; i < titulos.Count; i++)
                    d[titulos[i]] = l.Valores[i] switch
                    {
                        DateOnly data => data.ToDateTime(TimeOnly.MinValue),   // códigos ficam sem recuo (filtros do Excel)
                        // valor que veio como texto ("1.500,00", "R$ 10,00"): vira número, para sair em moeda e poder somar
                        string texto when t.Colunas[i].Tipo == TipoColuna.Valor && LerValor(texto) is { } numero => numero,
                        var v => v,
                    };
                return d;
            }).ToList();
            abas[NomeAba(t.Titulo, abas.Keys)] = linhas;
        }
        using var ms = new MemoryStream();
        ms.SaveAs(abas, excelType: ExcelType.XLSX);
        return FormatarMoeda(ms.ToArray(), [.. tabelas.Select(t => t.Colunas.Select((c, i) => (c, i)).Where(x => x.c.Tipo == TipoColuna.Valor).Select(x => x.i).ToHashSet())]);
    }

    /// <summary>Formato de moeda das colunas de valor: R$ com separador de milhar e duas casas; negativos com o sinal.</summary>
    public const string FormatoMoeda = "\"R$\" #,##0.00;\"R$\" -#,##0.00";

    /// <summary>Aplica o formato de moeda às células numéricas das colunas de valor (índices por aba, na ordem das tabelas), menos no cabeçalho.</summary>
    private static byte[] FormatarMoeda(byte[] xlsx, IReadOnlyList<HashSet<int>> colunasValor)
    {
        if (colunasValor.All(c => c.Count == 0)) return xlsx;
        using var ms = new MemoryStream();
        ms.Write(xlsx);
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            // estilos: um formato de número próprio e um estilo de célula com ele
            var estilos = zip.GetEntry("xl/styles.xml");
            if (estilos is null) return xlsx;
            XDocument st;
            using (var r = estilos.Open()) st = XDocument.Load(r);
            var ns = st.Root!.Name.Namespace;
            var numFmts = st.Root.Element(ns + "numFmts");
            if (numFmts is null) { numFmts = new XElement(ns + "numFmts"); st.Root.AddFirst(numFmts); }
            var idFmt = numFmts.Elements(ns + "numFmt").Select(e => (int)e.Attribute("numFmtId")!).Append(163).Max() + 1;
            numFmts.Add(new XElement(ns + "numFmt", new XAttribute("numFmtId", idFmt), new XAttribute("formatCode", FormatoMoeda)));
            numFmts.SetAttributeValue("count", numFmts.Elements(ns + "numFmt").Count());
            var cellXfs = st.Root.Element(ns + "cellXfs");
            if (cellXfs is null) return xlsx;
            var indiceEstilo = cellXfs.Elements(ns + "xf").Count();
            cellXfs.Add(new XElement(ns + "xf", new XAttribute("numFmtId", idFmt), new XAttribute("fontId", 0), new XAttribute("fillId", 0),
                                     new XAttribute("borderId", 0), new XAttribute("xfId", 0), new XAttribute("applyNumberFormat", 1)));
            cellXfs.SetAttributeValue("count", indiceEstilo + 1);
            estilos.Delete();
            using (var w = zip.CreateEntry("xl/styles.xml").Open()) st.Save(w);

            for (var aba = 0; aba < colunasValor.Count; aba++)
            {
                if (colunasValor[aba].Count == 0 || zip.GetEntry($"xl/worksheets/sheet{aba + 1}.xml") is not { } folha) continue;
                XDocument sh;
                using (var r = folha.Open()) sh = XDocument.Load(r);
                var nsf = sh.Root!.Name.Namespace;
                foreach (var c in sh.Descendants(nsf + "c"))
                {
                    var refCel = (string?)c.Attribute("r") ?? "";
                    var letras = new string([.. refCel.TakeWhile(char.IsLetter)]);
                    if (!int.TryParse(refCel[letras.Length..], out var linha) || linha <= 1) continue;
                    var tipo = (string?)c.Attribute("t");
                    if (tipo is not (null or "n") || !colunasValor[aba].Contains(IndiceColuna(letras))) continue;
                    c.SetAttributeValue("s", indiceEstilo);
                }
                folha.Delete();
                using var w = zip.CreateEntry($"xl/worksheets/sheet{aba + 1}.xml").Open();
                sh.Save(w);
            }
        }
        return ms.ToArray();
    }

    private static readonly System.Globalization.CultureInfo PtBr = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Texto de valor em reais ("1.500,00", "R$ -10,50", "(10,00)") como número; null se não for um valor.</summary>
    public static decimal? LerValor(string texto)
    {
        var t = texto.Trim().Replace("R$", "").Trim();
        var negativo = t.StartsWith('(') && t.EndsWith(')');
        if (negativo) t = t[1..^1].Trim();
        if (t.Length == 0 || t.Contains('%') || !t.Any(char.IsDigit)) return null;
        return decimal.TryParse(t, System.Globalization.NumberStyles.Number, PtBr, out var v) ? (negativo ? -v : v) : null;
    }

    private static int IndiceColuna(string letras) => letras.ToUpperInvariant().Aggregate(0, (acc, ch) => acc * 26 + (ch - 'A' + 1)) - 1;

    /// <summary>Lê de volta a primeira aba (usado nos testes e na importação).</summary>
    public static IReadOnlyList<IDictionary<string, object?>> Ler(byte[] xlsx)
    {
        using var ms = new MemoryStream(xlsx);
        return ms.Query(useHeaderRow: true, excelType: ExcelType.XLSX)
                 .Cast<IDictionary<string, object?>>().ToList();
    }

    private static List<string> TitulosUnicos(IReadOnlyList<ColunaRelatorio> colunas)
    {
        var res = new List<string>();
        foreach (var c in colunas)
        {
            var t = string.IsNullOrWhiteSpace(c.Titulo) ? "Coluna" : c.Titulo;
            var nome = t;
            for (var n = 2; res.Contains(nome); n++) nome = $"{t} ({n})";
            res.Add(nome);
        }
        return res;
    }

    private static string NomeAba(string titulo, IEnumerable<string> existentes)
    {
        var limpo = new string(titulo.Select(ch => "[]:*?/\\".Contains(ch) ? '-' : ch).ToArray()).Trim();
        if (limpo.Length == 0) limpo = "Relatório";
        if (limpo.Length > 31) limpo = limpo[..31];
        var nome = limpo;
        for (var n = 2; existentes.Contains(nome); n++) nome = $"{limpo[..Math.Min(limpo.Length, 27)]} ({n})";
        return nome;
    }
}
