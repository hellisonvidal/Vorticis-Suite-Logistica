using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using Vorticis.Application.Relatorios;

namespace Vorticis.Infra.Relatorios;

/// <summary>
/// Gera PDF com PDFsharp/MigraDoc (licença MIT) e a fonte Liberation Sans embutida (SIL OFL 1.1, métrica da Arial),
/// para que o resultado seja idêntico em qualquer máquina. Termos com "{folhas}" recebem o total de folhas: o documento
/// é montado uma vez para contar as páginas e outra para o resultado final.
/// </summary>
public sealed class GeradorPdf : IGeradorPdf
{
    public const string Fonte = "Liberation Sans";

    static GeradorPdf()
    {
        GlobalFontSettings.FontResolver ??= new ResolvedorFontes();
    }

    public ArquivoGerado Gerar(DocumentoRelatorio documento)
    {
        var temFolhas = (documento.TermoAbertura?.Texto ?? "").Contains("{folhas}") || (documento.TermoEncerramento?.Texto ?? "").Contains("{folhas}");
        var paginas = temFolhas ? Renderizar(documento, folhas: 0).PageCount : 0;
        var final = Renderizar(documento, paginas);
        var total = final.PdfDocument.PageCount;   // ler antes de salvar: depois de salvo o documento fica bloqueado
        if ((documento.AssinaturaDigital ?? ContextoAssinaturaPdf.Atual) is { } assinatura) PrepararAssinatura(final.PdfDocument, assinatura);
        using var ms = new MemoryStream();
        final.PdfDocument.Save(ms, false);
        return new ArquivoGerado(ms.ToArray(), total);
    }

    /// <summary>A assinatura é aplicada ao salvar: o PDFsharp reserva o espaço, calcula o intervalo assinado e grava o CMS.</summary>
    private static void PrepararAssinatura(PdfSharp.Pdf.PdfDocument pdf, AssinaturaDigitalPdf a)
    {
        var assinador = new AssinadorPdfCms(a.Certificado);
        var opcoes = new PdfSharp.Pdf.Signatures.DigitalSignatureOptions
        {
            Reason = a.Motivo ?? "Assinatura digital do relatório",
            Location = a.Local ?? "",
            AppName = "Vorticis Contábil",
            PageIndex = pdf.PageCount - 1,
            Rectangle = new PdfSharp.Drawing.XRect(30, 24, 260, 36),
            AppearanceHandler = new CarimboAssinatura(assinador.CertificateName, a.Motivo ?? "Assinatura digital do relatório"),
        };
        PdfSharp.Pdf.Signatures.DigitalSignatureHandler.ForDocument(pdf, assinador, opcoes);
    }

    private static PdfDocumentRenderer Renderizar(DocumentoRelatorio d, int folhas)
    {
        var doc = new Document();
        doc.Info.Title = d.Titulo;
        doc.Info.Author = "Vorticis Contábil";
        var normal = doc.Styles[StyleNames.Normal]!;
        normal.Font.Name = Fonte;
        normal.Font.Size = 8.5;

        var termoTexto = (Termo t) => t.Texto.Replace("{folhas}", folhas.ToString());

        if (d.TermoAbertura is { } ta) AdicionarTermo(doc, d, ta, termoTexto(ta));

        // Só os termos (emitidos isoladamente) não têm seção de conteúdo.
        var temAssinaturas = d.Assinaturas is { Count: > 0 };
        var secao = d.Blocos.Count > 0 || temAssinaturas ? NovaSecao(doc, d) : null;
        foreach (var bloco in d.Blocos)
        {
            if (secao is null) break;
            if (bloco.NovaPagina) secao.AddPageBreak();
            if (bloco.Titulo is { } titulo)
            {
                var p = secao.AddParagraph(titulo);
                p.Format.Font.Bold = true;
                p.Format.Font.Size = 9.5;
                p.Format.SpaceBefore = Unit.FromPoint(8);
                p.Format.SpaceAfter = Unit.FromPoint(4);
                p.Format.KeepWithNext = true;
            }
            if (bloco.Texto is { } texto) AdicionarTexto(secao.AddParagraph(), texto);
            if (bloco.Colunas is { } colunas && bloco.Linhas is { } linhas) AdicionarTabela(secao, colunas, linhas);
        }
        if (secao is not null && d.Assinaturas is { Count: > 0 } assinaturas) AdicionarAssinaturas(secao, d, assinaturas);

        if (d.TermoEncerramento is { } te) AdicionarTermo(doc, d, te, termoTexto(te));

        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        return renderer;
    }

