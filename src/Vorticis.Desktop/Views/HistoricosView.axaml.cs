using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Vorticis.Desktop.Views;

public partial class HistoricosView : UserControl
{
    public HistoricosView() => InitializeComponent();

    /// <summary>Clique numa variável: insere o marcador {X} na posição do cursor (ou no lugar do trecho selecionado).</summary>
    private void AoInserirVariavel(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string marcador }) return;
        var caixa = TextoHistorico;
        var texto = caixa.Text ?? "";
        var (ini, fim) = (Math.Min(caixa.SelectionStart, caixa.SelectionEnd), Math.Max(caixa.SelectionStart, caixa.SelectionEnd));
        ini = Math.Clamp(ini, 0, texto.Length);
        fim = Math.Clamp(fim, ini, texto.Length);
        caixa.Text = texto[..ini] + marcador + texto[fim..];
        caixa.Focus();
        caixa.CaretIndex = ini + marcador.Length;
    }
}
