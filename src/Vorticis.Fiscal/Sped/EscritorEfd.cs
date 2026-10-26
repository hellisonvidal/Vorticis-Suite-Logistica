using System.Globalization;
using System.Security.Cryptography;
using Vorticis.Domain.Sped;

namespace Vorticis.Fiscal.Sped;

/// <summary>
/// Monta o arquivo da EFD por blocos: cada bloco recebe seus registros, o fechamento "x990" é calculado e o bloco 9
/// (9001, 9900 por registro, 9990 e 9999) sai da contagem final. Formato geral em <see cref="FormatoSped"/>.
/// </summary>
internal sealed class EscritorEfd
{
    private readonly List<(char Bloco, List<string> Linhas)> _blocos = [];
    private readonly Dictionary<string, int> _contagem = [];
    private readonly List<string> _ordemRegistros = [];
    private List<string> _atual = null!;
    private char _blocoAtual;

    /// <summary>Abre o bloco com o registro de abertura (x001) com o indicador de movimento (0 com dados, 1 sem dados).</summary>
    public void Abrir(char bloco, bool comDados)
    {
        _atual = [];
        _blocoAtual = bloco;
        _blocos.Add((bloco, _atual));
        Reg($"{bloco}001", comDados ? "0" : "1");
    }

    public void Reg(string registro, params string?[] campos)
    {
        _atual.Add(FormatoSped.Linha(registro, campos));
        if (!_contagem.TryAdd(registro, 1)) _contagem[registro]++;
        else _ordemRegistros.Add(registro);
    }

    /// <summary>Fecha o bloco com x990 (quantidade de linhas do bloco, inclusive abertura e fechamento).</summary>
    public void Fechar() => Reg($"{_blocoAtual}990", (_atual.Count + 1).ToString(CultureInfo.InvariantCulture));

    /// <summary>Quantidade de linhas já escritas no bloco aberto (para o QTD_LIN do 0990 etc.).</summary>
    public ArquivoGeradoEfd Concluir()
    {
        // bloco 9: 9001, 9900 (um por registro, inclusive os do próprio bloco 9), 9990 e 9999
        var registros = _ordemRegistros.Concat(["9001", "9900", "9990", "9999"]).ToList();
        _atual = [];
        _blocos.Add(('9', _atual));
        _blocoAtual = '9';
        _atual.Add(FormatoSped.Linha("9001", "0"));
        foreach (var r in registros)
        {
            var qtd = r switch
            {
                "9001" or "9990" or "9999" => 1,
                "9900" => registros.Count,
                _ => _contagem[r],
            };
            _atual.Add(FormatoSped.Linha("9900", r, qtd.ToString(CultureInfo.InvariantCulture)));
        }
        _atual.Add(FormatoSped.Linha("9990", (_atual.Count + 2).ToString(CultureInfo.InvariantCulture)));
        var total = _blocos.Sum(b => b.Linhas.Count) + 1;
        _atual.Add(FormatoSped.Linha("9999", total.ToString(CultureInfo.InvariantCulture)));

        var linhas = _blocos.SelectMany(b => b.Linhas).ToList();
        var contagem = new Dictionary<string, int>(_contagem) { ["9001"] = 1, ["9900"] = registros.Count, ["9990"] = 1, ["9999"] = 1 };
        var conteudo = FormatoSped.Codificar(linhas);
        return new ArquivoGeradoEfd(linhas, contagem, conteudo, Convert.ToHexString(SHA1.HashData(conteudo)));
    }
}

/// <summary>Formatação de campos da EFD e validações comuns (chave de acesso, CFOP, telefone).</summary>
internal static class CamposEfd
{
    public static string D(DateOnly? d) => FormatoSped.Data(d);
    public static string V(decimal v) => FormatoSped.Valor(v);

    /// <summary>Valor opcional: zero vira campo vazio quando <paramref name="vazioSeZero"/>.</summary>
    public static string V(decimal v, bool vazioSeZero) => vazioSeZero && v == 0 ? "" : FormatoSped.Valor(v);

