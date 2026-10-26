using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Vorticis.Domain.Contabil;

public enum SeparadorExtrato { Espacos, PontoVirgula, Tabulacao }

/// <summary>
/// Layout personalizado de extrato em PDF, TXT ou CSV (bancos que não oferecem OFX): como separar as colunas de cada linha e em
/// qual coluna (1, 2, 3...) estão a data, a descrição, o valor e o documento. <paramref name="ColunaDescricao"/> 0 = junta as colunas
/// de texto que sobram; <paramref name="ColunaValor"/> 0 = automático (o valor da linha; com dois ou mais, o penúltimo, porque o último costuma ser o saldo); <paramref name="ColunaDocumento"/> 0 = sem documento.
/// <paramref name="Ignorar"/>: palavras (separadas por vírgula) que descartam a linha, como as de saldo.
/// <paramref name="InverterSinal"/>: para extratos que mostram as saídas como positivas (ex.: fatura de cartão).
/// </summary>
public sealed record LayoutExtrato(string Nome = "", SeparadorExtrato Separador = SeparadorExtrato.Espacos, int ColunaData = 1, int ColunaDescricao = 0,
                                   int ColunaValor = 0, int ColunaDocumento = 0, string FormatoData = "dd/MM/yyyy", string Ignorar = "SALDO",
                                   bool InverterSinal = false);

/// <summary>Linha do documento carregado, já separada em colunas, e se ela virou movimento pelo layout.</summary>
public sealed record LinhaDocumentoExtrato(int Numero, string Texto, IReadOnlyList<string> Colunas, TransacaoExtrato? Transacao);

/// <summary>
/// Lê extratos em texto (o texto extraído do PDF, ou arquivos TXT/CSV) com um <see cref="LayoutExtrato"/>: cada linha cuja coluna da data
/// tem uma data e que tem um valor vira um movimento; as demais (cabeçalhos, saldos, rodapés) são ignoradas.
/// </summary>
public static partial class LeitorExtratoTexto
{
    public static readonly IReadOnlyList<string> FormatosData = ["dd/MM/yyyy", "dd/MM/yy", "dd/MM", "yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy"];

