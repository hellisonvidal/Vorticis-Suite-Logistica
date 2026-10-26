using System.Text;

namespace Vorticis.Domain.Comum;

/// <summary>
/// Planilha lida para importação: nomes das colunas (primeira linha não vazia) e as linhas seguintes como texto.
/// Vem de .xlsx (Infra) ou de texto CSV (<see cref="DeTexto"/>).
/// </summary>
public sealed record TabelaImportada(IReadOnlyList<string> Colunas, IReadOnlyList<IReadOnlyList<string>> Linhas)
{
    /// <summary>Número da linha na planilha (1 = cabeçalho) de cada linha de dados.</summary>
    public IReadOnlyList<int> NumerosLinha { get; init; } = Enumerable.Range(2, Linhas.Count).ToArray();

    public string Valor(int linha, int coluna) =>
        coluna >= 0 && coluna < Linhas[linha].Count ? Linhas[linha][coluna].Trim() : "";

    /// <summary>Primeira coluna cujo nome normalizado está entre os sinônimos; -1 se nenhuma.</summary>
    public int Coluna(params string[] sinonimos)
    {
        for (var i = 0; i < Colunas.Count; i++)
            if (sinonimos.Contains(Normalizar(Colunas[i]))) return i;
        return -1;
    }

    /// <summary>Monta a tabela a partir de linhas brutas: descarta linhas vazias e usa a primeira como cabeçalho.</summary>
    public static TabelaImportada DeLinhas(IEnumerable<IReadOnlyList<string>> linhas)
    {
        var lidas = linhas.Select((l, i) => (Campos: l, Numero: i + 1)).Where(l => l.Campos.Any(c => c.Trim().Length > 0)).ToList();
        if (lidas.Count == 0) return new([], []);
        var cabecalho = lidas[0].Campos.Select((c, i) => c.Trim().Length > 0 ? c.Trim() : $"Coluna {i + 1}").ToList();
        // Nomes repetidos ganham sufixo para poderem ser escolhidos no mapeamento.
        for (var i = 0; i < cabecalho.Count; i++)
            for (int j = i + 1, n = 2; j < cabecalho.Count; j++)
                if (string.Equals(cabecalho[j], cabecalho[i], StringComparison.OrdinalIgnoreCase)) cabecalho[j] = $"{cabecalho[j]} ({n++})";
        return new(cabecalho, lidas.Skip(1).Select(l => l.Campos).ToList()) { NumerosLinha = lidas.Skip(1).Select(l => l.Numero).ToList() };
    }

    /// <summary>CSV/TXT: separador detectado pela primeira linha (; | tab ,), aspas duplas respeitadas.</summary>
    public static TabelaImportada DeTexto(string texto)
    {
        texto = texto.TrimStart('﻿');
        var primeira = texto.Split('\n')[0];
        var separador = new[] { ';', '|', '\t', ',' }.MaxBy(s => primeira.Count(c => c == s));
        return DeLinhas(LinhasCsv(texto, separador));
    }

    private static IEnumerable<IReadOnlyList<string>> LinhasCsv(string texto, char separador)
    {
        var campos = new List<string>();
        var atual = new StringBuilder();
        var aspas = false;
        for (var i = 0; i < texto.Length; i++)
        {
            var ch = texto[i];
            if (aspas)
            {
                if (ch == '"' && i + 1 < texto.Length && texto[i + 1] == '"') { atual.Append('"'); i++; }
                else if (ch == '"') aspas = false;
                else atual.Append(ch);
            }
            else if (ch == '"' && atual.ToString().Trim().Length == 0) { atual.Clear(); aspas = true; }
            else if (ch == separador) { campos.Add(atual.ToString()); atual.Clear(); }
            else if (ch == '\n')
            {
                campos.Add(atual.ToString().TrimEnd('\r'));
                atual.Clear();
                yield return campos;
                campos = [];
            }
            else atual.Append(ch);
        }
        if (atual.Length > 0 || campos.Count > 0)
        {
            campos.Add(atual.ToString().TrimEnd('\r'));
            yield return campos;
        }
    }

    /// <summary>Maiúsculas, sem acentos, demais caracteres viram "_" (para comparar nomes de colunas).</summary>
    public static string Normalizar(string s) => Contabil.LeitorPlanoReferencial.Normalizar(s).Trim('_');
}
