using System.Text;
using Vorticis.Domain.Contabil;

namespace Vorticis.Domain.Tests;

/// <summary>Tabelas FICTÍCIAS, só para testar o leitor — não são o plano referencial da RFB.</summary>
public class PlanoReferencialTests
{
    private const string Completa = """
        CODIGO|DESCRICAO|DT_INI|DT_FIM|ORDEM|TIPO|COD_SUP|NIVEL|COD_NAT
        1|ATIVO|01012026||1|S||1|01
        1.01|ATIVO CIRCULANTE|01012026||2|S|1|2|01
        1.01.01|Caixa e equivalentes de caixa|01012026||3|A|1.01|3|01
        2|PASSIVO|01012026||4|S||1|02
        2.03|PATRIMÔNIO LÍQUIDO|01012026||5|S|2|2|03
        2.03.01|Capital social realizado|01012026||6|A|2.03|3|03
        """;

    [Fact]
    public void Le_formato_completo_com_cabecalho()
    {
        var r = LeitorPlanoReferencial.Ler(Completa);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        var caixa = r.Valor.Single(c => c.Codigo == "1.01.01");
        Assert.Equal((true, (short)3, "1.01", "01", new DateOnly(2026, 1, 1)), (caixa.Analitica, caixa.Nivel, caixa.CodigoPai, caixa.CodNat, caixa.Inicio));
        Assert.Equal("03", r.Valor.Single(c => c.Codigo == "2.03.01").CodNat);
    }

    [Fact]
    public void Deduz_tipo_nivel_pai_e_natureza_quando_ausentes()
    {
        const string csv = "Código;Descrição\n1;ATIVO\n1.01;Disponível\n2;PASSIVO E PL\n2.01;Fornecedores\n2.03;Patrimônio Líquido\n2.03.01;Capital\n3;RESULTADO\n3.01;Receita de vendas\n";
        var r = LeitorPlanoReferencial.Ler(csv);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        var c = r.Valor.ToDictionary(x => x.Codigo);
        Assert.False(c["1"].Analitica);
        Assert.True(c["1.01"].Analitica);
        Assert.Equal((short)3, c["2.03.01"].Nivel);
        Assert.Equal(("01", "02", "03", "04"), (c["1.01"].CodNat, c["2.01"].CodNat, c["2.03.01"].CodNat, c["3.01"].CodNat));
    }

    [Theory]
    [InlineData("CONTA_X|NOME_Y\n1|A\n", "Cabeçalho não reconhecido")]
    [InlineData("CODIGO|DESCRICAO\n1|A\n1|B\n", "repetidos")]
    [InlineData("CODIGO|DESCRICAO\n1.01|A\n", "conta superior não está no arquivo")]
    [InlineData("CODIGO|DESCRICAO|TIPO\n1|A|A\n1.01|B|A\n", "analítica, mas tem subcontas")]
    [InlineData("CODIGO|DESCRICAO\n", "sem linhas de dados")]
    public void Rejeita_arquivos_invalidos(string texto, string erro) =>
        Assert.Contains(LeitorPlanoReferencial.Ler(texto).Erros, e => e.Contains(erro));

    [Fact]
    public void Decodifica_iso_8859_1_dos_arquivos_da_rfb()
    {
        var latin1 = Encoding.Latin1.GetBytes("CODIGO|DESCRICAO\n1|PATRIMÔNIO LÍQUIDO\n");
        Assert.Contains("PATRIMÔNIO LÍQUIDO", LeitorPlanoReferencial.Decodificar(latin1));
        Assert.Contains("Ação", LeitorPlanoReferencial.Decodificar(Encoding.UTF8.GetBytes("Ação")));
    }

    [Fact]
    public void Sugere_pela_descricao_respeitando_natureza_e_analiticas()
    {
        ContaReferencial[] refs =
        [
            new("1.01.01", "Caixa e equivalentes de caixa", true, 3, "1.01", "01"),
            new("1.01.02", "Clientes", true, 3, "1.01", "01"),
            new("1.01", "Ativo circulante — caixa", false, 2, "1", "01"),
            new("3.01.01", "Receita de vendas de mercadorias", true, 3, "3.01", "04"),
        ];
        Assert.Equal("1.01.01", SugestaoReferencial.Sugerir("Caixa Geral", "01", refs)?.Conta.Codigo);
        Assert.Equal("3.01.01", SugestaoReferencial.Sugerir("Venda de Mercadorias", "04", refs)?.Conta.Codigo);
        Assert.Null(SugestaoReferencial.Sugerir("Caixa Geral", "04", refs));   // natureza diferente
        Assert.Null(SugestaoReferencial.Sugerir("Veículos", "01", refs));
    }
}
