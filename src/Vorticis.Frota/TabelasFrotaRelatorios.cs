using Vorticis.Application.Relatorios;
using Vorticis.Domain.Comum;

namespace Vorticis.Frota;

/// <summary>Relatórios de consumo, manutenção, viagens, coletas, diárias e o resumo gerencial da frota.</summary>
public static partial class TabelasFrota
{
    public const string TituloConsumoVeiculo = "Consumo por veículo";
    public const string TituloConsumoMotorista = "Consumo por motorista";
    public const string TituloPreventivas = "Manutenções preventivas";
    public const string TituloHistoricoManutencao = "Histórico de manutenção por veículo";
    public const string TituloViagensMotorista = "Viagens por motorista";
    public const string TituloViagensCliente = "Viagens por cliente";
    public const string TituloViagensRota = "Viagens por rota";
    public const string TituloColetasCliente = "Coletas por cliente";
    public const string TituloColetasRota = "Coletas por rota";
    public const string TituloColetasMotorista = "Coletas por motorista";
    public const string TituloDiariasAcerto = "Diárias pagas e acertadas";
    public const string TituloResumo = "Resumo gerencial da frota";

    /// <summary>Relatórios que não pedem período (mostram a situação de hoje).</summary>
    public static bool SemPeriodo(string titulo) => titulo is TituloVencimentos or TituloPreventivas;

    /// <summary>Relatórios que aceitam os filtros de veículo e motorista.</summary>
    public static bool UsaFiltros(string titulo) => titulo is TituloConsumoVeiculo or TituloConsumoMotorista or TituloPreventivas or TituloHistoricoManutencao
        or TituloViagensMotorista or TituloViagensCliente or TituloViagensRota or TituloColetasCliente or TituloColetasRota or TituloColetasMotorista or TituloDiariasAcerto;

    private static decimal Div(decimal a, decimal b, int casas = 2) => b > 0 ? Math.Round(a / b, casas) : 0;

    // ------------------------------------------------------------------ consumo

    /// <summary>
    /// Consumo (km/l pelo tanque cheio) resumido por veículo ou por motorista no período: litros e valor abastecidos (sem Arla), preço médio do litro,
    /// km rodado e média. Por motorista, cada média é atribuída a quem fez o abastecimento que fechou o ciclo.
    /// </summary>
    public static TabelaRelatorio ConsumoResumo(IReadOnlyList<Veiculo> veiculos, IReadOnlyList<Motorista> motoristas, IReadOnlyList<Abastecimento> abastecimentos, decimal tolerancia, bool porMotorista)
    {
        var comb = abastecimentos.Where(a => a.Combustivel != "ARLA32").ToList();
        var medias = RegrasFrota.Medias(abastecimentos);
        var linhas = new List<LinhaRelatorio>();
        IEnumerable<(string Nome, List<Abastecimento> Ab, List<MediaConsumo> Md, decimal? Esperada)> grupos = porMotorista
            ? motoristas.OrderBy(m => m.Nome).Select(m => (m.Nome, comb.Where(a => a.MotoristaId == m.Id).ToList(), medias.Where(x => x.MotoristaId == m.Id).ToList(), (decimal?)null))
                        .Append(("(sem motorista)", comb.Where(a => a.MotoristaId is null).ToList(), medias.Where(x => x.MotoristaId is null).ToList(), null))
            : veiculos.OrderBy(v => v.Codigo).Select(v => (v.Descricao, comb.Where(a => a.VeiculoId == v.Id).ToList(),
                                                          medias.Where(x => x.VeiculoId == v.Id).ToList(), v.MediaEsperada));
        decimal tLit = 0, tVal = 0, tKm = 0, tLitMedia = 0;
        var n = 0;
        foreach (var (nome, ab, md, esperada) in grupos)
        {
            if (ab.Count == 0) continue;
            var lit = ab.Sum(a => a.Litros);
            var val = ab.Sum(a => a.ValorTotal);
            var km = md.Sum(x => x.KmRodado);
            var litMedia = md.Sum(x => x.Litros);
            var media = Div(km, litMedia);
            var fora = md.Count > 0 && esperada is not null && RegrasFrota.ConsumoForaDoEsperado(media, esperada, tolerancia);
            linhas.Add(L(fora, nome, (long)ab.Count, lit, val, Div(val, lit, 3), km, md.Count > 0 ? media : null, esperada, fora ? "ABAIXO DO ESPERADO" : ""));
            (tLit, tVal, tKm, tLitMedia) = (tLit + lit, tVal + val, tKm + km, tLitMedia + litMedia);
            n += ab.Count;
        }
        linhas.Add(L(true, "TOTAL", (long)n, tLit, tVal, Div(tVal, tLit, 3), tKm, tLitMedia > 0 ? Div(tKm, tLitMedia) : null, null, ""));
        return new TabelaRelatorio(porMotorista ? TituloConsumoMotorista : TituloConsumoVeiculo,
        [
            new(porMotorista ? "Motorista" : "Veículo"), new("Abastecimentos", TipoColuna.Inteiro, 2.6), new("Litros", TipoColuna.Valor), new("Valor", TipoColuna.Valor),
            new("Preço médio do litro", TipoColuna.Valor), new("Km rodado", TipoColuna.Valor), new("Média km/l", TipoColuna.Valor), new("Esperada km/l", TipoColuna.Valor), new("Situação"),
        ], linhas, "Média pelo método do tanque cheio (só os ciclos fechados dentro do período). Arla 32 não entra.");
    }

