using Vorticis.Domain.Cadastros;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;

namespace Vorticis.Domain.Tests;

public class DinheiroTests
{
    [Fact]
    public void Rateio_soma_exatamente_o_total()
    {
        var parcelas = Dinheiro.Ratear(100m, [1, 1, 1]);
        Assert.Equal([33.34m, 33.33m, 33.33m], parcelas);
        Assert.Equal(100m, parcelas.Sum());
    }

    [Fact]
    public void Rateio_proporcional_com_pesos_desiguais()
    {
        var parcelas = Dinheiro.Ratear(1000.01m, [50, 30, 20]);
        Assert.Equal(1000.01m, parcelas.Sum());
        Assert.Equal([500.01m, 300.00m, 200.00m], parcelas);
    }

    [Fact]
    public void Rateio_rejeita_pesos_zerados() => Assert.Throws<ArgumentException>(() => Dinheiro.Ratear(10m, [0, 0]));

    [Theory]
    [InlineData(2.345, 2.35)]
    [InlineData(-2.345, -2.35)]
    [InlineData(2.344, 2.34)]
    public void Arredondamento_comercial(decimal valor, decimal esperado) => Assert.Equal(esperado, Dinheiro.Arredondar(valor));
}

public class CnpjTests
{
    [Theory]
    [InlineData("11.222.333/0001-81", true)]
    [InlineData("11222333000181", true)]
    [InlineData("11222333000182", false)]
    [InlineData("00000000000000", false)]
    [InlineData("1122233300018", false)]
    [InlineData("12.ABC.345/01DE-35", true)]   // exemplo oficial do CNPJ alfanumérico (RFB)
    [InlineData("12.ABC.345/01DE-36", false)]
    public void Valida_digitos_verificadores(string cnpj, bool valido) => Assert.Equal(valido, Cnpj.Valido(cnpj));
}

public class EstornoTests
{
    private static LancamentoRegistrado Original(StatusLancamento status = StatusLancamento.Efetivado,
                                                 OrigemLancamento origem = OrigemLancamento.Manual) => new(
        42, 1, 7, new(2026, 3, 10), status, origem, null,
        [new("1.1.01.001", IndicadorDC.D, 150m, "Venda à vista"), new("3.1.01.001", IndicadorDC.C, 150m, "Venda à vista")]);

    [Fact]
    public void Gera_lancamento_invertido_vinculado_ao_original()
    {
        var r = Estorno.Gerar(Original(), new(2026, 3, 15), "valor errado");

        Assert.True(r.Sucesso);
        Assert.Equal(OrigemLancamento.Estorno, r.Valor.Origem);
        Assert.Equal(42, r.Valor.EstornoDeId);
        Assert.Equal([IndicadorDC.C, IndicadorDC.D], r.Valor.Partidas.Select(p => p.DC));
        Assert.All(r.Valor.Partidas, p => Assert.StartsWith("Estorno do lançamento nº 7 (valor errado)", p.Historico));
    }

    [Fact]
    public void Nao_estorna_duas_vezes_nem_estorno_nem_data_anterior()
    {
        Assert.Contains(Estorno.Gerar(Original(StatusLancamento.Estornado), new(2026, 3, 15), "x").Erros, e => e.Contains("já foi estornado"));
        Assert.Contains(Estorno.Gerar(Original(origem: OrigemLancamento.Estorno), new(2026, 3, 15), "x").Erros, e => e.Contains("estornar um estorno"));
        Assert.Contains(Estorno.Gerar(Original(), new(2026, 3, 9), "x").Erros, e => e.Contains("anterior"));
        Assert.Contains(Estorno.Gerar(Original(), new(2026, 3, 15), " ").Erros, e => e.Contains("motivo"));
    }
}

public class BalanceteTests
{
    private static readonly ContaPlano[] Plano =
    [
        new(1, "1", "ATIVO", 1, false, null, IndicadorDC.D),
        new(2, "1.1", "CIRCULANTE", 2, false, 1, IndicadorDC.D),
        new(3, "1.1.01.001", "Caixa", 3, true, 2, IndicadorDC.D),
        new(4, "1.1.01.002", "Bancos", 3, true, 2, IndicadorDC.D),
        new(5, "3", "RECEITAS", 1, false, null, IndicadorDC.C),
        new(6, "3.1.01.001", "Vendas", 2, true, 5, IndicadorDC.C),
        new(7, "4.9", "Sem movimento", 1, true, null, IndicadorDC.D),
    ];

    [Fact]
    public void Totaliza_sinteticas_e_fecha_debito_com_credito()
    {
        var linhas = Balancete.Montar(Plano, [new(3, 100m, 50m, 30m), new(4, 0m, 200m, 0m), new(6, -100m, 0m, 220m)]);

        var ativo = linhas.Single(l => l.Codigo == "1");
        Assert.Equal((100m, 250m, 30m, 320m), (ativo.SaldoAnterior, ativo.Debitos, ativo.Creditos, ativo.SaldoFinal));
        Assert.Equal(-320m, linhas.Single(l => l.Codigo == "3").SaldoFinal);
        Assert.DoesNotContain(linhas, l => l.Codigo == "4.9");

        var analiticas = linhas.Where(l => l.Analitica).ToArray();
        Assert.Equal(analiticas.Sum(l => l.Debitos), analiticas.Sum(l => l.Creditos));
        Assert.Equal(0m, analiticas.Sum(l => l.SaldoFinal));
    }

    [Fact]
    public void Sinaliza_saldo_invertido()
    {
        var linhas = Balancete.Montar(Plano, [new(3, 0m, 0m, 10m), new(4, 0m, 10m, 0m)]);
        Assert.True(linhas.Single(l => l.Codigo == "1.1.01.001").SaldoInvertido);
        Assert.Equal("10,00 C", LinhaBalancete.Formatar(-10m));
    }
}

public class CpfTests
{
    [Theory]
    [InlineData("529.982.247-25", true)]
    [InlineData("52998224725", true)]
    [InlineData("529.982.247-24", false)]
    [InlineData("111.111.111-11", false)]
    [InlineData("1234567890", false)]
    public void Valida_digitos_verificadores(string cpf, bool valido) => Assert.Equal(valido, Cpf.Valido(cpf));

    [Fact]
    public void Formata_e_valida_uf() =>
        Assert.Equal(("529.982.247-25", true, false), (Cpf.Formatar("52998224725"), Uf.Valida("sp"), Uf.Valida("XX")));
}
