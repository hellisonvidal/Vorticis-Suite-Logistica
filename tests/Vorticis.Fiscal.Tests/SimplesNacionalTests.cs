using Vorticis.Fiscal.Comum;
using Vorticis.Fiscal.Documentos;
using Vorticis.Fiscal.Simples;

namespace Vorticis.Fiscal.Tests;

/// <summary>Casos conferidos à mão com as tabelas da LC 123/2006 (Anexos I a V, em vigor desde 2018).</summary>
public class CalculadoraSimplesTests
{
    private static readonly TabelaSimples Tabela = TabelaSimples.Carregar(ParametrosEmbutidos.Json(ChavesParametro.SimplesNacional));
    private static readonly DateOnly Mar = new(2026, 3, 1);

    private static ResultadoSimples Calc(decimal rbt12, decimal folha12, params ReceitaSimples[] receitas) =>
        CalculadoraSimples.Calcular(Tabela, new EntradaSimples(Mar, receitas, rbt12, folha12));

    [Fact]
    public void Tabelas_embutidas_tem_5_anexos_6_faixas_e_reparticoes_que_fecham_100()
    {
        Assert.Equal(5, Tabela.Anexos.Count);
        Assert.All(Tabela.Anexos.Values, f => Assert.Equal(6, f.Count));
        Assert.All(Tabela.Anexos.Values.SelectMany(f => f), f => Assert.Equal(100m, f.Reparticao.Values.Sum()));
        Assert.Equal((4_800_000m, 3_600_000m, 0.28m, 10m), (Tabela.LimiteRbt12, Tabela.SublimiteIcmsIss, Tabela.LimiteFatorR, Tabela.MinimoDas));
        // limites e parcelas a deduzir de cada anexo (primeira e última faixa)
        Assert.Equal((4m, 0m), (Tabela.Anexos[AnexoSimples.I][0].Aliquota, Tabela.Anexos[AnexoSimples.I][0].Deduzir));
        Assert.Equal((19m, 378_000m), (Tabela.Anexos[AnexoSimples.I][5].Aliquota, Tabela.Anexos[AnexoSimples.I][5].Deduzir));
        Assert.Equal((33m, 828_000m), (Tabela.Anexos[AnexoSimples.IV][5].Aliquota, Tabela.Anexos[AnexoSimples.IV][5].Deduzir));
        Assert.Equal((30.5m, 540_000m), (Tabela.Anexos[AnexoSimples.V][5].Aliquota, Tabela.Anexos[AnexoSimples.V][5].Deduzir));
    }

    [Fact]
    public void Anexo_I_segunda_faixa_aliquota_efetiva_5_32_e_reparticao_por_tributo()
    {
        // (300.000 × 7,3% − 5.940) ÷ 300.000 = 5,32% · DAS = 50.000 × 5,32% = 2.660,00
        var r = Calc(300_000m, 0, new ReceitaSimples(AtividadeSimples.ComercioRevenda, 50_000m));
        var s = Assert.Single(r.Segmentos);
        Assert.Equal((AnexoSimples.I, 2, 5.32m), (s.Anexo, s.Faixa, s.AliquotaEfetiva));
        Assert.Equal(2_660m, r.ValorDas);
        Assert.Equal(146.30m, s.Tributos["IRPJ"]);
        Assert.Equal(93.10m, s.Tributos["CSLL"]);
        Assert.Equal(338.88m, s.Tributos["COFINS"]);
        Assert.Equal(73.42m, s.Tributos["PIS"]);
        Assert.Equal(1_103.90m, s.Tributos["CPP"]);
        Assert.Equal(904.40m, s.Tributos["ICMS"]);
        Assert.Equal(r.ValorDas, s.Tributos.Values.Sum());
        Assert.Equal(new DateOnly(2026, 4, 20), r.Vencimento);
    }

    [Fact]
    public void Substituicao_tributaria_e_monofasico_retiram_icms_e_pis_cofins()
    {
        var st = Calc(300_000m, 0, new ReceitaSimples(AtividadeSimples.ComercioRevenda, 50_000m, TratamentoSimples.SubstituicaoTributaria));
        Assert.Equal(1_755.60m, st.ValorDas);                  // 2.660,00 − ICMS 904,40
        Assert.False(st.Segmentos[0].Tributos.ContainsKey("ICMS"));
        Assert.Equal(3.5112m, st.Segmentos[0].AliquotaAplicada);

        var mono = Calc(300_000m, 0, new ReceitaSimples(AtividadeSimples.ComercioRevenda, 50_000m, TratamentoSimples.Monofasico));
        Assert.Equal(2_247.70m, mono.ValorDas);                // 2.660,00 − COFINS 338,88 − PIS 73,42

        var exportacao = Calc(300_000m, 0, new ReceitaSimples(AtividadeSimples.ComercioRevenda, 50_000m, TratamentoSimples.Exportacao));
        Assert.Equal(["IRPJ", "CSLL", "CPP"], exportacao.Segmentos[0].Tributos.Keys);
    }

