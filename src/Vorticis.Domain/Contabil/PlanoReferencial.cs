using System.Globalization;
using System.Text;
using Vorticis.Domain.Comum;

namespace Vorticis.Domain.Contabil;

/// <summary>Conta do plano de contas referencial da RFB (tabelas dinâmicas da ECD/ECF).</summary>
public sealed record ContaReferencial(
    string Codigo,
    string Descricao,
    bool Analitica,
    short Nivel,
    string? CodigoPai,
    string CodNat,
    DateOnly? Inicio = null,
    DateOnly? Fim = null);

/// <summary>Tipos de plano referencial (ECD/ECF). Conferir a tabela vigente no manual da ECD antes de usar códigos além dos usuais.</summary>
public sealed record TipoPlanoReferencial(string Codigo, string Descricao)
{
    public static readonly IReadOnlyList<TipoPlanoReferencial> Todos =
    [
        new("1", "PJ em geral – Lucro Real"),
        new("2", "PJ em geral – Lucro Presumido"),
        new("3", "Financeiras – Lucro Real"),
        new("4", "Seguradoras – Lucro Real"),
        new("5", "Imunes e isentas em geral"),
        new("6", "Imunes e isentas – financeiras"),
        new("7", "Imunes e isentas – seguradoras"),
        new("8", "Entidades fechadas de previdência complementar"),
        new("9", "Partidos políticos"),
        new("10", "Financeiras – Lucro Presumido"),
    ];
}

/// <summary>
/// Lê o plano referencial a partir de arquivo texto com cabeçalho (exportação das tabelas da RFB/PVA, planilha salva
/// como CSV etc.). Separador detectado automaticamente (| ; tab ,). Colunas reconhecidas pelo nome:
/// CODIGO, DESCRICAO, TIPO (S/A), NIVEL, COD_SUP, COD_NAT, DT_INI, DT_FIM (variações comuns aceitas).
/// Só CODIGO e DESCRICAO são obrigatórias; o restante é deduzido da hierarquia quando ausente.
/// Arquivos da RFB costumam vir em ISO-8859-1: use <see cref="Decodificar"/>.
/// </summary>
public static class LeitorPlanoReferencial
{
    private static readonly Dictionary<string, string[]> Sinonimos = new()
    {
        ["codigo"] = ["CODIGO", "COD", "COD_CTA_REF", "CODIGO_CONTA", "CONTA", "COD_CTA"],
        ["descricao"] = ["DESCRICAO", "DESC", "NOME", "DESCRICAO_CONTA"],
        ["tipo"] = ["TIPO", "IND_CTA", "TIPO_CONTA", "SINTETICA_ANALITICA"],
        ["nivel"] = ["NIVEL", "NIV"],
        ["pai"] = ["COD_SUP", "CODIGO_SUPERIOR", "COD_CTA_SUP", "CONTA_SUPERIOR", "SUPERIOR", "COD_SUPERIOR"],
        ["natureza"] = ["COD_NAT", "NATUREZA", "NAT"],
        ["inicio"] = ["DT_INI", "DATA_INICIO", "INICIO", "DT_INICIO"],
        ["fim"] = ["DT_FIM", "DATA_FIM", "FIM"],
    };

