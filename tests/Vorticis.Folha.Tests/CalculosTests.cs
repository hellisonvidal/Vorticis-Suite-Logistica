using Vorticis.Folha.Regras;

namespace Vorticis.Folha.Tests;

/// <summary>Cálculos conferidos à mão com as tabelas de 2026 (Portaria MPS/MF 13/2026 e Lei 15.270/2025).</summary>
public class CalculosTests
{
    internal static readonly TabelasLegais T2026 = TabelasLegais.Em(new DateOnly(2026, 6, 1), TabelasLegais.Embutidas());

    internal static ContextoCalculo Ctx(int mes = 6, AcumuladoMes? acum = null)
        => new(new DateOnly(2026, mes, 1), T2026, RubricasPadrao.Catalogo, acum ?? AcumuladoMes.Nenhum);

    internal static FuncionarioCalc Func(decimal salario = 3000m, string admissao = "2020-01-01", string? desligamento = null,
                                         IReadOnlyList<DependenteCalc>? deps = null, decimal insal = 0, bool peric = false,
                                         string tipo = "MENSAL", string categoria = "101")
        => new(1, "001", "Maria da Silva", DateOnly.Parse(admissao), desligamento is null ? null : DateOnly.Parse(desligamento), tipo, salario, 220,
               insal, peric, categoria, deps ?? []);

    [Fact]
    public void Tabelas_de_2026_carregadas_com_os_valores_oficiais()
    {
        Assert.Equal(8475.55m, T2026.Inss.Teto);
        Assert.Equal(1621.00m, T2026.SalarioMinimo);
        Assert.Equal((1980.38m, 67.54m), (T2026.SalarioFamilia.LimiteRemuneracao, T2026.SalarioFamilia.Cota));
        Assert.Equal(607.20m, T2026.Irrf.DescontoSimplificado);
        Assert.Equal(189.59m, T2026.Irrf.DeducaoDependente);
        Assert.NotNull(T2026.Irrf.Reducao);
        Assert.Equal(8m, T2026.Fgts.Aliquota);
        // Vigência anterior (2025) continua disponível para competências antigas
        var t2025 = TabelasLegais.Em(new DateOnly(2025, 8, 1), TabelasLegais.Embutidas());
        Assert.Equal(8157.41m, t2025.Inss.Teto);
        Assert.Null(t2025.Irrf.Reducao);
    }

    [Theory]
    [InlineData(1000, 75.00)]                  // 1ª faixa
    [InlineData(1621, 121.58)]                 // 1.621 x 7,5% = 121,575
    [InlineData(3000, 248.60)]                 // 121,575 + 1.281,84 x 9% + 97,16 x 12%
    [InlineData(5000, 501.51)]                 // até a 4ª faixa
    [InlineData(10000, 988.09)]                // limitado ao teto
    public void Inss_progressivo_por_faixa(double baseCalculo, double esperado)
        => Assert.Equal((decimal)esperado, CalculoInss.Calcular((decimal)baseCalculo, T2026.Inss));

    [Fact]
    public void Irrf_ate_5000_com_desconto_simplificado_e_zerado_pela_reducao()
    {
        // Rendimento 5.000: simplificado 607,20 -> base 4.392,80 -> 22,5% - 675,49 = 312,89; redução 312,89
        var r = CalculoIrrf.Calcular(5000m, 501.51m, 0, 0, T2026.Irrf);
        Assert.True(r.UsouSimplificado);
        Assert.Equal((4392.80m, 312.89m, 312.89m, 0m), (r.BaseCalculo, r.ImpostoTabela, r.Reducao, r.Imposto));
    }

    [Fact]
    public void Irrf_acima_de_5000_usa_deducao_legal_quando_maior_e_reducao_parcial()
    {
        // 6.000: INSS 641,51 > simplificado -> base 5.358,49 x 27,5% - 908,73 = 564,85; redução 978,62 - 0,133145 x 6.000 = 179,75
        var r = CalculoIrrf.Calcular(6000m, 641.51m, 0, 0, T2026.Irrf);
        Assert.False(r.UsouSimplificado);
        Assert.Equal((5358.49m, 564.85m, 179.75m, 385.10m), (r.BaseCalculo, r.ImpostoTabela, r.Reducao, r.Imposto));
    }

