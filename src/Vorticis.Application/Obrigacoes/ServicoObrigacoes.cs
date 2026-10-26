using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Obrigacoes;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Application.Obrigacoes;

public enum StatusObrigacao { Pendente, Cumprida, Atrasada }

/// <summary>Regimes da empresa ativa e se ela tem empregados ativos (filtros do catálogo).</summary>
public sealed record PerfilObrigacoesEmpresa(RegimesObrigacao Regimes, bool TemEmpregados);

public sealed record BaixaObrigacao(string Obrigacao, string Competencia, DateOnly CumpridaEm, string? Observacao);

/// <summary>Obrigação cadastrada pela empresa (municipal, estadual, contratual...). No calendário vira a definição "P&lt;id&gt;".</summary>
public sealed record ObrigacaoPersonalizada(long Id, string Nome, string Orgao, string Esfera, Periodicidade Periodicidade, TipoRegra Regra, int Dia,
                                            int MesesApos, int MesVencimento, int AnosApos, AjusteNaoUtil Ajuste, string? Observacao, bool Ativa = true)
{
    public string Codigo => $"P{Id}";

    public DefinicaoObrigacao ParaDefinicao() => new(Codigo, Nome, string.IsNullOrWhiteSpace(Orgao) ? Esfera : Orgao, ModuloObrigacao.Todos, Periodicidade, Regra,
        Dia, MesesApos, MesVencimento, AnosApos, Ajuste, RegimesObrigacao.Todos, false, Observacao, null, true, Esfera);
}

public sealed record ItemObrigacao(OcorrenciaObrigacao Ocorrencia, StatusObrigacao Status, DateOnly? CumpridaEm, string? Observacao, int DiasParaVencer)
{
    public bool Vencendo7Dias => Status == StatusObrigacao.Pendente && DiasParaVencer is >= 0 and <= 7;
}

public interface IRepositorioObrigacoes
{
    Task<PerfilObrigacoesEmpresa> ObterPerfilAsync(long empresaId, CancellationToken ct);
    Task<IReadOnlyList<ObrigacaoPersonalizada>> ListarPersonalizadasAsync(long empresaId, CancellationToken ct);
    /// <summary>Insere (Id 0) ou atualiza; devolve o id.</summary>
    Task<long> SalvarPersonalizadaAsync(long empresaId, ObrigacaoPersonalizada o, CancellationToken ct);
    Task ExcluirPersonalizadaAsync(long empresaId, long id, CancellationToken ct);
    Task<IReadOnlyList<BaixaObrigacao>> ListarBaixasAsync(long empresaId, CancellationToken ct);
    Task SalvarBaixaAsync(long empresaId, BaixaObrigacao baixa, long usuarioId, CancellationToken ct);
    Task RemoverBaixaAsync(long empresaId, string obrigacao, string competencia, CancellationToken ct);
}

/// <summary>
/// Calendário de obrigações da empresa: catálogo federal embutido (filtrado pelo regime e por ter empregados) mais as obrigações
/// personalizadas, com vencimento calculado e status. Consultar vale para qualquer perfil com acesso à empresa; marcar como cumprida
/// e personalizar exigem <see cref="Permissao.ObrigacoesManter"/>.
/// </summary>
public sealed class ServicoObrigacoes(IRepositorioObrigacoes repositorio, Autorizador autorizador)
{
    public static StatusObrigacao CalcularStatus(DateOnly vencimento, BaixaObrigacao? baixa, DateOnly hoje) =>
        baixa is not null ? StatusObrigacao.Cumprida : vencimento < hoje ? StatusObrigacao.Atrasada : StatusObrigacao.Pendente;

    private async Task<string?> ExigirAcessoAsync(long usuarioId, long empresaId, CancellationToken ct)
        => (await autorizador.ObterAsync(usuarioId, empresaId, ct)).TemAcesso ? null : "Você não tem acesso a esta empresa.";

    /// <param name="modulos">Null = todos os módulos; senão só as obrigações desses módulos (e as de "todos"/personalizadas).</param>
    public async Task<Resultado<IReadOnlyList<ItemObrigacao>>> ListarAsync(long usuarioId, long empresaId, DateOnly de, DateOnly ate, DateOnly hoje,
                                                                         IEnumerable<ModuloObrigacao>? modulos = null, CancellationToken ct = default)
    {
        if (await ExigirAcessoAsync(usuarioId, empresaId, ct) is { } negado) return Resultado<IReadOnlyList<ItemObrigacao>>.Falha(negado);
        var perfil = await repositorio.ObterPerfilAsync(empresaId, ct);
        var personalizadas = (await repositorio.ListarPersonalizadasAsync(empresaId, ct)).Where(p => p.Ativa).Select(p => p.ParaDefinicao());
        var definicoes = CatalogoObrigacoes.Federais.Where(d => d.AplicaA(perfil.Regimes, perfil.TemEmpregados)
                                                                 && (modulos is null || d.Modulo == ModuloObrigacao.Todos || modulos.Contains(d.Modulo)))
                                                    .Concat(personalizadas);
        var baixas = (await repositorio.ListarBaixasAsync(empresaId, ct)).ToDictionary(b => (b.Obrigacao, b.Competencia));
        var itens = CalendarioObrigacoes.Ocorrencias(definicoes, de, ate).Select(o =>
        {
            baixas.TryGetValue((o.Definicao.Codigo, o.Competencia.Chave), out var b);
            return new ItemObrigacao(o, CalcularStatus(o.Vencimento, b, hoje), b?.CumpridaEm, b?.Observacao, o.Vencimento.DayNumber - hoje.DayNumber);
        }).ToList();
        return Resultado<IReadOnlyList<ItemObrigacao>>.Ok(itens);
    }