    private static Section NovaSecao(Document doc, DocumentoRelatorio d)
    {
        var s = doc.AddSection();
        var ps = doc.DefaultPageSetup.Clone();
        // Dimensões explícitas: no MigraDoc 6, Orientation não troca largura/altura de um PageSetup clonado.
        ps.PageFormat = PageFormat.A4;
        ps.Orientation = Orientation.Portrait;
        ps.PageWidth = Unit.FromCentimeter(d.Paisagem ? 29.7 : 21.0);
        ps.PageHeight = Unit.FromCentimeter(d.Paisagem ? 21.0 : 29.7);
        var largura = (d.Paisagem ? 29.7 : 21.0) - 2.8;
        ps.HeaderDistance = Unit.FromCentimeter(0.8);
        // A margem superior acompanha a altura do cabeçalho (identificação + título): se fosse fixa, um cabeçalho com várias
        // linhas ultrapassaria a margem e se sobreporia ao corpo da página.
        ps.TopMargin = Unit.FromCentimeter(0.8 + AlturaCabecalhoCm(d, largura));
        ps.BottomMargin = Unit.FromCentimeter(1.6);
        ps.LeftMargin = ps.RightMargin = Unit.FromCentimeter(1.4);
        ps.FooterDistance = Unit.FromCentimeter(0.7);
        s.PageSetup = ps;

        // Cabeçalho: identificação (empresa/CNPJ/período) no alto, com a folha à direita; o TÍTULO do relatório fica ABAIXO dela.
        var cab = s.Headers.Primary;
        var t = cab.AddTable();
        t.AddColumn(Unit.FromCentimeter(largura - 4));
        t.AddColumn(Unit.FromCentimeter(4));
        var r = t.AddRow();
        var ident = d.Cabecalho.Count > 0 ? d.Cabecalho[0] : "";
        var p0 = r.Cells[0].AddParagraph(ident);
        p0.Format.Font.Size = 8;
        p0.Format.Font.Bold = true;
        var pag = r.Cells[1].AddParagraph($"{d.RotuloPagina} ");
        pag.AddPageField();
        pag.Format.Alignment = ParagraphAlignment.Right;
        pag.Format.Font.Size = 8;
        foreach (var linha in d.Cabecalho.Skip(1))
        {
            var p = cab.AddParagraph(linha);
            p.Format.Font.Size = 8;
        }
        var titulo = cab.AddParagraph(d.Titulo);
        titulo.Format.Font.Bold = true;
        titulo.Format.Font.Size = 11;
        titulo.Format.SpaceBefore = Unit.FromPoint(5);
        titulo.Format.Borders.Bottom.Width = 0.5;
        titulo.Format.Borders.DistanceFromBottom = Unit.FromPoint(2);

        var rod = s.Footers.Primary.AddParagraph($"Vorticis Contábil — emitido em {DateTime.Now:dd/MM/yyyy HH:mm}");
        rod.Format.Font.Size = 6.5;
        rod.Format.Font.Color = Colors.Gray;
        return s;
    }

    /// <summary>Estimativa (conservadora) da altura do cabeçalho em cm, considerando quebra de linhas longas.</summary>
    private static double AlturaCabecalhoCm(DocumentoRelatorio d, double larguraCm)
    {
        static int Linhas(string? t, double cm, double caracteresPorCm) => Math.Max(1, (int)Math.Ceiling((t ?? "").Length / (cm * caracteresPorCm)));
        var ident = d.Cabecalho.Count == 0 ? 1 : Linhas(d.Cabecalho[0], larguraCm - 4, 5.6) + d.Cabecalho.Skip(1).Sum(l => Linhas(l, larguraCm, 6.2));
        return ident * 0.38 + Linhas(d.Titulo, larguraCm, 4.6) * 0.5 + 0.35;
    }

