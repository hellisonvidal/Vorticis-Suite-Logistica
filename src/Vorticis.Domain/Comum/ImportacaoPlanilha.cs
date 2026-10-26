namespace Vorticis.Domain.Comum;

/// <summary>Campo de um cadastro que pode vir da planilha. Sinônimos: nomes de coluna reconhecidos sozinhos (já normalizados ou não).</summary>
public sealed record CampoImportacao(string Chave, string Rotulo, bool Obrigatorio = false, string Exemplo = "", params string[] Sinonimos);

/// <summary>Uma linha da planilha já "traduzida" pelo de-para: o valor de cada campo do cadastro pela coluna escolhida.</summary>
public sealed class LinhaImportacao(int numero, IReadOnlyDictionary<string, string> valores)
{
    /// <summary>Número da linha na planilha (1 = cabeçalho).</summary>
    public int Numero { get; } = numero;
    public IReadOnlyDictionary<string, string> Valores { get; } = valores;

    public string? Texto(string campo) => Valores.TryGetValue(campo, out var v) && v.Trim().Length > 0 ? v.Trim() : null;
    public decimal? Valor(string campo) => Formato.LerValor(Texto(campo));
    public int? Inteiro(string campo) => Valor(campo) is { } v && v == Math.Truncate(v) ? (int)v : null;
    public DateOnly? Data(string campo) => Formato.LerData(Texto(campo));

    /// <summary>Sim/não: S, Sim, X, 1, True, Ativo... é verdadeiro; vazio usa o padrão.</summary>
    public bool Sim(string campo, bool padrao = false) => Texto(campo)?.ToUpperInvariant() switch
    {
        null => padrao,
        "S" or "SIM" or "X" or "1" or "TRUE" or "VERDADEIRO" or "ATIVO" or "ATIVA" or "Y" or "YES" => true,
        _ => false,
    };

    public bool Vazia => Valores.Values.All(v => string.IsNullOrWhiteSpace(v));
}

public static class ImportacaoPlanilha
{
    /// <summary>
    /// De-para sugerido: para cada campo, a coluna da planilha com o mesmo nome do campo (ou um dos sinônimos), ignorando acentos,
    /// maiúsculas e pontuação. Campos sem coluna parecida ficam de fora.
    /// </summary>
    public static Dictionary<string, string> Sugerir(TabelaImportada tabela, IEnumerable<CampoImportacao> campos)
    {
        var mapa = new Dictionary<string, string>();
        var usadas = new HashSet<int>();
        foreach (var c in campos)
        {
            // o rótulo também vale sem o trecho entre parênteses ("Tipo do item (00 a 99)" casa com "Tipo do item")
            var semParenteses = c.Rotulo.Split('(')[0];
            var nomes = c.Sinonimos.Append(c.Rotulo).Append(semParenteses).Append(c.Chave).Select(TabelaImportada.Normalizar).Distinct().ToArray();
            for (var i = 0; i < tabela.Colunas.Count; i++)
            {
                if (usadas.Contains(i) || !nomes.Contains(TabelaImportada.Normalizar(tabela.Colunas[i]))) continue;
                mapa[c.Chave] = tabela.Colunas[i];
                usadas.Add(i);
                break;
            }
        }
        return mapa;
    }

    /// <summary>Campos obrigatórios sem coluna escolhida.</summary>
    public static IReadOnlyList<string> Faltam(IEnumerable<CampoImportacao> campos, IReadOnlyDictionary<string, string> mapa) =>
        [.. campos.Where(c => c.Obrigatorio && (!mapa.TryGetValue(c.Chave, out var col) || string.IsNullOrWhiteSpace(col))).Select(c => c.Rotulo)];

    /// <summary>Linhas da planilha pelo de-para (as totalmente vazias ficam de fora).</summary>
    public static IReadOnlyList<LinhaImportacao> Linhas(TabelaImportada tabela, IReadOnlyDictionary<string, string> mapa)
    {
        var indices = mapa.Where(m => !string.IsNullOrWhiteSpace(m.Value))
                          .Select(m => (Campo: m.Key, Indice: tabela.Colunas.ToList().FindIndex(c => c == m.Value)))
                          .Where(x => x.Indice >= 0).ToList();
        var linhas = new List<LinhaImportacao>();
        for (var i = 0; i < tabela.Linhas.Count; i++)
        {
            var l = new LinhaImportacao(tabela.NumerosLinha[i], indices.ToDictionary(x => x.Campo, x => tabela.Valor(i, x.Indice)));
            if (!l.Vazia) linhas.Add(l);
        }
        return linhas;
    }
}
