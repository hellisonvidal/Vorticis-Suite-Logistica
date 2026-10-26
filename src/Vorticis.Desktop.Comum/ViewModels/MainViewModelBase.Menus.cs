using Vorticis.Application.Sistema;
using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Domain.Comum;

namespace Vorticis.Desktop.Comum.ViewModels;

/// <summary>Item da árvore da aba Menus: o caminho (títulos do menu até o item) identifica o item nas preferências.</summary>
public sealed record NoMenu(string Caminho, string Titulo, string? Chave, bool Essencial, IReadOnlyList<NoMenu> Filhos);

public abstract partial class MainViewModelBase
{
    /// <summary>Recurso (<see cref="ItemMenu.SoCom"/>) ligado quando a empresa ativa contabiliza em lote.</summary>
    public const string RecursoContabilizacaoLote = "contabilizacao.lote";

    private PreferenciasMenu _preferenciasMenu = PreferenciasMenu.Vazia;
    private IReadOnlyList<ItemMenu>? _menusVisiveis;
    private IReadOnlySet<string> _chavesBloqueadas = new HashSet<string>();
    private IReadOnlySet<string>? _caminhosEssenciais;

    /// <summary>O menu mudou (o usuário escondeu ou mostrou itens): a janela refaz a barra de menus.</summary>
    public event EventHandler? MenusAlterados;

    /// <summary>Itens ocultos do usuário e os que o administrador ocultou para todos.</summary>
    public PreferenciasMenu PreferenciasDeMenu => _preferenciasMenu;

    /// <summary>Menu sem os itens que o usuário (ou o administrador, para todos) escondeu. Telas escondidas ficam bloqueadas também por atalho e chamada direta.</summary>
    public IReadOnlyList<ItemMenu> MenusVisiveis => _menusVisiveis ??= Menus;

    /// <summary>Telas cujo único acesso no menu foi escondido.</summary>
    public bool TelaBloqueadaPorMenu(string? tela) => tela is not null && _chavesBloqueadas.Contains(tela);

    private static string Limpo(string titulo) => titulo.Replace("_", "").Trim();

    private static string Caminho(string? pai, ItemMenu item) => pai is null ? Limpo(item.Titulo) : $"{pai} › {Limpo(item.Titulo)}";

    /// <summary>Telas que nunca podem ser escondidas: Parâmetros (onde se desfaz a escolha), Sair, Ajuda e a tela inicial.</summary>
    public static bool ChaveEssencial(string? chave) => chave is not null
        && (chave is TelasComuns.Parametros or TelasComuns.Sair or TelasComuns.Manual or TelasComuns.ManualContextual or TelasComuns.Inicio
                or TelasComuns.Sobre or TelasComuns.Licenca
            || chave.EndsWith(":" + TelasComuns.Parametros, StringComparison.Ordinal));

    /// <summary>Árvore de todos os menus do programa, com os itens essenciais marcados (um submenu com item essencial também é essencial).</summary>
    public IReadOnlyList<NoMenu> ArvoreDeMenus() => Montar(Menus, null);

    private static List<NoMenu> Montar(IEnumerable<ItemMenu> itens, string? pai)
    {
        var res = new List<NoMenu>();
        foreach (var i in itens.Where(i => !i.EhSeparador))
        {
            var caminho = Caminho(pai, i);
            var filhos = i.Filhos is null ? [] : Montar(i.Filhos, caminho);
            res.Add(new NoMenu(caminho, Limpo(i.Titulo), i.Chave, ChaveEssencial(i.Chave) || filhos.Any(f => f.Essencial), filhos));
        }
        return res;
    }

    private IReadOnlySet<string> CaminhosEssenciais()
    {
        if (_caminhosEssenciais is not null) return _caminhosEssenciais;
        var set = new HashSet<string>();
        void Coletar(IEnumerable<NoMenu> nos) { foreach (var n in nos) { if (n.Essencial) set.Add(n.Caminho); Coletar(n.Filhos); } }
        Coletar(ArvoreDeMenus());
        return _caminhosEssenciais = set;
    }

