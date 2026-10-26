using Avalonia.Controls;
using Avalonia.Interactivity;
using Vorticis.Fiscal.Desktop.ViewModels;

namespace Vorticis.Fiscal.Desktop.Views;

public partial class PainelImportacaoView : UserControl
{
    public PainelImportacaoView() => InitializeComponent();

    private async void ExportarResumo(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ImportacaoXmlViewModel vm || TopLevel.GetTopLevel(this) is not { } top) return;
        try
        {
            if (await SelecaoXml.ExportarResumoAsync(top, vm) is { } caminho) vm.Mensagem = $"Resumo exportado em {caminho}.";
        }
        catch (Exception ex) { vm.Erros.Add($"Não foi possível exportar o resumo: {ex.Message}"); }
    }
}
