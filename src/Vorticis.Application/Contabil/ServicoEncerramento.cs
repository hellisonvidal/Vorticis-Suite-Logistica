using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Application.Contabil;

/// <summary>Contas usadas no encerramento. Padrão: as do plano modelo.</summary>
public sealed record ContasEncerramento(string Apuracao = "5.1.01.001", string LucrosAcumulados = "2.3.03.001",
                                        string PrejuizosAcumulados = "2.3.03.002");

public sealed record PreviaEncerramento(int Ano, DateOnly Data, decimal Receitas, decimal CustosDespesas, decimal Resultado,
                                        int ContasAZerar, long? LancamentoExistente);

/// <summary>
/// Encerramento do exercício (Lei 6.404, art. 191; ITG 2000): na data final do exercício,
/// (1) zera as contas de resultado contra a conta de Apuração do Resultado do Exercício e
/// (2) transfere o saldo da apuração para Lucros ou Prejuízos Acumulados.
/// Os lançamentos levam IND_LCTO = "E" (ECD I200) e são gravados juntos, numa transação.
/// "Desfazer" estorna os dois (também com IND_LCTO = "E"), para corrigir lançamentos e encerrar de novo.
/// </summary>
public sealed class ServicoEncerramento(IRepositorioContabil repositorio, IConsultasContabeis consultas, Autorizador autorizador)
{
    public async Task<Resultado<PreviaEncerramento>> PreverAsync(long empresaId, int ano, ContasEncerramento contas, long usuarioId,
                                                                CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<PreviaEncerramento>(usuarioId, empresaId, Permissao.ExercicioEncerrar, ct) is { } negado) return negado;
        var dados = await CarregarAsync(empresaId, ano, contas, ct);
        if (!dados.Sucesso) return Resultado<PreviaEncerramento>.Falha(dados.Erros);
        var d = dados.Valor;
        var receitas = d.Saldos.Where(s => s.Saldo < 0).Sum(s => -s.Saldo);
        var despesas = d.Saldos.Where(s => s.Saldo > 0).Sum(s => s.Saldo);
        return Resultado<PreviaEncerramento>.Ok(new PreviaEncerramento(ano, d.Periodo.FimExercicio, receitas, despesas, receitas - despesas,
                                                                      d.Saldos.Count, d.Existente));
    }

    public async Task<Resultado<IReadOnlyList<LancamentoEfetivado>>> EncerrarAsync(long empresaId, int ano, ContasEncerramento contas, long usuarioId,
                                                                                  CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<LancamentoEfetivado>>(usuarioId, empresaId, Permissao.ExercicioEncerrar, ct) is { } negado) return negado;
        var dados = await CarregarAsync(empresaId, ano, contas, ct);
        if (!dados.Sucesso) return Resultado<IReadOnlyList<LancamentoEfetivado>>.Falha(dados.Erros);
        var d = dados.Valor;
        if (d.Existente is long n)
            return Resultado<IReadOnlyList<LancamentoEfetivado>>.Falha($"O exercício {ano} já foi encerrado (lançamento nº {n}). Desfaça o encerramento para refazê-lo.");
        if (d.Saldos.Count == 0)
            return Resultado<IReadOnlyList<LancamentoEfetivado>>.Falha($"Não há saldos em contas de resultado no exercício {ano}.");

        var data = d.Periodo.FimExercicio;
        var historico1 = $"Encerramento do exercício {ano}: transferência para apuração do resultado";
        var partidas1 = d.Saldos.Select(s => new PartidaProposta(s.Codigo, s.Saldo > 0 ? IndicadorDC.C : IndicadorDC.D, Math.Abs(s.Saldo), historico1)).ToList();
        var liquido = d.Saldos.Sum(s => s.Saldo);   // > 0: despesas maiores (prejuízo) → apuração a débito
        if (liquido != 0)
            partidas1.Add(new PartidaProposta(contas.Apuracao, liquido > 0 ? IndicadorDC.D : IndicadorDC.C, Math.Abs(liquido), historico1));

        var propostas = new List<LancamentoProposto>
        {
            new(empresaId, data, OrigemLancamento.Encerramento, partidas1, IndicadorLancamento.Encerramento),
        };
        var saldoApuracao = d.SaldoApuracao + liquido;
        if (saldoApuracao != 0)
        {
            var lucro = saldoApuracao < 0;
            var historico2 = $"Encerramento do exercício {ano}: {(lucro ? "lucro" : "prejuízo")} transferido ao patrimônio líquido";
            propostas.Add(new LancamentoProposto(empresaId, data, OrigemLancamento.Encerramento,
            [
                new(contas.Apuracao, lucro ? IndicadorDC.D : IndicadorDC.C, Math.Abs(saldoApuracao), historico2),
                new(lucro ? contas.LucrosAcumulados : contas.PrejuizosAcumulados, lucro ? IndicadorDC.C : IndicadorDC.D, Math.Abs(saldoApuracao), historico2),
            ], IndicadorLancamento.Encerramento));
        }
        return await GravarAsync(propostas, usuarioId, ct);
    }

    public async Task<Resultado<IReadOnlyList<LancamentoEfetivado>>> DesfazerAsync(long empresaId, int ano, string motivo, long usuarioId,
                                                                                  CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<LancamentoEfetivado>>(usuarioId, empresaId, Permissao.ExercicioEncerrar, ct) is { } negado) return negado;
        if (string.IsNullOrWhiteSpace(motivo)) return Resultado<IReadOnlyList<LancamentoEfetivado>>.Falha("Informe o motivo.");
        var periodo = await repositorio.ObterPeriodoAsync(empresaId, new DateOnly(ano, 12, 31), ct);
        if (periodo is null) return Resultado<IReadOnlyList<LancamentoEfetivado>>.Falha($"Exercício {ano} não cadastrado.");
        var lancamentos = await repositorio.ListarLancamentosAtivosAsync(empresaId, periodo.ExercicioId, OrigemLancamento.Encerramento, ct);
        if (lancamentos.Count == 0) return Resultado<IReadOnlyList<LancamentoEfetivado>>.Falha($"O exercício {ano} não está encerrado.");

        // Estorna na ordem inversa, também como lançamento de encerramento na data final.
        var propostas = lancamentos.OrderByDescending(l => l.Numero).Select(l => new LancamentoProposto(
            empresaId, periodo.FimExercicio, OrigemLancamento.Estorno,
            l.Partidas.Select(p => p with
            {
                DC = p.DC.Inverso(),
                Historico = Truncar($"Estorno do encerramento (lançamento nº {l.Numero}, {motivo.Trim()}) - {p.Historico}"),
            }).ToArray(),
            IndicadorLancamento.Encerramento, EstornoDeId: l.Id)).ToList();
        return await GravarAsync(propostas, usuarioId, ct);
    }

    private sealed record SaldoResultado(string Codigo, decimal Saldo);
    private sealed record Dados(PeriodoInfo Periodo, IReadOnlyList<SaldoResultado> Saldos, decimal SaldoApuracao, long? Existente);

    private async Task<Resultado<Dados>> CarregarAsync(long empresaId, int ano, ContasEncerramento contas, CancellationToken ct)
    {
        var periodo = await repositorio.ObterPeriodoAsync(empresaId, new DateOnly(ano, 12, 31), ct);
        if (periodo is null) return Resultado<Dados>.Falha($"Exercício {ano} não cadastrado.");

        var codigos = new[] { contas.Apuracao, contas.LucrosAcumulados, contas.PrejuizosAcumulados };
        var info = await repositorio.ObterContasAsync(empresaId, codigos, ct);
        var erros = new List<string>();
        void Exigir(string codigo, string papel, params string[] naturezas)
        {
            if (!info.TryGetValue(codigo, out var c)) erros.Add($"Conta de {papel} {codigo} não existe no plano.");
            else if (!c.Analitica) erros.Add($"A conta de {papel} {codigo} precisa ser analítica.");
            else if (!naturezas.Contains(c.CodNat)) erros.Add($"A conta de {papel} {codigo} tem natureza {c.CodNat}; esperado {string.Join(" ou ", naturezas)}.");
        }
        Exigir(contas.Apuracao, "apuração do resultado", "04");
        Exigir(contas.LucrosAcumulados, "lucros acumulados", "03");
        Exigir(contas.PrejuizosAcumulados, "prejuízos acumulados", "03");
        if (erros.Count > 0) return Resultado<Dados>.Falha(erros);

        var plano = await consultas.ObterPlanoAsync(empresaId, ct);
        var movimentos = (await consultas.ObterMovimentosAsync(empresaId, periodo.FimExercicio.AddDays(1), periodo.FimExercicio, ct))
            .ToDictionary(m => m.ContaId, m => m.SaldoAnterior);   // saldo acumulado até a data final
        var apuracaoId = info[contas.Apuracao].Id;
        var saldos = plano.Where(c => c.Analitica && c.CodNat == "04" && c.Id != apuracaoId)
                          .Select(c => new SaldoResultado(c.Codigo, movimentos.GetValueOrDefault(c.Id)))
                          .Where(s => s.Saldo != 0)
                          .OrderBy(s => s.Codigo, StringComparer.Ordinal)
                          .ToArray();
        var existente = await repositorio.ObterNumeroLancamentoAtivoAsync(empresaId, periodo.ExercicioId, OrigemLancamento.Encerramento, ct);
        return Resultado<Dados>.Ok(new Dados(periodo, saldos, movimentos.GetValueOrDefault(apuracaoId), existente));
    }

    private async Task<Resultado<IReadOnlyList<LancamentoEfetivado>>> GravarAsync(IReadOnlyList<LancamentoProposto> propostas, long usuarioId,
                                                                               CancellationToken ct)
    {
        var validados = new List<LancamentoValidado>();
        foreach (var p in propostas)
        {
            var contas = await repositorio.ObterContasAsync(p.EmpresaId, p.Partidas.Select(x => x.CodigoConta).Distinct(), ct);
            var v = ValidadorLancamento.Validar(p, new ContextoValidacao(contas, new Dictionary<string, long>(), new Dictionary<string, long>(),
                                                                         await repositorio.ObterPeriodoAsync(p.EmpresaId, p.Data, ct)));
            if (!v.Sucesso) return Resultado<IReadOnlyList<LancamentoEfetivado>>.Falha(v.Erros);
            validados.Add(v.Valor);
        }
        try
        {
            return Resultado<IReadOnlyList<LancamentoEfetivado>>.Ok(await repositorio.GravarEfetivadosAsync(validados, usuarioId, ct));
        }
        catch (ViolacaoRegraException ex)
        {
            return Resultado<IReadOnlyList<LancamentoEfetivado>>.Falha(ex.Message);
        }
    }

    private static string Truncar(string s) => s.Length <= ValidadorLancamento.TamanhoMaximoHistorico ? s : s[..ValidadorLancamento.TamanhoMaximoHistorico];
}