    [Fact]
    public void Irrf_com_dependentes_e_acima_de_7350_sem_reducao()
    {
        // 7.000 com 3 dependentes: deduções 781,51 + 568,77 = 1.350,28; base 5.649,72 -> 644,94; redução 46,61 -> 598,33
        var a = CalculoIrrf.Calcular(7000m, 781.51m, 3, 0, T2026.Irrf);
        Assert.Equal((5649.72m, 644.94m, 46.61m, 598.33m), (a.BaseCalculo, a.ImpostoTabela, a.Reducao, a.Imposto));
        // 8.000: sem redução (acima de 7.350)
        var b = CalculoIrrf.Calcular(8000m, 921.51m, 0, 0, T2026.Irrf);
        Assert.Equal((7078.49m, 1037.85m, 0m, 1037.85m), (b.BaseCalculo, b.ImpostoTabela, b.Reducao, b.Imposto));
    }

    [Fact]
    public void Irrf_de_2025_nao_tem_reducao()
    {
        var t2025 = TabelasLegais.Em(new DateOnly(2025, 8, 1), TabelasLegais.Embutidas());
        var r = CalculoIrrf.Calcular(5000m, 480m, 0, 0, t2025.Irrf);
        Assert.Equal(0m, r.Reducao);
        Assert.True(r.Imposto > 0);
    }

    [Fact]
    public void Folha_mensal_simples_salario_3000()
    {
        var r = CalculadoraFolhaMensal.Calcular(Func(), [], Ctx());
        Assert.Equal(3000m, r.TotalProventos);
        Assert.Equal((3000m, 248.60m, 0m, 240m), (r.BaseInss, r.Inss, r.Irrf, r.Fgts));
        Assert.Equal(2751.40m, r.Liquido);
        Assert.Contains(r.Itens, i => i.Codigo == RubricasPadrao.Inss && i.Memoria.Contains("9%"));
    }

    [Fact]
    public void Folha_mensal_horas_extras_dsr_faltas_e_adicionais()
    {
        // Março/2026: 26 dias úteis e 5 domingos. Valor-hora 10,00; 10h extras a 50% = 150,00; DSR = 150/26 x 5 = 28,85
        var f = Func(2200m);
        var ev = new[] { new EventoCalc(RubricasPadrao.HoraExtra50, 10, null), new EventoCalc(RubricasPadrao.Faltas, 2, null) };
        var r = CalculadoraFolhaMensal.Calcular(f, ev, Ctx(3));
        Assert.Equal(150m, r.Itens.Single(i => i.Codigo == RubricasPadrao.HoraExtra50).Valor);
        Assert.Equal(28.85m, r.Itens.Single(i => i.Codigo == RubricasPadrao.Dsr).Valor);
        // 2 faltas x 73,33 = 146,67 e perda de 2 DSR = 146,67; faltas reduzem as bases
        Assert.Equal(146.67m, r.Itens.Single(i => i.Codigo == RubricasPadrao.Faltas).Valor);
        Assert.Equal(146.67m, r.Itens.Single(i => i.Codigo == RubricasPadrao.DsrFaltas).Valor);
        Assert.Equal(2200m + 150m + 28.85m - 146.67m - 146.67m, r.BaseInss);

        // Insalubridade 20% do mínimo (324,20) e periculosidade 30% (660): paga a maior
        var p = CalculadoraFolhaMensal.Calcular(Func(2200m, insal: 20, peric: true), [], Ctx());
        Assert.Contains(p.Itens, i => i.Codigo == RubricasPadrao.Periculosidade && i.Valor == 660m);
        Assert.DoesNotContain(p.Itens, i => i.Codigo == RubricasPadrao.Insalubridade);
        var ins = CalculadoraFolhaMensal.Calcular(Func(2200m, insal: 20), [], Ctx());
        Assert.Equal(324.20m, ins.Itens.Single(i => i.Codigo == RubricasPadrao.Insalubridade).Valor);
    }

    [Fact]
    public void Folha_mensal_admissao_no_mes_e_salario_familia()
    {
        var deps = new[] { new DependenteCalc("Filho", new DateOnly(2020, 5, 1), false, true, true), new DependenteCalc("Filha", new DateOnly(2010, 5, 1), false, true, true) };
        var r = CalculadoraFolhaMensal.Calcular(Func(1800m, "2026-06-16", deps: deps), [], Ctx());
        // 15 dias de 30: salário 900,00; 1 cota (a filha de 16 anos não tem direito) x 67,54 x 15/30 = 33,77
        Assert.Equal(900m, r.Itens.Single(i => i.Codigo == RubricasPadrao.Salario).Valor);
        Assert.Equal(33.77m, r.SalarioFamilia);
        Assert.Equal(900m, r.BaseInss);
        // Acima do limite: sem salário-família
        var alto = CalculadoraFolhaMensal.Calcular(Func(2500m, deps: deps), [], Ctx());
        Assert.Equal(0m, alto.SalarioFamilia);
    }

