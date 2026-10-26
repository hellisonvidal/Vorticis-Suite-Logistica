using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Application.Contabil;

public sealed class ServicoRelatorios(IConsultasContabeis consultas, Autorizador autorizador)
{
    public async Task<Resultado<IReadOnlyList<LinhaBalancete>>> BalanceteAsync(long empresaId, DateOnly inicio, DateOnly fim, long usuarioId,
                                                                              bool incluirZeradas = false, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<LinhaBalancete>>(usuarioId, empresaId, Permissao.RelatorioBalancete, ct) is { } negado) return negado;
        if (fim < inicio) return Resultado<IReadOnlyList<LinhaBalancete>>.Falha("Data final anterior à inicial.");
        var plano = await consultas.ObterPlanoAsync(empresaId, ct);
        var movimentos = await consultas.ObterMovimentosAsync(empresaId, inicio, fim, ct);
        return Resultado<IReadOnlyList<LinhaBalancete>>.Ok(Balancete.Montar(plano, movimentos, incluirZeradas));
    }

    /// <summary>Detalhamento do balancete por participante (plano com participantes): saldos de cada cliente/fornecedor na conta.</summary>
    public async Task<Resultado<IReadOnlyList<MovimentoParticipante>>> ParticipantesBalanceteAsync(long empresaId, DateOnly inicio, DateOnly fim,
                                                                                                 long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<MovimentoParticipante>>(usuarioId, empresaId, Permissao.RelatorioBalancete, ct) is { } negado)
            return negado;
        var lista = await consultas.ObterMovimentosParticipantesAsync(empresaId, inicio, fim, ct);
        return Resultado<IReadOnlyList<MovimentoParticipante>>.Ok(lista.Where(m => m.SaldoAnterior != 0 || m.Debitos != 0 || m.Creditos != 0).ToList());
    }

    public async Task<Resultado<RazaoConta>> RazaoAsync(long empresaId, string codigoConta, DateOnly inicio, DateOnly fim, long usuarioId,
                                                       CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<RazaoConta>(usuarioId, empresaId, Permissao.RelatorioRazao, ct) is { } negado) return negado;
        try
        {
            return await consultas.ObterRazaoAsync(empresaId, codigoConta, inicio, fim, ct) is { } razao
                ? Resultado<RazaoConta>.Ok(razao)
                : Resultado<RazaoConta>.Falha($"Conta {codigoConta} não encontrada.");
        }
        catch (ArgumentException ex)
        {
            return Resultado<RazaoConta>.Falha(ex.Message);
        }
    }

    public async Task<Resultado<IReadOnlyList<LinhaDiario>>> DiarioAsync(long empresaId, DateOnly inicio, DateOnly fim, long usuarioId,
                                                                        CancellationToken ct = default)
    {
        // A tela de lançamento lista os lançamentos efetuados: quem lança ou estorna também pode consultá-los.
        var p = await autorizador.ObterAsync(usuarioId, empresaId, ct);
        if (!p.Pode(Permissao.RelatorioDiario) && !p.Pode(Permissao.LancamentoEstornar) && !p.Pode(Permissao.LancamentoIncluir))
            return await autorizador.NegarAsync<IReadOnlyList<LinhaDiario>>(usuarioId, empresaId, Permissao.RelatorioDiario, ct)
                   ?? Resultado<IReadOnlyList<LinhaDiario>>.Falha("Acesso negado.");
        return Resultado<IReadOnlyList<LinhaDiario>>.Ok(await consultas.ObterDiarioAsync(empresaId, inicio, fim, ct));
    }
}