    [Fact]
    public void Exportacao_deixa_so_irpj_csll_e_cpp()
    {
        var r = Calc(300_000m, 0, new ReceitaSimples(AtividadeSimples.ComercioRevenda, 50_000m, TratamentoSimples.Exportacao));
        Assert.Equal(146.30m + 93.10m + 1_103.90m, r.ValorDas);
    }

    [Fact]
    public void Anexo_III_com_iss_retido_remove_a_parcela_do_iss()
    {
        // 1ª faixa: 6% · DAS = 20.000 × 6% = 1.200,00 · ISS = 33,5% = 402,00
        var normal = Calc(150_000m, 0, new ReceitaSimples(AtividadeSimples.ServicosAnexoIII, 20_000m));
        Assert.Equal(1_200m, normal.ValorDas);
        Assert.Equal(402m, normal.Segmentos[0].Tributos["ISS"]);
        var retido = Calc(150_000m, 0, new ReceitaSimples(AtividadeSimples.ServicosAnexoIII, 20_000m, TratamentoSimples.IssRetido));
        Assert.Equal(798m, retido.ValorDas);
    }

    [Fact]
    public void Fator_R_define_anexo_III_ou_V()
    {
        // RBT12 600.000: com folha de 180.000 (30%) → Anexo III, 3ª faixa: (600.000 × 13,5% − 17.640) ÷ 600.000 = 10,56% → 5.280,00
        var iii = Calc(600_000m, 180_000m, new ReceitaSimples(AtividadeSimples.ServicosFatorR, 50_000m));
        Assert.Equal((AnexoSimples.III, 0.30m, 10.56m), (iii.Segmentos[0].Anexo, iii.FatorR, iii.Segmentos[0].AliquotaEfetiva));
        Assert.Equal(5_280m, iii.ValorDas);
        // exatamente 28% ainda é Anexo III
        Assert.Equal(AnexoSimples.III, Calc(600_000m, 168_000m, new ReceitaSimples(AtividadeSimples.ServicosFatorR, 1_000m)).Segmentos[0].Anexo);
        // folha de 120.000 (20%) → Anexo V, 3ª faixa: (600.000 × 19,5% − 9.900) ÷ 600.000 = 17,85% → 8.925,00
        var v = Calc(600_000m, 120_000m, new ReceitaSimples(AtividadeSimples.ServicosFatorR, 50_000m));
        Assert.Equal((AnexoSimples.V, 0.20m, 17.85m), (v.Segmentos[0].Anexo, v.FatorR, v.Segmentos[0].AliquotaEfetiva));
        Assert.Equal(8_925m, v.ValorDas);
        Assert.Contains(v.Memoria, m => m.Contains("Fator R") && m.Contains("<"));
    }

    [Theory]
    [InlineData(180_000.00, 1)]
    [InlineData(180_000.01, 2)]
    [InlineData(360_000.00, 2)]
    [InlineData(720_000.01, 4)]
    [InlineData(1_800_000.00, 4)]
    [InlineData(3_600_000.00, 5)]
    [InlineData(3_600_000.01, 6)]
    [InlineData(4_800_000.00, 6)]
    public void Faixa_pela_rbt12_limites_incluem_o_valor_do_teto(double rbt12, int faixa)
    {
        Assert.Equal(faixa, Tabela.NumeroFaixa(AnexoSimples.I, (decimal)rbt12));
    }

