using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Application.Contabil;

public sealed class ServicoDemonstracoes(IConsultasContabeis consultas, Autorizador autorizador)
{
    /// <summary>Balanço Patrimonial na data (saldos acumulados até ela).</summary>
    public async Task<Resultado<BalancoPatrimonial>> BalancoAsync(long empresaId, DateOnly data, short nivelMaximo, long usuarioId,
                                                                 CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<BalancoPatrimonial>(usuarioId, empresaId, Permissao.RelatorioDemonstracoes, ct) is { } negado) return negado;
        var plano = await consultas.ObterPlanoAsync(empresaId, ct);
        var saldos = await consultas.ObterMovimentosAsync(empresaId, data.AddDays(1), data, ct);   // saldo anterior ao dia seguinte = saldo na data
        var balancete = Balancete.Montar(plano, saldos.Select(s => s with { Debitos = 0, Creditos = 0 }).ToArray());
        return Resultado<BalancoPatrimonial>.Ok(Demonstracoes.MontarBalanco(data, plano, balancete, nivelMaximo));
    }

    /// <summary>
    /// DMPL e DLPA do período (modelos do CFC). O lucro líquido é o da DRE; os lançamentos de encerramento não entram como
    /// mutação (só transferem o resultado ao PL), então o saldo final confere com o Balanço na data final.
    /// </summary>
    public async Task<Resultado<DemonstracaoMutacoes>> MutacoesAsync(long empresaId, DateOnly inicio, DateOnly fim, long usuarioId,
                                                                    CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<DemonstracaoMutacoes>(usuarioId, empresaId, Permissao.RelatorioDemonstracoes, ct) is { } negado) return negado;
        if (fim < inicio) return Resultado<DemonstracaoMutacoes>.Falha("Data final anterior à inicial.");
        var plano = await consultas.ObterPlanoAsync(empresaId, ct);
        if (!plano.Any(c => c.CodNat == "03")) return Resultado<DemonstracaoMutacoes>.Falha("O plano de contas não tem contas de patrimônio líquido (natureza 03).");
        var saldos = (await consultas.ObterMovimentosAsync(empresaId, inicio, fim, ct)).ToDictionary(m => m.ContaId, m => m.SaldoAnterior);
        var semEncerramento = await consultas.ObterMovimentosAsync(empresaId, inicio, fim, ct, excluirEncerramento: true);
        var resultado = plano.Where(c => c.Analitica && c.CodNat == "04").Select(c => c.Id).ToHashSet();
        var lucro = semEncerramento.Where(m => resultado.Contains(m.ContaId)).Sum(m => m.Creditos - m.Debitos);
        var partidas = await consultas.ObterPartidasAsync(empresaId, inicio, fim, ct);
        return Resultado<DemonstracaoMutacoes>.Ok(Mutacoes.Montar(inicio, fim, plano, saldos, partidas, lucro));
    }

    /// <summary>DRE do período, desconsiderando os lançamentos de encerramento (que zeram as contas de resultado).</summary>
    public async Task<Resultado<DemonstracaoResultado>> DreAsync(long empresaId, DateOnly inicio, DateOnly fim, short nivelMaximo, long usuarioId,
                                                                CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<DemonstracaoResultado>(usuarioId, empresaId, Permissao.RelatorioDemonstracoes, ct) is { } negado) return negado;
        if (fim < inicio) return Resultado<DemonstracaoResultado>.Falha("Data final anterior à inicial.");
        var plano = await consultas.ObterPlanoAsync(empresaId, ct);
        var movimentos = await consultas.ObterMovimentosAsync(empresaId, inicio, fim, ct, excluirEncerramento: true);
        var balancete = Balancete.Montar(plano, movimentos.Select(m => m with { SaldoAnterior = 0 }).ToArray());
        return Resultado<DemonstracaoResultado>.Ok(Demonstracoes.MontarDre(inicio, fim, plano, balancete, nivelMaximo));
    }
}