    /// <summary>Quadro da tela inicial: atrasadas (últimos 90 dias) e pendentes dos próximos <paramref name="dias"/> dias, sem as cumpridas.</summary>
    public async Task<Resultado<IReadOnlyList<ItemObrigacao>>> ProximasAsync(long usuarioId, long empresaId, DateOnly hoje, int dias = 15, int maximo = 8,
                                                                           IEnumerable<ModuloObrigacao>? modulos = null,
                                                                           CancellationToken ct = default)
    {
        var r = await ListarAsync(usuarioId, empresaId, hoje.AddDays(-90), hoje.AddDays(dias), hoje, modulos, ct);
        if (!r.Sucesso) return r;
        return Resultado<IReadOnlyList<ItemObrigacao>>.Ok(r.Valor.Where(i => i.Status != StatusObrigacao.Cumprida).Take(maximo).ToList());
    }

    public async Task<Resultado> MarcarCumpridaAsync(long usuarioId, long empresaId, string obrigacao, string competencia, DateOnly cumpridaEm,
                                                     string? observacao, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.ObrigacoesManter, ct) is { } negado) return Resultado.Falha(negado);
        if (string.IsNullOrWhiteSpace(obrigacao) || string.IsNullOrWhiteSpace(competencia)) return Resultado.Falha("Informe a obrigação e a competência.");
        if (cumpridaEm > DateOnly.FromDateTime(DateTime.Today).AddDays(1)) return Resultado.Falha("A data de cumprimento não pode ser futura.");
        await repositorio.SalvarBaixaAsync(empresaId, new BaixaObrigacao(obrigacao, competencia, cumpridaEm, string.IsNullOrWhiteSpace(observacao) ? null : observacao.Trim()), usuarioId, ct);
        return Resultado.Ok();
    }

    public async Task<Resultado> DesfazerCumprimentoAsync(long usuarioId, long empresaId, string obrigacao, string competencia, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.ObrigacoesManter, ct) is { } negado) return Resultado.Falha(negado);
        await repositorio.RemoverBaixaAsync(empresaId, obrigacao, competencia, ct);
        return Resultado.Ok();
    }

    public async Task<Resultado<IReadOnlyList<ObrigacaoPersonalizada>>> ListarPersonalizadasAsync(long usuarioId, long empresaId, CancellationToken ct = default)
    {
        if (await ExigirAcessoAsync(usuarioId, empresaId, ct) is { } negado) return Resultado<IReadOnlyList<ObrigacaoPersonalizada>>.Falha(negado);
        return Resultado<IReadOnlyList<ObrigacaoPersonalizada>>.Ok(await repositorio.ListarPersonalizadasAsync(empresaId, ct));
    }

    public async Task<Resultado<long>> SalvarPersonalizadaAsync(long usuarioId, long empresaId, ObrigacaoPersonalizada o, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.ObrigacoesManter, ct) is { } negado) return Resultado<long>.Falha(negado);
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(o.Nome)) erros.Add("Informe o nome da obrigação.");
        switch (o.Regra)
        {
            case TipoRegra.DiaFixo when o.Dia is < 1 or > 31: erros.Add("O dia do vencimento deve estar entre 1 e 31."); break;
            case TipoRegra.DiaUtil when o.Dia is < 1 or > 23: erros.Add("O dia útil deve estar entre 1 e 23."); break;
        }
        if (o.MesesApos is < 0 or > 24) erros.Add("Os meses após a competência devem estar entre 0 e 24.");
        if (o.MesVencimento is < 1 or > 12) erros.Add("O mês do vencimento deve estar entre 1 e 12.");
        if (erros.Count > 0) return Resultado<long>.Falha(erros);
        var normal = o with { Nome = o.Nome.Trim(), Orgao = (o.Orgao ?? "").Trim(), Observacao = string.IsNullOrWhiteSpace(o.Observacao) ? null : o.Observacao.Trim() };
        return Resultado<long>.Ok(await repositorio.SalvarPersonalizadaAsync(empresaId, normal, ct));
    }

    public async Task<Resultado> ExcluirPersonalizadaAsync(long usuarioId, long empresaId, long id, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.ObrigacoesManter, ct) is { } negado) return Resultado.Falha(negado);
        await repositorio.ExcluirPersonalizadaAsync(empresaId, id, ct);
        return Resultado.Ok();
    }
}
