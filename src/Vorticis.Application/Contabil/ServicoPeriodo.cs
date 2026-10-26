using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Application.Contabil;

/// <summary>
/// Fechamento mensal. Fecha-se em ordem cronológica (não se fecha março com fevereiro aberto) e
/// reabre-se em ordem inversa, para que nenhum saldo de mês fechado mude por lançamento retroativo.
/// </summary>
public sealed class ServicoPeriodo(IRepositorioContabil repositorio, Autorizador autorizador)
{
    public async Task<IReadOnlyList<CompetenciaStatus>> ListarAsync(long empresaId, int ano, long usuarioId, CancellationToken ct = default)
        => (await autorizador.ObterAsync(usuarioId, empresaId, ct)).TemAcesso ? await repositorio.ListarPeriodosAsync(empresaId, ano, ct) : [];

    public async Task<Resultado> FecharAsync(long empresaId, DateOnly competencia, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.PeriodoFechar, ct) is { } negado) return Resultado.Falha(negado);
        competencia = new DateOnly(competencia.Year, competencia.Month, 1);
        var periodos = await repositorio.ListarPeriodosAsync(empresaId, competencia.Year, ct);
        var alvo = periodos.FirstOrDefault(p => p.Competencia == competencia);
        if (alvo is null) return Resultado.Falha($"Período {competencia:MM/yyyy} não cadastrado.");
        if (!alvo.Aberto) return Resultado.Falha($"Período {competencia:MM/yyyy} já está fechado.");

        var anteriorAberto = periodos.FirstOrDefault(p => p.Competencia < competencia && p.Aberto);
        if (anteriorAberto is not null)
            return Resultado.Falha($"Feche antes o período {anteriorAberto.Competencia:MM/yyyy}.");

        var problemas = await repositorio.VerificarInvariantesAsync(empresaId, competencia, ct);
        if (problemas.Count > 0)
            return Resultado.Falha(problemas.Select(p => $"[{p.Codigo}] {p.Descricao}"));

        await repositorio.AlterarStatusPeriodoAsync(empresaId, competencia, fechar: true, usuarioId, ct);
        return Resultado.Ok();
    }

    public async Task<Resultado> ReabrirAsync(long empresaId, DateOnly competencia, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.PeriodoReabrir, ct) is { } negado) return Resultado.Falha(negado);
        competencia = new DateOnly(competencia.Year, competencia.Month, 1);
        var periodos = await repositorio.ListarPeriodosAsync(empresaId, competencia.Year, ct);
        var alvo = periodos.FirstOrDefault(p => p.Competencia == competencia);
        if (alvo is null) return Resultado.Falha($"Período {competencia:MM/yyyy} não cadastrado.");
        if (alvo.Aberto) return Resultado.Falha($"Período {competencia:MM/yyyy} já está aberto.");

        var posteriorFechado = periodos.LastOrDefault(p => p.Competencia > competencia && !p.Aberto);
        if (posteriorFechado is not null)
            return Resultado.Falha($"Reabra antes o período {posteriorFechado.Competencia:MM/yyyy}.");

        await repositorio.AlterarStatusPeriodoAsync(empresaId, competencia, fechar: false, usuarioId, ct);
        return Resultado.Ok();
    }
}
