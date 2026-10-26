using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Desktop.Comum.Views;
using Vorticis.Domain.Comum;

namespace Vorticis.Desktop.Comum;

/// <summary>
/// Aplicação Avalonia de qualquer módulo da suíte. A classe App de cada executável herda desta (XAML com o tema
/// comum e as telas do módulo) e informa só a janela principal do módulo em <see cref="CriarPrincipal"/>.
/// </summary>
public abstract class AppBase : Avalonia.Application
{
    /// <summary>ViewModel da janela principal, construído com a <see cref="IModuloDesktop"/> do executável.</summary>
    protected abstract MainViewModelBase CriarPrincipal();

    public override void OnFrameworkInitializationCompleted()
    {
        CultureInfo.DefaultThreadCurrentCulture = Formato.PtBr;
        CultureInfo.DefaultThreadCurrentUICulture = Formato.PtBr;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = CriarPrincipal();
            ErrosInesperados.Instalar(vm.MostrarErroInesperado);   // erro não previsto não fecha o módulo
            desktop.MainWindow = new MainWindow { DataContext = vm };
            desktop.ShutdownRequested += async (_, _) => await vm.DisposeAsync();
            _ = vm.IniciarAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>Ponto de entrada comum dos executáveis: <c>Main</c> e <c>BuildAvaloniaApp</c> de cada Program.cs.</summary>
public static class AplicacaoDesktop
{
    public static void Executar<TApp>(string[] args) where TApp : Avalonia.Application, new()
    {
        Configuracao.MigrarPastasDoNomeAnterior();
        try
        {
            // Instalador: configura o banco sem abrir janela (código de saída 0 = pronto, 1 = falhou; log em
            // %LOCALAPPDATA%\Vorticis\logs\configuracao-banco.log).
            if (ConfiguracaoAutomatica.Solicitada(args))
            {
                Environment.ExitCode = ConfiguracaoAutomatica.ExecutarAsync(args).GetAwaiter().GetResult();
                return;
            }
            Configurar<TApp>().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            BancoDoAplicativo.Encerrar();
        }
    }

    public static AppBuilder Configurar<TApp>() where TApp : Avalonia.Application, new() =>
        AppBuilder.Configure<TApp>().UsePlatformDetect().WithInterFont().LogToTrace();
}
