using Vorticis.Application.Relatorios;
using Vorticis.Domain.Comum;

namespace Vorticis.Frota;

public sealed partial class ServicoFrota
{
    /// <summary>Relatórios de consumo, manutenção, viagens, coletas, diárias e resumo; a permissão já foi conferida por <see cref="RelatorioAsync"/>.</summary>
    private async Task<Resultado<TabelaRelatorio>> RelatorioNovoAsync(long empresa, string titulo, DateOnly inicio, DateOnly fim, FiltroRelatorioFrota f, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var cfg = await ConfiguracaoAsync(empresa, ct);
        bool Veic(long? id) => f.VeiculoId is null || id == f.VeiculoId;
        bool Mot(long? id) => f.MotoristaId is null || id == f.MotoristaId;
        TabelaRelatorio tabela;
        switch (titulo)
        {
            case TabelasFrota.TituloConsumoVeiculo or TabelasFrota.TituloConsumoMotorista:
                var ab = (await ListarAbastecimentosAsync(empresa, inicio, fim, null, ct)).Where(a => Veic(a.VeiculoId) && Mot(a.MotoristaId)).ToList();
                tabela = TabelasFrota.ConsumoResumo([.. (await ListarVeiculosAsync(empresa, ct)).Where(v => Veic(v.Id))], await ListarMotoristasAsync(empresa, ct), ab,
                                                    cfg.ToleranciaConsumo, titulo == TabelasFrota.TituloConsumoMotorista);
                break;
            case TabelasFrota.TituloPreventivas:
                tabela = TabelasFrota.Preventivas([.. (await ListarPlanosAsync(empresa, ct)).Where(p => Veic(p.VeiculoId))], hoje, cfg.DiasAviso);
                break;
            case TabelasFrota.TituloHistoricoManutencao:
                tabela = TabelasFrota.HistoricoManutencao([.. (await ListarOrdensAsync(empresa, inicio, fim, ct)).Where(o => o.Data >= inicio && o.Data <= fim && Veic(o.VeiculoId))]);
                break;
            case TabelasFrota.TituloViagensMotorista or TabelasFrota.TituloViagensCliente or TabelasFrota.TituloViagensRota:
                tabela = TabelasFrota.ViagensPor([.. (await ListarViagensAsync(empresa, inicio, fim, ct)).Where(v => v.Saida >= inicio && v.Saida <= fim && Veic(v.VeiculoId) && Mot(v.MotoristaId))], titulo);
                break;
            case TabelasFrota.TituloColetasCliente or TabelasFrota.TituloColetasRota or TabelasFrota.TituloColetasMotorista:
                tabela = TabelasFrota.ColetasPor([.. (await ListarColetasAsync(empresa, inicio, fim, null, ct)).Where(c => Veic(c.VeiculoId) && Mot(c.MotoristaId))], titulo);
                break;
            case TabelasFrota.TituloDiariasAcerto:
                tabela = TabelasFrota.DiariasAcerto([.. (await ListarDiariasAsync(empresa, inicio, fim, f.MotoristaId, null, ct)).Where(d => Veic(d.VeiculoId))]);
                break;
            default:   // resumo gerencial
                var dias = fim.DayNumber - inicio.DayNumber + 1;
                var desde = inicio.AddDays(-dias);
                var veiculos = (await ListarVeiculosAsync(empresa, ct)).Where(v => v.Ativo).ToList();
                var ordens = await ListarOrdensAsync(empresa, desde, fim, ct);
                var vencidos = (await AvisosInternosAsync(empresa, hoje, cfg, ct)).Count(a => a.Gravidade == GravidadeAviso.Vencido);
                tabela = TabelasFrota.Resumo(inicio, fim, veiculos.Count, await ListarAbastecimentosAsync(empresa, desde, fim, null, ct), ordens,
                                             await ListarViagensAsync(empresa, desde, fim, ct), await ListarDiariasAsync(empresa, desde, fim, null, null, ct),
                                             await ListarColetasAsync(empresa, desde, fim, null, ct), ordens.Count(o => o.Situacao is not ("CONCLUIDA" or "CANCELADA" or "REPROVADA")), vencidos);
                break;
        }
        return Resultado<TabelaRelatorio>.Ok(tabela);
    }
}
