using Avalonia.Controls;
using Avalonia.Interactivity;
using Vorticis.Desktop.ViewModels;

namespace Vorticis.Desktop.Views;

public partial class ImportacaoExtratoView : UserControl
{
    public ImportacaoExtratoView() => InitializeComponent();

    private async void ProcurarOfx(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ImportacaoExtratoViewModel vm && await EscolhaArquivo.AbrirAsync(this, "Extrato bancário (OFX, PDF, TXT ou CSV)", EscolhaArquivo.Extrato) is { } c)
            vm.Arquivo = c;
    }
}
