using System.Text;
using Vorticis.Fiscal.Documentos;

namespace Vorticis.Fiscal.Tests;

public class RegrasFiscaisTests
{
    private static readonly RegraFiscal Geral51 = new(1, 1, "Compras em geral no estado", TipoOperacao.Entrada, "51", "1102");
    private static readonly RegraFiscal Industria = new(2, 2, "Compra para industrialização", TipoOperacao.Entrada, "5101", "1101");
    private static readonly RegraFiscal Inativa = new(3, 3, "Desligada", TipoOperacao.Entrada, "5405", "1403", Ativa: false);
    private static readonly RegraFiscal VendaSt = new(4, 4, "Venda com ST", TipoOperacao.Saida, "5102", "5405", CstIcms: "060", CstPis: "04");

    private static ItemFiscal Item(string cfop) =>
        new(1, "P1", "Peça", "84714900", null, cfop, "UN", 1, 10, 10, 0, 0, 0, 0, 10, "00", "0", 0, 0, 0, 0, 0, null, 0, 0, 0, "01", 0, 0, 0, "01", 0, 0, 0, 0, 0, 0);

    [Fact]
    public void A_mais_especifica_da_operacao_vence_e_inativa_nao_vale()
    {
        RegraFiscal[] regras = [Geral51, Industria, Inativa, VendaSt];
        Assert.Equal("1101", RegrasFiscais.Encontrar(regras, TipoOperacao.Entrada, "5101")!.CfopDestino);   // 5101 vence 51
        Assert.Equal("1102", RegrasFiscais.Encontrar(regras, TipoOperacao.Entrada, "5102")!.CfopDestino);   // 51 vale para os 51xx
        Assert.Equal("5405", RegrasFiscais.Encontrar(regras, TipoOperacao.Saida, "5102")!.CfopDestino);     // a de saída só vale na saída
        Assert.Null(RegrasFiscais.Encontrar(regras, TipoOperacao.Entrada, "5405"));                         // só a inativa casava
        Assert.Null(RegrasFiscais.Encontrar(regras, TipoOperacao.Saida, "6102"));
    }

    [Fact]
    public void Aplicar_troca_cfop_e_csts_preenchidos_e_guarda_o_original()
    {
        var item = Item("5101");
        var outro = item with { Numero = 2, Cfop = "6102" };
        var r = RegrasFiscais.Aplicar([item, outro], TipoOperacao.Entrada, [Geral51, Industria]);
        Assert.Equal(("1101", "5101", "00", "01"), (r[0].Cfop, r[0].CfopOriginal, r[0].CstIcms, r[0].CstPis));   // CSTs em branco: os do XML
        Assert.Same(outro, r[1]);                                                                                   // sem regra: não muda
        Assert.Equal("1102", RegrasFiscais.Aplicar(r, TipoOperacao.Entrada, [Geral51])[0].Cfop);                     // parte do CFOP original
        var s = RegrasFiscais.Aplicar([Item("5102")], TipoOperacao.Saida, [Geral51, VendaSt])[0];
        Assert.Equal(("5405", "5102", "060", "04", "01"), (s.Cfop, s.CfopOriginal, s.CstIcms, s.CstPis, s.CstCofins));
    }

    [Fact]
    public void Valida_cfops_e_csts_conforme_a_operacao()
    {
        Assert.Empty(RegrasFiscais.Validar(Geral51));
        Assert.Empty(RegrasFiscais.Validar(VendaSt));
        Assert.Contains(RegrasFiscais.Validar(Geral51 with { CfopOrigem = "1102" }), e => e.StartsWith("CFOP do XML"));
        Assert.Contains(RegrasFiscais.Validar(Geral51 with { CfopDestino = "5102" }), e => e.Contains("(entrada)"));
        Assert.Contains(RegrasFiscais.Validar(VendaSt with { CfopDestino = "1102" }), e => e.Contains("(saída)"));
        Assert.Contains(RegrasFiscais.Validar(VendaSt with { CstPis = "1" }), e => e.StartsWith("CST do PIS"));
        Assert.Contains(RegrasFiscais.Validar(Geral51 with { Descricao = " " }), e => e.Contains("descrição"));
        Assert.Null(RegrasFiscais.Normalizar(Geral51 with { CstIcms = " " }).CstIcms);
        Assert.All(RegrasFiscais.Sugeridas, n => Assert.Empty(RegrasFiscais.Validar(n)));
        Assert.Equal(RegrasFiscais.Sugeridas.Count, RegrasFiscais.Sugeridas.Select(n => n.CfopOrigem).Distinct().Count());
    }
}

public class RegrasFiscaisIntegracaoTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Importacao_aplica_as_regras_de_entrada_e_as_de_saida()
    {
        BancoDeTeste.ExigirBanco();
        var cnpj = CnpjsDeTeste.Valido(7501);
        var empresa = await db.CriarEmpresaAsync(cnpj);
        var adm = await db.AdminAsync();
        var fiscal = db.App.Fiscal();
        var regras = fiscal.RegrasFiscais;
        Assert.False((await regras.GravarAsync(empresa, new RegraFiscal(0, 1, "Errada", TipoOperacao.Entrada, "1102", "1102"), adm, Ct)).Sucesso);
        Assert.Equal(RegrasFiscais.Sugeridas.Count, (await regras.IncluirSugeridasAsync(empresa, adm, Ct)).Valor);
        Assert.Equal(0, (await regras.IncluirSugeridasAsync(empresa, adm, Ct)).Valor);              // não repete
        Assert.False((await regras.GravarAsync(empresa, new RegraFiscal(0, 99, "Repetida", TipoOperacao.Entrada, "5102", "1102"), adm, Ct)).Sucesso);
        Assert.True((await regras.GravarAsync(empresa, new RegraFiscal(0, 50, "Demais compras do estado", TipoOperacao.Entrada, "51", "1949"), adm, Ct)).Sucesso);
        // o mesmo CFOP do XML pode ter uma regra de entrada e outra de saída
        Assert.True((await regras.GravarAsync(empresa, new RegraFiscal(0, 60, "Venda com ST", TipoOperacao.Saida, "5102", "5405", " 60 ", "", null), adm, Ct)).Sucesso);
        Assert.Equal("60", (await regras.ListarAsync(empresa, adm, Ct)).Single(r => r.Codigo == 60).CstIcms);

        var mar = new DateOnly(2026, 3, 2);
        var compra = XmlsDeTeste.Nfe(XmlsDeTeste.CnpjFornecedor, cnpj, 1, 901, mar, [new ItemNfe("5102", 100m), new ItemNfe("6102", 50m), new ItemNfe("5120", 10m), new ItemNfe("5551", 5m)]);
        var venda = XmlsDeTeste.Nfe(cnpj, XmlsDeTeste.CnpjCliente, 1, 902, mar, [new ItemNfe("5102", 70m), new ItemNfe("6102", 30m)], chave: XmlsDeTeste.Chave(902));
        var r = await fiscal.Documentos.ImportarAsync(empresa, [new ArquivoXml("c.xml", Encoding.UTF8.GetBytes(compra)), new ArquivoXml("v.xml", Encoding.UTF8.GetBytes(venda))], adm, ct: Ct);
        Assert.Equal(2, r.Valor.Importados);

        var docs = (await fiscal.Documentos.ConsultarAsync(empresa, new FiltroDocumentos(mar, mar), adm, Ct)).Valor;
        var entrada = (await fiscal.Digitacao.ObterAsync(empresa, docs.Single(d => d.Operacao == TipoOperacao.Entrada).Id, adm, Ct)).Valor;
        // 5102 → 1102 e 6102 → 2102 (sugeridas); 5120 → 1949 pela regra "51"; 5551 não tem regra e fica como veio
        Assert.Equal([("1102", "5102"), ("2102", "6102"), ("1949", "5120"), ("5551", "5551")], entrada.Itens.Select(i => (i.Cfop, i.CfopOriginal ?? i.Cfop)));
        var saida = (await fiscal.Digitacao.ObterAsync(empresa, docs.Single(d => d.Operacao == TipoOperacao.Saida).Id, adm, Ct)).Valor;
        Assert.Equal([("5405", "5102", "60"), ("6102", "6102", saida.Itens[1].CstIcms)], saida.Itens.Select(i => (i.Cfop, i.CfopOriginal ?? i.Cfop, i.CstIcms)));
    }
}
