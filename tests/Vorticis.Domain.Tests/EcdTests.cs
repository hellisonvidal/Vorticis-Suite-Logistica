using Vorticis.Domain.Contabil;
using Vorticis.Domain.Sped;

namespace Vorticis.Domain.Tests;

public class EcdTests
{
    [Fact]
    public void Formata_campos_conforme_o_manual()
    {
        Assert.Equal("01012025", FormatoSped.Data(new DateOnly(2025, 1, 1)));
        Assert.Equal("", FormatoSped.Data(null));
        Assert.Equal("1129998,99", FormatoSped.Valor(1_129_998.99m));
        Assert.Equal("10,50", FormatoSped.Valor(-10.5m));
        Assert.Equal("0,00", FormatoSped.Valor(0));
        Assert.Equal(("D", "C", "C"), (FormatoSped.Indicador(5), FormatoSped.Indicador(-5), FormatoSped.Indicador(0, 'C')));
        Assert.Equal("A - B \"x\" 'y'... ?", FormatoSped.Texto("A | B “x” ‘y’… €"));
        Assert.Equal("Linha quebrada", FormatoSped.Texto("Linha\r\n  quebrada  "));
        Assert.Equal("Ação", FormatoSped.Texto("Ação"));
        Assert.Equal("abc", FormatoSped.Texto("abcdef", 3));
        Assert.Equal("|I010|G|9.00|", FormatoSped.Linha("I010", "G", "9.00"));
        Assert.Equal([0x7C, 0xC7, 0x7C, 0x0D, 0x0A], FormatoSped.Codificar(["|Ç|"]));
    }

    [Fact]
    public void Qualificacao_do_socio_vem_do_qsa()
    {
        Assert.Equal("205", QualificacaoAssinante.DeQualificacaoSocio(49, "Sócio-Administrador"));
        Assert.Equal("203", QualificacaoAssinante.DeQualificacaoSocio(null, "Diretor"));
        Assert.Equal("999", QualificacaoAssinante.DeQualificacaoSocio(22, "Sócio"));
        Assert.Equal("Contador", QualificacaoAssinante.Descricao("900"));
    }

    // Plano com PL em grupo próprio de nível 1 (3 raízes no balanço) e resultado em uma só raiz.
    private static readonly DateOnly Ini = new(2025, 1, 1);
    private static readonly List<ContaEcd> Plano =
    [
        new(1, "1", "ATIVO", 1, false, null, "01", IndicadorDC.D, Ini),
        new(2, "1.1", "CIRCULANTE", 2, false, 1, "01", IndicadorDC.D, Ini),
        new(3, "1.1.1", "DISPONÍVEL", 3, false, 2, "01", IndicadorDC.D, Ini),
        new(4, "1.1.1.01", "Banco", 4, true, 3, "01", IndicadorDC.D, Ini),
        new(10, "2", "PASSIVO", 1, false, null, "02", IndicadorDC.C, Ini),
        new(11, "2.1", "CIRCULANTE", 2, false, 10, "02", IndicadorDC.C, Ini),
        new(12, "2.1.1", "FORNECEDORES", 3, false, 11, "02", IndicadorDC.C, Ini),
        new(13, "2.1.1.01", "Fornecedor A", 4, true, 12, "02", IndicadorDC.C, Ini),
        new(20, "3", "PATRIMÔNIO LÍQUIDO", 1, false, null, "03", IndicadorDC.C, Ini),
        new(21, "3.1", "CAPITAL E LUCROS", 2, false, 20, "03", IndicadorDC.C, Ini),
        new(22, "3.1.1", "CAPITAL", 3, false, 21, "03", IndicadorDC.C, Ini),
        new(23, "3.1.1.01", "Capital social", 4, true, 22, "03", IndicadorDC.C, Ini),
        new(24, "3.1.2", "LUCROS ACUMULADOS", 3, false, 21, "03", IndicadorDC.C, Ini),
        new(25, "3.1.2.01", "Lucros acumulados", 4, true, 24, "03", IndicadorDC.C, Ini),
        new(30, "4", "RESULTADO", 1, false, null, "04", IndicadorDC.C, Ini),
        new(31, "4.1", "RECEITAS", 2, false, 30, "04", IndicadorDC.C, Ini),
        new(32, "4.1.01", "Vendas", 3, true, 31, "04", IndicadorDC.C, Ini),
        new(33, "4.2", "APURAÇÃO", 2, false, 30, "04", IndicadorDC.C, Ini),
        new(34, "4.2.01", "Apuração", 3, true, 33, "04", IndicadorDC.C, Ini),
    ];

    private static LancamentoEcd Lanc(long n, DateOnly d, long deb, long cred, decimal v, IndicadorLancamento ind = IndicadorLancamento.Normal) =>
        new(n, n, d, ind, null, [new(deb, null, IndicadorDC.D, v, "Hist"), new(cred, null, IndicadorDC.C, v, "Hist")]);

