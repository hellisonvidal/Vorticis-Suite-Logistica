using System.Diagnostics;
using Vorticis.Application.Relatorios;
using Vorticis.Domain.Contabil;
using PdfSharp.Pdf.IO;

namespace Vorticis.Infra.Tests;

public class LivrosPdfTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static string? TextoDoPdf(byte[] pdf)
    {
        if (!File.Exists("/usr/bin/pdftotext")) return null;
        var arquivo = Path.GetTempFileName();
        File.WriteAllBytes(arquivo, pdf);
        var p = Process.Start(new ProcessStartInfo("/usr/bin/pdftotext", [arquivo, "-"]) { RedirectStandardOutput = true })!;
        var texto = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        File.Delete(arquivo);
        return texto;
    }

    private static int Paginas(byte[] pdf)
    {
        using var ms = new MemoryStream(pdf);
        return PdfReader.Open(ms, PdfDocumentOpenMode.Import).PageCount;
    }

    [Fact]
    public async Task Diario_tem_termos_com_numero_de_folhas_correto_e_os_demais_livros_sao_gerados()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync("11222333000181");
        var adm = await db.AdminAsync();
        for (int i = 1; i <= 60; i++)   // volume suficiente para várias folhas
            Assert.True((await db.App.Lancamentos.EfetivarAsync(new LancamentoProposto(empresa, new(2026, 1 + i % 12, 1 + i % 27), OrigemLancamento.Manual,
                [new("1.1.01.001", IndicadorDC.D, 100m + i, $"Venda à vista nº {i}"), new("3.1.01.001", IndicadorDC.C, 100m + i, $"Venda à vista nº {i}")]),
                adm, ct)).Sucesso);

        var termos = new DadosTermos(1, "Fulana de Tal", "123.456.789-09", "Beltrano Contador", "SP-123456/O-7");
        var diario = await db.App.Livros.DiarioAsync(empresa, new(2026, 1, 1), new(2026, 12, 31), termos, adm, ct);
        Assert.True(diario.Sucesso, string.Join("; ", diario.Erros));
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(diario.Valor.Conteudo, 0, 4));
        Assert.True(diario.Valor.Paginas >= 4);
        Assert.Equal(diario.Valor.Paginas, Paginas(diario.Valor.Conteudo));

        if (Environment.GetEnvironmentVariable("VORTICIS_SCREENSHOTS") is { } pastaDiario)
            File.WriteAllBytes(Path.Combine(pastaDiario, "livro_diario.pdf"), diario.Valor.Conteudo);
        if (TextoDoPdf(diario.Valor.Conteudo) is { } texto)
        {
            Assert.Contains("TERMO DE ABERTURA", texto);
            Assert.Contains("TERMO DE ENCERRAMENTO", texto);
            Assert.Contains($"Contém este livro {diario.Valor.Paginas} folhas", texto);   // folhas do termo = folhas reais
            Assert.Contains("Venda à vista nº 60", texto);
            Assert.Contains("CRC SP-123456/O-7", texto);
            Assert.Contains($"Folha {diario.Valor.Paginas}", texto);
        }

        Assert.Contains((await db.App.Livros.DiarioAsync(empresa, new(2026, 1, 1), new(2026, 12, 31), termos with { ContadorCrc = "" }, adm, ct)).Erros,
                        e => e.Contains("CRC"));

        var razao = await db.App.Livros.RazaoAsync(empresa, new(2026, 1, 1), new(2026, 12, 31), adm, ct);
        Assert.True(razao.Sucesso, string.Join("; ", razao.Erros));
        if (TextoDoPdf(razao.Valor.Conteudo) is { } tr) Assert.Contains("1.1.01.001 — Caixa Geral", tr);

        var balancete = await db.App.Livros.BalanceteAsync(empresa, new(2026, 1, 1), new(2026, 12, 31), adm, ct);
        Assert.True(balancete.Sucesso);
        var demonstracoes = await db.App.Livros.DemonstracoesAsync(empresa, new(2026, 1, 1), new(2026, 12, 31), 3, "Beltrano — CRC SP-123456/O-7", null, adm, ct);
        Assert.True(demonstracoes.Sucesso, string.Join("; ", demonstracoes.Erros));
        if (TextoDoPdf(demonstracoes.Valor.Conteudo) is { } td)
        {
            Assert.Contains("BALANÇO PATRIMONIAL", td);
            Assert.Contains("LUCRO LÍQUIDO DO PERÍODO", td);
        }

        var pasta = Environment.GetEnvironmentVariable("VORTICIS_SCREENSHOTS");
        if (pasta is not null)
        {
            File.WriteAllBytes(Path.Combine(pasta, "livro_diario.pdf"), diario.Valor.Conteudo);
            File.WriteAllBytes(Path.Combine(pasta, "demonstracoes.pdf"), demonstracoes.Valor.Conteudo);
        }
    }
}
