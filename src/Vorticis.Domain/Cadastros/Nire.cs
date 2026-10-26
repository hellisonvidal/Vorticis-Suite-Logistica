namespace Vorticis.Domain.Cadastros;

/// <summary>
/// NIRE (Número de Identificação do Registro de Empresas, Junta Comercial): 11 dígitos, exigido na ECD (0000.NIRE) e na ECF.
/// Os dois primeiros identificam a UF da Junta. O dígito verificador varia por Junta e não é conferido aqui.
/// Entidades sem registro em Junta (associações, por exemplo) ficam sem NIRE.
/// </summary>
public static class Nire
{
    public const int Tamanho = 11;

    /// <summary>Só os dígitos; vazio vira null.</summary>
    public static string? Normalizar(string? nire)
    {
        var d = new string((nire ?? "").Where(char.IsAsciiDigit).ToArray());
        return d.Length == 0 ? null : d;
    }

    public static bool Valido(string? nire) => Normalizar(nire) is null or { Length: Tamanho };
}
