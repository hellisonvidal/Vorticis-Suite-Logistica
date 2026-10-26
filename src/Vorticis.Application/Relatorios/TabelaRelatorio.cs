using System.Globalization;
using System.Text;
using Vorticis.Domain.Comum;

namespace Vorticis.Application.Relatorios;

public enum TipoColuna { Texto, Valor, Data, Inteiro }

/// <param name="LarguraCm">Largura no PDF; 0 = automática pelo tipo.</param>
public sealed record ColunaRelatorio(string Titulo, TipoColuna Tipo = TipoColuna.Texto, double LarguraCm = 0);

/// <summary>Valores da linha: string, decimal (Valor), DateOnly (Data) ou long/int (Inteiro); null = vazio.</summary>
public sealed record LinhaRelatorio(IReadOnlyList<object?> Valores, bool Destaque = false, int Recuo = 0);

/// <summary>
/// Relatório como exibido na tela: a mesma tabela é exportada para PDF, Excel (.xlsx) e CSV,
/// garantindo que o arquivo tenha exatamente o que o usuário viu.
/// </summary>
public sealed record TabelaRelatorio(string Titulo, IReadOnlyList<ColunaRelatorio> Colunas, IReadOnlyList<LinhaRelatorio> Linhas,
                                     string? Rodape = null);

public static class ExportacaoRelatorio
{
    private static readonly CultureInfo PtBr = Formato.PtBr;

    public static string Texto(object? valor, TipoColuna tipo) => valor switch
    {
        null => "",
        decimal d => d.ToString("#,##0.00", PtBr),
        DateOnly dt => dt.ToString("dd/MM/yyyy", PtBr),
        DateTime dt => dt.ToString("dd/MM/yyyy", PtBr),
        IFormattable f when tipo == TipoColuna.Inteiro => f.ToString("0", PtBr),
        _ => valor.ToString() ?? "",
    };

    /// <summary>Documento PDF com as tabelas exibidas (cabeçalho: empresa, período, emissão).</summary>
    public static DocumentoRelatorio ParaPdf(string titulo, IReadOnlyList<string> cabecalho, IReadOnlyList<TabelaRelatorio> tabelas, bool paisagem)
    {
        var larguraUtil = paisagem ? 26.5 : 18.0;
        var blocos = new List<Bloco>();
        foreach (var t in tabelas)
        {
            // Larguras: fixas por tipo; o texto divide o espaço restante.
            var fixas = t.Colunas.Select(c => c.LarguraCm > 0 ? c.LarguraCm : c.Tipo switch
            {
                TipoColuna.Valor => 2.8, TipoColuna.Data => 2.2, TipoColuna.Inteiro => 1.4, _ => 0,
            }).ToArray();
            var textos = fixas.Count(f => f == 0);
            var resto = Math.Max(2.5, (larguraUtil - fixas.Sum()) / Math.Max(1, textos));
            var colunas = t.Colunas.Select((c, i) => new Coluna(c.Titulo, fixas[i] > 0 ? fixas[i] : resto,
                c.Tipo is TipoColuna.Valor or TipoColuna.Inteiro ? Alinhamento.Direita : Alinhamento.Esquerda)).ToList();
            var linhas = t.Linhas.Select(l => new LinhaTabela(
                l.Valores.Select((v, i) => Texto(v, t.Colunas[i].Tipo)).ToList(), l.Destaque, l.Recuo)).ToList();
            blocos.Add(new Bloco(tabelas.Count > 1 ? t.Titulo : null, null, colunas, linhas));
            if (t.Rodape is { Length: > 0 } rodape) blocos.Add(new Bloco(Texto: SemSimbolos(rodape)));
        }
        return new DocumentoRelatorio(titulo, cabecalho, blocos, paisagem);
    }

    /// <summary>Símbolos da tela que as fontes do PDF não têm.</summary>
    private static string SemSimbolos(string texto) =>
        texto.Replace("✓", "(OK)").Replace("✗", "(ATENÇÃO)").Replace("⚠", "(!)");

    /// <summary>CSV com ponto e vírgula e números no padrão brasileiro (abre direto no Excel em português).</summary>
    public static byte[] Csv(TabelaRelatorio t)
    {
        static string Campo(string s) => s.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? '"' + s.Replace("\"", "\"\"") + '"' : s;
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', t.Colunas.Select(c => Campo(c.Titulo))));
        foreach (var l in t.Linhas)
            sb.AppendLine(string.Join(';', l.Valores.Select((v, i) => Campo(Texto(v, t.Colunas[i].Tipo)))));
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }
}
