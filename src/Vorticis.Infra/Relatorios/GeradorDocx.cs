using System.IO.Compression;
using System.Security;
using System.Text;
using Vorticis.Application.Relatorios;

namespace Vorticis.Infra.Relatorios;

/// <summary>
/// Gera o documento em Word (.docx, Office Open XML) sem pacote externo: título, linhas de identificação, blocos de texto (tabelas
/// viram linhas separadas por tabulação) e o bloco de assinaturas. Serve para o usuário editar o texto no Word ou no LibreOffice.
/// </summary>
public static class GeradorDocx
{
    public static byte[] Gerar(DocumentoRelatorio documento)
    {
        var corpo = new StringBuilder();
        corpo.Append(Paragrafo(documento.Titulo, negrito: true, centro: true, tamanho: 28));
        foreach (var l in documento.Cabecalho) corpo.Append(Paragrafo(l, centro: true));
        foreach (var b in documento.Blocos)
        {
            if (b.NovaPagina) corpo.Append("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>");
            if (!string.IsNullOrWhiteSpace(b.Titulo)) corpo.Append(Paragrafo(b.Titulo, negrito: true));
            if (!string.IsNullOrWhiteSpace(b.Texto)) corpo.Append(Paragrafo(b.Texto, justificado: true));
            if (b.Colunas is { Count: > 0 } cols) corpo.Append(Paragrafo(string.Join("\t", cols.Select(c => c.Titulo)), negrito: true));
            foreach (var linha in b.Linhas ?? []) corpo.Append(Paragrafo(string.Join("\t", linha.Celulas), negrito: linha.Negrito));
        }
        foreach (var a in documento.Assinaturas ?? [])
        {
            corpo.Append(Paragrafo(""));
            corpo.Append(Paragrafo("______________________________________________", centro: true));
            corpo.Append(Paragrafo(a, centro: true));
        }
        var orientacao = documento.Paisagem ? " w:orient=\"landscape\" w:w=\"16838\" w:h=\"11906\"" : " w:w=\"11906\" w:h=\"16838\"";
        var xml = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>{corpo}<w:sectPr><w:pgSz{orientacao}/><w:pgMar w:top="1418" w:right="1134" w:bottom="1134" w:left="1701" w:header="709" w:footer="709" w:gutter="0"/></w:sectPr></w:body></w:document>
            """;
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Escrever(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/></Types>
                """);
            Escrever(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
                """);
            Escrever(zip, "word/_rels/document.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>
                """);
            Escrever(zip, "word/styles.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial" w:cs="Arial"/><w:sz w:val="22"/><w:lang w:val="pt-BR"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after="160" w:line="276" w:lineRule="auto"/></w:pPr></w:pPrDefault></w:docDefaults></w:styles>
                """);
            Escrever(zip, "word/document.xml", xml);
        }
        return ms.ToArray();
    }

    private static void Escrever(ZipArchive zip, string nome, string conteudo)
    {
        using var s = zip.CreateEntry(nome, CompressionLevel.Optimal).Open();
        var bytes = new UTF8Encoding(false).GetBytes(conteudo.Trim());
        s.Write(bytes);
    }

    /// <summary>Parágrafo com as quebras de linha do texto preservadas.</summary>
    private static string Paragrafo(string texto, bool negrito = false, bool centro = false, bool justificado = false, int? tamanho = null)
    {
        var ppr = centro ? "<w:pPr><w:jc w:val=\"center\"/></w:pPr>" : justificado ? "<w:pPr><w:jc w:val=\"both\"/></w:pPr>" : "";
        var rpr = negrito || tamanho is not null ? $"<w:rPr>{(negrito ? "<w:b/>" : "")}{(tamanho is { } t ? $"<w:sz w:val=\"{t}\"/>" : "")}</w:rPr>" : "";
        var linhas = texto.Replace("\r\n", "\n").Split('\n')
            .Select(l => string.Join("<w:tab/>", l.Split('\t').Select(p => $"<w:t xml:space=\"preserve\">{SecurityElement.Escape(p)}</w:t>")));
        return $"<w:p>{ppr}<w:r>{rpr}{string.Join("<w:br/>", linhas)}</w:r></w:p>";
    }
}