    [Fact]
    public void Folha_mensal_irrf_com_reducao_2026_e_encargos_patronais()
    {
        var r = CalculadoraFolhaMensal.Calcular(Func(6000m), [], Ctx());
        Assert.Equal((641.51m, 385.10m), (r.Inss, r.Irrf));
        Assert.Equal(6000m - 641.51m - 385.10m, r.Liquido);
        // Geral: 20% + RAT 2% x FAP 1,2 + terceiros 5,8%; FGTS 8%
        var g = CalculoEncargos.Calcular(r.BaseInss, r.Fgts, new ParametrosEncargos(2m, 1.2m, 5.8m, RegimeEncargos.Geral));
        Assert.Equal((1200m, 144m, 348m, 480m), (g.InssPatronal, g.Rat, g.Terceiros, g.Fgts));
        // Simples Nacional anexos I-III e V: sem CPP; anexo IV: 20% + RAT, sem terceiros
        var sem = CalculoEncargos.Calcular(6000m, 480m, new ParametrosEncargos(2m, 1m, 5.8m, RegimeEncargosExtensoes.De("SIMPLES_NACIONAL", "III")));
        Assert.Equal((0m, 0m, 0m, 480m), (sem.InssPatronal, sem.Rat, sem.Terceiros, sem.Fgts));
        var iv = CalculoEncargos.Calcular(6000m, 480m, new ParametrosEncargos(2m, 1m, 5.8m, RegimeEncargosExtensoes.De("SIMPLES_NACIONAL", "IV")));
        Assert.Equal((1200m, 120m, 0m), (iv.InssPatronal, iv.Rat, iv.Terceiros));
    }

    [Fact]
    public void Folha_mensal_acumula_inss_e_irrf_com_as_ferias_do_mes()
    {
        // Férias já tributaram 4.000 (INSS 368,60): o mensal de 3.000 tributa 7.000 - 781,51 = 412,91 de INSS
        var r = CalculadoraFolhaMensal.Calcular(Func(), [], Ctx(acum: new AcumuladoMes(4000m, 368.60m, 4000m, 0m)));
        Assert.Equal(412.91m, r.Inss);
    }

    [Fact]
    public void Ferias_com_um_terco_e_abono_pecuniario()
    {
        var f = Func();
        var cheias = CalculadoraFerias.Calcular(f, new SolicitacaoFerias(new DateOnly(2026, 7, 1), 30, 0, 0, 0), Ctx(6));
        Assert.Equal(3000m, cheias.Itens.Single(i => i.Codigo == RubricasPadrao.FeriasGozadas).Valor);
        Assert.Equal(1000m, cheias.Itens.Single(i => i.Codigo == RubricasPadrao.TercoFerias).Valor);
        Assert.Equal((368.60m, 0m, 320m, 3631.40m), (cheias.Inss, cheias.Irrf, cheias.Fgts, cheias.Liquido));

        // 20 dias + 10 de abono: abono e seu 1/3 são indenizatórios (sem INSS/IRRF/FGTS)
        var abono = CalculadoraFerias.Calcular(f, new SolicitacaoFerias(new DateOnly(2026, 7, 1), 20, 10, 0, 0), Ctx(6));
        Assert.Equal(2000m, abono.Itens.Single(i => i.Codigo == RubricasPadrao.FeriasGozadas).Valor);
        Assert.Equal(666.67m, abono.Itens.Single(i => i.Codigo == RubricasPadrao.TercoFerias).Valor);
        Assert.Equal(1000m, abono.Itens.Single(i => i.Codigo == RubricasPadrao.AbonoPecuniario).Valor);
        Assert.Equal(333.33m, abono.Itens.Single(i => i.Codigo == RubricasPadrao.TercoAbono).Valor);
        Assert.Equal((2666.67m, 215.69m), (abono.BaseInss, abono.Inss));
        Assert.Equal(4000m, abono.TotalProventos);

        // Média das variáveis entra na remuneração: 3.000 + 300 -> 3.300 + 1/3 = 4.400
        var media = CalculadoraFerias.Calcular(f, new SolicitacaoFerias(new DateOnly(2026, 7, 1), 30, 0, 0, 300m), Ctx(6));
        Assert.Equal(4400m, media.TotalProventos);
    }

