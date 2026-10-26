namespace Vorticis.Fiscal.Documentos;

/// <summary>
/// CFOPs de entrada de bens para o ativo imobilizado (Ajuste SINIEF 07/01, tabela de CFOP): 1.551/2.551/3.551 (compra para o
/// ativo imobilizado) e 1.406/2.406 (compra para o ativo com ICMS por substituição tributária). A nota de entrada com esses CFOPs
/// oferece o cadastro dos bens no imobilizado do Contábil.
/// </summary>
public static class CfopImobilizado
{
    public static IReadOnlySet<string> Codigos { get; } = new HashSet<string> { "1551", "2551", "3551", "1406", "2406" };

    public static bool Eh(string? cfop) => cfop is not null && Codigos.Contains(cfop.Replace(".", "").Trim());

    /// <summary>Itens da nota que são bens do ativo imobilizado.</summary>
    public static IReadOnlyList<ItemFiscal> Itens(TipoOperacao operacao, IEnumerable<ItemFiscal> itens) =>
        operacao == TipoOperacao.Entrada ? itens.Where(i => Eh(i.Cfop) || Eh(i.CfopOriginal)).ToList() : [];
}