    // ------------------------------------------------------------------ manutenção

    /// <summary>Planos de manutenção ativos: próxima manutenção por km e por data, quanto falta e a situação (vencida, a vencer ou em dia). Mais urgentes primeiro.</summary>
    public static TabelaRelatorio Preventivas(IReadOnlyList<PlanoManutencao> planos, DateOnly hoje, int diasAviso)
    {
        var situacoes = planos.Where(p => p.Ativo).Select(p => RegrasFrota.Situacao(p, p.KmAtual, hoje, diasAviso))
            .OrderBy(s => s.Gravidade is null ? 9 : (int)s.Gravidade).ThenBy(s => s.Plano.Placa).ThenBy(s => s.Plano.Descricao).ToList();
        var linhas = situacoes.Select(s => L(s.Gravidade == GravidadeAviso.Vencido, RegrasFrota.FormatarPlaca(s.Plano.Placa ?? ""), s.Plano.Descricao, s.Plano.UltimaKm, s.Plano.UltimaData,
                                             s.ProximaKm, s.Plano.KmAtual, s.ProximaKm is { } k ? k - s.Plano.KmAtual : null, s.ProximaData,
                                             s.ProximaData is { } d ? (long)(d.DayNumber - hoje.DayNumber) : null, s.Texto)).ToList();
        linhas.Add(L(true, "TOTAL", $"{situacoes.Count} plano(s): {situacoes.Count(s => s.Gravidade == GravidadeAviso.Vencido)} vencida(s), {situacoes.Count(s => s.Gravidade == GravidadeAviso.AVencer)} a vencer",
                     null, null, null, null, null, null, null, ""));
        return new TabelaRelatorio(TituloPreventivas,
        [
            new("Placa", LarguraCm: 2), new("Serviço"), new("Última km", TipoColuna.Valor), new("Última data", TipoColuna.Data), new("Próxima km", TipoColuna.Valor),
            new("Km atual", TipoColuna.Valor), new("Faltam km", TipoColuna.Valor), new("Próxima data", TipoColuna.Data), new("Dias", TipoColuna.Inteiro, 1.4), new("Situação", LarguraCm: 2.4),
        ], linhas, "Faltam km negativo = passou do km previsto. Dias negativo = passou da data prevista.");
    }

