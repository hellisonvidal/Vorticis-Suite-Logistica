using Vorticis.Domain.Contabil;

namespace Vorticis.Domain.Tests;

public class DemonstracoesTests
{
    private static readonly ContaPlano[] Plano =
    [
        new(1, "1", "ATIVO", 1, false, null, IndicadorDC.D, "01"),
        new(2, "1.1", "Caixa", 2, true, 1, IndicadorDC.D, "01"),
        new(3, "2", "PASSIVO E PL", 1, false, null, IndicadorDC.C, "02"),
        new(4, "2.1", "Fornecedores", 2, true, 3, IndicadorDC.C, "02"),
        new(5, "2.3", "Capital", 2, true, 3, IndicadorDC.C, "03"),
        new(6, "3", "RECEITAS", 1, false, null, IndicadorDC.C, "04"),
        new(7, "3.1", "Vendas", 2, true, 6, IndicadorDC.C, "04"),
        new(8, "4", "DESPESAS", 1, false, null, IndicadorDC.D, "04"),
        new(9, "4.1", "Aluguel", 2, true, 8, IndicadorDC.D, "04"),
    ];

    // Capital 1.000; vendas 700 à vista; aluguel 200 pago; compra a prazo 0 → caixa 1.500, fornecedores 0
    private static IReadOnlyList<LinhaBalancete> Saldos() => Balancete.Montar(Plano,
        [new(2, 1_500m, 0, 0), new(4, -300m, 0, 0), new(5, -1_000m, 0, 0), new(7, -700m, 0, 0), new(9, 500m, 0, 0)]);

    [Fact]
    public void Balanco_fecha_incluindo_resultado_nao_encerrado()
    {
        var bp = Demonstracoes.MontarBalanco(new(2026, 12, 31), Plano, Saldos(), nivelMaximo: 2);
        Assert.Equal(1_500m, bp.TotalAtivo);
        Assert.Equal(200m, bp.ResultadoNaoEncerrado);   // 700 de receita − 500 de despesa
        Assert.True(bp.Fecha);
        Assert.Contains(bp.PassivoPl, l => l.Descricao.StartsWith("Lucro do período") && l.Valor == 200m);
        Assert.Equal(300m, bp.PassivoPl.Single(l => l.Codigo == "2.1").Valor);   // passivo apresentado positivo
    }

    [Fact]
    public void Dre_apresenta_receitas_positivas_e_resultado()
    {
        var mov = Balancete.Montar(Plano, [new(7, 0, 0, 700m), new(9, 0, 500m, 0)]);
        var dre = Demonstracoes.MontarDre(new(2026, 1, 1), new(2026, 12, 31), Plano, mov, nivelMaximo: 2);
        Assert.Equal(200m, dre.Resultado);
        Assert.Equal(700m, dre.Linhas.Single(l => l.Codigo == "3.1").Valor);
        Assert.Equal(-500m, dre.Linhas.Single(l => l.Codigo == "4.1").Valor);
        Assert.Equal("LUCRO LÍQUIDO DO PERÍODO", dre.Linhas[^1].Descricao);
    }

    [Fact]
    public void Nivel_maximo_oculta_contas_detalhadas()
    {
        var bp = Demonstracoes.MontarBalanco(new(2026, 12, 31), Plano, Saldos(), nivelMaximo: 1);
        Assert.DoesNotContain(bp.Ativo, l => l.Codigo == "1.1");
        Assert.Contains(bp.Ativo, l => l.Codigo == "1");
    }
}
