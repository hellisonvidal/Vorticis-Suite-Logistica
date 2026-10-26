using System.Text;
using Vorticis.Fiscal.Documentos;

namespace Vorticis.Fiscal.Tests;

public class RegrasNaturezasTests
{
    private static readonly NaturezaEntrada Geral51 = new(1, 1, "Compras em geral no estado", "51", "1102");
    private static readonly NaturezaEntrada Industria = new(2, 2, "Compra para industrialização", "5101", "1101");
    private static readonly NaturezaEntrada Inativa = new(3, 3, "Desligada", "5405", "1403", Ativa: false);

    [Fact]
    public void A_mais_especifica_vence_e_inativa_nao_vale()
    {
        NaturezaEntrada[] regras = [Geral51, Industria, Inativa];
        Assert.Equal("1101", RegrasNaturezas.Encontrar(regras, "5101")!.CfopEntrada);   // 5101 vence 51
        Assert.Equal("1102", RegrasNaturezas.Encontrar(regras, "5102")!.CfopEntrada);   // 51 vale para os 51xx
        Assert.Null(RegrasNaturezas.Encontrar(regras, "5405"));                         // só a inativa casava
        Assert.Null(RegrasNaturezas.Encontrar(regras, "6102"));
    }

    [Fact]
    public void Aplicar_troca_o_cfop_e_guarda_o_original()
    {
        var item = new ItemFiscal(1, "P1", "Peça", "84714900", null, "5101", "UN", 1, 10, 10, 0, 0, 0, 0, 10, "00", "0", 0, 0, 0, 0, 0, null, 0, 0, 0, null, 0, 0, 0, null, 0, 0, 0, 0, 0, 0);
        var outro = item with { Numero = 2, Cfop = "6102" };
        var r = RegrasNaturezas.Aplicar([item, outro], [Geral51, Industria]);
        Assert.Equal(("1101", "5101"), (r[0].Cfop, r[0].CfopOriginal));
        Assert.Same(outro, r[1]);                                                        // sem natureza: não muda
        // reaplicar parte do CFOP original (não do de entrada)
        Assert.Equal("1102", RegrasNaturezas.Aplicar(r, [Geral51])[0].Cfop);
    }

    [Fact]
    public void Valida_cfops_e_as_sugeridas_sao_coerentes()
    {
        Assert.Empty(RegrasNaturezas.Validar(Geral51));
        Assert.Contains(RegrasNaturezas.Validar(Geral51 with { CfopSaida = "1102" }), e => e.StartsWith("CFOP de saída"));
        Assert.Contains(RegrasNaturezas.Validar(Geral51 with { CfopEntrada = "5102" }), e => e.StartsWith("CFOP de entrada"));
        Assert.Contains(RegrasNaturezas.Validar(Geral51 with { Descricao = " " }), e => e.Contains("descrição"));
        Assert.All(RegrasNaturezas.Sugeridas, n => Assert.Empty(RegrasNaturezas.Validar(n)));
        Assert.Equal(RegrasNaturezas.Sugeridas.Count, RegrasNaturezas.Sugeridas.Select(n => n.CfopSaida).Distinct().Count());
    }
}

public class NaturezasIntegracaoTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Importacao_de_entrada_usa_as_naturezas_e_a_saida_nao()
    {
        BancoDeTeste.ExigirBanco();
        var cnpj = CnpjsDeTeste.Valido(7501);
        var empresa = await db.CriarEmpresaAsync(cnpj);
        var adm = await db.AdminAsync();
        var fiscal = db.App.Fiscal();
        Assert.False((await fiscal.Naturezas.GravarAsync(empresa, new NaturezaEntrada(0, 1, "Errada", "1102", "1102"), adm, Ct)).Sucesso);
        Assert.Equal(RegrasNaturezas.Sugeridas.Count, (await fiscal.Naturezas.IncluirSugeridasAsync(empresa, adm, Ct)).Valor);
        Assert.Equal(0, (await fiscal.Naturezas.IncluirSugeridasAsync(empresa, adm, Ct)).Valor);              // não repete
        Assert.False((await fiscal.Naturezas.GravarAsync(empresa, new NaturezaEntrada(0, 99, "Repetida", "5102", "1102"), adm, Ct)).Sucesso);
        Assert.True((await fiscal.Naturezas.GravarAsync(empresa, new NaturezaEntrada(0, 50, "Demais vendas do estado", "51", "1949"), adm, Ct)).Sucesso);

        var mar = new DateOnly(2026, 3, 2);
        var compra = XmlsDeTeste.Nfe(XmlsDeTeste.CnpjFornecedor, cnpj, 1, 901, mar, [new ItemNfe("5102", 100m), new ItemNfe("6102", 50m), new ItemNfe("5120", 10m), new ItemNfe("5551", 5m)]);
        var venda = XmlsDeTeste.Nfe(cnpj, XmlsDeTeste.CnpjCliente, 1, 902, mar, [new ItemNfe("5102", 70m)], chave: XmlsDeTeste.Chave(902));
        var r = await fiscal.Documentos.ImportarAsync(empresa, [new ArquivoXml("c.xml", Encoding.UTF8.GetBytes(compra)), new ArquivoXml("v.xml", Encoding.UTF8.GetBytes(venda))], adm, ct: Ct);
        Assert.Equal(2, r.Valor.Importados);

        var docs = (await fiscal.Documentos.ConsultarAsync(empresa, new FiltroDocumentos(mar, mar), adm, Ct)).Valor;
        var entrada = (await fiscal.Digitacao.ObterAsync(empresa, docs.Single(d => d.Operacao == TipoOperacao.Entrada).Id, adm, Ct)).Valor;
        // 5102 → 1102 e 6102 → 2102 (sugeridas); 5120 → 1949 pela natureza "51"; 5551 não tem natureza e fica como veio
        Assert.Equal([("1102", "5102"), ("2102", "6102"), ("1949", "5120"), ("5551", "5551")], entrada.Itens.Select(i => (i.Cfop, i.CfopOriginal ?? i.Cfop)));
        var saida = (await fiscal.Digitacao.ObterAsync(empresa, docs.Single(d => d.Operacao == TipoOperacao.Saida).Id, adm, Ct)).Valor;
        Assert.Equal("5102", saida.Itens.Single().Cfop);
    }
}
