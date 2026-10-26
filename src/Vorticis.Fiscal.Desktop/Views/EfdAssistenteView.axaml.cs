using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Vorticis.Desktop.Comum.Views;
using Vorticis.Fiscal.Desktop.ViewModels;

namespace Vorticis.Fiscal.Desktop.Views;

public partial class EfdAssistenteView : UserControl
{
    public EfdAssistenteView()
    {
        InitializeComponent();
    }

    private async void ProcurarPfx(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EfdAssistenteViewModel vm || sender is not Control controle) return;
        var caminho = await EscolhaArquivo.AbrirAsync(controle, "Certificado A1",
            new FilePickerFileType("Certificado A1 (.pfx, .p12)") { Patterns = ["*.pfx", "*.p12", "*.PFX", "*.P12"] });
        if (caminho is not null) vm.ArquivoPfx = caminho;
    }

    private async void EscolherPasta(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EfdAssistenteViewModel vm || TopLevel.GetTopLevel(this) is not { } top) return;
        var pastas = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Pasta para salvar o arquivo", AllowMultiple = false });
        if (pastas.Count > 0 && pastas[0].TryGetLocalPath() is { } caminho) vm.Pasta = caminho;
    }
}
