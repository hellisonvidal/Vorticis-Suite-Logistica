using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Vorticis.Desktop.Comum.ViewModels;

namespace Vorticis.Desktop.Comum.Views;

public partial class BackupView : UserControl
{
    public BackupView() => InitializeComponent();

    /// <summary>Escolhe a pasta onde salvar os backups; depois é só clicar em "Usar esta pasta".</summary>
    private async void EscolherPasta(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BackupViewModel vm || TopLevel.GetTopLevel(this) is not { } top) return;
        var pastas = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Pasta para os backups", AllowMultiple = false });
        if (pastas.Count > 0 && pastas[0].TryGetLocalPath() is { } caminho)
        {
            vm.Pasta = caminho;
            await vm.SalvarPastaCommand.ExecuteAsync(null);
        }
    }
}
