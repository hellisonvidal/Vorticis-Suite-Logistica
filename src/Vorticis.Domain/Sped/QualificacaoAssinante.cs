using System.Globalization;
using System.Text;

namespace Vorticis.Domain.Sped;

/// <summary>
/// Tabela de Qualificação do Assinante da ECD (J930.COD_ASSIN / IDENT_QUALIF), conforme o Manual de Orientação do
/// Leiaute 9. O responsável pela assinatura (IND_RESP_LEGAL = S) pode ter qualquer código, menos o 900.
/// </summary>
public static class QualificacaoAssinante
{
    public const string PessoaJuridica = "001";
    public const string Administrador = "205";
    public const string Procurador = "309";
    public const string Contador = "900";
    public const string Outros = "999";

    public static readonly IReadOnlyDictionary<string, string> Tabela = new Dictionary<string, string>
    {
        ["001"] = "Pessoa Jurídica (e-CNPJ ou e-PJ)",
        ["203"] = "Diretor",
        ["204"] = "Conselheiro de Administração",
        ["205"] = "Administrador",
        ["206"] = "Administrador do Grupo",
        ["207"] = "Administrador de Sociedade Filiada",
        ["220"] = "Administrador Judicial – Pessoa Física",
        ["222"] = "Administrador Judicial – Pessoa Jurídica - Profissional Responsável",
        ["223"] = "Administrador Judicial/Gestor",
        ["226"] = "Gestor Judicial",
        ["309"] = "Procurador",
        ["312"] = "Inventariante",
        ["313"] = "Liquidante",
        ["315"] = "Interventor",
        ["401"] = "Titular – Pessoa Física - EIRELI",
        ["801"] = "Empresário",
        ["900"] = "Contador",
        ["940"] = "Auditor Independente",
        ["999"] = "Outros",
    };

    public static string Descricao(string codigo) => Tabela.TryGetValue(codigo, out var d) ? d : "Outros";

    /// <summary>
    /// Código de assinante a partir da qualificação do sócio no QSA da RFB (código e/ou descrição). Sócio-administrador e
    /// administrador → 205; diretor/presidente → 203; empresário (individual) → 801; titular de EIRELI → 401; procurador → 309;
    /// liquidante → 313; inventariante → 312; interventor → 315; demais → 999 (Outros).
    /// </summary>
    public static string DeQualificacaoSocio(int? codigoQsa, string? descricao)
    {
        switch (codigoQsa)
        {
            case 5 or 49: return "205";        // Administrador; Sócio-Administrador
            case 10 or 16: return "203";       // Diretor; Presidente
            case 50: return "801";             // Empresário
            case 65: return "401";             // Titular Pessoa Física (EIRELI)
            case 17: return "309";             // Procurador
            case 9: return "313";              // Liquidante (código histórico do QSA)
            case 14: return "312";             // Inventariante
        }
        var d = Normalizar(descricao);
        if (d.Contains("ADMINISTRADOR")) return "205";
        if (d.Contains("DIRETOR") || d.Contains("PRESIDENTE")) return "203";
        if (d.Contains("EMPRESARIO")) return "801";
        if (d.Contains("TITULAR")) return "401";
        if (d.Contains("PROCURADOR")) return "309";
        if (d.Contains("LIQUIDANTE")) return "313";
        if (d.Contains("INVENTARIANTE")) return "312";
        if (d.Contains("INTERVENTOR")) return "315";
        if (d.Contains("CONSELHEIRO")) return "204";
        return Outros;
    }

    private static string Normalizar(string? s)
    {
        var d = (s ?? "").ToUpperInvariant().Normalize(NormalizationForm.FormD);
        return new string(d.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    }
}
