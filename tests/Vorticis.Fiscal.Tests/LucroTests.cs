using Vorticis.Fiscal.Comum;
using Vorticis.Fiscal.Documentos;
using Vorticis.Fiscal.Guias;
using Vorticis.Fiscal.Lucro;
using Vorticis.Fiscal.Retencoes;

namespace Vorticis.Fiscal.Tests;

/// <summary>Casos conferidos à mão: presunção, adicional de IRPJ, CSLL, compensação de 30% e PIS/COFINS.</summary>
public class CalculadoraLucroTests
{
    private static readonly ParametrosLucro P = ParametrosLucro.Carregar(ParametrosEmbutidos.Json(ChavesParametro.LucroPresumidoReal));

    private static ResultadoLucro Presumido(decimal comercio, decimal servicos, decimal outras = 0, decimal irrf = 0, decimal csllRetida = 0) =>
        CalculadoraLucro.CalcularPresumido(P, new EntradaPresumido(2026, 1,
            [new ReceitaPresumida("COMERCIO_INDUSTRIA", comercio), new ReceitaPresumida("SERVICOS_GERAIS", servicos)], outras, irrf, csllRetida));

    [Fact]
    public void Parametros_tem_as_aliquotas_e_presuncoes_da_lei()
    {
        Assert.Equal((15m, 10m, 20_000m, 9m, 30m), (P.IrpjAliquota, P.IrpjAdicionalAliquota, P.IrpjAdicionalLimiteMensal, P.CsllAliquota, P.LimiteCompensacao));
        Assert.Equal((0.65m, 3m, 1.65m, 7.6m), (P.PisCumulativo, P.CofinsCumulativo, P.PisNaoCumulativo, P.CofinsNaoCumulativo));
        Assert.Equal((8m, 12m), (P.Atividade("COMERCIO_INDUSTRIA")!.Irpj, P.Atividade("COMERCIO_INDUSTRIA")!.Csll));
        Assert.Equal((16m, 12m), (P.Atividade("TRANSPORTE_PASSAGEIROS")!.Irpj, P.Atividade("TRANSPORTE_PASSAGEIROS")!.Csll));
        Assert.Equal((32m, 32m), (P.Atividade("SERVICOS_GERAIS")!.Irpj, P.Atividade("SERVICOS_GERAIS")!.Csll));
        Assert.Equal(1.6m, P.Atividade("REVENDA_COMBUSTIVEIS")!.Irpj);
    }

    [Fact]
    public void Presumido_comercio_8_e_12_por_cento_com_adicional()
    {
        // 1.000.000 × 8% = 80.000 → IRPJ 12.000 + adicional 10% × (80.000 − 60.000) = 2.000 · CSLL 1.000.000 × 12% = 120.000 × 9% = 10.800
        var r = Presumido(1_000_000m, 0);
        Assert.Equal((80_000m, 120_000m), (r.BaseIrpj, r.BaseCsll));
        Assert.Equal((12_000m, 2_000m, 14_000m), (r.IrpjNormal, r.IrpjAdicional, r.IrpjAPagar));
        Assert.Equal((10_800m, 10_800m), (r.CsllDevida, r.CsllAPagar));
        Assert.Equal(new DateOnly(2026, 4, 30), r.Vencimento);
    }

    [Fact]
    public void Presumido_servicos_32_por_cento_e_mistos()
    {
        // 500.000 × 32% = 160.000 → 24.000 + 10% × 100.000 = 10.000 · CSLL 160.000 × 9% = 14.400
        var s = Presumido(0, 500_000m);
        Assert.Equal((160_000m, 24_000m, 10_000m, 14_400m), (s.BaseIrpj, s.IrpjNormal, s.IrpjAdicional, s.CsllDevida));
        // misto: comércio 1.000.000 + serviços 500.000 → base 240.000 / CSLL 280.000
        var m = Presumido(1_000_000m, 500_000m);
        Assert.Equal((240_000m, 280_000m), (m.BaseIrpj, m.BaseCsll));
        Assert.Equal((36_000m, 18_000m, 25_200m), (m.IrpjNormal, m.IrpjAdicional, m.CsllDevida));
        Assert.Equal(1_500_000m, m.ReceitaBruta);
    }

