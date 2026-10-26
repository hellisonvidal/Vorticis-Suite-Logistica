using Avalonia.Controls;
using Avalonia.Interactivity;
using Vorticis.Desktop.Comum;
using Vorticis.Fiscal.Desktop.ViewModels;

namespace Vorticis.Fiscal.Desktop.Views;

/// <summary>Lançamento da nota fiscal em abas, mostrado na janela pop-up aberta pela consulta (<see cref="DigitacaoDocumentoView"/>).</summary>
public partial class DigitacaoNotaView : UserControl
{
    public DigitacaoNotaView() => InitializeComponent();

    /// <summary>Ao sair do campo do fornecedor/cliente, busca pelo CPF/CNPJ digitado (CNPJ completo já busca ao digitar).</summary>
    private void ParticipanteSaiu(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DigitacaoDocumentoViewModel vm && vm.BuscarParticipanteCommand.CanExecute(null)) vm.BuscarParticipanteCommand.Execute(null);
    }

    /// <summary>Nota nova › Importar XML: importa o arquivo escolhido e abre a nota importada neste lançamento.</summary>
    private async void ImportarXml(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DigitacaoDocumentoViewModel vm || TopLevel.GetTopLevel(this) is not { } top) return;
        try
        {
            var caminhos = await SelecaoXml.ArquivosAsync(top);
            if (caminhos.Count > 0) await vm.Importador.ImportarArquivosAsync(caminhos);
        }
        catch (Exception ex) { vm.Importador.Erros.Add(ErrosInesperados.Registrar(ex)); }   // nunca fecha o módulo
    }
}
