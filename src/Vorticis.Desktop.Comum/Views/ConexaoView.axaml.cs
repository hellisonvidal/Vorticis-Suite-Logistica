using Avalonia.Controls;
using Avalonia.Interactivity;
using Vorticis.Desktop.Comum.ViewModels;

namespace Vorticis.Desktop.Comum.Views;

public partial class ConexaoView : UserControl
{
    public ConexaoView() => InitializeComponent();

    private async void CopiarDiagnostico_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ConexaoViewModel vm && TopLevel.GetTopLevel(this)?.Clipboard is { } area)
        {
            await area.SetTextAsync(vm.Diagnostico);
            vm.Mensagem = "Diagnóstico copiado para a área de transferência.";
        }
    }
}
