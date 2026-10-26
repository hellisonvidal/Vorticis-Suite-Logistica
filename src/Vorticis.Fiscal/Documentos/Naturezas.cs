namespace Vorticis.Fiscal.Documentos;

/// <summary>
/// Natureza das notas de entrada: diretriz de troca do CFOP de saída do emitente (completo, ex.: "5102", ou só o início, ex.: "51")
/// pelo CFOP de entrada da escrituração (ex.: "1102").
/// </summary>
public sealed record NaturezaEntrada(long Id, int Codigo, string Descricao, string CfopSaida, string CfopEntrada, bool Ativa = true)
{
    public override string ToString() => $"{Codigo} – {Descricao}";
}

public static class RegrasNaturezas
{
    /// <summary>Naturezas oferecidas no botão "Incluir naturezas sugeridas" (o usuário confere e ajusta à operação da empresa).</summary>
    public static readonly IReadOnlyList<NaturezaEntrada> Sugeridas =
    [
        new(0, 1, "Compra para industrialização (no estado)", "5101", "1101"),
        new(0, 2, "Compra para comercialização (no estado)", "5102", "1102"),
        new(0, 3, "Compra para industrialização (fora do estado)", "6101", "2101"),
        new(0, 4, "Compra para comercialização (fora do estado)", "6102", "2102"),
        new(0, 5, "Compra com ST para comercialização (no estado)", "5405", "1403"),
        new(0, 6, "Compra com ST para comercialização (fora do estado)", "6404", "2403"),
        new(0, 7, "Serviço de transporte (no estado)", "5353", "1353"),
        new(0, 8, "Serviço de transporte (fora do estado)", "6353", "2353"),
        new(0, 9, "Bonificação ou brinde (no estado)", "5910", "1910"),
        new(0, 10, "Bonificação ou brinde (fora do estado)", "6910", "2910"),
        new(0, 11, "Outras entradas (no estado)", "5949", "1949"),
        new(0, 12, "Outras entradas (fora do estado)", "6949", "2949"),
    ];

    private static string Dig(string? s) => new([.. (s ?? "").Where(char.IsAsciiDigit)]);

    public static IReadOnlyList<string> Validar(NaturezaEntrada n)
    {
        var erros = new List<string>();
        if (n.Codigo <= 0) erros.Add("Informe o código da natureza.");
        if (string.IsNullOrWhiteSpace(n.Descricao)) erros.Add("Informe a descrição da natureza.");
        var s = Dig(n.CfopSaida);
        if (s.Length is < 1 or > 4 || s[0] is not ('5' or '6' or '7')) erros.Add("CFOP de saída: de 1 a 4 dígitos, começando por 5, 6 ou 7 (ex.: 5102, ou 51 para todos os 51xx).");
        var e = Dig(n.CfopEntrada);
        if (e.Length != 4 || e[0] is not ('1' or '2' or '3')) erros.Add("CFOP de entrada: 4 dígitos começando por 1, 2 ou 3 (ex.: 1102).");
        return erros;
    }

    /// <summary>Natureza ativa que vale para o CFOP de saída: a de CFOP mais longo que é o início dele (5102 vence 51, que vence 5).</summary>
    public static NaturezaEntrada? Encontrar(IEnumerable<NaturezaEntrada> naturezas, string? cfopSaida) =>
        naturezas.Where(n => n.Ativa && Cfop.Casa(n.CfopSaida, cfopSaida)).OrderByDescending(n => n.CfopSaida.Length).FirstOrDefault();

    /// <summary>Itens com o CFOP de entrada das naturezas; o CFOP do XML fica guardado como original. Sem natureza, o item não muda.</summary>
    public static IReadOnlyList<ItemFiscal> Aplicar(IEnumerable<ItemFiscal> itens, IReadOnlyList<NaturezaEntrada> naturezas) =>
        [.. itens.Select(i =>
        {
            var original = i.CfopOriginal ?? i.Cfop;
            return Encontrar(naturezas, original) is { } n ? i with { Cfop = n.CfopEntrada, CfopOriginal = original } : i;
        })];
}
