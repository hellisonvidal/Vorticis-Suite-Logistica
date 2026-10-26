using System.Text;
using Vorticis.Fiscal.Documentos;

namespace Vorticis.Fiscal.Tests;

public class ConferenciaXmlTests
{
    /// <summary>Chave válida montada como a SEFAZ (UF, AAMM, CNPJ do emitente, modelo, série, número, tipo de emissão, código e dígito).</summary>
    private static string ChaveValida(string cnpj, int numero, string modelo = "55", int serie = 1)
    {
        var c43 = $"352603{cnpj}{modelo}{serie:000}{numero:000000000}1{12345678}";
        int soma = 0, peso = 2;
        for (var i = 42; i >= 0; i--) { soma += (c43[i] - '0') * peso; peso = peso == 9 ? 2 : peso + 1; }
        var resto = soma % 11;
        return c43 + (resto < 2 ? 0 : 11 - resto);
    }

    private static DocumentoLido Ler(string xml) => ((LeituraDocumento)LeitorXmlFiscal.Ler(Encoding.UTF8.GetBytes(xml))).Documento;

    // Item de 1.000,00 com ICMS 18% = 180,00, PIS 1,65% = 16,50 e COFINS 7,6% = 76,00: tudo confere
    private static readonly ItemNfe Certo = new("5102", 1000m, Icms: 180m, Pis: 16.50m, Cofins: 76m);

    [Fact]
    public void Nota_coerente_nao_tem_avisos()
    {
        var doc = Ler(XmlsDeTeste.Nfe(XmlsDeTeste.CnpjFornecedor, XmlsDeTeste.CnpjEmpresa, 1, 123, new DateOnly(2026, 3, 2), [Certo],
                                      chave: ChaveValida(XmlsDeTeste.CnpjFornecedor, 123)));
        Assert.Empty(ConferenciaXml.Conferir(doc));
    }

    [Fact]
    public void Chave_e_conferida_com_digito_cnpj_serie_e_numero()
    {
        string Aviso(string chave) => string.Join(" | ", ConferenciaXml.Conferir(Ler(XmlsDeTeste.Nfe(XmlsDeTeste.CnpjFornecedor, XmlsDeTeste.CnpjEmpresa, 1, 123,
                                                                                                        new DateOnly(2026, 3, 2), [Certo], chave: chave))));
        var boa = ChaveValida(XmlsDeTeste.CnpjFornecedor, 123);
        Assert.Contains("dígito verificador", Aviso(boa[..43] + (boa[43] == '9' ? '0' : (char)(boa[43] + 1))));
        Assert.Contains("CNPJ do emitente", Aviso(ChaveValida(XmlsDeTeste.CnpjCliente, 123)));
        Assert.Contains("número da chave (124)", Aviso(ChaveValida(XmlsDeTeste.CnpjFornecedor, 124)));
        Assert.Contains("série da chave (2)", Aviso(ChaveValida(XmlsDeTeste.CnpjFornecedor, 123, serie: 2)));
        Assert.Contains("modelo 65", Aviso(ChaveValida(XmlsDeTeste.CnpjFornecedor, 123, modelo: "65")));
    }

    [Fact]
    public void Itens_sao_conferidos_tributo_por_tributo_e_contra_os_totais()
    {
        // ICMS de 150,00 com base 1.000,00 × 18% (= 180,00); PIS sem valor; sem NCM
        var errado = new ItemNfe("5102", 1000m, Icms: 150m, Pis: 0m, Cofins: 76m, Ncm: "");
        var avisos = ConferenciaXml.Conferir(Ler(XmlsDeTeste.Nfe(XmlsDeTeste.CnpjFornecedor, XmlsDeTeste.CnpjEmpresa, 1, 123, new DateOnly(2026, 3, 2), [Certo, errado],
                                                                 chave: ChaveValida(XmlsDeTeste.CnpjFornecedor, 123))));
        Assert.Contains(avisos, a => a.StartsWith("Item 2: ICMS de 150,00 não confere com base 1.000,00 × 18% = 180,00"));
        Assert.Contains(avisos, a => a.StartsWith("Item 2: PIS"));
        Assert.Contains(avisos, a => a == "Item 2: sem NCM.");
        Assert.DoesNotContain(avisos, a => a.StartsWith("Item 1"));

        // vProd do item (e do total de produtos) adulterado: quantidade × unitário não bate e a soma dos itens difere do vNF
        var xml = XmlsDeTeste.Nfe(XmlsDeTeste.CnpjFornecedor, XmlsDeTeste.CnpjEmpresa, 1, 123, new DateOnly(2026, 3, 2), [Certo],
                                  chave: ChaveValida(XmlsDeTeste.CnpjFornecedor, 123)).Replace("<vProd>1000.00</vProd><vFrete>", "<vProd>990.00</vProd><vFrete>");
        avisos = ConferenciaXml.Conferir(Ler(xml));
        Assert.Contains(avisos, a => a.StartsWith("Item 1: quantidade × valor unitário (1.000,00) difere do valor do produto (990,00)"));
        Assert.Contains(avisos, a => a == "A soma dos itens difere do total da nota em -10,00.");
    }
}
