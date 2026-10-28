using Avalonia.Controls;
using Avalonia.Input;
using Vorticis.Frota.Desktop.ViewModels;

namespace Vorticis.Frota.Desktop.Views;

/// <summary>Lista dos CT-e; Novo CT-e e Abrir (duplo clique ou Enter) editam o CT-e na janela pop-up <see cref="CteEdicaoView"/>.</summary>
public partial class CtesView : UserControl
{
    public PopupEdicao Popup { get; }

    public CtesView()
    {
        InitializeComponent();
        Popup = new PopupEdicao(this, () => new CteEdicaoView(), 1000, 820);
    }

    private void AoDuploClique(object? sender, TappedEventArgs e)
    {
        if (DataContext is CtesViewModel vm && (e.Source as Control)?.DataContext is CteLinha) vm.AbrirCommand.Execute(null);
    }

    private void AoTeclar(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not CtesViewModel vm) return;
        e.Handled = true;
        vm.AbrirCommand.Execute(null);
    }
}
