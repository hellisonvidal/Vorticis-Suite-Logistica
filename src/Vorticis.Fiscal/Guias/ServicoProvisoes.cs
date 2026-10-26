using Vorticis.Application.Contabil;
using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Contabilizacao;
using Vorticis.Fiscal.Lucro;
using Vorticis.Fiscal.Simples;

namespace Vorticis.Fiscal.Guias;

/// <summary>Contas de despesa e de passivo (obrigação a recolher) de um tributo, por código.</summary>
public sealed record ContaTributo(string Tributo, string ContaDespesa, string ContaPassivo);

public enum TipoApuracao { Simples, Lucro, PisCofins }

public interface IRepositorioProvisoes
{
    Task<IReadOnlyList<ContaTributo>> ListarContasAsync(long empresaId, CancellationToken ct);
    Task GravarContaAsync(long empresaId, string tributo, long contaDespesaId, long contaPassivoId, CancellationToken ct);
    Task ExcluirContaAsync(long empresaId, string tributo, CancellationToken ct);
    Task DefinirLancamentoAsync(long empresaId, TipoApuracao tipo, long apuracaoId, long? lancamentoId, CancellationToken ct);
}

/// <summary>
/// Contabilização da provisão dos tributos apurados: debita a despesa e credita o passivo a recolher (valor a pagar da apuração), com origem
/// "apuração de tributo", pelo <see cref="ServicoLancamento"/>. Desfazer estorna o lançamento. As contas são configuradas por tributo.
/// </summary>
public sealed class ServicoProvisoes(IRepositorioProvisoes repositorio, IRepositorioContabilizacao contas, IRepositorioSimples simples, IRepositorioLucro lucro,
                                     ServicoLancamento lancamentos, Autorizador autorizador)
{
    public static readonly IReadOnlyList<string> Tributos = ["DAS", "IRPJ", "CSLL", "PIS", "COFINS"];

    public async Task<Resultado<IReadOnlyList<ContaTributo>>> ListarContasAsync(long empresaId, long usuarioId, CancellationToken ct = default)
    {
        var p = await autorizador.ObterAsync(usuarioId, empresaId, ct);
        if (!p.Pode(Permissao.FiscalApurar) && !p.Pode(Permissao.FiscalRelatorios))
            return Resultado<IReadOnlyList<ContaTributo>>.Falha(await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FiscalApurar, ct) ?? "Acesso negado.");
        return Resultado<IReadOnlyList<ContaTributo>>.Ok(await repositorio.ListarContasAsync(empresaId, ct));
    }

    public async Task<Resultado> GravarContaAsync(long empresaId, ContaTributo c, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FiscalApurar, ct) is { } negado) return Resultado.Falha(negado);
        var tributo = c.Tributo.Trim().ToUpperInvariant();
        if (!Tributos.Contains(tributo)) return Resultado.Falha($"Tributo inválido. Use: {string.Join(", ", Tributos)}.");
        var resolvidas = await contas.ResolverContasAsync(empresaId, [c.ContaDespesa, c.ContaPassivo], ct);
        var erros = new List<string>();
        ContaResolvida? Conta(string codigo, string papel)
        {
            if (!resolvidas.TryGetValue(codigo.Trim(), out var r)) { erros.Add($"Conta de {papel} {codigo} não encontrada."); return null; }
            if (!r.Analitica) erros.Add($"Conta de {papel} {r.Codigo} é sintética.");
            if (!r.Ativa) erros.Add($"Conta de {papel} {r.Codigo} está inativa.");
            return r;
        }
        var d = Conta(c.ContaDespesa, "despesa");
        var p = Conta(c.ContaPassivo, "passivo");
        if (d is not null && p is not null && d.Id == p.Id) erros.Add("As contas de despesa e de passivo devem ser diferentes.");
        if (erros.Count > 0) return Resultado.Falha(erros);
        await repositorio.GravarContaAsync(empresaId, tributo, d!.Id, p!.Id, ct);
        return Resultado.Ok();
    }

    public async Task<Resultado> ExcluirContaAsync(long empresaId, string tributo, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FiscalApurar, ct) is { } negado) return Resultado.Falha(negado);
        await repositorio.ExcluirContaAsync(empresaId, tributo.Trim().ToUpperInvariant(), ct);
        return Resultado.Ok();
    }

    // ------------------------------------------------------------------ contabilização

    private async Task<Resultado<long>> ContabilizarAsync(long empresaId, DateOnly data, string historico, IReadOnlyList<(string Tributo, decimal Valor)> itens, long usuarioId,
                                                         CancellationToken ct)
    {
        var configuradas = (await repositorio.ListarContasAsync(empresaId, ct)).ToDictionary(c => c.Tributo);
        var partidas = new List<PartidaProposta>();
        foreach (var (tributo, valor) in itens.Where(i => i.Valor > 0))
        {
            if (!configuradas.TryGetValue(tributo, out var c))
                return Resultado<long>.Falha($"Configure as contas de despesa e de passivo do tributo {tributo} em Contas dos tributos.");
            partidas.Add(new PartidaProposta(c.ContaDespesa, IndicadorDC.D, valor, $"{historico} · {tributo}"));
            partidas.Add(new PartidaProposta(c.ContaPassivo, IndicadorDC.C, valor, $"{historico} · {tributo}"));
        }
        if (partidas.Count == 0) return Resultado<long>.Falha("Não há valor a pagar para provisionar.");
        var r = await lancamentos.EfetivarAsync(new LancamentoProposto(empresaId, data, OrigemLancamento.ApuracaoTributo, partidas), usuarioId, ct);
        return r.Sucesso ? Resultado<long>.Ok(r.Valor.Id) : Resultado<long>.Falha(r.Erros);
    }

    public async Task<Resultado<long>> ContabilizarSimplesAsync(long empresaId, DateOnly competencia, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.FiscalApurar, ct) is { } negado) return negado;
        var comp = new DateOnly(competencia.Year, competencia.Month, 1);
        var a = await simples.ObterApuracaoAsync(empresaId, comp, ct);
        if (a is null) return Resultado<long>.Falha($"Não há apuração do Simples Nacional de {comp:MM/yyyy}.");
        if (a.LancamentoId is not null) return Resultado<long>.Falha("A provisão desta apuração já está contabilizada.");
        var data = comp.AddMonths(1).AddDays(-1);
        var r = await ContabilizarAsync(empresaId, data, $"Provisão do DAS {comp:MM/yyyy}", [("DAS", a.ValorDas)], usuarioId, ct);
        if (r.Sucesso) await repositorio.DefinirLancamentoAsync(empresaId, TipoApuracao.Simples, a.Id, r.Valor, ct);
        return r;
    }

    public async Task<Resultado<long>> ContabilizarLucroAsync(long empresaId, RegimeLucro regime, int ano, int trimestre, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.FiscalApurar, ct) is { } negado) return negado;
        var a = await lucro.ObterApuracaoAsync(empresaId, regime, ano, trimestre, ct);
        if (a is null) return Resultado<long>.Falha($"Não há apuração do {trimestre}º trimestre de {ano}.");
        if (a.LancamentoId is not null) return Resultado<long>.Falha("A provisão desta apuração já está contabilizada.");
        var r = await ContabilizarAsync(empresaId, a.Resultado.Fim, $"Provisão de IRPJ/CSLL {trimestre}º tri/{ano}",
            [("IRPJ", a.Resultado.IrpjAPagar), ("CSLL", a.Resultado.CsllAPagar)], usuarioId, ct);
        if (r.Sucesso) await repositorio.DefinirLancamentoAsync(empresaId, TipoApuracao.Lucro, a.Id, r.Valor, ct);
        return r;
    }

    public async Task<Resultado<long>> ContabilizarPisCofinsAsync(long empresaId, DateOnly competencia, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.FiscalApurar, ct) is { } negado) return negado;
        var comp = new DateOnly(competencia.Year, competencia.Month, 1);
        var a = await lucro.ObterPisCofinsAsync(empresaId, comp, ct);
        if (a is null) return Resultado<long>.Falha($"Não há apuração de PIS/COFINS de {comp:MM/yyyy}.");
        if (a.LancamentoId is not null) return Resultado<long>.Falha("A provisão desta apuração já está contabilizada.");
        var r = await ContabilizarAsync(empresaId, comp.AddMonths(1).AddDays(-1), $"Provisão de PIS/COFINS {comp:MM/yyyy}",
            [("PIS", a.Resultado.PisAPagar), ("COFINS", a.Resultado.CofinsAPagar)], usuarioId, ct);
        if (r.Sucesso) await repositorio.DefinirLancamentoAsync(empresaId, TipoApuracao.PisCofins, a.Id, r.Valor, ct);
        return r;
    }

    /// <summary>Estorna o lançamento da provisão (na data dele) e libera a apuração para recálculo.</summary>
    public async Task<Resultado> DesfazerAsync(long empresaId, TipoApuracao tipo, long apuracaoId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FiscalApurar, ct) is { } negado) return Resultado.Falha(negado);
        long? lancamentoId = tipo switch
        {
            TipoApuracao.Simples => (await simples.ListarApuracoesAsync(empresaId, 0, ct)).FirstOrDefault(a => a.Id == apuracaoId)?.LancamentoId,
            TipoApuracao.Lucro => (await lucro.ListarApuracoesAsync(empresaId, 0, ct)).FirstOrDefault(a => a.Id == apuracaoId)?.LancamentoId,
            _ => (await lucro.ListarPisCofinsAsync(empresaId, 0, ct)).FirstOrDefault(a => a.Id == apuracaoId)?.LancamentoId,
        };
        if (lancamentoId is not { } lanc) return Resultado.Falha("Esta apuração não tem provisão contabilizada.");
        var data = await contas.ObterDataLancamentoAsync(lanc, ct) ?? DateOnly.FromDateTime(DateTime.Today);
        var r = await lancamentos.EstornarAsync(empresaId, lanc, data, "Desfazer provisão de tributo apurado", usuarioId, ct);
        if (!r.Sucesso) return Resultado.Falha(r.Erros);
        await repositorio.DefinirLancamentoAsync(empresaId, tipo, apuracaoId, null, ct);
        return Resultado.Ok();
    }
}
