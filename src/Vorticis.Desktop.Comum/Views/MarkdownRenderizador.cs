using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Vorticis.Desktop.Comum.Ajuda;

namespace Vorticis.Desktop.Comum.Views;

/// <summary>Desenha os blocos do Markdown simples do manual com controles do Avalonia (sem pacotes de terceiros).</summary>
public static class MarkdownRenderizador
{
    private static readonly IBrush Azul = new SolidColorBrush(Color.Parse("#1F3A5F"));
    private static readonly IBrush Link = new SolidColorBrush(Color.Parse("#1B5FBF"));
    private static readonly IBrush FundoCodigo = new SolidColorBrush(Color.Parse("#EEF1F5"));
    private static readonly IBrush Borda = new SolidColorBrush(Color.Parse("#C8D3E6"));
    private static readonly IBrush FundoCabecalho = new SolidColorBrush(Color.Parse("#EEF3FA"));

    public static StackPanel Construir(IEnumerable<Bloco> blocos, Action<string?> navegar)
    {
        var raiz = new StackPanel { Spacing = 8 };
        foreach (var b in blocos)
        {
            switch (b)
            {
                case BlocoTitulo t:
                    var tam = t.Nivel switch { 1 => 24, 2 => 19, 3 => 16, _ => 14 };
                    var titulo = Texto(t.Trechos, navegar, tam, Azul);
                    titulo.FontWeight = FontWeight.Bold;
                    titulo.Margin = new Thickness(0, t.Nivel <= 2 ? 10 : 6, 0, 0);
                    raiz.Children.Add(titulo);
                    break;
                case BlocoParagrafo p:
                    raiz.Children.Add(Texto(p.Trechos, navegar));
                    break;
                case BlocoLista l:
                    var lista = new StackPanel { Spacing = 3, Margin = new Thickness(12, 0, 0, 0) };
                    for (var i = 0; i < l.Itens.Count; i++)
                    {
                        var linha = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*") };
                        linha.Children.Add(new TextBlock { Text = l.Ordenada ? $"{i + 1}." : "•", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 8, 0) });
                        var texto = Texto(l.Itens[i], navegar);
                        Grid.SetColumn(texto, 1);
                        linha.Children.Add(texto);
                        lista.Children.Add(linha);
                    }
                    raiz.Children.Add(lista);
                    break;
                case BlocoTabela tb:
                    raiz.Children.Add(Tabela(tb, navegar));
                    break;
            }
        }
        return raiz;
    }

    private static Control Tabela(BlocoTabela tb, Action<string?> navegar)
    {
        var colunas = Math.Max(tb.Cabecalho.Count, tb.Linhas.Select(l => l.Count).DefaultIfEmpty(0).Max());
        var grade = new Grid();
        for (var c = 0; c < colunas; c++) grade.ColumnDefinitions.Add(new ColumnDefinition(c == 0 && colunas > 1 ? GridLength.Auto : GridLength.Star));
        var todas = new[] { tb.Cabecalho }.Concat(tb.Linhas).ToList();
        for (var r = 0; r < todas.Count; r++)
        {
            grade.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var c = 0; c < colunas; c++)
            {
                var texto = Texto(c < todas[r].Count ? todas[r][c] : [], navegar);
                if (r == 0) texto.FontWeight = FontWeight.Bold;
                var celula = new Border
                {
                    BorderBrush = Borda, BorderThickness = new Thickness(0.5), Padding = new Thickness(6, 3),
                    Background = r == 0 ? FundoCabecalho : null, Child = texto,
                };
                Grid.SetRow(celula, r);
                Grid.SetColumn(celula, c);
                grade.Children.Add(celula);
            }
        }
        return new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = grade };
    }

    private static TextBlock Texto(IEnumerable<Trecho> trechos, Action<string?> navegar, double tamanho = 13, IBrush? cor = null)
    {
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = tamanho };
        if (cor is not null) tb.Foreground = cor;
        foreach (var t in trechos)
        {
            if (t.Link is { } destino)
            {
                var link = new TextBlock
                {
                    Text = t.Texto, Foreground = Link, TextDecorations = TextDecorations.Underline, Cursor = new Cursor(StandardCursorType.Hand),
                    FontWeight = t.Negrito ? FontWeight.Bold : FontWeight.Normal, FontSize = tamanho,
                };
                link.PointerPressed += (_, e) => { navegar(destino); e.Handled = true; };
                tb.Inlines!.Add(new InlineUIContainer(link));
                continue;
            }
            var run = new Run(t.Texto)
            {
                FontWeight = t.Negrito ? FontWeight.Bold : FontWeight.Normal,
                FontStyle = t.Italico ? FontStyle.Italic : FontStyle.Normal,
            };
            if (t.Codigo) { run.FontFamily = new FontFamily("Consolas, Courier New, monospace"); run.Background = FundoCodigo; }
            tb.Inlines!.Add(run);
        }
        return tb;
    }
}
