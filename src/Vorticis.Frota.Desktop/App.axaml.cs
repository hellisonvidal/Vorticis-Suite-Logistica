using Avalonia.Markup.Xaml;

namespace Vorticis.Frota.Desktop;

public partial class App : AppBase
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    protected override MainViewModelBase CriarPrincipal() => new MainViewModel();
}