    [GeneratedRegex(@"\s{2,}|\t")] private static partial Regex Espacos();
    [GeneratedRegex(@"^\s*(R\$\s*)?[-+]?\s*(R\$\s*)?\d{1,3}(\.\d{3})*(,\d{2})\s*[-+]?\s*([DC]|\(?[DC]\)?)?\s*$|^\s*[-+]?\d+(\.\d{2})\s*[-+]?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Valor();

    public static IReadOnlyList<string> Colunas(string linha, SeparadorExtrato separador) => separador switch
    {
        SeparadorExtrato.PontoVirgula => linha.Split(';').Select(c => c.Trim().Trim('"')).ToList(),
        SeparadorExtrato.Tabulacao => linha.Split('\t').Select(c => c.Trim()).ToList(),
        _ => Espacos().Split(linha.Trim()).Where(c => c.Length > 0).ToList(),
    };

    public static bool EhValor(string s) => Valor().IsMatch(s);

    /// <summary>"1.234,56", "-1.234,56", "1.234,56-", "1.234,56 D", "R$ 10,00 C" ou "1234.56"; D ou sinal de menos = saída.</summary>
    public static decimal? LerValor(string s)
    {
        if (!EhValor(s)) return null;
        var t = s.Trim().ToUpperInvariant().Replace("R$", "").Replace("(", "").Replace(")", "").Replace(" ", "");
        var negativo = t.StartsWith('-') || t.EndsWith('-') || t.EndsWith('D');
        t = t.Trim('-', '+', 'D', 'C');
        var cultura = t.Contains(',') ? new CultureInfo("pt-BR") : CultureInfo.InvariantCulture;
        if (!decimal.TryParse(t, NumberStyles.Number, cultura, out var v)) return null;
        return negativo ? -v : v;
    }

    public static DateOnly? LerData(string s, string formato, int ano)
    {
        var t = s.Trim();
        if (formato == "dd/MM")
        {
            if (t.Length > 5) t = t[..5];
            return DateOnly.TryParseExact($"{t}/{ano}", "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        }
        if (t.Length > formato.Length) t = t[..formato.Length];
        return DateOnly.TryParseExact(t, formato, CultureInfo.InvariantCulture, DateTimeStyles.None, out var r) ? r : null;
    }

    /// <summary>Aplica o layout às linhas do documento. <paramref name="ano"/>: ano das datas sem ano (formato dd/MM).</summary>
    public static IReadOnlyList<LinhaDocumentoExtrato> Aplicar(IReadOnlyList<string> linhas, LayoutExtrato layout, int ano)
    {
        var ignorar = (layout.Ignorar ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ocorrencias = new Dictionary<string, int>();
        var res = new List<LinhaDocumentoExtrato>(linhas.Count);
        for (var i = 0; i < linhas.Count; i++)
        {
            var colunas = Colunas(linhas[i], layout.Separador);
            TransacaoExtrato? t = null;
            if (!ignorar.Any(p => linhas[i].Contains(p, StringComparison.CurrentCultureIgnoreCase)) && Coluna(colunas, layout.ColunaData) is { } textoData
                && LerData(textoData, layout.FormatoData, ano) is { } data)
            {
                var usadas = new HashSet<int> { layout.ColunaData };
                var valores = Enumerable.Range(1, colunas.Count).Where(c => c != layout.ColunaData && EhValor(colunas[c - 1])).ToList();
                // automático: o valor da linha; havendo dois ou mais, o penúltimo (o último costuma ser o saldo)
                int colValor = layout.ColunaValor > 0 ? layout.ColunaValor : valores.Count >= 2 ? valores[^2] : valores.LastOrDefault();
                if (colValor > 0 && Coluna(colunas, colValor) is { } textoValor && LerValor(textoValor) is { } valor && valor != 0)
                {
                    usadas.Add(colValor);
                    // valores à direita do valor do movimento (ex.: saldo do dia) não entram na descrição
                    for (var c = colValor + 1; c <= colunas.Count; c++) if (EhValor(colunas[c - 1])) usadas.Add(c);
                    var doc = layout.ColunaDocumento > 0 ? Coluna(colunas, layout.ColunaDocumento) : null;
                    if (layout.ColunaDocumento > 0) usadas.Add(layout.ColunaDocumento);
                    // texto colado à data na mesma coluna (ex.: "15/03/2026 PIX RECEBIDO") faz parte da descrição
                    var tamanhoData = layout.FormatoData == "dd/MM" ? 5 : layout.FormatoData.Length;
                    var resto = textoData.Trim().Length > tamanhoData ? textoData.Trim()[tamanhoData..].Trim() : "";
                    var desc = layout.ColunaDescricao > 0
                        ? Coluna(colunas, layout.ColunaDescricao) ?? ""
                        : string.Join(" ", new[] { resto }.Concat(Enumerable.Range(1, colunas.Count).Where(c => !usadas.Contains(c) && !EhValor(colunas[c - 1]))
                                                                                    .Select(c => colunas[c - 1])).Where(x => x.Length > 0));
                    if (layout.InverterSinal) valor = -valor;
                    t = new TransacaoExtrato(Identificar(data, valor, desc, ocorrencias), data, valor, desc.Trim(), string.IsNullOrWhiteSpace(doc) ? null : doc.Trim());
                }
            }
            res.Add(new LinhaDocumentoExtrato(i + 1, linhas[i], colunas, t));
        }
        return res;
    }

    /// <summary>Extrato montado a partir das linhas reconhecidas; banco/agência/conta ficam para quem chama (ex.: conta contábil do banco).</summary>
    public static ExtratoBancario Extrato(IReadOnlyList<LinhaDocumentoExtrato> linhas, string origem)
    {
        var ts = linhas.Where(l => l.Transacao is not null).Select(l => l.Transacao!).ToList();
        return new ExtratoBancario(origem, "", "", ts.Count > 0 ? ts.Min(t => t.Data) : null, ts.Count > 0 ? ts.Max(t => t.Data) : null, null, ts);
    }

    /// <summary>
    /// Sugere o layout olhando o documento: separador mais provável, a coluna onde aparecem datas e o formato delas; valor automático.
    /// </summary>
    public static LayoutExtrato Sugerir(IReadOnlyList<string> linhas, int ano)
    {
        var separador = linhas.Count(l => l.Contains(';')) > linhas.Count / 2 ? SeparadorExtrato.PontoVirgula
                      : linhas.Count(l => l.Contains('\t')) > linhas.Count / 2 ? SeparadorExtrato.Tabulacao : SeparadorExtrato.Espacos;
        var melhor = (Coluna: 1, Formato: FormatosData[0], Acertos: 0);
        foreach (var formato in FormatosData)
            for (var c = 1; c <= 3; c++)
            {
                var acertos = linhas.Count(l => Colunas(l, separador) is var cs && Coluna(cs, c) is { } s && LerData(s, formato, ano) is not null
                                                && cs.Any(EhValor));
                if (acertos > melhor.Acertos) melhor = (c, formato, acertos);
            }
        return new LayoutExtrato("", separador, melhor.Coluna, 0, 0, 0, melhor.Formato);
    }

    private static string? Coluna(IReadOnlyList<string> colunas, int numero) => numero >= 1 && numero <= colunas.Count ? colunas[numero - 1] : null;

    /// <summary>Identificador estável do movimento (data, valor, descrição e a ordem entre movimentos iguais): reimportar não duplica.</summary>
    private static string Identificar(DateOnly data, decimal valor, string descricao, Dictionary<string, int> ocorrencias)
    {
        var baseId = $"{data:yyyyMMdd}|{valor.ToString(CultureInfo.InvariantCulture)}|{descricao.Trim().ToUpperInvariant()}";
        ocorrencias[baseId] = ocorrencias.GetValueOrDefault(baseId) + 1;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{baseId}|{ocorrencias[baseId]}"));
        return Convert.ToHexString(hash)[..20];
    }
}
