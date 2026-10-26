using System.Globalization;
using System.Text.RegularExpressions;
using Vorticis.Domain.Comum;

namespace Vorticis.Domain.Contabil;

/// <summary>Uma variável de preenchimento automático do histórico padrão (ex.: {DOCUMENTO}).</summary>
public sealed record VariavelHistorico(string Nome, string Descricao)
{
    /// <summary>Como ela é escrita no texto do histórico: {NOME}.</summary>
    public string Marcador => "{" + Nome + "}";
}

/// <summary>Dados da linha/lançamento usados para preencher as variáveis do histórico (nulo/vazio = ainda não informado).</summary>
public sealed record ContextoHistorico(string? Documento = null, string? Participante = null, DateOnly? Data = null,
                                       string? Conta = null, decimal? Valor = null, string? Complemento = null);

/// <summary>Resultado da substituição: o texto e as variáveis que não puderam ser resolvidas (continuam no texto, entre chaves).</summary>
public sealed record HistoricoPreenchido(string Texto, IReadOnlyList<string> Pendentes)
{
    public bool Completo => Pendentes.Count == 0;
}

/// <summary>
/// Variáveis do histórico padrão: no cadastro o texto pode trazer marcadores como {DOCUMENTO} ou {COMPETENCIA}, que o
/// lançamento troca pelos dados da linha. Marcadores sem dado correspondente ficam no texto para o usuário completar.
/// </summary>
public static class VariaveisHistorico
{
    public static readonly IReadOnlyList<VariavelHistorico> Todas =
    [
        new("DOCUMENTO", "Nº do documento da linha (NF, recibo, boleto...)"),
        new("PARTICIPANTE", "Nome do cliente/fornecedor da linha"),
        new("DATA", "Data do lançamento (dd/mm/aaaa)"),
        new("MES", "Mês da data do lançamento (2 dígitos)"),
        new("ANO", "Ano da data do lançamento"),
        new("MES_ANTERIOR", "Mês anterior ao da data, no formato mm/aaaa"),
        new("COMPETENCIA", "Mês e ano da data, no formato mm/aaaa"),
        new("CONTA", "Nome da conta da linha (a de débito; se só houver crédito, a de crédito)"),
        new("VALOR", "Valor da linha (1.500,00)"),
        new("COMPLEMENTO", "Texto livre, digitado pelo usuário no lançamento"),
    ];

    private static readonly Regex Marcador = new(@"\{([A-Za-z_]+)\}", RegexOptions.Compiled);

    private static string Normalizar(string nome) => nome.ToUpperInvariant();

    /// <summary>Nomes das variáveis usadas no texto, sem repetição, na ordem em que aparecem (sem as chaves, em maiúsculas).</summary>
    public static IReadOnlyList<string> Usadas(string? texto) =>
        string.IsNullOrEmpty(texto) ? [] : Marcador.Matches(texto).Select(m => Normalizar(m.Groups[1].Value)).Distinct().ToList();

    /// <summary>Marcadores {X} do texto que não são variáveis conhecidas (erro de digitação no cadastro).</summary>
    public static IReadOnlyList<string> Desconhecidas(string? texto) =>
        Usadas(texto).Where(n => Todas.All(v => v.Nome != n)).ToList();

    /// <summary>Posição do primeiro marcador conhecido ainda no texto (para o cursor selecioná-lo e o usuário digitar por cima).</summary>
    public static (int Inicio, int Tamanho)? PrimeiroPendente(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return null;
        foreach (Match m in Marcador.Matches(texto))
            if (Todas.Any(v => v.Nome == Normalizar(m.Groups[1].Value))) return (m.Index, m.Length);
        return null;
    }

    public static bool TemVariaveis(string? texto) => Usadas(texto).Any(n => Todas.Any(v => v.Nome == n));

    /// <summary>Substitui as variáveis que o contexto resolve; as demais permanecem no texto e são listadas em Pendentes.</summary>
    public static HistoricoPreenchido Aplicar(string? modelo, ContextoHistorico contexto)
    {
        var pendentes = new List<string>();
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        var texto = Marcador.Replace(modelo ?? "", m =>
        {
            var nome = Normalizar(m.Groups[1].Value);
            var valor = Resolver(nome, contexto, pt);
            if (valor is null)
            {
                if (Todas.Any(v => v.Nome == nome) && !pendentes.Contains(nome)) pendentes.Add(nome);
                return m.Value;
            }
            return valor;
        });
        return new HistoricoPreenchido(texto, pendentes);
    }

    private static string? Resolver(string nome, ContextoHistorico c, CultureInfo pt)
    {
        static string? Texto(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        return nome switch
        {
            "DOCUMENTO" => Texto(c.Documento),
            "PARTICIPANTE" => Texto(c.Participante),
            "CONTA" => Texto(c.Conta),
            "COMPLEMENTO" => Texto(c.Complemento),
            "VALOR" => c.Valor is > 0 ? Formato.Moeda(c.Valor.Value) : null,
            "DATA" => c.Data?.ToString("dd/MM/yyyy", pt),
            "MES" => c.Data?.ToString("MM", pt),
            "ANO" => c.Data?.ToString("yyyy", pt),
            "COMPETENCIA" => c.Data?.ToString("MM/yyyy", pt),
            "MES_ANTERIOR" => c.Data?.AddMonths(-1).ToString("MM/yyyy", pt),
            _ => null,
        };
    }
}
