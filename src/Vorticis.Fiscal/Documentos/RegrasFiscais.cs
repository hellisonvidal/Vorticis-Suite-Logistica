namespace Vorticis.Fiscal.Documentos;

/// <summary>
/// Regra fiscal (de/para): o CFOP que vem no XML (completo, ex.: "5102", ou só o início, ex.: "51") vira o CFOP da escrituração.
/// Entrada: CFOP de saída do emitente → CFOP de entrada (ex.: 5102 → 1102). Saída: CFOP do XML da empresa → CFOP de saída da escrituração.
/// Os CSTs são opcionais: preenchidos, substituem os do XML.
/// </summary>
public sealed record RegraFiscal(long Id, int Codigo, string Descricao, TipoOperacao Operacao, string CfopOrigem, string CfopDestino,
                                 string? CstIcms = null, string? CstPis = null, string? CstCofins = null, bool Ativa = true)
{
    public override string ToString() => $"{Codigo} – {Descricao}";
}

public static class RegrasFiscais
{
    private static RegraFiscal E(int c, string d, string de, string para) => new(0, c, d, TipoOperacao.Entrada, de, para);

    /// <summary>Regras oferecidas no botão "Incluir regras sugeridas" (o usuário confere e ajusta à operação da empresa).</summary>
    public static readonly IReadOnlyList<RegraFiscal> Sugeridas =
    [
        E(1, "Compra para industrialização (no estado)", "5101", "1101"),
        E(2, "Compra para comercialização (no estado)", "5102", "1102"),
        E(3, "Compra para industrialização (fora do estado)", "6101", "2101"),
        E(4, "Compra para comercialização (fora do estado)", "6102", "2102"),
        E(5, "Compra com ST para comercialização (no estado)", "5405", "1403"),
        E(6, "Compra com ST para comercialização (fora do estado)", "6404", "2403"),
        E(7, "Serviço de transporte (no estado)", "5353", "1353"),
        E(8, "Serviço de transporte (fora do estado)", "6353", "2353"),
        E(9, "Bonificação ou brinde (no estado)", "5910", "1910"),
        E(10, "Bonificação ou brinde (fora do estado)", "6910", "2910"),
        E(11, "Outras entradas (no estado)", "5949", "1949"),
        E(12, "Outras entradas (fora do estado)", "6949", "2949"),
    ];

    public static string Digitos(string? s) => new([.. (s ?? "").Where(char.IsAsciiDigit)]);

    /// <summary>Limpa os campos digitados: só dígitos nos CFOPs e CSTs; CST vazio fica nulo (mantém o do XML).</summary>
    public static RegraFiscal Normalizar(RegraFiscal r)
    {
        static string? Cst(string? s) => Digitos(s) is { Length: > 0 } d ? d : null;
        return r with { Descricao = r.Descricao?.Trim() ?? "", CfopOrigem = Digitos(r.CfopOrigem), CfopDestino = Digitos(r.CfopDestino),
                        CstIcms = Cst(r.CstIcms), CstPis = Cst(r.CstPis), CstCofins = Cst(r.CstCofins) };
    }

    public static IReadOnlyList<string> Validar(RegraFiscal r)
    {
        var erros = new List<string>();
        if (r.Codigo <= 0) erros.Add("Informe o código da regra.");
        if (string.IsNullOrWhiteSpace(r.Descricao)) erros.Add("Informe a descrição da regra.");
        var de = Digitos(r.CfopOrigem);
        if (de.Length is < 1 or > 4 || de[0] is not ('5' or '6' or '7')) erros.Add("CFOP do XML: de 1 a 4 dígitos, começando por 5, 6 ou 7 (ex.: 5102, ou 51 para todos os 51xx).");
        var para = Digitos(r.CfopDestino);
        if (r.Operacao == TipoOperacao.Entrada && (para.Length != 4 || para[0] is not ('1' or '2' or '3')))
            erros.Add("CFOP da escrituração (entrada): 4 dígitos começando por 1, 2 ou 3 (ex.: 1102).");
        if (r.Operacao == TipoOperacao.Saida && (para.Length != 4 || para[0] is not ('5' or '6' or '7')))
            erros.Add("CFOP da escrituração (saída): 4 dígitos começando por 5, 6 ou 7 (ex.: 5102).");
        if (r.CstIcms is { Length: > 0 } ci && Digitos(ci).Length is not (2 or 3)) erros.Add("CST do ICMS: 2 dígitos (ex.: 00) ou 3 com a origem (ex.: 000), ou CSOSN (ex.: 102).");
        if (r.CstPis is { Length: > 0 } cp && Digitos(cp).Length != 2) erros.Add("CST do PIS: 2 dígitos (ex.: 50).");
        if (r.CstCofins is { Length: > 0 } cc && Digitos(cc).Length != 2) erros.Add("CST da COFINS: 2 dígitos (ex.: 50).");
        return erros;
    }

    /// <summary>Regra ativa da operação que vale para o CFOP do XML: a de CFOP mais longo que é o início dele (5102 vence 51, que vence 5).</summary>
    public static RegraFiscal? Encontrar(IEnumerable<RegraFiscal> regras, TipoOperacao operacao, string? cfopXml) =>
        regras.Where(r => r.Ativa && r.Operacao == operacao && Cfop.Casa(r.CfopOrigem, cfopXml)).OrderByDescending(r => r.CfopOrigem.Length).FirstOrDefault();

    /// <summary>
    /// Itens com o CFOP (e os CSTs preenchidos) das regras da operação; o CFOP do XML fica guardado como original, e as regras partem
    /// sempre dele (reaplicar não encadeia). Sem regra, o item não muda.
    /// </summary>
    public static IReadOnlyList<ItemFiscal> Aplicar(IEnumerable<ItemFiscal> itens, TipoOperacao operacao, IReadOnlyList<RegraFiscal> regras) =>
        [.. itens.Select(i =>
        {
            var original = i.CfopOriginal ?? i.Cfop;
            if (Encontrar(regras, operacao, original) is not { } r) return i;
            return i with
            {
                Cfop = r.CfopDestino, CfopOriginal = original,
                CstIcms = r.CstIcms ?? i.CstIcms, CstPis = r.CstPis ?? i.CstPis, CstCofins = r.CstCofins ?? i.CstCofins,
            };
        })];
}
