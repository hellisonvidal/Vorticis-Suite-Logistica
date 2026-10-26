using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Vorticis.Desktop.ViewModels;

namespace Vorticis.Desktop.Views;

public partial class ImportacaoReferencialView : UserControl
{
    public ImportacaoReferencialView() => InitializeComponent();

    private async void Procurar(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ImportacaoReferencialViewModel vm || TopLevel.GetTopLevel(this) is not { } top) return;
        var arquivos = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Tabela do plano referencial (RFB)",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Texto ou CSV") { Patterns = ["*.txt", "*.csv"] }, FilePickerFileTypes.All],
        });
        if (arquivos.Count > 0 && arquivos[0].TryGetLocalPath() is { } caminho) vm.Arquivo = caminho;
    }
}
