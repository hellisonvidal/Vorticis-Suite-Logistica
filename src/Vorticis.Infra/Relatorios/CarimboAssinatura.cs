using PdfSharp.Drawing;

namespace Vorticis.Infra.Relatorios;

/// <summary>Carimbo visível da assinatura digital, no rodapé da última página.</summary>
internal sealed class CarimboAssinatura(string titular, string motivo) : PdfSharp.Pdf.Annotations.IAnnotationAppearanceHandler
{
    public void DrawAppearance(XGraphics gfx, XRect rect)
    {
        var fonte = new XFont(GeradorPdf.Fonte, 7);
        gfx.DrawRectangle(XPens.Gray, rect);
        gfx.DrawString("Assinado digitalmente por " + titular, fonte, XBrushes.Black, new XPoint(rect.X + 4, rect.Y + 12));
        gfx.DrawString(motivo + " - " + DateTime.Now.ToString("dd/MM/yyyy HH:mm"), fonte, XBrushes.Black, new XPoint(rect.X + 4, rect.Y + 24));
    }
}
