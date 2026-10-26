using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Application.Sistema;

/// <summary>Itens de menu ocultos de um programa: os do próprio usuário e os que o administrador definiu para todos.</summary>
public sealed record PreferenciasMenu(IReadOnlySet<string> DoUsuario, IReadOnlySet<string> DeTodos)
{
    public static PreferenciasMenu Vazia { get; } = new(new HashSet<string>(), new HashSet<string>());

    /// <summary>Itens ocultos para o usuário: a regra de todos mais a escolha dele.</summary>
    public IReadOnlySet<string> Ocultos => new HashSet<string>(DoUsuario.Concat(DeTodos));
}

public interface IRepositorioPreferenciasMenu
{
    Task<PreferenciasMenu> ObterAsync(string programa, long usuarioId, CancellationToken ct);
    /// <summary>Troca os itens ocultos do usuário (<paramref name="usuarioId"/> nulo = regra de todos).</summary>
    Task SubstituirAsync(string programa, long? usuarioId, IReadOnlyCollection<string> ocultos, CancellationToken ct);
}

/// <summary>
/// Preferências de menu (Cadastros › Parâmetros › aba Menus): cada usuário esconde os itens que não usa; o administrador pode
/// aplicar a escolha para todos. Os itens essenciais nunca ficam ocultos (quem chama informa quais são).
/// </summary>
public sealed class ServicoPreferenciasMenu(IRepositorioPreferenciasMenu repositorio)
{
    public Task<PreferenciasMenu> ObterAsync(string programa, long usuarioId, CancellationToken ct = default)
        => repositorio.ObterAsync(programa, usuarioId, ct);

    /// <summary>
    /// Grava os itens ocultos. Para todos os usuários, só o administrador; nesse caso a escolha pessoal do administrador é mantida.
    /// <paramref name="essenciais"/> são ignorados (não podem ser ocultados).
    /// </summary>
    public async Task<Resultado> GravarAsync(SessaoUsuario sessao, string programa, IEnumerable<string> ocultos, IReadOnlySet<string> essenciais, bool paraTodos,
                                             CancellationToken ct = default)
    {
        if (paraTodos && !sessao.Administrador) return Resultado.Falha("Somente administradores aplicam as preferências de menu para todos os usuários.");
        var lista = ocultos.Where(c => !string.IsNullOrWhiteSpace(c) && !essenciais.Contains(c)).Distinct().ToList();
        await repositorio.SubstituirAsync(programa, paraTodos ? null : sessao.Id, lista, ct);
        return Resultado.Ok();
    }
}
