using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Application.Seguranca;

/// <param name="Administrador">Perfil Administrador: todas as funções, sempre; quem o recebe administra o sistema.</param>
public sealed record PerfilAcesso(long Id, string Nome, string? Descricao, bool Padrao, IReadOnlySet<Permissao> Permissoes, int Usuarios,
                                  bool Administrador = false);

public interface IRepositorioPerfis
{
    Task<IReadOnlyList<PerfilAcesso>> ListarAsync(CancellationToken ct);
    Task<bool> NomeEmUsoAsync(string nome, long? excetoId, CancellationToken ct);
    Task<long> CriarAsync(string nome, string? descricao, IReadOnlySet<Permissao> permissoes, CancellationToken ct);
    Task AtualizarAsync(long id, string nome, string? descricao, IReadOnlySet<Permissao> permissoes, CancellationToken ct);
    /// <summary>Lança <c>ViolacaoRegraException</c> se o perfil for padrão ou estiver em uso.</summary>
    Task ExcluirAsync(long id, CancellationToken ct);
    Task<bool> ConfiguracaoInicialConcluidaAsync(CancellationToken ct);
    Task ConcluirConfiguracaoInicialAsync(long administradorId, CancellationToken ct);
}

/// <summary>Configurações › Perfis de acesso. Somente administradores.</summary>
public sealed class ServicoPerfis(IRepositorioPerfis repositorio)
{
    public async Task<IReadOnlyList<PerfilAcesso>> ListarAsync(SessaoUsuario sessao, CancellationToken ct = default)
        => sessao.Administrador ? await repositorio.ListarAsync(ct) : [];

    public async Task<Resultado<long>> SalvarAsync(SessaoUsuario admin, long? id, string nome, string? descricao,
                                                  IEnumerable<Permissao> permissoes, CancellationToken ct = default)
    {
        if (!admin.Administrador) return Resultado<long>.Falha("Somente administradores definem perfis.");
        nome = nome.Trim();
        var erros = new List<string>();
        if (nome.Length is < 2 or > 60) erros.Add("O nome do perfil deve ter de 2 a 60 caracteres.");
        var conjunto = permissoes.Where(p => p != Permissao.AdministrarSistema).ToHashSet();
        if (id is long existente0 && (await repositorio.ListarAsync(ct)).FirstOrDefault(p => p.Id == existente0) is { Administrador: true })
            conjunto = Permissoes.Catalogo.Select(d => d.Permissao).ToHashSet();   // o perfil Administrador libera sempre tudo
        if (conjunto.Count == 0) erros.Add("Marque ao menos uma função para o perfil.");
        if (erros.Count == 0 && await repositorio.NomeEmUsoAsync(nome, id, ct)) erros.Add($"Já existe um perfil chamado \"{nome}\".");
        if (erros.Count > 0) return Resultado<long>.Falha(erros);

        var desc = string.IsNullOrWhiteSpace(descricao) ? null : descricao.Trim();
        if (id is long existente)
        {
            await repositorio.AtualizarAsync(existente, nome, desc, conjunto, ct);
            return Resultado<long>.Ok(existente);
        }
        return Resultado<long>.Ok(await repositorio.CriarAsync(nome, desc, conjunto, ct));
    }

    /// <summary>
    /// Primeiro uso: depois de criar o administrador, a definição dos perfis de acesso é obrigatória
    /// (as demais funções ficam bloqueadas até a conclusão).
    /// </summary>
    public async Task<bool> ConfiguracaoInicialPendenteAsync(CancellationToken ct = default)
        => !await repositorio.ConfiguracaoInicialConcluidaAsync(ct);

    public async Task<Resultado> ConcluirConfiguracaoInicialAsync(SessaoUsuario admin, CancellationToken ct = default)
    {
        if (!admin.Administrador) return Resultado.Falha("Somente administradores concluem a configuração dos perfis.");
        var perfis = await repositorio.ListarAsync(ct);
        if (!perfis.Any(p => p.Permissoes.Count > 0))
            return Resultado.Falha("Defina ao menos um perfil de acesso com funções liberadas antes de concluir.");
        await repositorio.ConcluirConfiguracaoInicialAsync(admin.Id, ct);
        return Resultado.Ok();
    }

    public async Task<Resultado> ExcluirAsync(SessaoUsuario admin, long id, CancellationToken ct = default)
    {
        if (!admin.Administrador) return Resultado.Falha("Somente administradores excluem perfis.");
        try
        {
            await repositorio.ExcluirAsync(id, ct);
            return Resultado.Ok();
        }
        catch (ViolacaoRegraException ex)
        {
            return Resultado.Falha(ex.Message);
        }
    }
}