    [Fact]
    public void Segregacao_em_varios_anexos_soma_os_das()
    {
        // RBT12 150.000: Anexo I 4% × 10.000 = 400 · Anexo II 4,5% × 10.000 = 450 · Anexo IV 4,5% × 10.000 = 450
        var r = Calc(150_000m, 0,
            new ReceitaSimples(AtividadeSimples.ComercioRevenda, 10_000m), new ReceitaSimples(AtividadeSimples.IndustriaVenda, 10_000m),
            new ReceitaSimples(AtividadeSimples.ServicosAnexoIV, 10_000m));
        Assert.Equal(3, r.Segmentos.Count);
        Assert.Equal(1_300m, r.ValorDas);
        Assert.Equal(30_000m, r.ReceitaMes);
        Assert.Equal(1_300m, r.TotalPorTributo.Values.Sum());
        Assert.True(r.TotalPorTributo.ContainsKey("IPI"));   // Anexo II
        // receitas do mesmo anexo e tratamento são somadas num segmento só
        Assert.Single(Calc(150_000m, 0, new ReceitaSimples(AtividadeSimples.ComercioRevenda, 1m), new ReceitaSimples(AtividadeSimples.ComercioRevenda, 2m)).Segmentos);
    }

    [Fact]
    public void Anexo_IV_nao_tem_cpp_no_das()
    {
        var r = Calc(150_000m, 0, new ReceitaSimples(AtividadeSimples.ServicosAnexoIV, 10_000m));
        Assert.False(r.Segmentos[0].Tributos.ContainsKey("CPP"));
        Assert.Equal(450m, r.ValorDas);
        Assert.Equal(200.25m, r.Segmentos[0].Tributos["ISS"]);   // 44,5% de 450
    }

    [Fact]
    public void Acima_do_sublimite_icms_e_iss_ficam_fora_do_das_e_acima_do_limite_avisa_exclusao()
    {
        // Anexo II, RBT12 4.000.000 (6ª faixa): (4.000.000 × 30% − 720.000) ÷ 4.000.000 = 12% · 6ª faixa não tem ICMS
        var r = Calc(4_000_000m, 0, new ReceitaSimples(AtividadeSimples.IndustriaVenda, 100_000m));
        Assert.Equal(12m, r.Segmentos[0].AliquotaEfetiva);
        Assert.Contains(r.Avisos, a => a.Contains("sublimite"));
        Assert.Contains(Calc(5_000_000m, 0, new ReceitaSimples(AtividadeSimples.IndustriaVenda, 1m)).Avisos, a => a.Contains("excede o limite"));
    }

    [Fact]
    public void Das_abaixo_do_minimo_avisa_e_rbt12_zero_usa_a_aliquota_nominal_da_primeira_faixa()
    {
        var r = Calc(0m, 0, new ReceitaSimples(AtividadeSimples.ComercioRevenda, 100m));   // 4% × 100 = 4,00
        Assert.Equal(4m, r.ValorDas);
        Assert.Contains(r.Avisos, a => a.Contains("abaixo do mínimo"));
    }

    [Fact]
    public void Aliquota_efetiva_arredonda_em_4_casas()
    {
        // (250.000 × 7,3% − 5.940) ÷ 250.000 = 4,9240% exato; (333.333,33 × 7,3% − 5.940) ÷ 333.333,33 = 5,5180%
        Assert.Equal(4.924m, CalculadoraSimples.AliquotaEfetiva(Tabela.Anexos[AnexoSimples.I][1], 250_000m));
        Assert.Equal(5.518m, CalculadoraSimples.AliquotaEfetiva(Tabela.Anexos[AnexoSimples.I][1], 333_333.33m), 3);
    }