    private static DadosEcd Dados(TipoMutacoesEcd mutacoes = TipoMutacoesEcd.Dmpl, bool encerrar = true)
    {
        var fim = new DateOnly(2025, 12, 31);
        var lancs = new List<LancamentoEcd>
        {
            Lanc(1, new(2025, 2, 1), 4, 13, 300m),          // compra a prazo (ativo × passivo)
            Lanc(2, new(2025, 5, 1), 4, 32, 1_000m),        // venda
        };
        if (encerrar)
        {
            lancs.Add(Lanc(3, fim, 32, 34, 1_000m, IndicadorLancamento.Encerramento));
            lancs.Add(Lanc(4, fim, 34, 25, 1_000m, IndicadorLancamento.Encerramento));
        }
        return new DadosEcd(
            new EmpresaEcd("11222333000181", "Teste SA", "SP", "3550308", "São Paulo", null, null, null, false, new DateOnly(2010, 1, 1)),
            new ParametrosEcd(Ini, fim, Mutacoes: mutacoes, DataArquivamentoAtos: new DateOnly(2010, 1, 1)),
            Plano, [], [], [new(4, 0, 5_000m), new(23, 0, -5_000m)], lancs,
            [new("Contador", "52998224725", "Contador", "900", false, "1SP1", "SP", "a@b.c", "1133334444", "SP/2025/1", new DateOnly(2026, 1, 1)),
             new("Sócia", "39053344705", "Administrador", "205", true)]);
    }

    [Fact]
    public void Balanco_com_pl_em_raiz_propria_agrupa_passivo_e_pl_em_uma_linha_de_nivel_1()
    {
        var calc = new CalculoEcd(Dados());
        var nivel1 = calc.Balanco.Where(b => b.Nivel == 1).ToList();
        Assert.Equal([("1", 'A'), ("P", 'P')], nivel1.Select(b => (b.Codigo, b.Grupo)));
        var p = nivel1[1];
        Assert.True(p.Totalizador);
        Assert.Equal(-6_300m, p.Final);                                   // 300 fornecedor + 5.000 capital + 1.000 lucro
        Assert.Equal(6_300m, nivel1[0].Final);
        Assert.Contains(calc.Balanco, b => b is { Codigo: "3", Nivel: 2, CodigoSuperior: "P" });
        Assert.Contains(calc.Balanco, b => b is { Codigo: "3.1.1", Totalizador: false, Nivel: 4 });
        Assert.Equal("1.1.1", calc.Aglutinacao[4]);
        Assert.Equal("4.1.01", calc.Aglutinacao[32]);                    // analítica de nível < aglutinação é o próprio detalhe

        // DRE com uma só raiz: ela é o nível 1
        Assert.Equal(("4", true, 1, -1_000m), (calc.Dre[0].Codigo, calc.Dre[0].Totalizador, calc.Dre[0].Nivel, calc.Dre[0].Final));

        var e = GeradorEcd.Gerar(Dados());
        Assert.Contains("|J100|P|T|1||P|PASSIVO E PATRIMÔNIO LÍQUIDO|5000,00|C|6300,00|C||", e.Linhas);
        Assert.Contains("|I355|4.1.01||1000,00|C|", e.Linhas);
        Assert.Contains("|I355|4.2.01||0,00|C|", e.Linhas);
        Assert.Contains(ValidadorEcd.Validar(Dados(), calc), x => x.Codigo == "NIRE_AUSENTE" && !x.Bloqueante);
        Assert.DoesNotContain(ValidadorEcd.Validar(Dados(), calc), x => x.Bloqueante);
    }

    [Fact]
    public void Dlpa_usa_um_unico_j210_de_lucros_acumulados()
    {
        var calc = new CalculoEcd(Dados(TipoMutacoesEcd.Dlpa));
        Assert.Equal(TipoMutacoesEcd.Dlpa, calc.TipoMutacoes);
        var m = Assert.Single(calc.MutacoesPl);
        Assert.Equal(("3.1.2", 0m, -1_000m), (m.Codigo, m.Inicial, m.Final));
        Assert.Equal(("1", 1_000m), (m.Fatos[0].Codigo, m.Fatos[0].Valor));
        Assert.Contains("|J210|0|3.1.2|LUCROS ACUMULADOS|0,00|C|1000,00|C||", GeradorEcd.Gerar(Dados(TipoMutacoesEcd.Dlpa)).Linhas);
    }

    [Fact]
    public void Sem_encerramento_e_com_signatario_incompleto_ha_bloqueantes()
    {
        var d = Dados(encerrar: false) with { Signatarios = [new("Contador", "11111111111", "Contador", "900", true)] };
        var p = ValidadorEcd.Validar(d, new CalculoEcd(d)).Where(x => x.Bloqueante).Select(x => x.Codigo).ToHashSet();
        Assert.Superset(new HashSet<string> { "NAO_ENCERRADO", "RESULTADO_NAO_ZERADO", "CONTADOR_CPF", "CONTADOR_CRC", "CONTADOR_EMAIL",
                                              "CONTADOR_RESPONSAVEL" }, p);
    }
}
