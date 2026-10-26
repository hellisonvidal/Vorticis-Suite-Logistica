namespace Vorticis.Domain.Cadastros;

public static class Cpf
{
    public static bool Valido(string? valor)
    {
        var c = Cnpj.SomenteDigitos(valor);
        if (c.Length != 11 || c.All(ch => ch == c[0])) return false;
        int Dv(int tamanho)
        {
            int soma = 0;
            for (int i = 0; i < tamanho; i++) soma += (c[i] - '0') * (tamanho + 1 - i);
            int resto = soma * 10 % 11;
            return resto == 10 ? 0 : resto;
        }
        return Dv(9) == c[9] - '0' && Dv(10) == c[10] - '0';
    }

    public static string Formatar(string c) => c.Length == 11 ? $"{c[..3]}.{c[3..6]}.{c[6..9]}-{c[9..]}" : c;
}

public static class Uf
{
    public static readonly IReadOnlySet<string> Todas = new HashSet<string>
    {
        "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA", "PB", "PR",
        "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO",
    };

    public static bool Valida(string? uf) => uf is not null && Todas.Contains(uf.Trim().ToUpperInvariant());

    // Código IBGE da UF = dois primeiros dígitos do código do município.
    private static readonly Dictionary<string, string> PorCodigoIbge = new()
    {
        ["11"] = "RO", ["12"] = "AC", ["13"] = "AM", ["14"] = "RR", ["15"] = "PA", ["16"] = "AP", ["17"] = "TO",
        ["21"] = "MA", ["22"] = "PI", ["23"] = "CE", ["24"] = "RN", ["25"] = "PB", ["26"] = "PE", ["27"] = "AL", ["28"] = "SE", ["29"] = "BA",
        ["31"] = "MG", ["32"] = "ES", ["33"] = "RJ", ["35"] = "SP",
        ["41"] = "PR", ["42"] = "SC", ["43"] = "RS",
        ["50"] = "MS", ["51"] = "MT", ["52"] = "GO", ["53"] = "DF",
    };

    /// <summary>UF a partir do código IBGE do município (7 dígitos) ou da própria UF (2 dígitos).</summary>
    public static string? DeCodigoIbge(string? codigo)
    {
        var d = new string((codigo ?? "").Where(char.IsAsciiDigit).ToArray());
        return d.Length >= 2 && PorCodigoIbge.TryGetValue(d[..2], out var uf) ? uf : null;
    }
}

/// <summary>Inscrição estadual: só dígitos (2 a 14) ou ISENTO. Os dígitos verificadores variam por UF e não são conferidos aqui.</summary>
public static class InscricaoEstadual
{
    public static string? Normalizar(string? ie)
    {
        if (string.IsNullOrWhiteSpace(ie)) return null;
        var t = ie.Trim().ToUpperInvariant();
        if (t is "ISENTO" or "ISENTA") return "ISENTO";
        return new string(t.Where(char.IsAsciiDigit).ToArray());
    }

    public static bool Valida(string? ie) => Normalizar(ie) is null or "ISENTO" or { Length: >= 2 and <= 14 };
}