    [Fact]
    public void Rbt12_proporcional_no_inicio_da_atividade_e_soma_de_12_meses()
    {
        var comp = new DateOnly(2026, 3, 1);
        // Empresa aberta em out/2025: 5 meses de atividade (out a fev), média × 12
        var serie = new Dictionary<DateOnly, decimal>
        {
            [new(2025, 10, 1)] = 10_000m, [new(2025, 11, 1)] = 20_000m, [new(2025, 12, 1)] = 30_000m, [new(2026, 1, 1)] = 40_000m, [new(2026, 2, 1)] = 50_000m,
        };
        var a = CalculadoraSimples.Acumular12Meses(comp, new DateOnly(2025, 10, 15), serie, 60_000m);
        Assert.Equal((360_000m, true), (a.Valor, a.Proporcional));
        Assert.Empty(a.MesesSemDado);
        // primeiro mês de atividade: receita do mês × 12
        Assert.Equal(120_000m, CalculadoraSimples.Acumular12Meses(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 5), new Dictionary<DateOnly, decimal>(), 10_000m).Valor);
        // 12 meses completos, com um mês sem lançamento
        var completa = Enumerable.Range(1, 12).Select(i => comp.AddMonths(-i)).Where(m => m != new DateOnly(2025, 6, 1)).ToDictionary(m => m, _ => 1_000m);
        var c = CalculadoraSimples.Acumular12Meses(comp, new DateOnly(2020, 1, 1), completa, 0);
        Assert.Equal((11_000m, false), (c.Valor, c.Proporcional));
        Assert.Equal([new DateOnly(2025, 6, 1)], c.MesesSemDado);
    }

    [Fact]
    public void Vencimento_prorroga_para_o_proximo_dia_util()
    {
        DateOnly V(int ano, int mes) => CalculadoraSimples.Calcular(Tabela, new EntradaSimples(new DateOnly(ano, mes, 1), [new ReceitaSimples(AtividadeSimples.ComercioRevenda, 1000m)], 100_000m, 0)).Vencimento;
        Assert.Equal(new DateOnly(2026, 6, 22), V(2026, 5));    // 20/06/2026 é sábado
        Assert.Equal(new DateOnly(2026, 3, 20), V(2026, 2));    // sexta
        Assert.Equal(new DateOnly(2027, 1, 20), V(2026, 12));
        Assert.Equal(new DateOnly(2026, 4, 3), Calendario.SextaFeiraSanta(2026));
        Assert.Equal(new DateOnly(2026, 4, 2), Calendario.DiaUtilAnterior(new DateOnly(2026, 4, 3)));
        Assert.Equal(new DateOnly(2026, 1, 30), Calendario.UltimoDiaUtilDoMes(2026, 1));
    }
}

public class ClassificadorReceitaTests
{
    private static LinhaItemFiscal L(TipoOperacao op, string cfop, decimal valor, string? cstPis = null, bool issRetido = false, decimal ipi = 0, decimal st = 0) =>
        new(1, ModeloDocumento.NFe55, op, "1", "1", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1), SituacaoDocumento.Autorizada, null, null, null, "k", issRetido,
            new ItemFiscal(1, null, null, null, null, cfop, null, 1, valor, valor, 0, 0, 0, 0, valor, null, null, 0, 0, 0, 0, st, null, 0, 0, ipi, cstPis, 0, 0, 0, null, 0, 0, 0, 0, 0, 0));

    [Fact]
    public void Segrega_pelo_cfop_cst_e_iss_retido()
    {
        var itens = new[]
        {
            L(TipoOperacao.Saida, "5102", 1000m), L(TipoOperacao.Saida, "6102", 500m),
            L(TipoOperacao.Saida, "5101", 2000m, ipi: 100m),
            L(TipoOperacao.Saida, "5405", 300m),
            L(TipoOperacao.Saida, "5102", 400m, cstPis: "04"),
            L(TipoOperacao.Saida, "7102", 600m),
            L(TipoOperacao.Saida, Cfop.Servico, 5000m, issRetido: true), L(TipoOperacao.Saida, Cfop.Servico, 1000m),
            L(TipoOperacao.Saida, "5152", 9999m),               // transferência: fora
            L(TipoOperacao.Entrada, "1202", 200m),              // devolução de venda
            L(TipoOperacao.Entrada, "1102", 777m),              // compra: fora
        };
        var r = ClassificadorReceita.Sugerir(itens, AtividadeSimples.ServicosAnexoIII);
        decimal Soma(AtividadeSimples a, TratamentoSimples t) => r.Where(x => x.Atividade == a && x.Tratamento == t).Sum(x => x.Valor);
        Assert.Equal(1300m, Soma(AtividadeSimples.ComercioRevenda, TratamentoSimples.Normal));   // 1000 + 500 − devolução 200
        Assert.Equal(1900m, Soma(AtividadeSimples.IndustriaVenda, TratamentoSimples.Normal));    // 2000 − IPI 100
        Assert.Equal(300m, Soma(AtividadeSimples.ComercioRevenda, TratamentoSimples.SubstituicaoTributaria));
        Assert.Equal(400m, Soma(AtividadeSimples.ComercioRevenda, TratamentoSimples.Monofasico));
        Assert.Equal(600m, Soma(AtividadeSimples.ComercioRevenda, TratamentoSimples.Exportacao));
        Assert.Equal(5000m, Soma(AtividadeSimples.ServicosAnexoIII, TratamentoSimples.IssRetido));
        Assert.Equal(1000m, Soma(AtividadeSimples.ServicosAnexoIII, TratamentoSimples.Normal));
    }
}
