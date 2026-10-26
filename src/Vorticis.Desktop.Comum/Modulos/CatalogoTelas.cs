using Vorticis.Desktop.Comum.ViewModels;

namespace Vorticis.Desktop.Comum.Modulos;

/// <summary>Instância de ViewModel de uma função, criada pela janela principal e refeita ao fechar a função ou sair.</summary>
public sealed class DefinicaoInstancia
{
    public required string Nome { get; init; }
    public required Func<MainViewModelBase, object> Criar { get; init; }
    /// <summary>Instância criada uma só vez (não é refeita ao fechar a função nem ao trocar de usuário).</summary>
    public bool Fixa { get; init; }
    /// <summary>Instâncias que recomeçam junto quando esta é fechada (telas compostas por abas).</summary>
    public IReadOnlyList<string> ReiniciaJunto { get; init; } = [];
    public Action<object>? AtualizarPermissoes { get; init; }
    /// <summary>Executado de imediato ao trocar a empresa ativa (limpar o que depende da empresa).</summary>
    public Action<object>? AoLimpar { get; init; }
    /// <summary>Executado ao trocar a empresa ativa, depois de atualizadas as permissões; o argumento diz se a tela está ativa.</summary>
    public Func<object, bool, Task>? AoTrocarEmpresa { get; init; }
}

/// <summary>Função acessível pelo menu: a chave é o CommandParameter do item de menu.</summary>
public sealed class DefinicaoTela
{
    public required string Chave { get; init; }
    /// <summary>Nome da <see cref="DefinicaoInstancia"/> que a tela exibe (várias chaves podem exibir a mesma instância).</summary>
    public required string Instancia { get; init; }
    /// <summary>Título da função ("Cadastros › Empresas"); aparece na janela e na barra de funções abertas.</summary>
    public required string Titulo { get; init; }
    /// <summary>Permissão para abrir, além de estar logado e sem configuração inicial pendente.</summary>
    public required Func<MainViewModelBase, bool> Pode { get; init; }
    /// <summary>Carga ao abrir a tela (erros vão para a barra de status).</summary>
    public Func<object, Task>? AoAbrir { get; init; }
}

/// <summary>
/// Registro das telas de um executável. O casco registra as comuns; cada módulo registra as suas em
/// <see cref="IModuloDesktop.RegistrarTelas"/>. Uso:
/// <code>
/// catalogo.Instancia(nameof(Bens), m => new BensViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
///         .Tela("bens", "Imobilizado › Cadastro de bens", m => m.TemAcessoNaEmpresa, v => v.CarregarAsync());
/// </code>
/// </summary>
public sealed class CatalogoTelas
{
    private readonly List<DefinicaoInstancia> _instancias = [];
    private readonly Dictionary<string, DefinicaoTela> _telas = [];

    public IReadOnlyList<DefinicaoInstancia> Instancias => _instancias;
    public IReadOnlyDictionary<string, DefinicaoTela> Telas => _telas;

    /// <summary>
    /// Declara um ViewModel. O <paramref name="nome"/> deve ser o da propriedade tipada que o expõe na janela principal
    /// (o casco notifica essa propriedade quando a instância é refeita).
    /// </summary>
    public InstanciaBuilder<T> Instancia<T>(string nome, Func<MainViewModelBase, T> criar, bool fixa = false,
                                            string[]? reiniciaJunto = null, Action<T>? atualizarPermissoes = null,
                                            Action<T>? aoLimpar = null, Func<T, bool, Task>? aoTrocarEmpresa = null)
        where T : class
    {
        if (_instancias.Any(i => i.Nome == nome)) throw new InvalidOperationException($"Instância já registrada: {nome}.");
        _instancias.Add(new DefinicaoInstancia
        {
            Nome = nome,
            Criar = m => criar(m),
            Fixa = fixa,
            ReiniciaJunto = reiniciaJunto ?? [],
            AtualizarPermissoes = atualizarPermissoes is null ? null : o => atualizarPermissoes((T)o),
            AoLimpar = aoLimpar is null ? null : o => aoLimpar((T)o),
            AoTrocarEmpresa = aoTrocarEmpresa is null ? null : (o, ativa) => aoTrocarEmpresa((T)o, ativa),
        });
        return new InstanciaBuilder<T>(this, nome);
    }

