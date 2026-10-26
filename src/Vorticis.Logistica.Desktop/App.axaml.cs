using Avalonia.Markup.Xaml;

namespace Vorticis.Logistica.Desktop;

/// <summary>Vorticis Suite Logística: um programa só, com as telas de Frota, Gerencial e Contábil num menu único.</summary>
public partial class App : AppBase
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        HospedeiroSuite.IncluirTelas(this, ModuloLogistica.Apps());
    }

    protected override MainViewModelBase CriarPrincipal() => new MainViewModel();
}
