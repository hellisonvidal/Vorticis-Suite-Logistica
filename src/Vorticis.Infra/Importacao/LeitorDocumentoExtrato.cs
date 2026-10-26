using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Comum;

namespace Vorticis.Infra.Importacao;

/// <summary>
/// Lê o texto de um extrato em PDF, TXT ou CSV, uma linha por linha impressa. No PDF, as palavras da mesma altura formam a linha e
/// um espaço largo entre elas (mudança de coluna) vira dois espaços, para o layout separar as colunas.
/// </summary>
public static class LeitorDocumentoExtrato
{
    public static bool EhPdf(string caminho) => caminho.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    public static Resultado<IReadOnlyList<string>> Ler(string caminho)
    {
        try
        {
            var linhas = EhPdf(caminho)
                ? LerPdf(File.ReadAllBytes(caminho))
                : LeitorPlanoReferencial.Decodificar(File.ReadAllBytes(caminho)).Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            linhas = linhas.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            return linhas.Count == 0
                ? Resultado<IReadOnlyList<string>>.Falha(EhPdf(caminho)
                    ? "Não encontrei texto no PDF (ele pode ser uma imagem escaneada). Peça ao banco o extrato em OFX ou em PDF gerado pelo internet banking."
                    : "O arquivo está vazio.")
                : Resultado<IReadOnlyList<string>>.Ok(linhas);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Resultado<IReadOnlyList<string>>.Falha($"Não foi possível ler o arquivo: {ex.Message}");
        }
    }

    public static List<string> LerPdf(byte[] conteudo)
    {
        var res = new List<string>();
        using var doc = PdfDocument.Open(conteudo);
        foreach (var pagina in doc.GetPages())
        {
            var palavras = pagina.GetWords().Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
            foreach (var linha in AgruparLinhas(palavras)) res.Add(Juntar(linha));
        }
        return res;
    }

    /// <summary>Palavras com a base na mesma altura (tolerância de meia altura de letra), de cima para baixo.</summary>
    private static IEnumerable<List<Word>> AgruparLinhas(List<Word> palavras)
    {
        var linhas = new List<(double Base, double Altura, List<Word> Palavras)>();
        foreach (var w in palavras.OrderByDescending(w => w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left))
        {
            var altura = Math.Max(1, w.BoundingBox.Height);
            var i = linhas.FindIndex(l => Math.Abs(l.Base - w.BoundingBox.Bottom) <= Math.Max(l.Altura, altura) / 2);
            if (i >= 0) linhas[i].Palavras.Add(w);
            else linhas.Add((w.BoundingBox.Bottom, altura, [w]));
        }
        return linhas.OrderByDescending(l => l.Base).Select(l => l.Palavras.OrderBy(w => w.BoundingBox.Left).ToList());
    }

    private static string Juntar(List<Word> linha)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < linha.Count; i++)
        {
            if (i > 0)
            {
                var anterior = linha[i - 1];
                var letra = anterior.BoundingBox.Width / Math.Max(1, anterior.Text.Length);
                sb.Append(linha[i].BoundingBox.Left - anterior.BoundingBox.Right > Math.Max(2.5, letra * 1.8) ? "  " : " ");
            }
            sb.Append(linha[i].Text);
        }
        return sb.ToString();
    }
}
