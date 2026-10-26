using Vorticis.Desktop.Comum;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Vorticis.Fiscal.Desktop.ViewModels;

namespace Vorticis.Fiscal.Desktop.Views;

public partial class DigitacaoDocumentoView : UserControl
{
    public DigitacaoDocumentoView() => InitializeComponent();

    private async void ImportarArquivos(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DigitacaoDocumentoViewModel vm || TopLevel.GetTopLevel(this) is not { } top) return;
        try
        {
            var caminhos = await SelecaoXml.ArquivosAsync(top);
            if (caminhos.Count > 0) await vm.Importador.ImportarArquivosAsync(caminhos);
        }
        catch (Exception ex) { vm.Importador.Erros.Add(ErrosInesperados.Registrar(ex)); }   // nunca fecha o módulo
    }

    private async void ImportarPasta(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DigitacaoDocumentoViewModel vm || TopLevel.GetTopLevel(this) is not { } top) return;
        try { if (await SelecaoXml.PastaAsync(top) is { } pasta) await vm.Importador.ImportarPastaAsync(pasta, true); }
        catch (Exception ex) { vm.Importador.Erros.Add(ErrosInesperados.Registrar(ex)); }
    }

    /// <summary>Ao sair do campo do fornecedor/cliente, busca pelo CPF/CNPJ digitado (CNPJ completo já busca ao digitar).</summary>
    private void ParticipanteSaiu(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DigitacaoDocumentoViewModel vm && vm.BuscarParticipanteCommand.CanExecute(null)) vm.BuscarParticipanteCommand.Execute(null);
    }
}
