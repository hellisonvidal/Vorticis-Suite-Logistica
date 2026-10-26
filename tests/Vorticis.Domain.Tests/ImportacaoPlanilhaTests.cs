using Vorticis.Domain.Comum;

namespace Vorticis.Domain.Tests;

public class ImportacaoPlanilhaTests
{
    private static readonly CampoImportacao[] Campos =
    [
        new("nome", "Nome / razão social", true, "", "nome", "razao_social"),
        new("cpfcnpj", "CPF/CNPJ", false, "", "cnpj", "cpf", "documento"),
        new("ativo", "Ativo", false, ""),
    ];

    [Fact]
    public void Sugere_as_colunas_pelos_sinonimos_sem_acento_nem_maiusculas_e_monta_as_linhas()
    {
        var t = TabelaImportada.DeTexto("Razão Social;Documento;Observação;ATIVO\nAlfa Ltda;11.222.333/0001-81;x;S\n;;;\nBeta;;;não");
        var mapa = ImportacaoPlanilha.Sugerir(t, Campos);
        Assert.Equal(new Dictionary<string, string> { ["nome"] = "Razão Social", ["cpfcnpj"] = "Documento", ["ativo"] = "ATIVO" }, mapa);
        Assert.Empty(ImportacaoPlanilha.Faltam(Campos, mapa));
        Assert.Equal(["Nome / razão social"], ImportacaoPlanilha.Faltam(Campos, new Dictionary<string, string> { ["cpfcnpj"] = "Documento" }));
        var linhas = ImportacaoPlanilha.Linhas(t, mapa);
        Assert.Equal(2, linhas.Count);                                   // a linha vazia some
        Assert.Equal((2, "Alfa Ltda", "11.222.333/0001-81", true), (linhas[0].Numero, linhas[0].Texto("nome"), linhas[0].Texto("cpfcnpj"), linhas[0].Sim("ativo")));
        Assert.Equal((4, (string?)null, false), (linhas[1].Numero, linhas[1].Texto("cpfcnpj"), linhas[1].Sim("ativo", true)));
        Assert.True(new LinhaImportacao(1, new Dictionary<string, string>()).Sim("ativo", true));   // vazio usa o padrão
    }

    [Fact]
    public void Linha_le_valores_inteiros_e_datas_nos_formatos_da_planilha()
    {
        var l = new LinhaImportacao(2, new Dictionary<string, string> { ["v"] = "1.234,56", ["i"] = "2023", ["f"] = "2,5", ["d"] = "31/12/2027", ["x"] = "1500.5" });
        Assert.Equal((1234.56m, 2023, (int?)null, new DateOnly(2027, 12, 31), 1500.5m), (l.Valor("v"), l.Inteiro("i"), l.Inteiro("f"), l.Data("d"), l.Valor("x")));
    }
}
