using Vorticis.Desktop.Comum.ViewModels;

namespace Vorticis.Desktop.Comum.Modulos;

/// <summary>
/// Programa único que reúne as telas de vários módulos num só menu (o Vorticis Suite Logística: Frota, Gerencial, Contábil, Fiscal e Folha).
/// Cada parte registra as suas telas como sempre; telas repetidas entre as partes ficam uma só, e os Parâmetros de cada parte
/// ganham chave própria (<see cref="Chave"/>). O menu é montado pelo programa, com os menus das partes já traduzidos.
/// </summary>
public abstract class ModuloCombinado(params IModuloDesktop[] partes) : IModuloDesktop
{
    private readonly Dictionary<(IModuloDesktop, string), string> _renomeadas = [];

    /// <summary>Partes na ordem de registro: em telas repetidas vale a da primeira.</summary>
    protected IReadOnlyList<IModuloDesktop> Partes => partes;

    /// <summary>Chaves que cada parte tem a sua (as demais repetidas ficam com a primeira parte).</summary>
    protected virtual IReadOnlySet<string> ChavesPorParte { get; } = new HashSet<string> { TelasComuns.Parametros };

    public abstract string Nome { get; }
    public abstract Vorticis.Application.Cadastros.ModulosEmpresa ModuloEmpresa { get; }
    public abstract string NomeCurto { get; }
    public abstract string Subtitulo { get; }
    public abstract string NomeExecutavel { get; }
    public abstract string CorDestaque { get; }
    public abstract string CorBarra { get; }
    public abstract IReadOnlyList<ItemMenu> ItensCadastros { get; }
    public abstract IReadOnlyList<ItemMenu> Menus { get; }

    /// <summary>Atalhos das partes, sem repetir tecla (vale o da primeira parte).</summary>
    public virtual IReadOnlyList<AtalhoTela> Atalhos =>
        [.. partes.SelectMany(p => p.Atalhos.Select(a => a with { Chave = a.Chave is null ? null : Chave(p, a.Chave) })).DistinctBy(a => a.Gesto)];

    public Vorticis.Application.Cadastros.ModulosEmpresa ModulosAoCadastrar =>
        partes.Aggregate(Vorticis.Application.Cadastros.ModulosEmpresa.Nenhum, (acc, p) => acc | p.ModuloEmpresa);

    public IReadOnlyList<string> AreasDoManual => [NomeCurto, .. partes.Select(p => p.NomeCurto)];

    /// <summary>Recursos ligados em qualquer das partes (ex.: contabilidade gerencial do Contábil).</summary>
    public async Task<IReadOnlySet<string>> RecursosAtivosAsync(MainViewModelBase main)
    {
        var todos = new HashSet<string>();
        foreach (var p in partes) todos.UnionWith(await p.RecursosAtivosAsync(main));
        return todos;
    }

    public void PrepararServicos(Vorticis.Infra.VorticisApp app)
    {
        foreach (var p in partes) p.PrepararServicos(app);
    }

    public void RegistrarTelas(CatalogoTelas catalogo)
    {
        RegistrarTelasPrimeiro(catalogo);
        foreach (var p in partes)
        {
            var proprio = new CatalogoTelas();
            p.RegistrarTelas(proprio);
            foreach (var (de, para) in catalogo.Incorporar(proprio, p.NomeExecutavel, ChavesPorParte)) _renomeadas[(p, de)] = para;
        }
        RegistrarTelasProprias(catalogo);
    }

    /// <summary>
    /// Telas do programa registradas antes das partes: uma chave de <c>ChavesPorParte</c> (ex.: Parâmetros) registrada aqui é do programa
    /// (uma janela só) e todas as partes passam a ter a sua com chave própria (<see cref="Chave"/>).
    /// </summary>
    protected virtual void RegistrarTelasPrimeiro(CatalogoTelas catalogo) { }

    /// <summary>Telas que não são de nenhuma parte (as do próprio programa).</summary>
    protected virtual void RegistrarTelasProprias(CatalogoTelas catalogo) { }

    /// <summary>Chave, no programa, da tela <paramref name="chave"/> da parte (renomeada quando cada parte tem a sua).</summary>
    protected string Chave(IModuloDesktop parte, string chave) =>
        _renomeadas.TryGetValue((parte, chave), out var nova) ? nova : ChavesPorParte.Contains(chave) && !ReferenceEquals(parte, partes[0]) ? $"{parte.NomeExecutavel}:{chave}" : chave;

    /// <summary>Itens de menu da parte com as chaves traduzidas para as do programa.</summary>
    protected IReadOnlyList<ItemMenu> DaParte(IModuloDesktop parte, IEnumerable<ItemMenu> itens) =>
        [.. itens.Select(i => i with { Chave = i.Chave is null ? null : Chave(parte, i.Chave), Filhos = i.Filhos is null ? null : DaParte(parte, i.Filhos) })];

    /// <summary>Submenu com os menus de topo da parte (ex.: "_Contábil" › Lançamentos, Relatórios...).</summary>
    protected ItemMenu Submenu(string titulo, IModuloDesktop parte, Func<ItemMenu, bool>? filtro = null) =>
        new(titulo, Filhos: DaParte(parte, parte.Menus.Where(filtro ?? (_ => true))));
}