    /// <summary>UTF-8 se for válido; senão ISO-8859-1 (padrão dos arquivos da RFB).</summary>
    public static string Decodificar(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    public static Resultado<IReadOnlyList<ContaReferencial>> Ler(string texto)
    {
        var linhas = texto.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).ToList();
        if (linhas.Count < 2) return Resultado<IReadOnlyList<ContaReferencial>>.Falha("Arquivo vazio ou sem linhas de dados.");

        var separador = new[] { '|', ';', '\t', ',' }.MaxBy(s => linhas[0].Count(c => c == s));
        string[] Campos(string l) => l.Trim().Trim(separador).Split(separador).Select(c => c.Trim().Trim('"')).ToArray();

        var cabecalho = Campos(linhas[0]).Select(Normalizar).ToArray();
        int Col(string chave) => Array.FindIndex(cabecalho, h => Sinonimos[chave].Contains(h));
        var (iCod, iDesc, iTipo, iNivel, iPai, iNat, iIni, iFim) =
            (Col("codigo"), Col("descricao"), Col("tipo"), Col("nivel"), Col("pai"), Col("natureza"), Col("inicio"), Col("fim"));
        if (iCod < 0 || iDesc < 0)
            return Resultado<IReadOnlyList<ContaReferencial>>.Falha(
                "Cabeçalho não reconhecido: a primeira linha deve conter ao menos as colunas CODIGO e DESCRICAO " +
                $"(encontrado: {string.Join(", ", cabecalho)}).");

        var erros = new List<string>();
        var brutas = new List<(string Cod, string Desc, string? Tipo, short? Nivel, string? Pai, string? Nat, DateOnly? Ini, DateOnly? Fim)>();
        for (int i = 1; i < linhas.Count; i++)
        {
            var c = Campos(linhas[i]);
            string? Val(int idx) => idx >= 0 && idx < c.Length && c[idx].Length > 0 ? c[idx] : null;
            var cod = Val(iCod);
            var desc = Val(iDesc);
            if (cod is null || desc is null) { erros.Add($"Linha {i + 1}: código ou descrição vazio."); continue; }
            brutas.Add((cod, desc, Val(iTipo)?.ToUpperInvariant(), short.TryParse(Val(iNivel), out var n) ? n : null, Val(iPai),
                        Val(iNat)?.PadLeft(2, '0'), Data(Val(iIni)), Data(Val(iFim))));
            if (erros.Count > 20) break;
        }
        if (erros.Count > 0) return Resultado<IReadOnlyList<ContaReferencial>>.Falha(erros);

        var duplicados = brutas.GroupBy(b => b.Cod).Where(g => g.Count() > 1).Select(g => g.Key).Take(10).ToArray();
        if (duplicados.Length > 0) return Resultado<IReadOnlyList<ContaReferencial>>.Falha($"Códigos repetidos: {string.Join(", ", duplicados)}.");

        var codigos = brutas.Select(b => b.Cod).ToHashSet();
        // Pai: coluna do arquivo ou, na falta, o código sem o último segmento (1.01.01 → 1.01)
        var pais = brutas.ToDictionary(b => b.Cod, b => b.Pai ?? (b.Cod.LastIndexOf('.') is var p and > 0 ? b.Cod[..p] : null));
        var semPai = pais.Where(kv => kv.Value is not null && !codigos.Contains(kv.Value)).Select(kv => kv.Key).Take(10).ToArray();
        if (semPai.Length > 0) return Resultado<IReadOnlyList<ContaReferencial>>.Falha($"Contas cuja conta superior não está no arquivo: {string.Join(", ", semPai)}.");
        var temFilhos = pais.Values.OfType<string>().ToHashSet();

        var porCodigo = brutas.ToDictionary(b => b.Cod);
        short Nivel(string cod) { short n = 1; for (var p = pais[cod]; p is not null; p = pais[p]) if (++n > 50) throw new InvalidOperationException($"Ciclo na hierarquia em {cod}."); return n; }
        string Natureza(string cod)
        {
            for (var atual = cod; atual is not null; atual = pais[atual])
                if (porCodigo[atual].Nat is { } nat) return nat;
            // Sem coluna de natureza: dedução pela raiz e pela descrição do grupo.
            for (var atual = cod; atual is not null; atual = pais[atual])
                if (Normalizar(porCodigo[atual].Desc).Contains("PATRIMONIO_LIQUIDO")) return "03";
            var raiz = cod;
            while (pais[raiz] is { } p) raiz = p;
            return raiz.Split('.')[0] switch { "1" => "01", "2" => "02", _ => "04" };
        }

        var res = new List<ContaReferencial>(brutas.Count);
        try
        {
            foreach (var b in brutas)
            {
                var analitica = b.Tipo is "A" or "ANALITICA" || (b.Tipo is null && !temFilhos.Contains(b.Cod));
                if (analitica && temFilhos.Contains(b.Cod)) erros.Add($"Conta {b.Cod} marcada como analítica, mas tem subcontas.");
                res.Add(new ContaReferencial(b.Cod, b.Desc, analitica, b.Nivel ?? Nivel(b.Cod), pais[b.Cod], Natureza(b.Cod), b.Ini, b.Fim));
            }
        }
        catch (InvalidOperationException ex)
        {
            return Resultado<IReadOnlyList<ContaReferencial>>.Falha(ex.Message);
        }
        return erros.Count > 0 ? Resultado<IReadOnlyList<ContaReferencial>>.Falha(erros.Take(20)) : Resultado<IReadOnlyList<ContaReferencial>>.Ok(res);
    }

    private static DateOnly? Data(string? s) =>
        s is null ? null
        : DateOnly.TryParseExact(s, ["ddMMyyyy", "dd/MM/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d
        : null;

    /// <summary>Maiúsculas, sem acentos, espaços viram "_" — para comparar nomes de colunas e descrições.</summary>
    public static string Normalizar(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '_');
        }
        return sb.ToString();
    }
}

/// <summary>
/// Sugestão de vínculo conta societária → referencial: mesma natureza, só contas analíticas, por semelhança das
/// descrições (palavras em comum, sem acentos e sem palavras vazias). É apenas sugestão: o contador confirma.
/// </summary>
public static class SugestaoReferencial
{
    private static readonly HashSet<string> Vazias =
        ["DE", "DA", "DO", "DAS", "DOS", "E", "A", "O", "AS", "OS", "EM", "NA", "NO", "PARA", "POR", "COM", "OUTROS", "OUTRAS"];

    public static IReadOnlySet<string> Palavras(string descricao) =>
        LeitorPlanoReferencial.Normalizar(descricao).Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Length > 1 && !Vazias.Contains(p))
            .Select(p => p.Length > 4 && p.EndsWith('S') ? p[..^1] : p)   // plural simples
            .ToHashSet();

    /// <returns>Melhor referencial e a pontuação (0–1), ou null se nenhuma tiver palavra em comum.</returns>
    public static (ContaReferencial Conta, double Pontuacao)? Sugerir(string descricao, string codNat, IEnumerable<ContaReferencial> referenciais)
    {
        var alvo = Palavras(descricao);
        if (alvo.Count == 0) return null;
        (ContaReferencial, double)? melhor = null;
        foreach (var r in referenciais.Where(r => r.Analitica && r.CodNat == codNat))
        {
            var p = Palavras(r.Descricao);
            var comuns = alvo.Count(p.Contains);
            if (comuns == 0) continue;
            var nota = (double)comuns / (alvo.Count + p.Count - comuns);   // Jaccard
            if (melhor is null || nota > melhor.Value.Item2) melhor = (r, nota);
        }
        return melhor;
    }
}
