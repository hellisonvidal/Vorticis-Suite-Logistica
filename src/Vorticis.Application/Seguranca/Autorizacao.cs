using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Application.Seguranca;

public interface IRepositorioPermissoes
{
    /// <summary>Permissões do usuário na empresa: administrador (todas), as do perfil atribuído, ou nenhuma.</summary>
    Task<PermissoesEfetivas> ObterAsync(long usuarioId, long empresaId, CancellationToken ct);
}

/// <summary>
/// Verificação de permissão usada pelos serviços antes de qualquer operação — a interface também esconde
/// o que não é permitido, mas a regra vale aqui, independentemente da tela.
/// </summary>
public sealed class Autorizador(IRepositorioPermissoes repositorio)
{
    public Task<PermissoesEfetivas> ObterAsync(long usuarioId, long empresaId, CancellationToken ct = default)
        => repositorio.ObterAsync(usuarioId, empresaId, ct);

    /// <summary>Null se permitido; senão, o motivo da recusa.</summary>
    public async Task<string?> VerificarAsync(long usuarioId, long empresaId, Permissao permissao, CancellationToken ct = default)
    {
        var p = await repositorio.ObterAsync(usuarioId, empresaId, ct);
        if (p.Pode(permissao)) return null;
        return p.TemAcesso
            ? $"Seu perfil ({p.NomePerfil}) não permite: {permissao.Descricao()}."
            : "Você não tem acesso a esta empresa.";
    }
}

public static class AutorizadorExtensions
{
    /// <summary>Atalho para serviços que devolvem <see cref="Resultado{T}"/>.</summary>
    public static async Task<Resultado<T>?> NegarAsync<T>(this Autorizador a, long usuarioId, long empresaId, Permissao p, CancellationToken ct)
        => await a.VerificarAsync(usuarioId, empresaId, p, ct) is { } motivo ? Resultado<T>.Falha(motivo) : null;
}
