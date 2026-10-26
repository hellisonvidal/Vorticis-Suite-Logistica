using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Vorticis.Desktop.Comum.Ajuda;

/// <summary>Trecho de texto em linha: negrito, itálico, código ou link.</summary>
public sealed record Trecho(string Texto, bool Negrito = false, bool Italico = false, bool Codigo = false, string? Link = null);

public abstract record Bloco;
public sealed record BlocoTitulo(int Nivel, IReadOnlyList<Trecho> Trechos, string Ancora) : Bloco;
public sealed record BlocoParagrafo(IReadOnlyList<Trecho> Trechos) : Bloco;
public sealed record BlocoLista(bool Ordenada, IReadOnlyList<IReadOnlyList<Trecho>> Itens) : Bloco;
public sealed record BlocoTabela(IReadOnlyList<IReadOnlyList<Trecho>> Cabecalho, IReadOnlyList<IReadOnlyList<IReadOnlyList<Trecho>>> Linhas) : Bloco;

/// <summary>
/// Interpretador mínimo de Markdown para o manual: títulos (#), parágrafos, listas (- e 1.), tabelas simples, negrito,
/// itálico, código em linha e links. Não há HTML embutido nem blocos de código: o manual não usa.
/// </summary>
public static partial class MarkdownSimples
{
    /// <summary>Âncora de um título: minúsculas, sem acentos, só letras e números separados por hífen.</summary>
    public static string Ancora(string titulo)
    {
        var d = titulo.Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder();
        foreach (var c in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        }
        return Regex.Replace(sb.ToString(), "-+", "-").Trim('-');
    }

    /// <summary>Texto sem acentos e em minúsculas, para buscas.</summary>
    public static string Simplificar(string texto) =>
        new([.. texto.Normalize(NormalizationForm.FormKD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).Select(char.ToLowerInvariant)]);

    public static IReadOnlyList<Bloco> Analisar(string markdown)
    {
        var blocos = new List<Bloco>();
        var usadas = new Dictionary<string, int>();
        var linhas = markdown.Replace("\r\n", "\n").Split('\n');
        var para = new List<string>();
        void FecharParagrafo()
        {
            if (para.Count > 0) blocos.Add(new BlocoParagrafo(Inline(string.Join('\n', para))));
            para.Clear();
        }
        for (var i = 0; i < linhas.Length; i++)
        {
            var linha = linhas[i].TrimEnd();
            var t = linha.TrimStart();
            if (t.Length == 0) { FecharParagrafo(); continue; }

            var m = TituloRegex().Match(t);
            if (m.Success)
            {
                FecharParagrafo();
                var texto = m.Groups[2].Value.Trim();
                var ancora = Ancora(texto);
                usadas[ancora] = usadas.GetValueOrDefault(ancora) + 1;
                if (usadas[ancora] > 1) ancora += "-" + usadas[ancora];
                blocos.Add(new BlocoTitulo(m.Groups[1].Length, Inline(texto), ancora));
                continue;
            }
            if (t.StartsWith('|'))
            {
                FecharParagrafo();
                var tabela = new List<string>();
                while (i < linhas.Length && linhas[i].TrimStart().StartsWith('|')) tabela.Add(linhas[i++].Trim());
                i--;
                var corpo = tabela.Where(l => !SeparadorRegex().IsMatch(l)).Select(Celulas).ToList();
                if (corpo.Count > 0) blocos.Add(new BlocoTabela(corpo[0], corpo.Skip(1).ToList()));
                continue;
            }
            var li = ItemRegex().Match(t);
            if (li.Success)
            {
                FecharParagrafo();
                var ordenada = char.IsDigit(t[0]);
                var itens = new List<IReadOnlyList<Trecho>>();
                while (i < linhas.Length)
                {
                    var atual = linhas[i].Trim();
                    var mi = ItemRegex().Match(atual);
                    if (mi.Success && char.IsDigit(atual[0]) == ordenada) itens.Add(Inline(mi.Groups[1].Value));
                    else if (itens.Count > 0 && atual.Length > 0 && linhas[i].StartsWith(' ') && !ItemRegex().IsMatch(atual))
                        itens[^1] = [.. itens[^1], .. Inline(" " + atual)];   // continuação indentada
                    else break;
                    i++;
                }
                i--;
                blocos.Add(new BlocoLista(ordenada, itens));
                continue;
            }
            para.Add(t);
        }
        FecharParagrafo();
        return blocos;
    }

    private static List<IReadOnlyList<Trecho>> Celulas(string linha) =>
        [.. linha.Trim('|').Split('|').Select(c => Inline(c.Trim()))];

