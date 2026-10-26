using CommunityToolkit.Mvvm.ComponentModel;

namespace Vorticis.Desktop.Comum.ViewModels;

/// <summary>Uma aba da janela Parâmetros: o conteúdo é a tela de parâmetros de um módulo ou a aba Menus.</summary>
public sealed record AbaParametros(string Titulo, object Conteudo);

/// <summary>
/// Cadastros › Parâmetros dos programas que reúnem vários módulos (Vorticis Suite Logística): uma janela só, com uma aba para cada módulo
/// (as mesmas telas de parâmetros e a mesma gravação de cada um) e a aba Menus, onde o usuário escolhe os itens de menu que quer ver.
/// </summary>
public sealed class ParametrosAbasViewModel(MainViewModelBase main, IReadOnlyList<(string Titulo, string Tela)> modulos) : ModuloViewModel(main), IAlteracoesPendentes
{
    private IReadOnlyList<AbaParametros>? _abas;
    private AbaParametros? _abaSelecionada;

    public string Titulo => "Parâmetros";

    public string Descricao => "Uma aba para cada área do programa. Em Menus, escolha os itens de menu que você quer ver.";

    /// <summary>As abas: os parâmetros de cada módulo, na ordem informada, e a aba Menus por último.</summary>
    public IReadOnlyList<AbaParametros> Abas => _abas ??=
    [
        .. modulos.Select(m => (m.Titulo, Vm: main.ViewModelDaTela(m.Tela))).Where(m => m.Vm is ParametrosViewModel).Select(m => new AbaParametros(m.Titulo, m.Vm!)),
        new AbaParametros("Menus", new PreferenciasMenuViewModel(main)),
    ];

    public AbaParametros? AbaSelecionada
    {
        get => _abaSelecionada ??= Abas[0];
        set => SetProperty(ref _abaSelecionada, value);
    }

    public IEnumerable<ParametrosViewModel> Modulos => Abas.Select(a => a.Conteudo).OfType<ParametrosViewModel>();

    public bool TemAlteracoesPendentes => Abas.Select(a => a.Conteudo).OfType<IAlteracoesPendentes>().Any(a => a.TemAlteracoesPendentes);

    public void AtualizarPermissoes()
    {
        foreach (var p in Modulos) p.AtualizarPermissoes();
    }

    /// <summary>Carrega todas as abas (ao abrir a janela e ao trocar de empresa).</summary>
    public async Task CarregarAsync()
    {
        foreach (var p in Modulos) await p.CarregarAsync();
        foreach (var m in Abas.Select(a => a.Conteudo).OfType<PreferenciasMenuViewModel>()) m.Carregar();
    }
}