    [Fact]
    public void Presumido_outras_receitas_integram_100_e_retencoes_abatem_o_valor_a_pagar()
    {
        // base 80.000 + 10.000 = 90.000 → 13.500 + 10% × 30.000 = 3.000 = 16.500; IRRF 3.000 → 13.500
        var r = Presumido(1_000_000m, 0, outras: 10_000m, irrf: 3_000m, csllRetida: 500m);
        Assert.Equal((90_000m, 130_000m), (r.BaseIrpj, r.BaseCsll));
        Assert.Equal((16_500m, 13_500m), (r.IrpjNormal + r.IrpjAdicional, r.IrpjAPagar));
        Assert.Equal((11_700m, 11_200m), (r.CsllDevida, r.CsllAPagar));
    }

    [Theory]
    [InlineData(60_000.00, 0.00)]
    [InlineData(60_000.01, 0.00)]
    [InlineData(60_100.00, 10.00)]
    [InlineData(100_000.00, 4_000.00)]
    [InlineData(-1.00, 0.00)]
    public void Adicional_de_irpj_incide_so_sobre_o_que_passa_de_60_mil_no_trimestre(double baseCalculo, double adicional)
    {
        Assert.Equal((decimal)adicional, CalculadoraLucro.Irpj(P, (decimal)baseCalculo).Adicional);
    }

    [Fact]
    public void Real_compensa_prejuizo_limitado_a_30_por_cento_do_lucro()
    {
        // lucro real antes da compensação: 500.000 + 100.000 − 20.000 = 580.000 · limite 30% = 174.000 (saldo 1.000.000)
        // lucro real 406.000 → IRPJ 60.900 + adicional 10% × 346.000 = 34.600 = 95.500
        // CSLL: base negativa 100.000 < limite → base 480.000 × 9% = 43.200
        var r = CalculadoraLucro.CalcularReal(P, new EntradaReal(2026, 2, 500_000m, 100_000m, 20_000m, 100_000m, 20_000m, 1_000_000m, 100_000m, 0, 0));
        Assert.Equal((174_000m, 100_000m), (r.CompensacaoIrpj, r.CompensacaoCsll));
        Assert.Equal((406_000m, 480_000m), (r.BaseIrpj, r.BaseCsll));
        Assert.Equal((60_900m, 34_600m, 95_500m), (r.IrpjNormal, r.IrpjAdicional, r.IrpjAPagar));
        Assert.Equal(43_200m, r.CsllAPagar);
        Assert.Equal(new DateOnly(2026, 7, 31), r.Vencimento);
        Assert.Equal(0m, r.PrejuizoIrpj);
    }

    [Fact]
    public void Real_com_prejuizo_no_trimestre_nao_paga_e_gera_prejuizo_fiscal()
    {
        var r = CalculadoraLucro.CalcularReal(P, new EntradaReal(2026, 1, -50_000m, 10_000m, 0, 10_000m, 0, 80_000m, 0, 500m, 0));
        Assert.Equal((0m, 0m, 0m), (r.BaseIrpj, r.IrpjAPagar, r.CsllAPagar));
        Assert.Equal((40_000m, 40_000m, 0m), (r.PrejuizoIrpj, r.PrejuizoCsll, r.CompensacaoIrpj));   // sem lucro não há compensação
    }