    /// <summary>Ordens de serviço do período agrupadas por veículo, com subtotal; canceladas e reprovadas aparecem mas não somam.</summary>
    public static TabelaRelatorio HistoricoManutencao(IReadOnlyList<OrdemServico> ordens)
    {
        var linhas = new List<LinhaRelatorio>();
        decimal tP = 0, tM = 0;
        foreach (var g in ordens.OrderBy(o => o.Placa).ThenBy(o => o.Data).ThenBy(o => o.Numero).GroupBy(o => o.Placa ?? ""))
        {
            foreach (var o in g)
                linhas.Add(L(false, RegrasFrota.FormatarPlaca(g.Key), o.Numero, o.Data, RegrasFrota.Rotulo(o.Tipo), o.Descricao, o.Oficina ?? "", o.Km, RegrasFrota.RotuloManutencao(o.Situacao),
                             o.ValorPecas, o.ValorMaoObra, o.Total));
            var validas = g.Where(o => o.Situacao is not ("CANCELADA" or "REPROVADA")).ToList();
            linhas.Add(L(true, $"Subtotal – {RegrasFrota.FormatarPlaca(g.Key)}", null, null, "", $"{validas.Count} ordem(ns)", "", null, "", validas.Sum(o => o.ValorPecas), validas.Sum(o => o.ValorMaoObra),
                         validas.Sum(o => o.Total)));
            tP += validas.Sum(o => o.ValorPecas); tM += validas.Sum(o => o.ValorMaoObra);
        }
        linhas.Add(L(true, "TOTAL", null, null, "", $"{ordens.Count} ordem(ns)", "", null, "", tP, tM, tP + tM));
        return new TabelaRelatorio(TituloHistoricoManutencao,
        [
            new("Placa", LarguraCm: 3), new("OS nº", TipoColuna.Inteiro, 1.4), new("Data", TipoColuna.Data), new("Tipo", LarguraCm: 2), new("Serviço"), new("Oficina"),
            new("Km", TipoColuna.Valor), new("Situação", LarguraCm: 3.2), new("Peças", TipoColuna.Valor), new("Mão de obra", TipoColuna.Valor), new("Total", TipoColuna.Valor),
        ], linhas);
    }

    // ------------------------------------------------------------------ viagens e coletas

    /// <summary>Viagens do período (sem as canceladas) somadas por motorista, cliente ou rota (origem → destino): frete, despesas e resultado.</summary>
    public static TabelaRelatorio ViagensPor(IReadOnlyList<Viagem> viagens, string titulo)
    {
        string Chave(Viagem v) => titulo switch
        {
            TituloViagensMotorista => v.MotoristaNome ?? "(sem motorista)",
            TituloViagensCliente => string.IsNullOrWhiteSpace(v.Cliente) ? "(sem cliente)" : v.Cliente!,
            _ => $"{v.Origem} → {v.Destino}",
        };
        var linhas = new List<LinhaRelatorio>();
        decimal tFrete = 0, tDesp = 0, tKm = 0;
        var n = 0;
        foreach (var g in viagens.Where(v => v.Situacao != "CANCELADA").GroupBy(Chave).OrderByDescending(g => g.Sum(v => v.ValorFrete)).ThenBy(g => g.Key))
        {
            var frete = g.Sum(v => v.ValorFrete);
            var desp = g.Sum(v => v.Despesas);
            var km = g.Sum(v => v.KmRodado ?? 0);
            linhas.Add(L(frete - desp < 0, g.Key, (long)g.Count(), km, frete, desp, frete - desp, km > 0 ? Div(frete, km) : null, Div(frete, g.Count())));
            (tFrete, tDesp, tKm) = (tFrete + frete, tDesp + desp, tKm + km);
            n += g.Count();
        }
        linhas.Add(L(true, "TOTAL", (long)n, tKm, tFrete, tDesp, tFrete - tDesp, tKm > 0 ? Div(tFrete, tKm) : null, n > 0 ? Div(tFrete, n) : null));
        return new TabelaRelatorio(titulo,
        [
            new(titulo switch { TituloViagensMotorista => "Motorista", TituloViagensCliente => "Cliente", _ => "Rota" }), new("Viagens", TipoColuna.Inteiro, 1.8), new("Km rodado", TipoColuna.Valor),
            new("Frete", TipoColuna.Valor), new("Despesas", TipoColuna.Valor), new("Frete − despesas", TipoColuna.Valor), new("Frete por km", TipoColuna.Valor), new("Frete médio", TipoColuna.Valor),
        ], linhas, "Despesas = pedágio + diárias + outras despesas lançadas na viagem. Ordenado do maior frete para o menor.");
    }

