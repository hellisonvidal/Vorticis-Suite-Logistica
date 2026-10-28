using Avalonia.Controls;
using Avalonia.Input;
using Vorticis.Frota.Desktop.ViewModels;

namespace Vorticis.Frota.Desktop.Views;

/// <summary>Lista dos manifestos; Novo manifesto e Abrir (duplo clique ou Enter) editam na janela pop-up <see cref="ManifestoEdicaoView"/>.</summary>
public partial class ManifestosView : UserControl
{
    public PopupEdicao Popup { get; }

    public ManifestosView()
    {
        InitializeComponent();
        Popup = new PopupEdicao(this, () => new ManifestoEdicaoView(), 760, 760);
    }

    private void AoDuploClique(object? sender, TappedEventArgs e)
    {
        if (DataContext is ManifestosViewModel vm && (e.Source as Control)?.DataContext is ManifestoLinha) vm.AbrirCommand.Execute(null);
    }

    private void AoTeclar(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not ManifestosViewModel vm) return;
        e.Handled = true;
        vm.AbrirCommand.Execute(null);
    }
}