    private static List<ItemMenu> Filtrar(IEnumerable<ItemMenu> itens, string? pai, IReadOnlySet<string> ocultos, IReadOnlySet<string> essenciais, List<ItemMenu> removidos)
    {
        var res = new List<ItemMenu>();
        foreach (var i in itens)
        {
            if (i.EhSeparador) { res.Add(i); continue; }
            var caminho = Caminho(pai, i);
            if (ocultos.Contains(caminho) && !essenciais.Contains(caminho)) { removidos.Add(i); continue; }
            if (i.Filhos is null) { res.Add(i); continue; }
            var filhos = Filtrar(i.Filhos, caminho, ocultos, essenciais, removidos);
            // separadores sobrando no começo, no fim ou em duplicidade
            var limpos = new List<ItemMenu>();
            foreach (var f in filhos)
                if (!f.EhSeparador || (limpos.Count > 0 && !limpos[^1].EhSeparador)) limpos.Add(f);
            while (limpos.Count > 0 && limpos[^1].EhSeparador) limpos.RemoveAt(limpos.Count - 1);
            if (limpos.Count == 0 && i.Filhos.Any(f => !f.EhSeparador)) { removidos.Add(i); continue; }   // submenu que ficou vazio
            res.Add(i with { Filhos = limpos });
        }
        return res;
    }

    private static IEnumerable<string> Chaves(IEnumerable<ItemMenu> itens) =>
        itens.SelectMany(i => (i.Chave is null ? [] : new[] { i.Chave }).Concat(i.Filhos is null ? [] : Chaves(i.Filhos)));

    /// <summary>Aplica as preferências de menu: refaz o menu visível, bloqueia as telas escondidas e fecha as que estavam abertas.</summary>
    public void AplicarPreferenciasMenu(PreferenciasMenu preferencias)
    {
        _preferenciasMenu = preferencias;
        var removidos = new List<ItemMenu>();
        _menusVisiveis = preferencias.Ocultos.Count == 0 ? Menus : Filtrar(Menus, null, preferencias.Ocultos, CaminhosEssenciais(), removidos);
        var visiveis = Chaves(_menusVisiveis).ToHashSet();
        _chavesBloqueadas = Chaves(removidos).Where(c => !visiveis.Contains(c) && !ChaveEssencial(c)).ToHashSet();
        AbrirCommand.NotifyCanExecuteChanged();
        foreach (var f in JanelasAbertas.Where(f => TelaBloqueadaPorMenu(f.Tela)).ToList()) FecharFuncao(f);
        if (Logado && _telaAtual is not null && TelaBloqueadaPorMenu(_telaAtual)) AbrirPrimeiraPermitida();
        MenusAlterados?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Lê do banco o que o usuário escondeu (e o que o administrador escondeu para todos).</summary>
    public async Task CarregarPreferenciasMenuAsync()
    {
        var prefs = PreferenciasMenu.Vazia;
        try
        {
            if (App is not null && Sessao is { Implantacao: false } sessao) prefs = await App.PreferenciasMenu.ObterAsync(Modulo.NomeExecutavel, sessao.Id);
        }
        catch (Exception) { /* sem preferências: o menu completo */ }
        AplicarPreferenciasMenu(prefs);
    }

    /// <summary>
    /// Grava os itens escondidos. Por padrão vale para o usuário; <paramref name="paraTodos"/> (só administrador) vale para todos os usuários do programa.
    /// </summary>
    public async Task<Resultado> SalvarPreferenciasMenuAsync(IEnumerable<string> ocultos, bool paraTodos)
    {
        if (App is null || Sessao is null) return Resultado.Falha("Sessão encerrada. Entre novamente.");
        var r = await App.PreferenciasMenu.GravarAsync(Sessao, Modulo.NomeExecutavel, ocultos, CaminhosEssenciais(), paraTodos);
        if (r.Sucesso) await CarregarPreferenciasMenuAsync();
        return r;
    }

    /// <summary>Mensagem da barra de status quando a função foi escondida do menu.</summary>
    private void AvisarTelaOculta(string tela)
    {
        var titulo = _catalogo.Telas.TryGetValue(tela, out var def) && !string.IsNullOrWhiteSpace(def.Titulo) ? def.Titulo : tela;
        Status = $"A função \"{titulo}\" está oculta nas suas preferências de menu. Para usá-la, marque-a de novo em Cadastros › Parâmetros › Menus.";
    }

    /// <summary>Atalho de teclado ou chamada direta: abre a tela ou, se ela foi escondida do menu, avisa na barra de status.</summary>
    public void AbrirPorAtalho(string tela)
    {
        if (AbrirCommand.CanExecute(tela)) AbrirCommand.Execute(tela);
        else if (Logado && TelaBloqueadaPorMenu(tela)) AvisarTelaOculta(tela);
    }

    /// <summary>A ViewModel da tela registrada com a chave (a instância viva da janela principal), ou null.</summary>
    public object? ViewModelDaTela(string chave)
        => _catalogo.Telas.TryGetValue(chave, out var def) && _instancias.TryGetValue(def.Instancia, out var vm) ? vm : null;
}