    [Fact]
    public void Real_compensa_ate_o_saldo_quando_ele_e_menor_que_o_limite_e_deduz_irrf()
    {
        // lucro 100.000, saldo 20.000 (< 30.000) → base 80.000 → IRPJ 12.000 + 10% × 20.000 = 14.000; IRRF 2.000 + incentivo 500 → 11.500
        var r = CalculadoraLucro.CalcularReal(P, new EntradaReal(2026, 3, 100_000m, 0, 0, 0, 0, 20_000m, 0, 2_000m, 0, 500m));
        Assert.Equal((20_000m, 80_000m, 14_000m, 11_500m), (r.CompensacaoIrpj, r.BaseIrpj, r.IrpjTotal, r.IrpjAPagar));
        Assert.Equal(9_000m, r.CsllDevida);
    }

    [Fact]
    public void Pis_cofins_cumulativo_e_nao_cumulativo_com_creditos_e_retencoes()
    {
        var comp = new DateOnly(2026, 3, 1);
        var cum = CalculadoraLucro.CalcularPisCofins(P, new EntradaPisCofins(comp, RegimePisCofins.Cumulativo, 100_000m, 50_000m, 0, 0));
        Assert.Equal((650m, 3_000m, 0m), (cum.PisAPagar, cum.CofinsAPagar, cum.PisCredito));   // crédito ignorado no cumulativo

        // não cumulativo: PIS 1.650 − 660 (créditos de 40.000) − 100 retido = 890; COFINS 7.600 − 3.040 = 4.560
        var nc = CalculadoraLucro.CalcularPisCofins(P, new EntradaPisCofins(comp, RegimePisCofins.NaoCumulativo, 100_000m, 40_000m, 100m, 0));
        Assert.Equal((1_650m, 7_600m, 660m, 3_040m), (nc.PisDebito, nc.CofinsDebito, nc.PisCredito, nc.CofinsCredito));
        Assert.Equal((890m, 4_560m), (nc.PisAPagar, nc.CofinsAPagar));
        Assert.Equal(new DateOnly(2026, 4, 24), nc.Vencimento);   // 25/04/2026 é sábado: antecipa

        var credor = CalculadoraLucro.CalcularPisCofins(P, new EntradaPisCofins(comp, RegimePisCofins.NaoCumulativo, 10_000m, 50_000m, 0, 0));
        Assert.Equal((0m, 0m), (credor.PisAPagar, credor.CofinsAPagar));
        Assert.Equal((660m, 3_040m), (credor.SaldoCredorPis, credor.SaldoCredorCofins));   // 165−825 e 760−3.800 em valor absoluto
        Assert.NotEmpty(credor.Avisos);
    }

    [Fact]
    public void Trimestres_e_vencimentos()
    {
        Assert.Equal((new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30)), CalculadoraLucro.Trimestre(2026, 2));
        Assert.Equal(new DateOnly(2027, 1, 29), CalculadoraLucro.VencimentoTrimestral(2026, 4));   // 31/01/2027 é domingo
    }
}

public class GeradorGuiasTests
{
    private static readonly ParametrosGuias P = ParametrosGuias.Carregar(ParametrosEmbutidos.Json(ChavesParametro.Retencoes));

    private static Retencao R(TributoRetido t, decimal v, string? cod = null, SentidoRetencao s = SentidoRetencao.Efetuada, int mes = 3) =>
        new(1, s, t, new DateOnly(2026, mes, 1), null, null, null, null, 0, 0, v, cod, "MANUAL", null);

