namespace Vorticis.Domain.Cadastros;

public static class Cnpj
{
    private static readonly int[] Pesos1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] Pesos2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    public static string SomenteDigitos(string? valor) => new((valor ?? "").Where(char.IsAsciiDigit).ToArray());

    /// <summary>00.000.000/0000-00 (também para o CNPJ alfanumérico); outros tamanhos ficam como estão.</summary>
    public static string Formatar(string c) => c.Length == 14 ? $"{c[..2]}.{c[2..5]}.{c[5..8]}/{c[8..12]}-{c[12..]}" : c;

    /// <summary>
    /// Valida CNPJ numérico (módulo 11). O CNPJ alfanumérico (IN RFB 2.229/2024, a partir de jul/2026)
    /// usa o mesmo algoritmo sobre (código ASCII − 48); suportado aqui para letras maiúsculas.
    /// </summary>
    public static bool Valido(string? valor)
    {
        var c = new string((valor ?? "").Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if (c.Length != 14 || !char.IsAsciiDigit(c[12]) || !char.IsAsciiDigit(c[13])) return false;
        if (c.All(ch => ch == c[0])) return false;

        int Dv(int[] pesos)
        {
            int soma = 0;
            for (int i = 0; i < pesos.Length; i++) soma += (c[i] - '0') * pesos[i];
            int resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }

        return Dv(Pesos1) == c[12] - '0' && Dv(Pesos2) == c[13] - '0';
    }
}
