using Vorticis.Application.Relatorios;

namespace Vorticis.Domain.Tests;

public class GraficosTests
{
    [Fact]
    public void Por_mes_soma_cada_serie_no_mes_e_preenche_os_meses_vazios()
    {
        var itens = new[] { (new DateOnly(2026, 1, 5), 100m), (new DateOnly(2026, 1, 20), 50m), (new DateOnly(2026, 3, 2), 70m), (new DateOnly(2026, 4, 1), 999m) };
        var d = MontagemGraficos.PorMes("Teste", "R$", FormaGrafico.Barras, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), (ValueTuple<DateOnly, decimal> x) => x.Item1,
                                        ("A", itens, x => x.Item2), ("B", itens.Take(1), x => 1m));
        Assert.Equal(["jan/26", "fev/26", "mar/26"], d.Categorias);
        Assert.Equal([150m, 0m, 70m], d.Series[0].Valores);           // abril fica de fora do período
        Assert.Equal([1m, 0m, 0m], d.Series[1].Valores);
        Assert.False(d.Vazio);
    }

    [Fact]
    public void Por_categoria_ordena_e_junta_o_resto_em_Outros()
    {
        var itens = Enumerable.Range(1, 6).Select(i => ($"V{i}", (decimal)i * 10)).ToList();
        var d = MontagemGraficos.PorCategoria("Teste", "R$", itens, x => x.Item1, "Valor", x => x.Item2, limite: 4);
        Assert.Equal(["V6", "V5", "V4", "Outros"], d.Categorias);
        Assert.Equal([60m, 50m, 40m, 60m], d.Series.Single().Valores);   // 30 + 20 + 10
    }

    [Fact]
    public void Escala_comeca_no_zero_e_usa_passos_redondos()
    {
        Assert.Equal([0m, 200m, 400m, 600m, 800m, 1000m], MontagemGraficos.Escala(0, 950));
        Assert.Equal([-20m, 0m, 20m, 40m, 60m, 80m], MontagemGraficos.Escala(-15, 72));
        Assert.Equal([0m, 0.2m, 0.4m, 0.6m, 0.8m, 1m], MontagemGraficos.Escala(0, 0));
        Assert.Equal(new DateOnly(2026, 12, 1), MontagemGraficos.Meses(new DateOnly(2026, 11, 15), new DateOnly(2027, 1, 2))[1]);
    }
}
