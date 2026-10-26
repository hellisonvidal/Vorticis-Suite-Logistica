using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;

namespace Vorticis.Domain.Tests;

public class ImportacaoPlanoTests
{
    private static MascaraPlano M(string m) => MascaraPlano.Ler(m).Valor;

    [Theory]
    [InlineData("1.1.1.1", "1.1.01.001")]
    [InlineData("1.1.01.001", "1.1.01.001")]
    [InlineData("1-1-1-12", "1.1.01.012")]
    [InlineData("1101001", "1.1.01.001")]
    [InlineData("11", "1.1")]
    [InlineData("111", "1.1.01")]
    [InlineData("110112", "1.1.01.012")]
    [InlineData("1", "1")]
    [InlineData("2.0.3", "2.0.03")]
    public void Converte_codigos_para_a_mascara(string original, string esperado)
    {
        var r = M("9.9.99.999").Converter(original);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        Assert.Equal(esperado, r.Valor);
    }

    [Theory]
    [InlineData("1.1.100.1", "não cabe")]
    [InlineData("1.1.1.1.1", "níveis")]
    [InlineData("110100010", "mais dígitos")]
    [InlineData("1.A.01", "letras")]
    public void Recusa_codigos_que_nao_cabem(string original, string erro) =>
        Assert.Contains(erro, M("9.9.99.999").Converter(original).Erros[0]);

    [Fact]
    public void Mascara_aceita_outros_separadores_e_deduz_a_atual()
    {
        Assert.Equal([1, 1, 2, 3], M("X-X-XX-XXX").Segmentos);
        Assert.Equal("9.9.99.999", M("#.#.##.###").Texto);
        Assert.False(MascaraPlano.Ler("").Sucesso);
        Assert.Equal("9.9.99.999", MascaraPlano.Deduzir(["1", "1.1", "1.1.01", "1.1.01.001"]));
    }

    [Fact]
    public void Le_planilha_deduzindo_tipo_natureza_e_saldo()
    {
        var t = TabelaImportada.DeTexto("""
            Classificação;Nome da conta;Tipo
            1;ATIVO;S
            1.1;CIRCULANTE;S
            1.1.1;DISPONIVEL;S
            1.1.1.1;Caixa;A
            2;PASSIVO E PATRIMONIO LIQUIDO;S
            2.3;PATRIMÔNIO LÍQUIDO;S
            2.3.1;"Capital; social";S
            2.3.1.1;Capital subscrito;A
            3;RESULTADO;S
            3.1;RECEITAS;S
            3.1.1;Receitas de vendas;S
            3.1.1.1;Vendas de mercadorias;A
            3.2;DESPESAS;S
            3.2.1;Administrativas;S
            3.2.1.10;Aluguéis;A
            """);
        var colunas = ColunasPlano.Detectar(t);
        Assert.Equal((0, 1, -1, 2), (colunas.Codigo, colunas.Descricao, colunas.Natureza, colunas.Tipo));
        var plano = LeitorPlanoContas.Ler(t, colunas, M("9.9.99.999"));
        Assert.True(plano.Valido, string.Join("; ", plano.Erros));
        var c = plano.Contas.ToDictionary(x => x.Codigo);
        Assert.Equal(("01", IndicadorDC.D, true, (short)4), (c["1.1.01.001"].CodNat, c["1.1.01.001"].NaturezaSaldo, c["1.1.01.001"].Analitica, c["1.1.01.001"].Nivel));
        Assert.Equal(("03", IndicadorDC.C), (c["2.3.01.001"].CodNat, c["2.3.01.001"].NaturezaSaldo));
        Assert.Equal("Capital; social", c["2.3.01"].Descricao);
        Assert.Equal(("04", IndicadorDC.C), (c["3.1.01.001"].CodNat, c["3.1.01.001"].NaturezaSaldo));
        Assert.Equal(("04", IndicadorDC.D, 16), (c["3.2.01.010"].CodNat, c["3.2.01.010"].NaturezaSaldo, c["3.2.01.010"].Linha));
        Assert.False(c["1"].Analitica);
        Assert.Equal(["1", "1.1", "1.1.01", "1.1.01.001"], plano.Contas.Take(4).Select(x => x.Codigo));
    }

    [Fact]
    public void Aponta_conta_superior_ausente_repetidos_e_analitica_com_filhas()
    {
        var t = TabelaImportada.DeTexto("Codigo,Descricao,Tipo\n1,ATIVO,S\n1.1.01,Sem pai,A\n1.1.01.1,Filha,A\n1.1.01.001,Repetida,A\n");
        var plano = LeitorPlanoContas.Ler(t, ColunasPlano.Detectar(t), M("9.9.99.999"));
        Assert.False(plano.Valido);
        Assert.Contains(plano.Erros, e => e.Contains("1.1.01.001 repetido"));
        Assert.Contains(plano.Erros, e => e.Contains("conta superior 1.1 de 1.1.01"));
        Assert.Contains(plano.Erros, e => e.Contains("analítica, mas tem subcontas"));
    }

    [Fact]
    public void Sem_mascara_mantem_niveis_e_troca_separadores()
    {
        var t = TabelaImportada.DeTexto("Conta|Descrição|Natureza\n1|ATIVO|Ativo\n1-01|CIRCULANTE|\n1-01-001|Caixa|\n");
        var plano = LeitorPlanoContas.Ler(t, ColunasPlano.Detectar(t), null);
        Assert.True(plano.Valido, string.Join("; ", plano.Erros));
        Assert.Equal(["1", "1.01", "1.01.001"], plano.Contas.Select(c => c.Codigo));
        Assert.All(plano.Contas, c => Assert.Equal("01", c.CodNat));
    }
}
