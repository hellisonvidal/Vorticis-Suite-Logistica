using System.Globalization;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Desktop.Comum.Views;
using Vorticis.Domain.Comum;

namespace Vorticis.Desktop.Comum;

/// <summary>
/// Base dos produtos que reúnem módulos numa janela só (Vorticis Suite Empresarial, Vorticis Suite Logística): cada um diz o nome, os módulos que
/// hospeda e quem faz o login; aqui ficam a escolha dos módulos instalados, as telas de cada módulo e a abertura da janela.
/// </summary>
public abstract class AppSuiteBase : Avalonia.Application
{
    /// <summary>Nome do produto (título da janela).</summary>
    protected abstract string NomeSuite { get; }

    /// <summary>Executável do módulo que conecta e faz o login (null = o que administra a suíte, senão o primeiro em ordem alfabética).</summary>
    protected virtual string? Principal => null;

    /// <summary>Módulos hospedados: executável (sem extensão) e como criar o módulo.</summary>
    protected abstract IReadOnlyList<(string Executavel, Func<MainViewModelBase> Criar)> Modulos { get; }

    /// <summary>App de cada módulo, de onde vêm as telas (DataTemplates) — cada tela continua declarada num lugar só.</summary>
    protected abstract IEnumerable<Avalonia.Application> AppsDosModulos();

    public override void Initialize()
    {
        CarregarXaml();
        HospedeiroSuite.IncluirTelas(this, AppsDosModulos());
    }

    /// <summary>AvaloniaXamlLoader.Load(this) do App.axaml do produto.</summary>
    protected abstract void CarregarXaml();

    public SuiteViewModel CriarSuite(string? pasta = null) =>
        new(HospedeiroSuite.Criar(Modulos, pasta), nome: NomeSuite, principal: Principal);

    public override void OnFrameworkInitializationCompleted()
    {
        CultureInfo.DefaultThreadCurrentCulture = Formato.PtBr;
        CultureInfo.DefaultThreadCurrentUICulture = Formato.PtBr;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var suite = CriarSuite();
            ErrosInesperados.Instalar(m => suite.Ativo.Vm.MostrarErroInesperado(m));   // erro não previsto não fecha a Suite
            var janela = new MainWindow();
            janela.UsarSuite(suite);
            desktop.MainWindow = janela;
            desktop.ShutdownRequested += async (_, _) =>
            {
                foreach (var m in suite.Modulos.Reverse()) await m.Vm.DisposeAsync();   // o principal (dono da conexão) por último
            };
            _ = suite.Principal.IniciarAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>Escolha dos módulos instalados e inclusão das telas de cada módulo na Suite.</summary>
public static class HospedeiroSuite
{
    /// <summary>Executáveis dos módulos instalados na pasta (nomes sem extensão).</summary>
    public static IReadOnlyList<string> Instalados(string pasta, IEnumerable<string> executaveis) =>
        [.. executaveis.Where(e => File.Exists(Path.Combine(pasta, e + ".exe")) || File.Exists(Path.Combine(pasta, e)))];

    /// <summary>Os módulos cujo executável está na pasta; sem nenhum (pasta de desenvolvimento), entram todos.</summary>
    public static IReadOnlyList<MainViewModelBase> Criar(IReadOnlyList<(string Executavel, Func<MainViewModelBase> Criar)> todos, string? pasta = null)
    {
        var instalados = Instalados(pasta ?? AppContext.BaseDirectory, todos.Select(t => t.Executavel));
        return [.. todos.Where(t => instalados.Count == 0 || instalados.Contains(t.Executavel)).Select(t => t.Criar())];
    }

    /// <summary>Leva para o destino as telas (DataTemplates) de cada módulo, carregando o App.axaml de cada executável.</summary>
    public static void IncluirTelas(Avalonia.Application destino, IEnumerable<Avalonia.Application> modulos)
    {
        foreach (var app in modulos)
        {
            app.Initialize();
            foreach (var t in app.DataTemplates.Where(t => t is not TemplatesComuns).ToList())
            {
                app.DataTemplates.Remove(t);
                destino.DataTemplates.Add(t);
            }
        }
    }
}
