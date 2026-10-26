using System.Text.RegularExpressions;

namespace Vorticis.Fiscal.Documentos;

/// <summary>Regras sobre o CFOP (Código Fiscal de Operações e Prestações): sentido, receita, devoluções e crédito.</summary>
public static partial class Cfop
{
    /// <summary>NFS-e não tem CFOP: o item recebe este código convencional (regras de contabilização e livros usam o mesmo).</summary>
    public const string Servico = "NFSE";

    /// <summary>Primeiro dígito: 1, 2 e 3 são entradas; 5, 6 e 7, saídas. Null para o código convencional e valores inválidos.</summary>
    public static TipoOperacao? Operacao(string? cfop) => cfop is { Length: 4 } c && c.All(char.IsAsciiDigit)
        ? c[0] switch { '1' or '2' or '3' => TipoOperacao.Entrada, '5' or '6' or '7' => TipoOperacao.Saida, _ => null }
        : null;

    /// <summary>Âmbito: 1/5 estadual, 2/6 interestadual, 3/7 exterior.</summary>
    public static string Ambito(string? cfop) => cfop is { Length: 4 } c
        ? c[0] switch { '1' or '5' => "Dentro do estado", '2' or '6' => "Fora do estado", '3' or '7' => "Exterior", _ => "" }
        : "";

    // Vendas (x.101 a x.129), vendas com ST (x.401 a x.405), comunicação/energia (x.301 a x.307), transporte (x.351 a x.359),
    // serviço tributado pelo ISS (x.933) e exportação (7.101 a 7.129, 7.358).
    [GeneratedRegex(@"^[567]1(0[1-9]|1\d|2\d)$|^[56]40[1-5]$|^[567]30[1-7]$|^[567]35[1-9]$|^[56]933$")]
    private static partial Regex RegexReceita();

    /// <summary>Operação de saída que compõe a receita bruta (vendas, serviços, transporte). Transferências, remessas e devoluções de compra ficam de fora.</summary>
    public static bool GeraReceita(string? cfop) => cfop == Servico || (cfop is { Length: 4 } && RegexReceita().IsMatch(cfop));

    // Devolução de vendas recebida (x.201 a x.213, x.410 a x.414): reduz a receita.
    [GeneratedRegex(@"^[123]2(0[1-9]|1[0-3])$|^[123]41[0-4]$")]
    private static partial Regex RegexDevolucaoVenda();

    public static bool EhDevolucaoDeVenda(string? cfop) => cfop is { Length: 4 } && RegexDevolucaoVenda().IsMatch(cfop);

    // Entradas de bens e serviços para revenda/industrialização/insumos (x.101 a x.126, x.401 a x.406), comunicação, energia e transporte.
    [GeneratedRegex(@"^[123]1(0[1-9]|1\d|2[0-6])$|^[123]40[1-6]$|^[123]35[1-9]$|^[123]30[1-7]$|^[12]933$")]
    private static partial Regex RegexCredito();

    /// <summary>Entrada que, em regra, gera crédito de PIS/COFINS no regime não cumulativo (o usuário pode ajustar o valor).</summary>
    public static bool GeraCreditoPisCofins(string? cfop) => cfop is { Length: 4 } && RegexCredito().IsMatch(cfop);

    /// <summary>O CFOP da regra casa com o do item: igual, ou prefixo de 1 a 3 dígitos (ex.: "5" = todas as saídas estaduais, "51" = vendas).</summary>
    public static bool Casa(string? padrao, string? cfop) =>
        !string.IsNullOrEmpty(padrao) && !string.IsNullOrEmpty(cfop) && cfop.StartsWith(padrao, StringComparison.OrdinalIgnoreCase);
}
