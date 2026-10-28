using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Vorticis.Frota.Desktop.ViewModels;

namespace Vorticis.Frota.Desktop.Views;

public partial class CtesView : UserControl
{
    public CtesView() => InitializeComponent();

    /// <summary>Escolhe um ou vários XML de NF-e e os anexa ao CT-e em edição.</summary>
    private async void AnexarXml(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CtesViewModel vm || TopLevel.GetTopLevel(this) is not { } top) return;
        var arquivos = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "XML das NF-e transportadas", AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("XML da NF-e") { Patterns = ["*.xml", "*.XML"] }, FilePickerFileTypes.All],
        });
        var lidos = new List<(string, byte[])>();
        foreach (var a in arquivos)
        {
            await using var fluxo = await a.OpenReadAsync();
            using var ms = new MemoryStream();
            await fluxo.CopyToAsync(ms);
            lidos.Add((a.Name, ms.ToArray()));
        }
        if (lidos.Count > 0) vm.AnexarXml(lidos);
    }
}
