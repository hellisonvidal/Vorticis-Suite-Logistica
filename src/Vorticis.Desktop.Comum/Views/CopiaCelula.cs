using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Vorticis.Desktop.Comum.Views;

/// <summary>
/// Copiar/colar por célula em todas as grades (ligado por estilo global em App.axaml: <c>v:CopiaCelula.Ativa="True"</c>).
/// Ctrl+C (ou Ctrl+Insert) copia só o conteúdo da célula atual — a coluna clicada da linha selecionada —, e não a linha
/// inteira; Ctrl+V (ou Shift+Insert) cola o texto da área de transferência na célula atual quando ela é editável.
/// Com a célula em edição, o próprio campo de texto trata o copiar/colar.
/// </summary>
public static class CopiaCelula
{
    public static readonly AttachedProperty<bool> AtivaProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, bool>("Ativa", typeof(CopiaCelula));

    public static bool GetAtiva(DataGrid g) => g.GetValue(AtivaProperty);
    public static void SetAtiva(DataGrid g, bool v) => g.SetValue(AtivaProperty, v);

    static CopiaCelula()
    {
        AtivaProperty.Changed.AddClassHandler<DataGrid>((g, e) =>
        {
            g.RemoveHandler(InputElement.KeyDownEvent, AoTeclar);
            if (e.NewValue is true)
            {
                g.ClipboardCopyMode = DataGridClipboardCopyMode.None;        // a cópia padrão leva a linha inteira
                g.AddHandler(InputElement.KeyDownEvent, AoTeclar, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            }
        });
    }

    private static void AoTeclar(object? sender, KeyEventArgs e)
    {
        if (sender is not DataGrid grade || e.Source is TextBox) return;       // em edição: o TextBox trata
        var copiar = e is { Key: Key.C, KeyModifiers: KeyModifiers.Control } or { Key: Key.Insert, KeyModifiers: KeyModifiers.Control };
        var colar = e is { Key: Key.V, KeyModifiers: KeyModifiers.Control } or { Key: Key.Insert, KeyModifiers: KeyModifiers.Shift };
        if (!copiar && !colar) return;
        e.Handled = true;
        if (copiar) _ = CopiarAsync(grade);
        else _ = ColarAsync(grade);
    }

    /// <summary>Texto mostrado na célula atual (coluna atual × linha selecionada); null se não há célula atual.</summary>
    public static string? TextoDaCelulaAtual(DataGrid grade)
    {
        if (grade.CurrentColumn is not { } coluna || (grade.SelectedItem ?? grade.CurrentItem()) is not { } item) return null;
        return TextoDe(Celula(grade, coluna, item)) ?? ValorDoBinding(coluna, item);
    }

    private static object? CurrentItem(this DataGrid g) => g.SelectedItems.Count > 0 ? g.SelectedItems[0] : null;

    private static DataGridCell? Celula(DataGrid grade, DataGridColumn coluna, object item)
    {
        var linha = grade.GetVisualDescendants().OfType<DataGridRow>().FirstOrDefault(r => ReferenceEquals(r.DataContext, item));
        return linha?.GetVisualDescendants().OfType<DataGridCell>()
                     .FirstOrDefault(c => ReferenceEquals(DataGridColumn.GetColumnContainingElement(c), coluna));
    }

    private static string? TextoDe(DataGridCell? celula)
    {
        if (celula is null) return null;
        foreach (var v in celula.GetVisualDescendants())
            switch (v)
            {
                case TextBox tb: return tb.Text;
                case TextBlock tx: return tx.Text;
                case CheckBox cb: return cb.IsChecked == true ? "Sim" : "Não";
            }
        return celula.Content is string s ? s : null;
    }

    /// <summary>Célula ainda não materializada (rolagem): lê a propriedade do item pelo caminho do binding da coluna.</summary>
    private static string? ValorDoBinding(DataGridColumn coluna, object item)
    {
        var caminho = (coluna as DataGridBoundColumn)?.Binding is Avalonia.Data.Binding b ? b.Path : null;
        if (string.IsNullOrEmpty(caminho)) return null;
        object? atual = item;
        foreach (var parte in caminho.Split('.'))
        {
            atual = atual?.GetType().GetProperty(parte)?.GetValue(atual);
            if (atual is null) return null;
        }
        return Convert.ToString(atual, System.Globalization.CultureInfo.CurrentCulture);
    }

    private static async Task CopiarAsync(DataGrid grade)
    {
        var texto = TextoDaCelulaAtual(grade);
        if (texto is null || TopLevel.GetTopLevel(grade)?.Clipboard is not { } area) return;
        await area.SetTextAsync(texto);
    }

    private static async Task ColarAsync(DataGrid grade)
    {
        if (grade.IsReadOnly || grade.CurrentColumn is not { IsReadOnly: false } coluna || grade.SelectedItem is not { } item) return;
        if (TopLevel.GetTopLevel(grade)?.Clipboard is not { } area) return;
        using var dados = await area.TryGetDataAsync();
        var texto = dados is null ? null : (await dados.TryGetTextAsync())?.Split('\r', '\n')[0];
        if (texto is null) return;
        if (!grade.BeginEdit()) return;
        // O campo de edição existe depois que a célula entra em edição: troca o texto e confirma.
        Dispatcher.UIThread.Post(() =>
        {
            if (Celula(grade, coluna, item)?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault() is { } tb)
            {
                tb.Text = texto;
                grade.CommitEdit();
            }
            else grade.CancelEdit();
        }, DispatcherPriority.Loaded);
    }
}
