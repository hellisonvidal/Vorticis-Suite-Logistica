using System.IO.Compression;
using System.Xml.Linq;
using Vorticis.Application.Relatorios;
using Vorticis.Infra.Relatorios;

namespace Vorticis.Infra.Tests;

/// <summary>Word (.docx) do gerador de contratos: pacote OOXML válido com título, cláusulas e assinaturas.</summary>
public class GeradorDocxTests
{
    [Fact]
    public void Gera_docx_com_titulo_texto_e_assinaturas()
    {
        var doc = new DocumentoRelatorio("CONTRATO Nº 7", ["Escritório & Cia"],
            [new Bloco(Texto: "CLÁUSULA 1ª – DO OBJETO.\nLinha 2 <com> símbolos"), new Bloco(Texto: "CLÁUSULA 2ª – DO PREÇO.")],
            Assinaturas: ["Padaria Boa\nCONTRATANTE", "Escritório\nCONTRATADA"]);
        var bytes = GeradorDocx.Gerar(doc);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
        Assert.NotNull(zip.GetEntry("_rels/.rels"));
        using var s = zip.GetEntry("word/document.xml")!.Open();
        var xml = XDocument.Load(s);                       // XML bem formado (caracteres escapados)
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var texto = string.Join("|", xml.Descendants(w + "t").Select(t => t.Value));
        Assert.Contains("CONTRATO Nº 7", texto);
        Assert.Contains("Escritório & Cia", texto);
        Assert.Contains("Linha 2 <com> símbolos", texto);
        Assert.Contains("CONTRATANTE", texto);
        Assert.Equal(2, xml.Descendants(w + "t").Count(t => t.Value.StartsWith("____")));
    }
}
