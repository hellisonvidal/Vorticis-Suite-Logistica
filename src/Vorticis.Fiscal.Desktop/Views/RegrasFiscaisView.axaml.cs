using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Vorticis.Fiscal.Desktop.Views;

public partial class RegrasFiscaisView : UserControl
{
    public RegrasFiscaisView() => InitializeComponent();

    /// <summary>Botão "Plano de contas…": consulta o plano (o formulário pode estar no pop-up, por isso a janela vem do próprio botão).</summary>
    private async void EscolherConta(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.RegrasFiscaisViewModel vm && TopLevel.GetTopLevel(sender as Control) is Window dono) await vm.EscolherContaAsync(dono);
    }
}