    [Theory]
    [InlineData(0, 30)] [InlineData(5, 30)] [InlineData(6, 24)] [InlineData(14, 24)] [InlineData(15, 18)] [InlineData(23, 18)]
    [InlineData(24, 12)] [InlineData(32, 12)] [InlineData(33, 0)]
    public void Dias_de_direito_conforme_as_faltas(int faltas, int dias) => Assert.Equal(dias, PeriodosFerias.DiasDireito(faltas));

    [Fact]
    public void Ferias_validam_abono_e_soma_de_dias()
    {
        var f = Func();
        Assert.NotEmpty(CalculadoraFerias.Validar(f, new SolicitacaoFerias(new DateOnly(2026, 7, 1), 15, 15, 0, 0)));   // abono > 1/3
        Assert.Empty(CalculadoraFerias.Validar(f, new SolicitacaoFerias(new DateOnly(2026, 7, 1), 20, 0, 0, 0)));       // fracionamento: saldo de 10 dias
        Assert.NotEmpty(CalculadoraFerias.Validar(f, new SolicitacaoFerias(new DateOnly(2026, 7, 1), 20, 0, 0, 0, 15)));  // 20 + 15 > 30
        Assert.Empty(CalculadoraFerias.Validar(f, new SolicitacaoFerias(new DateOnly(2026, 7, 1), 24, 0, 6, 0)));       // 6 faltas = 24 dias
        var p = PeriodosFerias.Calcular(new DateOnly(2024, 3, 10), new DateOnly(2026, 6, 1));
        Assert.Equal(3, p.Count);
        Assert.Equal((new DateOnly(2024, 3, 10), new DateOnly(2025, 3, 9), new DateOnly(2026, 3, 9)), (p[0].Inicio, p[0].Fim, p[0].FimConcessivo));
    }

    [Fact]
    public void Decimo_terceiro_avos_e_parcelas()
    {
        Assert.Equal(12, CalculadoraDecimoTerceiro.Avos(new DateOnly(2020, 1, 1), null, 2026));
        Assert.Equal(7, CalculadoraDecimoTerceiro.Avos(new DateOnly(2026, 5, 20), null, 2026));   // maio: 12 dias, não conta
        Assert.Equal(8, CalculadoraDecimoTerceiro.Avos(new DateOnly(2026, 5, 17), null, 2026));   // maio: 15 dias, conta

        var f = Func();
        var p1 = CalculadoraDecimoTerceiro.PrimeiraParcela(f, 12, Ctx(11));
        Assert.Equal((1500m, 0m, 0m, 120m), (p1.TotalProventos, p1.Inss, p1.Irrf, p1.Fgts));
        var p2 = CalculadoraDecimoTerceiro.SegundaParcela(f, 12, 0, 1500m, Ctx(12));
        Assert.Equal((248.60m, 0m, 120m), (p2.Inss, p2.Irrf, p2.Fgts));
        Assert.Equal(1251.40m, p2.Liquido);

        // Proporcional: 7 avos de 3.000 = 1.750 (1ª parcela 875)
        var prop = CalculadoraDecimoTerceiro.PrimeiraParcela(f, 7, Ctx(11));
        Assert.Equal(875m, prop.TotalProventos);
    }

    [Fact]
    public void Rescisao_sem_justa_causa_com_aviso_proporcional_e_multa_do_fgts()
    {
        // 5 anos completos -> aviso de 30 + 15 = 45 dias, projetado até 30/07/2026
        var f = Func(3000m, "2021-03-10", "2026-06-15");
        Assert.Equal(45, CalculadoraRescisao.DiasAviso(f.Admissao, new DateOnly(2026, 6, 15)));
        var s = new SolicitacaoRescisao(MotivoRescisao.SemJustaCausa, new DateOnly(2026, 6, 15), TipoAviso.Indenizado, 0, 20000m, []);
        var r = CalculadoraRescisao.Calcular(f, s, Ctx(6));
        decimal V(string cod) => r.Itens.Single(i => i.Codigo == cod).Valor;
        Assert.Equal(1500m, V(RubricasPadrao.SaldoSalario));
        Assert.Equal(4500m, V(RubricasPadrao.AvisoPrevioIndenizado));
        Assert.Equal(1250m, V(RubricasPadrao.FeriasProporcionais));        // 5/12 avos com a projeção do aviso
        Assert.Equal(416.67m, V(RubricasPadrao.TercoFeriasProporcionais));
        Assert.Equal(1750m, V(RubricasPadrao.DecimoProporcional));         // 7/12 avos
        Assert.Equal(112.50m, V(RubricasPadrao.Inss));                     // só sobre o saldo de salário
        Assert.Equal(133.19m, V(RubricasPadrao.InssDecimo));               // 13º tem tabela própria
        Assert.Equal(9416.67m, r.TotalProventos);
        Assert.Equal(9170.98m, r.Liquido);
        // FGTS da rescisão 8% x (1.500 + 4.500 + 1.750) = 620; multa 40% x (20.000 + 620) = 8.248
        Assert.Equal(620m, r.Fgts);
        Assert.Equal(8248m, r.MultaFgts);
    }

