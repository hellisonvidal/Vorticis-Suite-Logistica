using Vorticis.Desktop.Comum;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Vorticis.Fiscal.Desktop.ViewModels;

namespace Vorticis.Fiscal.Desktop.Views;

public partial class ImportacaoXmlView : UserControl
{
    public ImportacaoXmlView() => InitializeComponent();

    private async void EscolherArquivos(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ImportacaoXmlViewModel vm || TopLevel.GetTopLevel(this) is not { } top) return;
        try
        {
            var caminhos = await SelecaoXml.ArquivosAsync(top);
            if (caminhos.Count > 0) await vm.ImportarArquivosAsync(caminhos);
        }
        catch (Exception ex) { vm.Erros.Add(ErrosInesperados.Registrar(ex)); }   // nunca fecha o módulo
    }

    private async void EscolherPasta(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ImportacaoXmlViewModel vm || TopLevel.GetTopLevel(this) is not { } top) return;
        try { if (await SelecaoXml.PastaAsync(top) is { } caminho) vm.Pasta = caminho; }
        catch (Exception ex) { vm.Erros.Add(ErrosInesperados.Registrar(ex)); }
    }
}
