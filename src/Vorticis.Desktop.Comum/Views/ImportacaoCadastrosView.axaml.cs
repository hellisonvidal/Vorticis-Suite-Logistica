using Avalonia.Controls;
using Avalonia.Interactivity;
using Vorticis.Desktop.Comum.ViewModels;

namespace Vorticis.Desktop.Comum.Views;

public partial class ImportacaoCadastrosView : UserControl
{
    public ImportacaoCadastrosView() => InitializeComponent();

    private async void EscolherArquivo(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is ImportacaoCadastrosViewModel vm && await EscolhaArquivo.AbrirAsync(this, "Planilha para importar (Excel ou CSV)", EscolhaArquivo.Planilha) is { } c)
                await vm.CarregarArquivoAsync(c);
        }
        catch (Exception ex) { (DataContext as ImportacaoCadastrosViewModel)?.Erros.Add(ErrosInesperados.Registrar(ex)); }
    }
}