    /// <summary>
    /// Traz as telas de outro catálogo (programa que junta vários módulos). Tela com chave já registrada fica com a primeira (os módulos
    /// compartilham telas, como as duplicatas do Fiscal no Gerencial); as chaves em <paramref name="renomear"/> (ex.: Parâmetros, que cada
    /// módulo tem o seu) entram como "prefixo:chave". Instâncias sem tela nem uso por outras são descartadas. Devolve as chaves renomeadas.
    /// </summary>
    public IReadOnlyDictionary<string, string> Incorporar(CatalogoTelas outro, string prefixo, IReadOnlySet<string> renomear)
    {
        var chaves = new Dictionary<string, string>();
        var telas = new List<DefinicaoTela>();
        foreach (var t in outro._telas.Values)
        {
            if (!_telas.ContainsKey(t.Chave)) telas.Add(t);
            else if (renomear.Contains(t.Chave)) telas.Add(Copia(t, chaves[t.Chave] = $"{prefixo}:{t.Chave}"));
        }
        var usadas = telas.Select(t => t.Instancia).ToHashSet();
        foreach (var i in outro._instancias.Where(i => usadas.Contains(i.Nome)).ToList())
            foreach (var n in i.ReiniciaJunto) usadas.Add(n);
        var nomes = outro._instancias.Where(i => usadas.Contains(i.Nome))
                         .ToDictionary(i => i.Nome, i => _instancias.Any(x => x.Nome == i.Nome) ? $"{prefixo}.{i.Nome}" : i.Nome);
        foreach (var i in outro._instancias.Where(i => nomes.ContainsKey(i.Nome)))
            _instancias.Add(new DefinicaoInstancia
            {
                Nome = nomes[i.Nome], Criar = i.Criar, Fixa = i.Fixa, ReiniciaJunto = [.. i.ReiniciaJunto.Select(n => nomes.GetValueOrDefault(n, n))],
                AtualizarPermissoes = i.AtualizarPermissoes, AoLimpar = i.AoLimpar, AoTrocarEmpresa = i.AoTrocarEmpresa,
            });
        foreach (var t in telas)
        {
            var c = Copia(t, chaves.GetValueOrDefault(t.Chave, t.Chave));
            _telas.Add(c.Chave, new DefinicaoTela { Chave = c.Chave, Instancia = nomes[t.Instancia], Titulo = c.Titulo, Pode = c.Pode, AoAbrir = c.AoAbrir });
        }
        return chaves;

        static DefinicaoTela Copia(DefinicaoTela t, string chave) =>
            new() { Chave = chave, Instancia = t.Instancia, Titulo = t.Titulo, Pode = t.Pode, AoAbrir = t.AoAbrir };
    }

    internal void AdicionarTela(DefinicaoTela tela)
    {
        if (!_telas.TryAdd(tela.Chave, tela)) throw new InvalidOperationException($"Tela já registrada: {tela.Chave}.");
    }

    public sealed class InstanciaBuilder<T>(CatalogoTelas catalogo, string instancia) where T : class
    {
        /// <summary>Registra uma chave de tela para esta instância. Sem <paramref name="pode"/>, basta estar logado.</summary>
        public InstanciaBuilder<T> Tela(string chave, string titulo, Func<MainViewModelBase, bool>? pode = null,
                                        Func<T, Task>? aoAbrir = null)
        {
            catalogo.AdicionarTela(new DefinicaoTela
            {
                Chave = chave,
                Instancia = instancia,
                Titulo = titulo,
                Pode = pode ?? (_ => true),
                AoAbrir = aoAbrir is null ? null : o => aoAbrir((T)o),
            });
            return this;
        }
    }
}
