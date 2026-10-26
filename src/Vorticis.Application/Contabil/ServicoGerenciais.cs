using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Application.Contabil;

/// <summary>
/// Relatórios › Gerenciais: contabilização por centro de custo, orçado x realizado (com a manutenção do orçamento),
/// relação de custos e receita por centro de custo.
/// </summary>
public sealed class ServicoGerenciais(IConsultasContabeis consultas, IRepositorioOrcamento orcamentos, Autorizador autorizador)
{
    private const int LimiteMeses = 24;

    private static string? ValidarPeriodo(DateOnly inicio, DateOnly fim) => fim < inicio ? "Data final anterior à inicial." : null;

    public async Task<Resultado<IReadOnlyList<LinhaCentroConta>>> PorCentroCustoAsync(long empresaId, FiltroCentroCusto f, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<LinhaCentroConta>>(usuarioId, empresaId, Permissao.RelatorioBalancete, ct) is { } negado) return negado;
        if (ValidarPeriodo(f.Inicio, f.Fim) is { } erro) return Resultado<IReadOnlyList<LinhaCentroConta>>.Falha(erro);
        var plano = await consultas.ObterPlanoAsync(empresaId, ct);
        var centros = await consultas.ObterCentrosAsync(empresaId, ct);
        var movs = await consultas.ObterMovimentosCentroAsync(empresaId, f.Inicio, f.Fim, ct);
        var detalhes = f.Detalhar ? await consultas.ObterDetalhesCentroAsync(empresaId, f.Inicio, f.Fim, ct) : [];
        return Resultado<IReadOnlyList<LinhaCentroConta>>.Ok(Gerenciais.PorCentroCusto(plano, centros, movs, detalhes, f));
    }

    public async Task<Resultado<IReadOnlyList<LinhaCusto>>> RelacaoCustosAsync(long empresaId, FiltroCustos f, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<LinhaCusto>>(usuarioId, empresaId, Permissao.RelatorioDemonstracoes, ct) is { } negado) return negado;
        if (ValidarPeriodo(f.Inicio, f.Fim) is { } erro) return Resultado<IReadOnlyList<LinhaCusto>>.Falha(erro);
        var plano = await consultas.ObterPlanoAsync(empresaId, ct);
        var centros = await consultas.ObterCentrosAsync(empresaId, ct);
        var atual = await consultas.ObterMovimentosCentroAsync(empresaId, f.Inicio, f.Fim, ct);
        IReadOnlyList<MovimentoCentro>? anterior = null;
        if (Gerenciais.PeriodoComparado(f.Inicio, f.Fim, f.Comparar) is { } p)
            anterior = await consultas.ObterMovimentosCentroAsync(empresaId, p.Inicio, p.Fim, ct);
        return Resultado<IReadOnlyList<LinhaCusto>>.Ok(Gerenciais.RelacaoCustos(plano, centros, atual, anterior, f));
    }

    public async Task<Resultado<ReceitaPorCentro>> ReceitaPorCentroAsync(long empresaId, FiltroReceitaCentro f, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<ReceitaPorCentro>(usuarioId, empresaId, Permissao.RelatorioDemonstracoes, ct) is { } negado) return negado;
        if (ValidarPeriodo(f.Inicio, f.Fim) is { } erro) return Resultado<ReceitaPorCentro>.Falha(erro);
        if (Gerenciais.MesesDoPeriodo(f.Inicio, f.Fim).Count > LimiteMeses) return Resultado<ReceitaPorCentro>.Falha($"O período pode ter no máximo {LimiteMeses} meses.");
        var plano = await consultas.ObterPlanoAsync(empresaId, ct);
        var centros = await consultas.ObterCentrosAsync(empresaId, ct);
        var movs = await consultas.ObterMovimentosCentroAsync(empresaId, f.Inicio, f.Fim, ct);
        return Resultado<ReceitaPorCentro>.Ok(Gerenciais.ReceitaPorCentroCusto(plano, centros, movs, f));
    }

    // ---- Orçamento ------------------------------------------------------------------------------------------------

    private async Task<string?> PodeConsultarAsync(long empresaId, long usuarioId, CancellationToken ct)
    {
        var p = await autorizador.ObterAsync(usuarioId, empresaId, ct);
        return p.Pode(Permissao.RelatorioDemonstracoes) || p.Pode(Permissao.OrcamentoManter) ? null
            : await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.RelatorioDemonstracoes, ct) ?? "Acesso negado.";
    }

    public async Task<Resultado<IReadOnlyList<LinhaOrcamento>>> ListarOrcamentoAsync(long empresaId, int ano, long usuarioId, CancellationToken ct = default)
    {
        if (await PodeConsultarAsync(empresaId, usuarioId, ct) is { } negado) return Resultado<IReadOnlyList<LinhaOrcamento>>.Falha(negado);
        return Resultado<IReadOnlyList<LinhaOrcamento>>.Ok(await orcamentos.ListarAsync(empresaId, ano, ct));
    }

    /// <summary>Grava (substituindo) o orçamento do ano. Só contas analíticas de resultado; centros existentes e analíticos.</summary>
    public async Task<Resultado> SalvarOrcamentoAsync(long empresaId, int ano, IReadOnlyList<LinhaOrcamento> linhas, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.OrcamentoManter, ct) is { } negado) return Resultado.Falha(negado);
        if (ano is < 2000 or > 2100) return Resultado.Falha("Informe um ano entre 2000 e 2100.");
        var contas = (await consultas.ObterPlanoAsync(empresaId, ct)).ToDictionary(c => c.Id);
        var centros = (await consultas.ObterCentrosAsync(empresaId, ct)).ToDictionary(c => c.Id);
        var erros = new List<string>();
        var unificadas = new Dictionary<(long, long), decimal[]>();
        foreach (var l in linhas)
        {
            if (!contas.TryGetValue(l.ContaId, out var c) || Gerenciais.Classe(c) == ClasseResultado.Nenhuma)
            { erros.Add("O orçamento só aceita contas analíticas de resultado (receitas, custos e despesas)."); continue; }
            if (l.CentroId != 0 && (!centros.TryGetValue(l.CentroId, out var cc) || !cc.Analitico))
            { erros.Add($"Conta {c.Codigo}: o centro de custo não existe ou é sintético."); continue; }
            if (l.Valores.Count != 12) { erros.Add($"Conta {c.Codigo}: informe os 12 meses."); continue; }
            if (!unificadas.TryGetValue((l.ContaId, l.CentroId), out var v)) unificadas[(l.ContaId, l.CentroId)] = v = new decimal[12];
            for (var m = 0; m < 12; m++) v[m] += Math.Round(l.Valores[m], 2, MidpointRounding.AwayFromZero);
        }
        if (erros.Count > 0) return Resultado.Falha(erros.Distinct().ToList());
        await orcamentos.SubstituirAsync(empresaId, ano,
            unificadas.Where(kv => kv.Value.Any(x => x != 0)).Select(kv => new LinhaOrcamento(kv.Key.Item1, kv.Key.Item2, kv.Value)).ToList(), ct);
        return Resultado.Ok();
    }

    /// <summary>Orçamento do ano anterior reajustado pelo percentual (não grava: a tela mostra na grade para conferência).</summary>
    public async Task<Resultado<IReadOnlyList<LinhaOrcamento>>> CopiarOrcamentoAnteriorAsync(long empresaId, int ano, decimal percentual, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<LinhaOrcamento>>(usuarioId, empresaId, Permissao.OrcamentoManter, ct) is { } negado) return negado;
        var origem = await orcamentos.ListarAsync(empresaId, ano - 1, ct);
        return origem.Count == 0
            ? Resultado<IReadOnlyList<LinhaOrcamento>>.Falha($"Não há orçamento de {ano - 1} para copiar.")
            : Resultado<IReadOnlyList<LinhaOrcamento>>.Ok(Gerenciais.Reajustar(origem, percentual));
    }

    /// <summary>Realizado do ano anterior reajustado pelo percentual, como base do orçamento (não grava).</summary>
    public async Task<Resultado<IReadOnlyList<LinhaOrcamento>>> CopiarRealizadoAnteriorAsync(long empresaId, int ano, decimal percentual, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<LinhaOrcamento>>(usuarioId, empresaId, Permissao.OrcamentoManter, ct) is { } negado) return negado;
        var plano = await consultas.ObterPlanoAsync(empresaId, ct);
        var movs = await consultas.ObterMovimentosCentroAsync(empresaId, new DateOnly(ano - 1, 1, 1), new DateOnly(ano - 1, 12, 31), ct);
        var linhas = Gerenciais.RealizadoComoOrcamento(plano, movs, ano - 1, percentual);
        return linhas.Count == 0
            ? Resultado<IReadOnlyList<LinhaOrcamento>>.Falha($"Não há movimento de receitas, custos ou despesas em {ano - 1}.")
            : Resultado<IReadOnlyList<LinhaOrcamento>>.Ok(linhas);
    }

    /// <summary>Lê a planilha de orçamento (não grava).</summary>
    public async Task<Resultado<IReadOnlyList<LinhaOrcamento>>> LerPlanilhaAsync(long empresaId, TabelaImportada tabela, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<LinhaOrcamento>>(usuarioId, empresaId, Permissao.OrcamentoManter, ct) is { } negado) return negado;
        return Gerenciais.LerPlanilhaOrcamento(tabela, await consultas.ObterPlanoAsync(empresaId, ct), await consultas.ObterCentrosAsync(empresaId, ct));
    }

    public async Task<Resultado<IReadOnlyList<LinhaOrcadoRealizado>>> OrcadoRealizadoAsync(long empresaId, FiltroOrcadoRealizado f, long usuarioId, CancellationToken ct = default)
    {
        if (await PodeConsultarAsync(empresaId, usuarioId, ct) is { } negado) return Resultado<IReadOnlyList<LinhaOrcadoRealizado>>.Falha(negado);
        if (f.Ano is < 2000 or > 2100) return Resultado<IReadOnlyList<LinhaOrcadoRealizado>>.Falha("Informe um ano entre 2000 e 2100.");
        var plano = await consultas.ObterPlanoAsync(empresaId, ct);
        var centros = await consultas.ObterCentrosAsync(empresaId, ct);
        var movs = await consultas.ObterMovimentosCentroAsync(empresaId, new DateOnly(f.Ano, 1, 1), new DateOnly(f.Ano, 12, 31), ct);
        var orc = await orcamentos.ListarAsync(empresaId, f.Ano, ct);
        return Resultado<IReadOnlyList<LinhaOrcadoRealizado>>.Ok(Gerenciais.OrcadoRealizado(plano, centros, orc, movs, f));
    }
}
