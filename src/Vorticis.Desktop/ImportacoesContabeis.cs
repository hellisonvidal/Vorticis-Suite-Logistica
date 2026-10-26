using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Domain.Comum;

namespace Vorticis.Desktop;

/// <summary>Importação de planilha dos cadastros contábeis simples: centros de custo e históricos padrão (o plano de contas tem importação própria).</summary>
public static class ImportacoesContabeis
{
    private static readonly DefinicaoImportacao CentrosCusto = new("centroscusto", "Centros de custo",
    [
        new("codigo", "Código", true, "1.01", "codigo", "cod", "centro"),
        new("descricao", "Descrição", true, "Frota própria", "descricao", "nome"),
        new("pai", "Código do centro superior", false, "1", "pai", "superior", "codigo_pai", "centro_pai"),
    ], async (c, l) => await c.App.PlanoContas.IncluirCentroAsync(c.Empresa, l.Texto("codigo") ?? "", l.Texto("descricao") ?? "", l.Texto("pai"), c.Usuario));

    private static readonly DefinicaoImportacao Historicos = new("historicos", "Históricos padrão",
    [
        new("codigo", "Código", false, "10", "codigo", "cod"),
        new("descricao", "Descrição", true, "Pagamento de pedágio", "descricao", "historico", "texto"),
    ], async (c, l) => await c.App.Historicos.IncluirAsync(c.Empresa, l.Texto("codigo"), l.Texto("descricao") ?? "", c.Usuario));

    public static IReadOnlyList<DefinicaoImportacao> Todas { get; } = [CentrosCusto, Historicos];
}
