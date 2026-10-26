using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Vorticis.Application.Relatorios;
using Vorticis.Fiscal.Desktop.ViewModels;
using Vorticis.Fiscal.Desktop.Views;

namespace Vorticis.Desktop.Tests;

/// <summary>Grades do Fiscal: cabeçalhos por inteiro (largura mínima + dica) e colunas numéricas à direita.</summary>
public class GradesFiscaisTests(SessaoHeadless headless)
{
    private static string RaizDoRepositorio()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Vorticis.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }

    private static IEnumerable<string> ViewsFiscais() => Directory.GetFiles(Path.Combine(RaizDoRepositorio(), "src", "Vorticis.Fiscal.Desktop", "Views"), "*.axaml");

    [Fact]
    public void Toda_grade_do_fiscal_usa_o_ajuste_de_colunas()
    {
        var sem = ViewsFiscais().SelectMany(f => Regex.Matches(File.ReadAllText(f), "<DataGrid [^>]*>").Select(m => (Arquivo: Path.GetFileName(f), Tag: m.Value)))
            .Where(x => !x.Tag.Contains("GradesFiscais.Ajustar")).Select(x => x.Arquivo).Distinct().ToList();
        Assert.Empty(sem);
    }

    [Fact]
    public void Largura_de_toda_coluna_declarada_comporta_o_cabecalho_depois_do_ajuste()
    {
        var colunas = ViewsFiscais().SelectMany(f => Regex.Matches(File.ReadAllText(f), "<DataGridTextColumn [^>]*>")
            .Select(m => (Header: Regex.Match(m.Value, "Header=\"([^\"]*)\"").Groups[1].Value, Largura: Regex.Match(m.Value, "Width=\"([^\"]*)\"").Groups[1].Value)))
            .Where(c => c.Header.Length > 0).ToList();
        Assert.True(colunas.Count > 80);
        var cortadas = 0;
        foreach (var (header, largura) in colunas)
        {
            var c = new DataGridTextColumn
            {
                Header = header,
                Width = largura == "*" ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(double.Parse(largura, System.Globalization.CultureInfo.InvariantCulture)),
            };
            if (c.Width.IsAbsolute && c.Width.Value < GradesFiscais.LarguraMinima(header)) cortadas++;   // antes do ajuste: seria cortada
            GradesFiscais.AjustarColuna(c, GradesFiscais.EhNumerico(header));
            var minimo = GradesFiscais.LarguraMinima(header);
            Assert.True(c.MinWidth >= minimo, $"{header}: largura mínima {c.MinWidth} < {minimo}");
            if (c.Width.IsAbsolute) Assert.True(c.Width.Value >= minimo, $"{header}: largura {c.Width.Value} < {minimo}");
        }
        Assert.True(cortadas > 0, "O teste deveria ter encontrado cabeçalhos que não cabiam na largura original.");
    }

    [Fact]
    public void Colunas_numericas_ficam_a_direita_e_texto_a_esquerda()
    {
        var valor = new DataGridTextColumn { Header = "Valor", Width = new DataGridLength(110) };
        var nome = new DataGridTextColumn { Header = "Cliente / fornecedor", Width = new DataGridLength(1, DataGridLengthUnitType.Star) };
        GradesFiscais.AjustarColuna(valor, GradesFiscais.EhNumerico("Valor"));
        GradesFiscais.AjustarColuna(nome, GradesFiscais.EhNumerico("Cliente / fornecedor"));
        Assert.Contains("num", valor.CellStyleClasses);
        Assert.Equal("Valor", GradesFiscais.TextoCabecalho(valor));
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Right, Assert.IsType<TextBlock>(valor.Header).HorizontalAlignment);
        Assert.DoesNotContain("num", nome.CellStyleClasses);
        Assert.Equal("Cliente / fornecedor", nome.Header);
        Assert.All(new[] { "Valor", "Total", "Qtde", "Unitário", "ICMS", "ICMS-ST", "IPI", "PIS", "COFINS", "ISS" }, h => Assert.True(GradesFiscais.EhNumerico(h), h));
        Assert.All(new[] { "Número", "CFOP", "Descrição", "Situação" }, h => Assert.False(GradesFiscais.EhNumerico(h), h));
    }

    [Fact]
    public async Task Telas_renderizam_com_cabecalhos_inteiros_e_com_o_painel_de_importacao()
    {
        var ok = await Task.Run(async () => await headless.Sessao.Dispatch(async () =>
        {
            var ctx = new Vorticis.Fiscal.Desktop.MainViewModel();
            var docs = new DocumentosViewModel(ctx);
            var nota = new NotaEntradaViewModel(ctx);
            nota.Importada = true;                 // nota importada: aviso + escrituração
            nota.TemNovoParticipante = true;       // participante novo: campos para conferir
            nota.ParticipanteInfo = "Fornecedor não cadastrado: será cadastrado ao gravar a nota (confirmação).";
            nota.Importador.Resultados.Add(new ResultadoImportacaoLinha("a.xml", "Importado", "10", "Entrada", "R$ 10,00", "ok"));
            nota.Importador.Resumo = "1 arquivo(s): 1 importado(s)";
            nota.Importador.TemResumo = true;
            var importacao = new ImportacaoXmlViewModel(ctx) { Importando = true, Percentual = 40, Andamento = "40 de 100 - n40.xml" };
            var pares = new (object Vm, UserControl View)[] { (docs, new DocumentosView()), (nota, new DigitacaoDocumentoView()), (importacao, new ImportacaoXmlView()) };
            foreach (var (vm, view) in pares)
            {
                view.DataContext = vm;
                var janela = new Window { Content = view, Width = 1400, Height = 900 };
                janela.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.NotNull(janela.CaptureRenderedFrame());
                foreach (var grade in view.GetLogicalDescendants().OfType<DataGrid>())
                    foreach (var c in grade.Columns)
                    {
                        var texto = GradesFiscais.TextoCabecalho(c)!;
                        Assert.True(c.ActualWidth + 0.5 >= GradesFiscais.LarguraMinima(texto, grade.FontSize), $"Cabeçalho \"{texto}\" cortado: {c.ActualWidth}");
                    }
                janela.Close();
            }
            // colunas numéricas de uma tabela de relatório
            var tabela = new TabelaRelatorio("T", [new("Nome"), new("Valor total do documento", TipoColuna.Valor)], [new LinhaRelatorio(["A", 10m])], null);
            var exibida = new TabelaExibidaView { DataContext = new TabelaExibida(tabela) };
            var jt = new Window { Content = exibida, Width = 600, Height = 300 };
            jt.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(jt.CaptureRenderedFrame());
            var g = exibida.GetLogicalDescendants().OfType<DataGrid>().Single();
            Assert.Contains("num", g.Columns[1].CellStyleClasses);
            Assert.True(g.Columns[1].ActualWidth + 0.5 >= GradesFiscais.LarguraMinima("Valor total do documento", g.FontSize), $"{g.Columns[1].ActualWidth} / {g.Columns[1].Width} / {g.Columns[1].MinWidth} / {g.FontSize}");
            jt.Close();
            await Task.CompletedTask;
            return true;
        }, TestContext.Current.CancellationToken));
        Assert.True(ok);
    }
}