    /// <summary>Negrito (**), itálico (*), código (`) e links ([texto](destino)).</summary>
    public static IReadOnlyList<Trecho> Inline(string s)
    {
        var res = new List<Trecho>();
        bool neg = false, ita = false;
        var atual = new StringBuilder();
        void Soltar()
        {
            if (atual.Length > 0) res.Add(new Trecho(atual.ToString(), neg, ita));
            atual.Clear();
        }
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '`')
            {
                var fim = s.IndexOf('`', i + 1);
                if (fim > i) { Soltar(); res.Add(new Trecho(s[(i + 1)..fim], neg, ita, Codigo: true)); i = fim; continue; }
            }
            else if (c == '[')
            {
                var fimTexto = s.IndexOf("](", i, StringComparison.Ordinal);
                var fimLink = fimTexto > 0 ? s.IndexOf(')', fimTexto) : -1;
                if (fimLink > 0)
                {
                    Soltar();
                    res.Add(new Trecho(s[(i + 1)..fimTexto], neg, ita, Link: s[(fimTexto + 2)..fimLink]));
                    i = fimLink;
                    continue;
                }
            }
            else if (c == '*')
            {
                Soltar();
                if (i + 1 < s.Length && s[i + 1] == '*') { neg = !neg; i++; }
                else ita = !ita;
                continue;
            }
            atual.Append(c);
        }
        Soltar();
        return res;
    }

    public static string TextoPlano(IEnumerable<Trecho> trechos) => string.Concat(trechos.Select(t => t.Texto));

    public static string TextoPlano(Bloco b) => b switch
    {
        BlocoTitulo t => TextoPlano(t.Trechos),
        BlocoParagrafo p => TextoPlano(p.Trechos),
        BlocoLista l => string.Join(' ', l.Itens.Select(TextoPlano)),
        BlocoTabela tb => string.Join(' ', tb.Cabecalho.Concat(tb.Linhas.SelectMany(x => x)).Select(TextoPlano)),
        _ => "",
    };

    // ------------------------------------------------------------------ HTML (botão "Abrir no navegador")

    /// <summary>
    /// HTML de um conjunto de blocos. <paramref name="idDe"/> dá o id HTML de uma âncora do arquivo; <paramref name="destino"/>
    /// converte o destino de um link do Markdown (arquivo.md#secao) em href.
    /// </summary>
    public static string ParaHtml(IEnumerable<Bloco> blocos, Func<string, string> idDe, Func<string, string> destino)
    {
        var sb = new StringBuilder();
        string In(IEnumerable<Trecho> ts) => string.Concat(ts.Select(t =>
        {
            var h = WebUtility.HtmlEncode(t.Texto).Replace("\n", "<br>");
            if (t.Codigo) h = $"<code>{h}</code>";
            if (t.Negrito) h = $"<strong>{h}</strong>";
            if (t.Italico) h = $"<em>{h}</em>";
            if (t.Link is { } l) h = $"<a href=\"{WebUtility.HtmlEncode(destino(l))}\">{h}</a>";
            return h;
        }));
        foreach (var b in blocos)
        {
            switch (b)
            {
                case BlocoTitulo t: sb.Append($"<h{t.Nivel} id=\"{idDe(t.Ancora)}\">{In(t.Trechos)}</h{t.Nivel}>\n"); break;
                case BlocoParagrafo p: sb.Append($"<p>{In(p.Trechos)}</p>\n"); break;
                case BlocoLista l:
                    var tag = l.Ordenada ? "ol" : "ul";
                    sb.Append($"<{tag}>").Append(string.Concat(l.Itens.Select(i => $"<li>{In(i)}</li>"))).Append($"</{tag}>\n");
                    break;
                case BlocoTabela tb:
                    sb.Append("<table><thead><tr>").Append(string.Concat(tb.Cabecalho.Select(c => $"<th>{In(c)}</th>"))).Append("</tr></thead><tbody>");
                    foreach (var linha in tb.Linhas) sb.Append("<tr>").Append(string.Concat(linha.Select(c => $"<td>{In(c)}</td>"))).Append("</tr>");
                    sb.Append("</tbody></table>\n");
                    break;
            }
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")] private static partial Regex TituloRegex();
    [GeneratedRegex(@"^(?:[-*+]|\d+\.)\s+(.*)$")] private static partial Regex ItemRegex();
    [GeneratedRegex(@"^\|[\s:|-]+\|?$")] private static partial Regex SeparadorRegex();
}
