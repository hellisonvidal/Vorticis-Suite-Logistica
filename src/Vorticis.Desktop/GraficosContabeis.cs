using Vorticis.Application.Relatorios;
using Vorticis.Desktop.Comum.Modulos;

namespace Vorticis.Desktop;

/// <summary>Gráficos contábeis (Relatórios › Gráficos).</summary>
public static class GraficosContabeis
{
    private static readonly DefinicaoGrafico ResultadoMes = new("resultadomes", "Resultado contábil por mês", "Contábil", async c =>
    {
        var meses = MontagemGraficos.Meses(c.Inicio, c.Fim);
        var valores = new List<decimal>();
        foreach (var m in meses)
        {
            var fim = m.AddMonths(1).AddDays(-1);
            var r = await c.App.Demonstracoes.DreAsync(c.Empresa, m < c.Inicio ? c.Inicio : m, fim > c.Fim ? c.Fim : fim, 1, c.Usuario);
            valores.Add(r.Sucesso ? r.Valor.Resultado : 0);
        }
        return new DadosGrafico("Resultado contábil por mês", "R$", FormaGrafico.Barras, [.. meses.Select(MontagemGraficos.RotuloMes)],
                                [new SerieGrafico("Lucro (+) ou prejuízo (−)", valores)], "Resultado da DRE de cada mês, pelos lançamentos contábeis (antes do encerramento do exercício).");
    });

    public static IReadOnlyList<DefinicaoGrafico> Todos { get; } = [ResultadoMes];
}
