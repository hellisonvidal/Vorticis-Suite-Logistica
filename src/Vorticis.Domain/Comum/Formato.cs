using System.Globalization;

namespace Vorticis.Domain.Comum;

/// <summary>Formatação de valores para o usuário sempre em pt-BR, independente da cultura do Windows.</summary>
public static class Formato
{
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Moeda(decimal valor) => valor.ToString("N2", PtBr);

    /// <summary>Número sem zeros à direita (até 2 casas), vírgula decimal: 7,5 · 9 · 22,5.</summary>
    public static string Num(decimal valor) => valor.ToString("0.##", PtBr);

    /// <summary>
    /// Valor digitado ou vindo de planilha: aceita "1.500,00", "1500,5", "R$ 1.500" e o formato com ponto decimal
    /// ("1500.5", "-80.25"), além de negativos entre parênteses "(80,00)".
    /// </summary>
    public static decimal? LerValor(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var t = texto.Trim().Replace("R$", "").Replace(" ", "").Replace(" ", "");
        var negativo = t.StartsWith('(') && t.EndsWith(')');
        if (negativo) t = t[1..^1];
        var usaPontoDecimal = !t.Contains(',') && t.Count(c => c == '.') == 1 && t.Length - t.IndexOf('.') - 1 != 3;
        var cultura = usaPontoDecimal ? CultureInfo.InvariantCulture : PtBr;
        if (!decimal.TryParse(t, NumberStyles.Number, cultura, out var v)) return null;
        return negativo ? -v : v;
    }

    private static readonly string[] FormatosData =
        ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "yyyy-MM-dd", "ddMMyyyy", "yyyyMMdd", "dd-MM-yyyy", "dd.MM.yyyy", "dd/MM/yyyy HH:mm"];

    /// <summary>Data em formatos comuns de planilhas e extratos (também o número serial do Excel).</summary>
    public static DateOnly? LerData(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var t = texto.Trim();
        if (DateTime.TryParseExact(t, FormatosData, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return DateOnly.FromDateTime(d);
        if (t.Length >= 8 && t[..8].All(char.IsAsciiDigit)
            && DateTime.TryParseExact(t[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return DateOnly.FromDateTime(d);
        if (int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var serial) && serial is > 20000 and < 80000)
            return DateOnly.FromDateTime(DateTime.FromOADate(serial));
        return null;
    }

    /// <summary>CEP de 8 dígitos como 00000-000 (outros valores ficam como estão).</summary>
    public static string Cep(string? cep) => cep is { Length: 8 } c && c.All(char.IsAsciiDigit) ? $"{c[..5]}-{c[5..]}" : cep ?? "";
}
