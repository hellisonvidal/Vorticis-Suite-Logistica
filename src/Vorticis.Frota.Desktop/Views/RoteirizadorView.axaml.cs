using Avalonia.Controls;
using Avalonia.Interactivity;
using Vorticis.Frota.Desktop.ViewModels;

namespace Vorticis.Frota.Desktop.Views;

public partial class RoteirizadorView : UserControl
{
    public RoteirizadorView() => InitializeComponent();

    /// <summary>Abre o roteiro no navegador (Google Maps traça o caminho pela estrada, na ordem das paradas).</summary>
    private async void AbrirGoogleMaps(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RoteirizadorViewModel vm || TopLevel.GetTopLevel(this) is not { } top) return;
        if (vm.Paradas.Count < 2) { vm.Erros.Clear(); vm.Erros.Add("Inclua a origem e ao menos um destino."); return; }
        await top.Launcher.LaunchUriAsync(new Uri(vm.UrlMapa));
    }
}
