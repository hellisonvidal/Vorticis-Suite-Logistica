using Vorticis.Application.Relatorios;
using Vorticis.Desktop.Comum.Modulos;

namespace Vorticis.Frota.Desktop;

/// <summary>Gráficos da Frota e do TMS (Relatórios › Gráficos).</summary>
public static class GraficosFrota
{
    private static ServicoFrota F(ContextoGrafico c) => ServicosFrota.De(c.App);

    private static readonly DefinicaoGrafico CustosFrota = new("custosfrota", "Custos da frota por mês", "Frota", async c =>
    {
        var ab = await F(c).AbastecimentosAsync(c.Empresa, c.Inicio, c.Fim, null, c.Usuario);
        var os = (await F(c).OrdensAsync(c.Empresa, c.Inicio, c.Fim, c.Usuario)).Where(o => o.Situacao is not ("CANCELADA" or "REPROVADA")).ToList();
        var vi = (await F(c).ViagensAsync(c.Empresa, c.Inicio, c.Fim, c.Usuario)).Where(v => v.Situacao != "CANCELADA").ToList();
        var pe = await F(c).PassagensPedagioAsync(c.Empresa, c.Inicio, c.Fim, null, false, c.Usuario);
        var d = MontagemGraficos.PorMes<(DateOnly Data, decimal Valor, int Serie)>("Custos da frota por mês", "R$", FormaGrafico.Barras, c.Inicio, c.Fim, x => x.Data,
            ("Combustível", ab.Select(a => (a.Data, a.ValorTotal, 0)), x => x.Valor),
            ("Manutenção", os.Select(o => (o.Data, o.Total, 1)), x => x.Valor),
            ("Despesas de viagem", vi.Select(v => (v.Saida, v.Despesas, 2)), x => x.Valor),
            ("Pedágio", pe.Select(p => (DateOnly.FromDateTime(p.DataHora), p.Valor, 3)), x => x.Valor));
        return d with { Nota = "Combustível pelos abastecimentos; manutenção pelas ordens de serviço (sem as canceladas e reprovadas); despesas de viagem = pedágio, diárias e outras lançadas na viagem; pedágio pelas passagens importadas." };
    });

    private static readonly DefinicaoGrafico ConsumoVeiculo = new("consumoveiculo", "Consumo médio por veículo (km/l)", "Frota", async c =>
    {
        var ab = await F(c).AbastecimentosAsync(c.Empresa, c.Inicio, c.Fim, null, c.Usuario);
        var placas = (await F(c).VeiculosAsync(c.Empresa, c.Usuario)).ToDictionary(v => v.Id, v => RegrasFrota.FormatarPlaca(v.Placa));
        var medias = RegrasFrota.Medias(ab).GroupBy(m => m.VeiculoId)
            .Select(g => (Placa: placas.GetValueOrDefault(g.Key, "?"), Media: g.Sum(m => m.Litros) > 0 ? Math.Round(g.Sum(m => m.KmRodado) / g.Sum(m => m.Litros), 2) : 0m))
            .OrderByDescending(x => x.Media).ToList();
        return new DadosGrafico("Consumo médio por veículo", "km/l", FormaGrafico.Barras, [.. medias.Select(m => m.Placa)],
                                [new SerieGrafico("Média km/l", [.. medias.Select(m => m.Media)])], "Média pelo método do tanque cheio (km rodado ÷ litros dos ciclos fechados no período).");
    });

    private static readonly DefinicaoGrafico PedagioVeiculo = new("pedagioveiculo", "Pedágio por veículo", "Frota", async c =>
        MontagemGraficos.PorCategoria("Pedágio por veículo", "R$", await F(c).PassagensPedagioAsync(c.Empresa, c.Inicio, c.Fim, null, false, c.Usuario),
                                      p => RegrasFrota.FormatarPlaca(p.Placa), "Pedágio", p => p.Valor, nota: "Passagens importadas em Operações › Pedágio."));

    private static readonly DefinicaoGrafico FretesMes = new("fretesmes", "Frete emitido por mês", "Transporte", async c =>
    {
        var ctes = (await F(c).CtesAsync(c.Empresa, c.Inicio, c.Fim, null, c.Usuario)).Where(x => RegrasTms.CteEmitido(x.Situacao)).ToList();
        return MontagemGraficos.PorMes("Frete emitido por mês", "R$", FormaGrafico.Barras, c.Inicio, c.Fim, (Cte x) => x.Emissao,
                                       ("Frete (total da prestação)", ctes, x => x.ValorTotal), ("ICMS", ctes, x => x.Frete.Icms)) with
               { Nota = "CT-e emitidos, em trânsito ou entregues (digitados e cancelados não entram)." };
    });

    private static readonly DefinicaoGrafico EntregasPrazo = new("entregasprazo", "Entregas no prazo e com atraso", "Transporte", async c =>
    {
        var entregues = (await F(c).CtesAsync(c.Empresa, c.Inicio.AddMonths(-3), c.Fim, null, c.Usuario))
            .Where(x => x is { Situacao: "ENTREGUE", EntregueEm: not null, PrevisaoEntrega: not null }).ToList();
        DateOnly Dia(Cte x) => DateOnly.FromDateTime(x.EntregueEm!.Value);
        return MontagemGraficos.PorMes("Entregas por mês", "CT-e", FormaGrafico.Barras, c.Inicio, c.Fim, (Cte x) => Dia(x),
                                       ("No prazo", entregues.Where(x => Dia(x) <= x.PrevisaoEntrega), _ => 1m),
                                       ("Com atraso", entregues.Where(x => Dia(x) > x.PrevisaoEntrega), _ => 1m)) with
               { Nota = "Pelo mês da entrega; o prazo é a previsão do CT-e (emissão + prazo da tabela de frete)." };
    });

    private static readonly DefinicaoGrafico FretesCliente = new("fretescliente", "Frete por cliente", "Transporte", async c =>
        MontagemGraficos.PorCategoria("Frete por cliente", "R$", (await F(c).CtesAsync(c.Empresa, c.Inicio, c.Fim, null, c.Usuario)).Where(x => RegrasTms.CteEmitido(x.Situacao)),
                                      x => x.Tomador ?? "", "Frete", x => x.ValorTotal, nota: "Os maiores tomadores; os demais somados em Outros."));

    public static IReadOnlyList<DefinicaoGrafico> Todos { get; } = [CustosFrota, ConsumoVeiculo, PedagioVeiculo, FretesMes, EntregasPrazo, FretesCliente];
}
