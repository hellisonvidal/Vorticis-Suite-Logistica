using System.Globalization;
using System.Text;

namespace Vorticis.Domain.Sped;

/// <summary>
/// Regras gerais de preenchimento dos arquivos do SPED (Manual de Orientação do Leiaute 9 da ECD, capítulo 2):
/// campos separados por "|" (também no início e no fim da linha), linhas terminadas por CR LF, texto em ISO-8859-1
/// (Latin-1), datas ddmmaaaa, valores sem separador de milhar com vírgula decimal, campos vazios como "||".
/// </summary>
public static class FormatoSped
{
    /// <summary>Codificação exigida pelo PVA (ASCII ISO 8859-1). Caracteres fora dela são trocados antes (<see cref="Texto"/>).</summary>
    public static readonly Encoding Latin1 = Encoding.Latin1;

    public const string FimDeLinha = "\r\n";

    /// <summary>Data no padrão ddmmaaaa; nula vira campo vazio.</summary>
    public static string Data(DateOnly? data) => data is { } d ? d.ToString("ddMMyyyy", CultureInfo.InvariantCulture) : "";

    /// <summary>Valor monetário positivo com duas casas e vírgula decimal (ex.: 1129998,99). O sinal vai no indicador D/C.</summary>
    public static string Valor(decimal valor) => Math.Abs(Math.Round(valor, 2, MidpointRounding.AwayFromZero))
        .ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>Valor com sinal ("-" na frente quando negativo), duas casas e vírgula decimal: campos numéricos com sinal (NS) da ECF.</summary>
    public static string ValorComSinal(decimal valor) => (valor < 0 && Math.Round(valor, 2, MidpointRounding.AwayFromZero) != 0 ? "-" : "") + Valor(valor);

    /// <summary>Percentual com duas a quatro casas e vírgula decimal (ex.: 60,00; 33,3333).</summary>
    public static string Percentual(decimal valor) => valor.ToString("0.00##", CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>Indicador D/C de um saldo com sinal (débito positivo). Saldo zero recebe <paramref name="seZero"/>.</summary>
    public static string Indicador(decimal saldoComSinal, char seZero = 'D') => saldoComSinal > 0 ? "D" : saldoComSinal < 0 ? "C" : seZero.ToString();

    /// <summary>
    /// Texto de campo alfanumérico: sem o delimitador "|" e sem caracteres de controle (0 a 31), espaços repetidos reduzidos,
    /// tipografia comum fora do Latin-1 (travessões, aspas curvas, reticências) trocada pelo equivalente ASCII e o que
    /// ainda não couber no ISO-8859-1 substituído por "?". Respeita o tamanho máximo, quando informado.
    /// </summary>
    public static string Texto(string? texto, int? tamanhoMaximo = null)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        var sb = new StringBuilder(texto.Length);
        foreach (var ch in texto.Normalize(NormalizationForm.FormC))
        {
            var c = ch switch
            {
                '|' => '-',
                '–' or '—' or '−' => '-',
                '‘' or '’' or '‚' or '′' => '\'',
                '“' or '”' or '„' => '"',
                ' ' => ' ',
                _ => ch,
            };
            if (c == '…') { sb.Append("..."); continue; }
            if (c < ' ' || c == '\u007F') c = ' ';
            if (c > 'ÿ') c = '?';
            if (c == ' ' && (sb.Length == 0 || sb[^1] == ' ')) continue;
            sb.Append(c);
        }
        var s = sb.ToString().TrimEnd();
        return tamanhoMaximo is int max && s.Length > max ? s[..max].TrimEnd() : s;
    }

    /// <summary>Monta a linha de um registro: |REG|campo1|campo2|...| (sem o fim de linha).</summary>
    public static string Linha(string registro, params string?[] campos)
    {
        var sb = new StringBuilder("|").Append(registro).Append('|');
        foreach (var c in campos) sb.Append(c ?? "").Append('|');
        return sb.ToString();
    }

    /// <summary>Junta as linhas com CR LF (inclusive após a última) e codifica em ISO-8859-1.</summary>
    public static byte[] Codificar(IEnumerable<string> linhas)
    {
        var sb = new StringBuilder();
        foreach (var l in linhas) sb.Append(l).Append(FimDeLinha);
        return Latin1.GetBytes(sb.ToString());
    }
}
