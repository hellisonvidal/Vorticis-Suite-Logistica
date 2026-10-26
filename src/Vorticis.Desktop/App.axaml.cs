using Avalonia.Markup.Xaml;
using Vorticis.Desktop.ViewModels;

namespace Vorticis.Desktop;

public partial class App : AppBase
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    protected override MainViewModelBase CriarPrincipal() => new MainViewModel();
}
