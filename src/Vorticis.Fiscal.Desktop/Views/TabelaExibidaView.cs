using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Vorticis.Application.Relatorios;
using Vorticis.Fiscal.Desktop.ViewModels;

namespace Vorticis.Fiscal.Desktop.Views;

/// <summary>Grade montada em código para uma <see cref="TabelaExibida"/> (as colunas dependem do relatório): título, linhas e rodapé.</summary>
public sealed class TabelaExibidaView : UserControl
{
    public TabelaExibidaView() => DataContextChanged += (_, _) => Montar();

    private void Montar()
    {
        if (DataContext is not TabelaExibida t) { Content = null; return; }
        var grade = new DataGrid
        {
            ItemsSource = t.Linhas, AutoGenerateColumns = false, IsReadOnly = true, CanUserSortColumns = false,
            Height = Math.Min(460, 36 + 26 * Math.Max(1, t.Linhas.Count)),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        for (var i = 0; i < t.Colunas.Count; i++)
        {
            var c = t.Colunas[i];
            var numerico = c.Tipo is TipoColuna.Valor or TipoColuna.Inteiro;
            var coluna = new DataGridTextColumn
            {
                Header = c.Titulo, Binding = new Binding($"[{i}]"), IsReadOnly = true,
                Width = c.LarguraCm > 0 ? new DataGridLength(Math.Max(60, c.LarguraCm * 38)) : numerico ? new DataGridLength(110) : c.Tipo == TipoColuna.Data ? new DataGridLength(95) : new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 50,
            };
            GradesFiscais.AjustarColuna(coluna, numerico, grade.FontSize);
            grade.Columns.Add(coluna);
        }
        // Linhas de total em negrito.
        grade.LoadingRow += (_, e) =>
        {
            var i = e.Row.Index;
            e.Row.FontWeight = i >= 0 && i < t.Destaques.Count && t.Destaques[i] ? FontWeight.Bold : FontWeight.Normal;
        };
        var painel = new StackPanel { Spacing = 3, Margin = new Avalonia.Thickness(0, 0, 0, 10) };
        painel.Children.Add(new TextBlock { Text = t.Titulo, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#1F3A5F")) });
        painel.Children.Add(grade);
        if (!string.IsNullOrWhiteSpace(t.Rodape))
            painel.Children.Add(new TextBlock { Text = t.Rodape, FontSize = 11, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Left });
        Content = painel;
    }
}