    private static void AdicionarTermo(Document doc, DocumentoRelatorio d, Termo termo, string texto)
    {
        var s = NovaSecao(doc, d);
        var titulo = s.AddParagraph(termo.Titulo);
        titulo.Format.Font.Bold = true;
        titulo.Format.Font.Size = 14;
        titulo.Format.Alignment = ParagraphAlignment.Center;
        titulo.Format.SpaceBefore = Unit.FromCentimeter(2);
        titulo.Format.SpaceAfter = Unit.FromCentimeter(1);
        foreach (var paragrafo in texto.Split("\n\n"))
        {
            // Parágrafo corrido é justificado; blocos com quebras de linha (identificação) ficam à esquerda.
            var linhas = paragrafo.Split('\n');
            var p = s.AddParagraph();
            for (int i = 0; i < linhas.Length; i++)
            {
                if (i > 0) p.AddLineBreak();
                p.AddText(linhas[i]);
            }
            p.Format.Font.Size = 11;
            p.Format.Alignment = linhas.Length > 1 ? ParagraphAlignment.Left : ParagraphAlignment.Justify;
            p.Format.SpaceAfter = Unit.FromPoint(10);
        }
        AdicionarAssinaturas(s, d, termo.Assinaturas);
    }

    /// <summary>Linhas de assinatura lado a lado (nome, cargo/qualificação e documentos sob cada uma).</summary>
    private static void AdicionarAssinaturas(Section s, DocumentoRelatorio d, IReadOnlyList<string> assinaturas)
    {
        var tabela = s.AddTable();
        var largura = ((d.Paisagem ? 29.7 : 21.0) - 2.8) / Math.Max(1, assinaturas.Count);
        foreach (var _ in assinaturas) tabela.AddColumn(Unit.FromCentimeter(largura));
        var linha = tabela.AddRow();
        linha.TopPadding = Unit.FromCentimeter(2.5);
        for (int i = 0; i < assinaturas.Count; i++)
        {
            var p = linha.Cells[i].AddParagraph();
            AdicionarTexto(p, "_______________________________________\n" + assinaturas[i]);
            p.Format.Alignment = ParagraphAlignment.Center;
            p.Format.Font.Size = 10;
        }
    }

    private static void AdicionarTabela(Section s, IReadOnlyList<Coluna> colunas, IReadOnlyList<LinhaTabela> linhas)
    {
        var t = s.AddTable();
        t.Borders.Bottom.Width = 0.25;
        t.Borders.Bottom.Color = Colors.LightGray;
        foreach (var c in colunas)
        {
            var col = t.AddColumn(Unit.FromCentimeter(c.LarguraCm));
            col.Format.Alignment = Alinhar(c.Alinhamento);
        }
        var cab = t.AddRow();
        cab.HeadingFormat = true;
        cab.Shading.Color = new Color(235, 239, 245);
        cab.Format.Font.Bold = true;
        for (int i = 0; i < colunas.Count; i++) cab.Cells[i].AddParagraph(colunas[i].Titulo);

        foreach (var l in linhas)
        {
            var r = t.AddRow();
            r.Format.Font.Bold = l.Negrito;
            for (int i = 0; i < colunas.Count && i < l.Celulas.Count; i++)
            {
                var p = r.Cells[i].AddParagraph(l.Celulas[i]);
                if (i == 0 && l.Recuo > 0) p.Format.LeftIndent = Unit.FromPoint(6 * l.Recuo);
            }
        }
    }

    private static void AdicionarTexto(Paragraph p, string texto)
    {
        var linhas = texto.Split('\n');
        for (int i = 0; i < linhas.Length; i++)
        {
            if (i > 0) p.AddLineBreak();
            p.AddText(linhas[i]);
        }
    }

    private static ParagraphAlignment Alinhar(Alinhamento a) => a switch
    {
        Alinhamento.Direita => ParagraphAlignment.Right,
        Alinhamento.Centro => ParagraphAlignment.Center,
        _ => ParagraphAlignment.Left,
    };

    /// <summary>Fornece as fontes embutidas ao PDFsharp (necessário fora do Windows e garante o mesmo resultado no Windows).</summary>
    private sealed class ResolvedorFontes : IFontResolver
    {
        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
            new(bold ? "LiberationSans-Bold" : "LiberationSans-Regular");

        public byte[]? GetFont(string faceName)
        {
            using var s = typeof(GeradorPdf).Assembly.GetManifestResourceStream($"Fontes.{faceName}.ttf")
                          ?? throw new InvalidOperationException($"Fonte {faceName} não embutida.");
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