    [Fact]
    public void Retencoes_efetuadas_viram_guias_por_codigo_com_vencimentos()
    {
        var guias = GeradorGuias.DasRetencoes(
        [
            R(TributoRetido.IRRF, 150m, "1708"), R(TributoRetido.IRRF, 50m, "1708"), R(TributoRetido.PIS, 65m), R(TributoRetido.COFINS, 300m), R(TributoRetido.CSLL, 100m),
            R(TributoRetido.INSS, 1_100m), R(TributoRetido.ISS, 500m), R(TributoRetido.IRRF, 999m, "1708", SentidoRetencao.Sofrida),
        ], P).ToList();
        Assert.Equal(["CSRF", "INSS", "IRRF", "ISS"], guias.Select(g => g.Tributo).Order().ToArray());
        var irrf = guias.Single(g => g.Tributo == "IRRF");
        Assert.Equal((200m, "1708", new DateOnly(2026, 4, 20)), (irrf.Valor, irrf.CodigoReceita, irrf.Vencimento));   // sofrida não entra
        var csrf = guias.Single(g => g.Tributo == "CSRF");
        Assert.Equal((465m, "5952", new DateOnly(2026, 4, 30)), (csrf.Valor, csrf.CodigoReceita, csrf.Vencimento));   // 4,65% de 10.000
        var inss = guias.Single(g => g.Tributo == "INSS");
        Assert.Equal((DocumentoGuia.DctfWeb, "2631", new DateOnly(2026, 4, 20)), (inss.Documento, inss.CodigoReceita, inss.Vencimento));
        Assert.Equal((DocumentoGuia.Iss, new DateOnly(2026, 4, 10)), (guias.Single(g => g.Tributo == "ISS").Documento, guias.Single(g => g.Tributo == "ISS").Vencimento));
    }

    [Fact]
    public void Codigos_de_receita_dos_darf()
    {
        Assert.Equal("2089", P.Codigo("IRPJ_PRESUMIDO"));
        Assert.Equal("2372", P.Codigo("CSLL_PRESUMIDO"));
        Assert.Equal("0220", P.Codigo("IRPJ_REAL_TRIMESTRAL"));
        Assert.Equal("6012", P.Codigo("CSLL_REAL_TRIMESTRAL"));
        Assert.Equal("6912", P.Codigo("PIS_NAO_CUMULATIVO"));
        Assert.Equal("5856", P.Codigo("COFINS_NAO_CUMULATIVO"));
        Assert.Equal("8109", P.Codigo("PIS_CUMULATIVO"));
        Assert.Equal("2172", P.Codigo("COFINS_CUMULATIVO"));
        Assert.Equal(("1708", "5952", "2631"), (P.CodigoIrrfServicos, P.CodigoCsrf, P.CodigoInss));
    }

    private static DocumentoFiscal Doc(TipoOperacao op, TotaisDocumento t) =>
        new(7, ModeloDocumento.NFSe, new string('1', 50), op, "15", "1", new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 10), null, SituacaoDocumento.Autorizada,
            new ParteDocumento("1", "E", "SP"), new ParteDocumento("2", "D", "SP"), 3, "1", t, null, false);

    [Fact]
    public void Retencoes_das_notas_saida_e_sofrida_entrada_e_efetuada()
    {
        var totais = new TotaisDocumento(10_000m, 0, 0, 0, 0, 10_000m, 0, 0, 0, 0, 0, 0, 0, 500m, true, 65m, 300m, 100m, 150m, 1_100m);
        var sofridas = GeradorRetencoes.DeDocumento(Doc(TipoOperacao.Saida, totais), P);
        Assert.Equal(6, sofridas.Count);
        Assert.All(sofridas, r => { Assert.Equal(SentidoRetencao.Sofrida, r.Sentido); Assert.Null(r.CodigoReceita); });
        Assert.Equal(new DateOnly(2026, 3, 1), sofridas[0].Competencia);
        var efetuadas = GeradorRetencoes.DeDocumento(Doc(TipoOperacao.Entrada, totais), P);
        Assert.Equal("1708", efetuadas.Single(r => r.Tributo == TributoRetido.IRRF).CodigoReceita);
        Assert.Equal(1.5m, efetuadas.Single(r => r.Tributo == TributoRetido.IRRF).Aliquota);
        Assert.Equal(11m, efetuadas.Single(r => r.Tributo == TributoRetido.INSS).Aliquota);
        Assert.Equal(500m, efetuadas.Single(r => r.Tributo == TributoRetido.ISS).Valor);
    }
}
