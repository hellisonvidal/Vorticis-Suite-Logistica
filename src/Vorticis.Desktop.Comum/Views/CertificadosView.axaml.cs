using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Vorticis.Desktop.Comum.ViewModels;

namespace Vorticis.Desktop.Comum.Views;

public partial class CertificadosView : UserControl
{
    private static readonly FilePickerFileType Pfx = new("Certificado A1 (.pfx, .p12)") { Patterns = ["*.pfx", "*.p12", "*.PFX", "*.P12"] };

    public CertificadosView() => InitializeComponent();

    private async void AoEscolherArquivo(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CertificadosViewModel vm && await EscolhaArquivo.AbrirAsync(this, "Certificado digital A1", Pfx) is { } c) vm.Arquivo = c;
    }
}
