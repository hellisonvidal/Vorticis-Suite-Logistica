using Vorticis.Application.Contabil;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;

namespace Vorticis.Frota;

/// <summary>
/// Contabilização das diárias pagas: débito na despesa de diárias (com o centro de custo da diária, que vem da frota) e crédito na conta
/// a pagar ou caixa, ambas escolhidas em Cadastros › Parâmetros › Frota. On-line, na hora do pagamento; em lote, pelo processador da origem FROTA.
/// </summary>
public sealed partial class ServicoFrota
{
    /// <summary>Processador da origem FROTA da Contabilização em lote: as mesmas regras da contabilização on-line.</summary>
    public IProcessadorPendencia ProcessadorPendencias => new ProcessadorFrota(this);

    private async Task<Diaria?> ObterDiariaAsync(long empresa, long id, CancellationToken ct)
        => (await ListarDiariasAsync(empresa, DateOnly.MinValue, DateOnly.MaxValue, null, null, ct)).FirstOrDefault(x => x.Id == id);

    /// <summary>Monta o lançamento da diária paga (ou o motivo de não ser possível).</summary>
    private async Task<(LancamentoProposto? Proposta, IReadOnlyList<string> Erros)> MontarLancamentoDiariaAsync(long empresa, Diaria d, CancellationToken ct)
    {
        var cfg = await ConfiguracaoAsync(empresa, ct);
        if (!cfg.ContabilizaDiarias) return (null, ["Informe a conta de despesa e a conta de crédito das diárias em Cadastros › Parâmetros › Frota."]);
        if (d.Situacao is not ("PAGA" or "ACERTADA")) return (null, ["Só diárias pagas podem ser contabilizadas."]);
        if (d.ValorPagamento <= 0) return (null, ["O valor da diária é zero."]);
        var centro = d.CentroCustoId is { } cc ? await _db.EscalarAsync<string>("SELECT codigo FROM contabil.centro_custo WHERE id = @c", ct, ("c", cc)) : null;
        var historico = $"Diária nº {d.Numero} – {d.Colaborador}" + (d.FrotaNome is null ? "" : $" ({d.FrotaNome})");
        IReadOnlyList<PartidaProposta> partidas =
        [
            new(cfg.ContaDiariaDespesa!, IndicadorDC.D, d.ValorPagamento, historico, centro),
            new(cfg.ContaDiariaCredito!, IndicadorDC.C, d.ValorPagamento, historico),
        ];
        return (new LancamentoProposto(empresa, d.Data, OrigemLancamento.Frota, partidas), []);
    }

    /// <summary>Gera o lançamento da diária paga e o guarda na diária (não gera duas vezes).</summary>
    private async Task<ResultadoProcessamento> ContabilizarDiariaAsync(long empresa, Diaria d, long usuario, CancellationToken ct)
    {
        if (await _db.EscalarAsync<long?>("SELECT lancamento_id FROM frota.diaria WHERE id = @i", ct, ("i", d.Id)) is not null)
            return ResultadoProcessamento.Cancelada("a diária já está contabilizada.");
        var (proposta, erros) = await MontarLancamentoDiariaAsync(empresa, d, ct);
        if (proposta is null) return ResultadoProcessamento.Falha(erros);
        var r = await app.Lancamentos.EfetivarAsync(proposta, usuario, ct);
        if (!r.Sucesso) return ResultadoProcessamento.Falha(r.Erros);
        await _db.ExecutarAsync("UPDATE frota.diaria SET lancamento_id = @l WHERE id = @i", ct, ("l", r.Valor.Id), ("i", d.Id));
        return ResultadoProcessamento.Ok(r.Valor.Id);
    }

    private Task<Resultado<LancamentoEfetivado>> EstornarLancamentoAsync(long empresa, long lancamento, Diaria d, long usuario, CancellationToken ct)
        => app.Lancamentos.EstornarAsync(empresa, lancamento, d.Data, $"desfazer contabilização da diária nº {d.Numero}", usuario, ct);

    private sealed class ProcessadorFrota(ServicoFrota s) : IProcessadorPendencia
    {
        public string Origem => OrigensPendencia.Frota;

        private static long? IdDaReferencia(PendenciaContabil p)
            => p.Referencia.StartsWith("DIARIA:", StringComparison.Ordinal) && long.TryParse(p.Referencia.AsSpan(7), out var id) ? id : null;

        public async Task<IReadOnlyList<string>> PreviaAsync(long empresaId, PendenciaContabil p, long usuarioId, CancellationToken ct)
        {
            if (IdDaReferencia(p) is not { } id || await s.ObterDiariaAsync(empresaId, id, ct) is not { } d) return ["A diária não existe mais."];
            var (proposta, erros) = await s.MontarLancamentoDiariaAsync(empresaId, d, ct);
            return proposta is null ? erros : [.. proposta.Partidas.Select(x => $"{x.DC} {x.CodigoConta}  {Formato.Moeda(x.Valor)}" + (x.CodigoCentroCusto is null ? "" : $"  C.C. {x.CodigoCentroCusto}"))];
        }

        public async Task<ResultadoProcessamento> ContabilizarAsync(long empresaId, PendenciaContabil p, long usuarioId, CancellationToken ct)
        {
            if (IdDaReferencia(p) is not { } id || await s.ObterDiariaAsync(empresaId, id, ct) is not { } d)
                return ResultadoProcessamento.Cancelada("a diária não existe mais.");
            return await s.ContabilizarDiariaAsync(empresaId, d, usuarioId, ct);
        }

        public async Task<Resultado> DesfazerAsync(long empresaId, PendenciaContabil p, long usuarioId, CancellationToken ct)
        {
            if (IdDaReferencia(p) is not { } id || await s.ObterDiariaAsync(empresaId, id, ct) is not { } d) return Resultado.Ok();
            var lancamento = p.LancamentoId ?? await s._db.EscalarAsync<long?>("SELECT lancamento_id FROM frota.diaria WHERE id = @i", ct, ("i", id));
            if (lancamento is null) return Resultado.Ok();
            var r = await s.EstornarLancamentoAsync(empresaId, lancamento.Value, d, usuarioId, ct);
            if (!r.Sucesso) return Resultado.Falha(r.Erros);
            await s._db.ExecutarAsync("UPDATE frota.diaria SET lancamento_id = NULL WHERE id = @i", ct, ("i", id));
            return Resultado.Ok();
        }
    }
}
