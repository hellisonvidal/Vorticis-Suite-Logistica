namespace Vorticis.Desktop.Comum.Modulos;

/// <summary>
/// Item do menu principal: abre a tela de <see cref="Chave"/> (via comando Abrir da janela principal) ou agrupa
/// <see cref="Filhos"/>. Use "_" antes da letra de acesso rápido, como no restante do menu ("_Cadastros").
/// </summary>
public sealed record ItemMenu(string Titulo, string? Chave = null, string? Atalho = null, bool Habilitado = true,
                              IReadOnlyList<ItemMenu>? Filhos = null, string? Recurso = null)
{
    public const string TituloSeparador = "-";

    public bool EhSeparador => Titulo == TituloSeparador;

    public static ItemMenu Separador { get; } = new(TituloSeparador);

    /// <summary>Item que abre a tela da chave; <paramref name="atalho"/> é só indicativo (ex.: "Ctrl+L").</summary>
    public static ItemMenu Tela(string titulo, string chave, string? atalho = null) => new(titulo, chave, atalho);

    public static ItemMenu Submenu(string titulo, params ItemMenu[] filhos) => new(titulo, Filhos: filhos);

    /// <summary>Item desabilitado de função ainda não implementada.</summary>
    public static ItemMenu EmBreve(string titulo) => new($"{titulo} (em breve)", Habilitado: false);

    /// <summary>O item só aparece quando o recurso está ligado nos parâmetros da empresa ativa (<see cref="IModuloDesktop.RecursosAtivosAsync"/>).</summary>
    public ItemMenu SoCom(string recurso) => this with { Recurso = recurso };
}

/// <summary>
/// Atalho de teclado do módulo. Com <see cref="Chave"/>, abre a tela; sem chave, é só documentação de um atalho
/// tratado pela própria tela (aparece na tela Sobre). <see cref="Gesto"/> usa a sintaxe do Avalonia ("Ctrl+L").
/// </summary>
public sealed record AtalhoTela(string Gesto, string? Chave, string Descricao);
