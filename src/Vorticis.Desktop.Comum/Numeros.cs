using Vorticis.Domain.Comum;

namespace Vorticis.Desktop.Comum;

public static class Numeros
{
    /// <summary>
    /// Lê valores digitados: "1.234,56" e "1234,56" (pt-BR) ou "1234.56" (ponto decimal, comum ao colar de planilhas/OFX).
    /// Um único ponto que não seja seguido de exatamente 3 dígitos é tratado como separador decimal.
    /// </summary>
    public static decimal? LerValor(string? texto) => Formato.LerValor(texto);

    public static string Moeda(decimal v) => Formato.Moeda(v);

    public static string MoedaOuVazio(decimal v) => v == 0 ? "" : Formato.Moeda(v);
}
