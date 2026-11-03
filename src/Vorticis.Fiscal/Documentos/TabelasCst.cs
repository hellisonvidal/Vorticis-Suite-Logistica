namespace Vorticis.Fiscal.Documentos;

/// <summary>Código de situação tributária com a legenda (o que o código quer dizer), para escolher na lista.</summary>
public sealed record OpcaoCst(string Codigo, string Descricao)
{
    public string Rotulo => Codigo.Length == 0 ? Descricao : $"{Codigo} – {Descricao}";
    public override string ToString() => Rotulo;
}

/// <summary>
/// Tabelas de CST (descrições dos códigos dos leiautes da NF-e e da EFD; não são alíquotas): CST do ICMS (tabela B), CSOSN do Simples Nacional
/// e CST do PIS e da COFINS. Usadas no cadastro de regras fiscais.
/// </summary>
public static class TabelasCst
{
    public static readonly OpcaoCst EmBranco = new("", "(em branco – mantém o do XML)");

    public static IReadOnlyList<OpcaoCst> Icms { get; } =
    [
        EmBranco,
        new("00", "Tributada integralmente"),
        new("10", "Tributada e com cobrança do ICMS por substituição tributária"),
        new("20", "Com redução de base de cálculo"),
        new("30", "Isenta ou não tributada e com cobrança do ICMS por substituição tributária"),
        new("40", "Isenta"),
        new("41", "Não tributada"),
        new("50", "Suspensão"),
        new("51", "Diferimento"),
        new("60", "ICMS cobrado anteriormente por substituição tributária"),
        new("70", "Com redução de base de cálculo e cobrança do ICMS por substituição tributária"),
        new("90", "Outras"),
        new("101", "Simples Nacional: tributada com permissão de crédito"),
        new("102", "Simples Nacional: tributada sem permissão de crédito"),
        new("103", "Simples Nacional: isenção do ICMS para faixa de receita bruta"),
        new("201", "Simples Nacional: com permissão de crédito e cobrança do ICMS por ST"),
        new("202", "Simples Nacional: sem permissão de crédito e com cobrança do ICMS por ST"),
        new("203", "Simples Nacional: isenção para faixa de receita bruta e cobrança do ICMS por ST"),
        new("300", "Simples Nacional: imune"),
        new("400", "Simples Nacional: não tributada"),
        new("500", "Simples Nacional: ICMS cobrado anteriormente por ST ou por antecipação"),
        new("900", "Simples Nacional: outros"),
    ];

    /// <summary>CST do PIS e da COFINS (a mesma tabela): 01 a 49 nas saídas, 50 a 99 nas entradas.</summary>
    public static IReadOnlyList<OpcaoCst> PisCofins { get; } =
    [
        EmBranco,
        new("01", "Saída tributável com alíquota básica"),
        new("02", "Saída tributável com alíquota diferenciada"),
        new("03", "Saída tributável com alíquota por unidade de medida"),
        new("04", "Saída tributável monofásica – revenda a alíquota zero"),
        new("05", "Saída tributável por substituição tributária"),
        new("06", "Saída tributável a alíquota zero"),
        new("07", "Saída isenta da contribuição"),
        new("08", "Saída sem incidência da contribuição"),
        new("09", "Saída com suspensão da contribuição"),
        new("49", "Outras operações de saída"),
        new("50", "Entrada com direito a crédito – receita tributada no mercado interno"),
        new("51", "Entrada com direito a crédito – receita não tributada no mercado interno"),
        new("52", "Entrada com direito a crédito – receita de exportação"),
        new("53", "Entrada com direito a crédito – receitas tributadas e não tributadas no mercado interno"),
        new("54", "Entrada com direito a crédito – receitas tributadas no mercado interno e de exportação"),
        new("55", "Entrada com direito a crédito – receitas não tributadas no mercado interno e de exportação"),
        new("56", "Entrada com direito a crédito – receitas tributadas e não tributadas no mercado interno e de exportação"),
        new("60", "Crédito presumido – receita tributada no mercado interno"),
        new("61", "Crédito presumido – receita não tributada no mercado interno"),
        new("62", "Crédito presumido – receita de exportação"),
        new("63", "Crédito presumido – receitas tributadas e não tributadas no mercado interno"),
        new("64", "Crédito presumido – receitas tributadas no mercado interno e de exportação"),
        new("65", "Crédito presumido – receitas não tributadas no mercado interno e de exportação"),
        new("66", "Crédito presumido – receitas tributadas e não tributadas no mercado interno e de exportação"),
        new("67", "Crédito presumido – outras operações"),
        new("70", "Aquisição sem direito a crédito"),
        new("71", "Aquisição com isenção"),
        new("72", "Aquisição com suspensão"),
        new("73", "Aquisição a alíquota zero"),
        new("74", "Aquisição sem incidência da contribuição"),
        new("75", "Aquisição por substituição tributária"),
        new("98", "Outras operações de entrada"),
        new("99", "Outras operações"),
    ];

    /// <summary>A opção do código gravado (código fora da tabela, como o CST com a origem "000", entra como "informado").</summary>
    public static OpcaoCst Opcao(IReadOnlyList<OpcaoCst> tabela, string? codigo) =>
        string.IsNullOrWhiteSpace(codigo) ? EmBranco : tabela.FirstOrDefault(o => o.Codigo == codigo.Trim()) ?? new OpcaoCst(codigo.Trim(), "código informado");
}
