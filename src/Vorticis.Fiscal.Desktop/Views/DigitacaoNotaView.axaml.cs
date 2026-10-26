using Avalonia.Controls;
using Avalonia.Interactivity;
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
}
