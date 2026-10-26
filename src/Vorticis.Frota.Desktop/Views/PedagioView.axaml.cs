using Avalonia.Controls;
using Avalonia.Interactivity;
using Vorticis.Desktop.Comum.Views;
using Vorticis.Frota.Desktop.ViewModels;

namespace Vorticis.Frota.Desktop.Views;

public partial class PedagioView : UserControl
{
    public PedagioView() => InitializeComponent();

    private async void Importar(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PedagioViewModel vm && await EscolhaArquivo.AbrirAsync(this, "Arquivo de pedágio da operadora (Excel ou CSV)", EscolhaArquivo.Planilha) is { } c) await vm.ImportarAsync(c);
    }
}
