using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Domain.Comum;
using Vorticis.Fiscal.Documentos;
using Vorticis.Fiscal.Produtos;

namespace Vorticis.Fiscal.Desktop;

/// <summary>Importação de planilha dos cadastros fiscais/gerenciais: produtos e naturezas de entrada.</summary>
public static class ImportacoesFiscais
{
    private static readonly DefinicaoImportacao Produtos = new("produtos", "Produtos",
    [
        new("codigo", "Código", false, "1", "cod", "codigo_produto"),
        new("descricao", "Descrição", true, "Óleo diesel S10", "descricao", "produto", "nome"),
        new("unidade", "Unidade", false, "L", "unidade", "un", "und"),
        new("ncm", "NCM", false, "27101921", "ncm"),
        new("cest", "CEST", false, "", "cest"),
        new("gtin", "GTIN / código de barras", false, "", "gtin", "ean", "codigo_barras"),
        new("origem", "Origem (0 a 8)", false, "0", "origem"),
        new("tipo", "Tipo do item (00 a 99)", false, "07", "tipo", "tipo_item"),
        new("cfope", "CFOP de entrada", false, "1653", "cfop_entrada"),
        new("cfops", "CFOP de saída", false, "", "cfop_saida"),
        new("cste", "CST ICMS entrada", false, "", "cst_entrada", "cst_icms_entrada"),
        new("csts", "CST ICMS saída", false, "", "cst_saida", "cst_icms_saida"),
        new("icms", "Alíquota ICMS (%)", false, "", "aliq_icms", "icms"),
        new("ipi", "Alíquota IPI (%)", false, "", "aliq_ipi", "ipi"),
        new("pis", "Alíquota PIS (%)", false, "", "aliq_pis", "pis"),
        new("cofins", "Alíquota COFINS (%)", false, "", "aliq_cofins", "cofins"),
    ], async (c, l) =>
    {
        var svc = c.App.Fiscal().Produtos;
        var codigo = l.Inteiro("codigo") is { } cod and > 0 ? cod
            : await c.ProximoCodigoAsync("produtos", async () => (await svc.ListarAsync(c.Empresa, false, null, c.Usuario)) is { Sucesso: true } r
                                                                ? r.Valor.Select(p => p.Codigo).DefaultIfEmpty(0).Max() : 0);
        var tipo = l.Texto("tipo") is { } t ? (t.Length == 1 ? "0" + t : t) : "00";
        var p = new Produto(0, codigo, l.Texto("descricao") ?? "", l.Texto("unidade")?.ToUpperInvariant() ?? "UN", l.Texto("ncm"), l.Texto("cest"), l.Texto("gtin"),
                            l.Texto("origem") ?? "0", tipo, l.Texto("cfope"), l.Texto("cfops"), l.Texto("cste"), l.Texto("csts"),
                            l.Valor("icms") ?? 0, l.Valor("ipi") ?? 0, l.Valor("pis") ?? 0, l.Valor("cofins") ?? 0);
        return await svc.GravarAsync(c.Empresa, p, c.Usuario);
    });

    private static readonly DefinicaoImportacao Naturezas = new("naturezas", "Naturezas de entrada",
    [
        new("codigo", "Código", false, "1", "cod"),
        new("descricao", "Descrição", true, "Compra para comercialização", "descricao", "natureza", "nome"),
        new("saida", "CFOP de saída (XML)", true, "5102", "cfop_saida", "cfop_xml", "cfop_fornecedor"),
        new("entrada", "CFOP de entrada", true, "1102", "cfop_entrada"),
    ], async (c, l) =>
    {
        var svc = c.App.Fiscal().Naturezas;
        var codigo = l.Inteiro("codigo") is { } cod and > 0 ? cod
            : await c.ProximoCodigoAsync("naturezas", async () => (await svc.ListarAsync(c.Empresa, c.Usuario)).Select(n => n.Codigo).DefaultIfEmpty(0).Max());
        return await svc.GravarAsync(c.Empresa, new NaturezaEntrada(0, codigo, l.Texto("descricao") ?? "", l.Texto("saida") ?? "", l.Texto("entrada") ?? ""), c.Usuario);
    });

    public static IReadOnlyList<DefinicaoImportacao> Todas { get; } =
        [Produtos with { TelaCadastro = ViewModels.Telas.Produtos }, Naturezas with { TelaCadastro = ViewModels.Telas.Naturezas }];
}
