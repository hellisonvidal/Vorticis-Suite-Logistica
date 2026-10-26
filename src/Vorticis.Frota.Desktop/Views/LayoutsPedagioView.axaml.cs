using Avalonia.Controls;
using Avalonia.Interactivity;
using Vorticis.Desktop.Comum.Views;
using Vorticis.Frota.Desktop.ViewModels;

namespace Vorticis.Frota.Desktop.Views;

public partial class LayoutsPedagioView : UserControl
{
    public LayoutsPedagioView() => InitializeComponent();

    private async void CarregarExemplo(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LayoutsPedagioViewModel vm && await EscolhaArquivo.AbrirAsync(this, "Arquivo de pedágio da operadora (Excel ou CSV)", EscolhaArquivo.Planilha) is { } c) vm.CarregarExemplo(c);
    }
}
