using Avalonia.Markup.Xaml;

namespace Vorticis.Logistica.Desktop;

/// <summary>
/// Vorticis Suite Logística: os módulos da transportadora (Frotas, Transporte com o faturamento, Manutenção, Financeiro, Gerencial e Contabilidade)
/// na mesma janela, com um login só e a navegação entre módulos no layout escolhido pelo usuário.
/// </summary>
public partial class App : AppSuiteBase
{
    protected override string NomeSuite => ModuloLogistica.NomeSuite;

    protected override string? Principal => new ModuloFrotaLogistica().NomeExecutavel;

    /// <summary>Todos os módulos estão no mesmo executável (não há um .exe por módulo para conferir).</summary>
    protected override bool SoInstalados => false;

    /// <summary>Na ordem do trabalho da transportadora, não em ordem alfabética.</summary>
    protected override bool OrdemAlfabetica => false;

    protected override IReadOnlyList<(string Executavel, Func<MainViewModelBase> Criar)> Modulos =>
        [.. ModuloLogistica.Todos().Select(m => (m.NomeExecutavel, (Func<MainViewModelBase>)(() => new MainViewModel(m))))];

    protected override IEnumerable<Avalonia.Application> AppsDosModulos() => ModuloLogistica.Apps();

    protected override void CarregarXaml() => AvaloniaXamlLoader.Load(this);
}