    public static string Aliq(decimal v) => FormatoSped.Percentual(v);

    /// <summary>Valor unitário com até 6 casas (H010.VL_UNIT).</summary>
    public static string ValorUnitario(decimal v) => Math.Round(v, 6).ToString("0.00####", CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>Quantidade com até 5 casas e vírgula decimal.</summary>
    public static string Qtd(decimal v) => v.ToString("0.#####", CultureInfo.InvariantCulture).Replace('.', ',');

    public static string Digitos(string? s) => s is null ? "" : new string(s.Where(char.IsAsciiDigit).ToArray());

    public static string T(string? s, int max) => FormatoSped.Texto(s, max);

    /// <summary>Telefone só com dígitos (DDD + número, no máximo 11 dígitos; vazio se tiver menos de 8).</summary>
    public static string Fone(string? s)
    {
        var d = Digitos(s);
        if (d.Length > 11) d = d[^11..];
        return d.Length < 8 ? "" : d;
    }

    /// <summary>Nome do último dia do mês seguinte à competência no dia pedido (limitado ao fim do mês).</summary>
    public static DateOnly Vencimento(DateOnly fimPeriodo, int dia)
    {
        var prox = new DateOnly(fimPeriodo.Year, fimPeriodo.Month, 1).AddMonths(1);
        return new DateOnly(prox.Year, prox.Month, Math.Min(Math.Max(dia, 1), DateTime.DaysInMonth(prox.Year, prox.Month)));
    }

    /// <summary>Chave de acesso de 44 dígitos com o dígito verificador (módulo 11, pesos 2 a 9) conferido.</summary>
    public static bool ChaveValida(string? chave)
    {
        if (chave is not { Length: 44 } || !chave.All(char.IsAsciiDigit)) return false;
        var soma = 0; var peso = 2;
        for (var i = 42; i >= 0; i--)
        {
            soma += (chave[i] - '0') * peso;
            peso = peso == 9 ? 2 : peso + 1;
        }
        var resto = soma % 11;
        var dv = resto < 2 ? 0 : 11 - resto;
        return chave[43] - '0' == dv;
    }

    public static bool CfopEntrada(string cfop) => cfop.Length == 4 && cfop[0] is '1' or '2' or '3';
    public static bool CfopSaida(string cfop) => cfop.Length == 4 && cfop[0] is '5' or '6' or '7';

    public static string Descricao(string unidade) => unidade switch
    {
        "UN" or "UND" => "Unidade", "KG" => "Quilograma", "G" => "Grama", "L" or "LT" => "Litro", "M" => "Metro", "M2" => "Metro quadrado", "M3" => "Metro cúbico",
        "CX" => "Caixa", "PC" or "PCS" => "Peça", "PAR" => "Par", "DZ" => "Dúzia", "FD" => "Fardo", "SC" => "Saco", "TON" or "T" => "Tonelada", "HR" => "Hora",
        _ => unidade,
    };

    /// <summary>Unidade válida para o 0190 (maiúscula, até 6 caracteres); vazia vira UN.</summary>
    public static string Unidade(string? u) => string.IsNullOrWhiteSpace(u) ? "UN" : FormatoSped.Texto(u.Trim().ToUpperInvariant(), 6);

    /// <summary>CST do ICMS no formato da EFD: origem + CST (3 posições); CSOSN (3 dígitos) segue como veio.</summary>
    public static string CstIcms(string? cst, string? origem)
    {
        var c = Digitos(cst);
        var o = string.IsNullOrEmpty(origem) ? "0" : origem[..1];
        return c.Length switch { 0 => o + "90", 1 => o + "0" + c, 2 => o + c, 3 => c, _ => c[^3..] };
    }

    public static string Cst2(string? cst, string padrao)
    {
        var c = Digitos(cst);
        return c.Length == 0 ? padrao : c.Length == 1 ? "0" + c : c[^2..];
    }
}
