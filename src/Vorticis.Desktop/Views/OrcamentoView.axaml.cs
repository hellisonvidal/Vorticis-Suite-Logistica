using Avalonia.Controls;
using Avalonia.Interactivity;
using Vorticis.Desktop.Comum.Views;
using Vorticis.Desktop.ViewModels;

namespace Vorticis.Desktop.Views;

public partial class OrcamentoView : UserControl
{
    public OrcamentoView()
    {
        InitializeComponent();
        Grade.CellEditEnded += (_, e) =>
        {
            if (DataContext is OrcamentoViewModel vm && e.Row.DataContext is LinhaOrcamentoEdicao l) vm.ConferirLinha(l);
        };
    }

    private async void Procurar(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not OrcamentoViewModel vm) return;
        if (await EscolhaArquivo.AbrirAsync(this, "Planilha do orçamento", EscolhaArquivo.Planilha) is { } caminho) vm.Arquivo = caminho;
    }
}