    [Fact]
    public void Rescisao_por_acordo_pedido_de_demissao_e_justa_causa()
    {
        var f = Func(3000m, "2021-03-10", "2026-06-15");
        var dia = new DateOnly(2026, 6, 15);
        // Acordo: aviso indenizado pela metade (22 dias) e multa de 20%
        var ac = CalculadoraRescisao.Calcular(f, new SolicitacaoRescisao(MotivoRescisao.Acordo484A, dia, TipoAviso.Indenizado, 0, 10000m, []), Ctx(6));
        Assert.Equal(2200m, ac.Itens.Single(i => i.Codigo == RubricasPadrao.AvisoPrevioIndenizado).Valor);
        Assert.True(ac.MultaFgts > 0 && ac.MultaFgts < 2400m);
        // Pedido de demissão com aviso não cumprido: desconta 30 dias, sem multa
        var pd = CalculadoraRescisao.Calcular(f, new SolicitacaoRescisao(MotivoRescisao.PedidoDemissao, dia, TipoAviso.NaoCumprido, 0, 10000m, []), Ctx(6));
        Assert.Equal(3000m, pd.Itens.Single(i => i.Codigo == RubricasPadrao.AvisoNaoCumprido).Valor);
        Assert.Equal(0m, pd.MultaFgts);
        // Justa causa: só saldo de salário e férias vencidas (+1/3)
        var jc = CalculadoraRescisao.Calcular(f, new SolicitacaoRescisao(MotivoRescisao.JustaCausa, dia, TipoAviso.Dispensado, 0, 10000m,
            [new FeriasVencidas(new DateOnly(2025, 3, 10), 30, false)]), Ctx(6));
        Assert.DoesNotContain(jc.Itens, i => i.Codigo is RubricasPadrao.DecimoProporcional or RubricasPadrao.FeriasProporcionais);
        Assert.Equal(3000m, jc.Itens.Single(i => i.Codigo == RubricasPadrao.FeriasVencidas).Valor);
        Assert.Equal(1000m, jc.Itens.Single(i => i.Codigo == RubricasPadrao.TercoFeriasVencidas).Valor);
        // Incompatibilidades
        Assert.Throws<ErroCalculoFolhaException>(() => CalculadoraRescisao.Calcular(f,
            new SolicitacaoRescisao(MotivoRescisao.JustaCausa, dia, TipoAviso.Indenizado, 0, 0, []), Ctx(6)));
    }

    [Fact]
    public void Provisoes_de_ferias_e_13o_com_encargos()
    {
        var f = Func(3000m);
        var prov = CalculoProvisoes.Calcular(f, 3000m, 1m, new ParametrosEncargos(2m, 1m, 5.8m, RegimeEncargos.Geral), T2026.Fgts);
        Assert.Equal((333.33m, 250m), (prov.Ferias, prov.Decimo));
        Assert.Equal(69.50m, prov.EncargosDecimo.Previdenciarios);   // 27,8% (20 + RAT 2 + terceiros 5,8) sobre o 13º
        Assert.Equal(20m, prov.EncargosDecimo.Fgts);
    }

    [Fact]
    public void Calendario_pascoa_e_feriados()
    {
        Assert.Equal(new DateOnly(2026, 4, 5), Calendario.Pascoa(2026));
        Assert.Contains(new DateOnly(2026, 4, 3), Calendario.FeriadosNacionais(2026));
        Assert.Equal((26, 5), Calendario.DiasUteisEDescanso(2026, 3));
    }
}
