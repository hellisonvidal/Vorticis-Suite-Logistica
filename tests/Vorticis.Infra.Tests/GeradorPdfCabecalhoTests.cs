using Vorticis.Application.Relatorios;
using Vorticis.Infra.Relatorios;

namespace Vorticis.Infra.Tests;

/// <summary>Layout do PDF: o título do relatório fica abaixo da identificação, sem sobrepor o corpo da página.</summary>
public class GeradorPdfCabecalhoTests
{
    [Fact]
    public void Titulo_abaixo_da_identificacao_e_corpo_abaixo_do_titulo()
    {
        if (!File.Exists("/usr/bin/pdftotext")) return;
        var cab = new[] { "EMPRESA EXEMPLO LTDA", "CNPJ 11.444.777/0001-61 — NIRE 123", "Período: 01/01/2026 a 31/12/2026", "Regime: Lucro Presumido" };
        var linhas = Enumerable.Range(1, 5).Select(i => new LinhaTabela(["Item " + i, "10,00"])).ToList();
        var bloco = new Bloco(Titulo: "Bloco", Colunas: [new Coluna("Descrição", 8), new Coluna("Valor", 3, Alinhamento.Direita)], Linhas: linhas);
        foreach (var paisagem in new[] { false, true })
        {
            var pdf = new GeradorPdf().Gerar(new DocumentoRelatorio("BALANÇO PATRIMONIAL", cab, [bloco], paisagem)).Conteudo;
            var arq = Path.Combine(Path.GetTempPath(), $"vorticis_cab_{paisagem}.pdf");
            File.WriteAllBytes(arq, pdf);
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/usr/bin/pdftotext", ["-bbox", arq, "-"]) { RedirectStandardOutput = true })!;
            var xml = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            double YMin(string palavra) => System.Text.RegularExpressions.Regex.Matches(xml, $"yMin=\"([0-9.]+)\" xMax=\"[0-9.]+\" yMax=\"[0-9.]+\">{palavra}<")
                .Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Min();
            Assert.True(YMin("BALANÇO") > YMin("EXEMPLO"), "título deve ficar abaixo da identificação");
            Assert.True(YMin("Item") > YMin("BALANÇO") + 10, "corpo deve ficar abaixo do título");
        }
    }
}
