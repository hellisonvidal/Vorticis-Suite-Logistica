using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Vorticis.Desktop.ViewModels;

namespace Vorticis.Desktop.Views;

public partial class EcfAssistenteView : UserControl
{
    public EcfAssistenteView()
    {
        InitializeComponent();
        this.FindControl<TextBlock>("Instrucoes")!.Text = EcfAssistenteViewModel.InstrucoesPva;
        this.FindControl<TextBlock>("Observacao")!.Text = EcfAssistenteViewModel.ObservacaoPva;
    }

    private async void ProcurarPfx(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: SignatarioAssinatura linha } controle) return;
        var caminho = await EscolhaArquivo.AbrirAsync(controle, "Certificado A1",
            new FilePickerFileType("Certificado A1 (.pfx, .p12)") { Patterns = ["*.pfx", "*.p12", "*.PFX", "*.P12"] });
        if (caminho is not null) linha.ArquivoPfx = caminho;
    }

    private async void EscolherPasta(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EcfAssistenteViewModel vm || TopLevel.GetTopLevel(this) is not { } top) return;
        var pastas = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Pasta para salvar a ECF", AllowMultiple = false });
        if (pastas.Count > 0 && pastas[0].TryGetLocalPath() is { } caminho) vm.Pasta = caminho;
    }
}