    /// <summary>Coletas do período somadas por cliente (remetente), rota ou motorista: quantidade, volumes, peso e valor da mercadoria.</summary>
    public static TabelaRelatorio ColetasPor(IReadOnlyList<Coleta> coletas, string titulo)
    {
        string Chave(Coleta c) => titulo switch
        {
            TituloColetasCliente => c.Remetente ?? "(sem remetente)",
            TituloColetasMotorista => c.MotoristaNome ?? "(sem motorista)",
            _ => c.RotaNome ?? "(sem rota)",
        };
        var linhas = new List<LinhaRelatorio>();
        long tQtd = 0, tVol = 0, tConc = 0, tCanc = 0;
        decimal tPeso = 0, tValor = 0;
        foreach (var g in coletas.GroupBy(Chave).OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
        {
            long conc = g.Count(c => c.Situacao == "COLETADA"), canc = g.Count(c => c.Situacao == "CANCELADA"), vol = g.Sum(c => c.Volumes);
            linhas.Add(L(false, g.Key, (long)g.Count(), conc, canc, vol, g.Sum(c => c.PesoKg), g.Sum(c => c.ValorMercadoria)));
            (tQtd, tVol, tConc, tCanc, tPeso, tValor) = (tQtd + g.Count(), tVol + vol, tConc + conc, tCanc + canc, tPeso + g.Sum(c => c.PesoKg), tValor + g.Sum(c => c.ValorMercadoria));
        }
        linhas.Add(L(true, "TOTAL", tQtd, tConc, tCanc, tVol, tPeso, tValor));
        return new TabelaRelatorio(titulo,
        [
            new(titulo switch { TituloColetasCliente => "Cliente (remetente)", TituloColetasMotorista => "Motorista", _ => "Rota" }), new("Coletas", TipoColuna.Inteiro, 1.8),
            new("Coletadas", TipoColuna.Inteiro, 2), new("Canceladas", TipoColuna.Inteiro, 2), new("Volumes", TipoColuna.Inteiro, 2), new("Peso (kg)", TipoColuna.Valor), new("Valor da mercadoria", TipoColuna.Valor),
        ], linhas);
    }

    // ------------------------------------------------------------------ diárias

    /// <summary>Diárias já pagas: valor pago, valor gasto no acerto e saldo; as com adiantamento ainda sem acerto ficam em destaque.</summary>
    public static TabelaRelatorio DiariasAcerto(IReadOnlyList<Diaria> diarias)
    {
        var pagas = diarias.Where(d => d.Situacao is "PAGA" or "ACERTADA").OrderBy(d => d.Data).ThenBy(d => d.Numero).ToList();
        string Estado(Diaria d) => d.Situacao == "ACERTADA" ? "Acertada" : d.Adiantamento > 0 ? "Aguardando acerto" : "Paga (sem acerto)";
        var linhas = pagas.Select(d => L(d.Situacao == "PAGA" && d.Adiantamento > 0, d.Data, d.Numero, d.Colaborador, d.FrotaNome ?? "", d.ValorPagamento, d.ValorGasto, d.Saldo, Estado(d))).ToList();
        var aguardando = pagas.Where(d => d.Situacao == "PAGA" && d.Adiantamento > 0).ToList();
        var acertadas = pagas.Where(d => d.Situacao == "ACERTADA").ToList();
        linhas.Add(L(true, null, null, $"{pagas.Count} paga(s): {acertadas.Count} acertada(s), {aguardando.Count} aguardando acerto", "", pagas.Sum(d => d.ValorPagamento),
                     acertadas.Sum(d => d.ValorGasto ?? 0), acertadas.Sum(d => d.Saldo ?? 0), $"A acertar: {Formato.Moeda(aguardando.Sum(d => d.Adiantamento))}"));
        return new TabelaRelatorio(TituloDiariasAcerto,
        [
            new("Data", TipoColuna.Data), new("Nº", TipoColuna.Inteiro, 1.2), new("Colaborador"), new("Frota"), new("Valor pago", TipoColuna.Valor), new("Valor gasto", TipoColuna.Valor),
            new("Saldo do acerto", TipoColuna.Valor), new("Situação", LarguraCm: 4),
        ], linhas, "Saldo do acerto: positivo = o colaborador devolve; negativo = a empresa paga a diferença. Totais de gasto e saldo consideram só as acertadas.");
    }

    // ------------------------------------------------------------------ resumo gerencial

    /// <summary>
    /// Indicadores do período comparados com o período anterior (mesmo número de dias, logo antes). Os dados devem cobrir os dois períodos.
    /// </summary>
    public static TabelaRelatorio Resumo(DateOnly inicio, DateOnly fim, int veiculosAtivos, IReadOnlyList<Abastecimento> abastecimentos, IReadOnlyList<OrdemServico> ordens,
                                         IReadOnlyList<Viagem> viagens, IReadOnlyList<Diaria> diarias, IReadOnlyList<Coleta> coletas, int ordensEmAndamento, int avisosVencidos)
    {
        var dias = fim.DayNumber - inicio.DayNumber + 1;
        var (ai, af) = (inicio.AddDays(-dias), inicio.AddDays(-1));

        decimal[] Calc(DateOnly i, DateOnly f)
        {
            var ab = abastecimentos.Where(a => a.Data >= i && a.Data <= f).ToList();
            var vi = viagens.Where(v => v.Saida >= i && v.Saida <= f && v.Situacao != "CANCELADA").ToList();
            var os = ordens.Where(o => o.Data >= i && o.Data <= f && o.Situacao is not ("CANCELADA" or "REPROVADA")).Sum(o => o.Total);
            var dia = diarias.Where(d => d.Data >= i && d.Data <= f).Sum(d => d.Total);
            var co = coletas.Where(c => DateOnly.FromDateTime(c.AgendadaEm) >= i && DateOnly.FromDateTime(c.AgendadaEm) <= f).ToList();
            var md = RegrasFrota.Medias(ab);
            var comb = ab.Sum(a => a.ValorTotal);
            var frete = vi.Sum(v => v.ValorFrete);
            var km = vi.Sum(v => v.KmRodado ?? 0);
            var custo = comb + os + vi.Sum(v => v.Despesas) + dia;
            return [vi.Count, km, frete, vi.Sum(v => v.Despesas), comb, ab.Where(a => a.Combustivel != "ARLA32").Sum(a => a.Litros), Div(md.Sum(m => m.KmRodado), md.Sum(m => m.Litros)),
                    os, dia, custo, frete - custo, Div(custo, km), co.Count, co.Count(c => c.Situacao == "COLETADA")];
        }
        var (atual, anterior) = (Calc(inicio, fim), Calc(ai, af));
        (string Nome, string Tipo)[] defs =
        [
            ("Viagens (sem canceladas)", "n"), ("Km rodado nas viagens", "n"), ("Frete das viagens (R$)", "m"), ("Despesas de viagem (R$)", "m"), ("Combustível abastecido (R$)", "m"),
            ("Litros abastecidos", "n"), ("Média de consumo (km/l)", "m"), ("Manutenção – ordens do período (R$)", "m"), ("Diárias (R$)", "m"),
            ("Custo total: combustível + manutenção + despesas + diárias (R$)", "m"), ("Resultado: frete − custo total (R$)", "m"), ("Custo por km rodado (R$)", "m"),
            ("Coletas agendadas", "n"), ("Coletas realizadas", "n"),
        ];
        string F(decimal v, string t) => t == "m" ? Formato.Moeda(v) : v.ToString("N0", Formato.PtBr);
        var linhas = new List<LinhaRelatorio>();
        for (var i = 0; i < defs.Length; i++)
        {
            var (a, b) = (atual[i], anterior[i]);
            linhas.Add(L(false, defs[i].Nome, F(a, defs[i].Tipo), F(b, defs[i].Tipo), b == 0 ? "" : Formato.Moeda(Math.Round((a - b) / Math.Abs(b) * 100, 2)) + "%"));
        }
        linhas.Add(L(true, "Posição de hoje", "", "", ""));
        linhas.Add(L(false, "Veículos ativos", veiculosAtivos.ToString("N0", Formato.PtBr), "", ""));
        linhas.Add(L(false, "Ordens de serviço em andamento", ordensEmAndamento.ToString("N0", Formato.PtBr), "", ""));
        linhas.Add(L(avisosVencidos > 0, "Documentos, CNHs e manutenções vencidos", avisosVencidos.ToString("N0", Formato.PtBr), "", ""));
        return new TabelaRelatorio(TituloResumo,
        [
            new("Indicador"), new($"{inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}", LarguraCm: 4.2), new($"Anterior: {ai:dd/MM/yyyy} a {af:dd/MM/yyyy}", LarguraCm: 4.8), new("Variação", LarguraCm: 2.4),
        ], linhas, "Diárias lançadas nas viagens e as lançadas em Operação › Diárias podem se repetir se forem informadas nos dois lugares.");
    }
}
