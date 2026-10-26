using Vorticis.Domain.Comum;

namespace Vorticis.Domain.Contabil;

/// <summary>
/// Máscara do código das contas, ex.: "9.9.99.999" (4 níveis com 1, 1, 2 e 3 dígitos). Qualquer letra ou dígito
/// (ou #) marca uma posição; os demais caracteres separam os níveis. O código gravado usa sempre o ponto como separador.
/// </summary>
public sealed class MascaraPlano
{
    private MascaraPlano(string texto, IReadOnlyList<int> segmentos) => (Texto, Segmentos) = (texto, segmentos);

    public string Texto { get; }
    public IReadOnlyList<int> Segmentos { get; }

    public static IReadOnlyList<string> Sugestoes { get; } = ["9.9.99.999", "9.9.9.99.999", "9.9.99.9999", "9.9.99.99.9999", "9.99.99.999", "9.9.999.9999"];

    public static Resultado<MascaraPlano> Ler(string? texto)
    {
        var t = (texto ?? "").Trim();
        var segmentos = new List<int>();
        var n = 0;
        foreach (var ch in t)
        {
            if (char.IsLetterOrDigit(ch) || ch == '#') n++;
            else if (n > 0) { segmentos.Add(n); n = 0; }
        }
        if (n > 0) segmentos.Add(n);
        if (segmentos.Count == 0) return Resultado<MascaraPlano>.Falha("Informe a máscara, ex.: 9.9.99.999.");
        if (segmentos.Sum() + segmentos.Count - 1 > 30) return Resultado<MascaraPlano>.Falha("A máscara passa de 30 caracteres.");
        if (segmentos.Count > 10) return Resultado<MascaraPlano>.Falha("A máscara pode ter no máximo 10 níveis.");
        return Resultado<MascaraPlano>.Ok(new MascaraPlano(string.Join('.', segmentos.Select(s => new string('9', s))), segmentos));
    }

    /// <summary>Máscara que comporta os códigos existentes (maior quantidade de dígitos em cada nível).</summary>
    public static string? Deduzir(IEnumerable<string> codigos)
    {
        var larguras = new List<int>();
        foreach (var c in codigos)
        {
            var segs = c.Split('.');
            for (var i = 0; i < segs.Length; i++)
                if (i < larguras.Count) larguras[i] = Math.Max(larguras[i], segs[i].Length);
                else larguras.Add(segs[i].Length);
        }
        return larguras.Count == 0 ? null : string.Join('.', larguras.Select(l => new string('9', l)));
    }

    /// <summary>
    /// Converte um código para a máscara. Códigos com separadores (ponto, traço, barra, espaço) têm cada nível
    /// completado com zeros à esquerda (1.1.1.1 → 1.1.01.001); códigos só com dígitos são divididos pelas larguras
    /// da máscara (1101001 → 1.1.01.001; com 9.9.9.99, 1111 → 1.1.1.01). Falha quando um nível não cabe na máscara.
    /// </summary>
    public Resultado<string> Converter(string codigo)
    {
        var c = codigo.Trim();
        if (c.Length == 0) return Resultado<string>.Falha("código vazio");
        if (c.Any(char.IsLetter)) return Resultado<string>.Falha($"o código {c} contém letras");
        var segs = c.Split(c.Where(ch => !char.IsAsciiDigit(ch)).Distinct().ToArray(), StringSplitOptions.RemoveEmptyEntries);
        if (segs.Length == 0) return Resultado<string>.Falha($"o código {c} não tem dígitos");

        if (segs.Length == 1 && segs[0].Length > Segmentos[0])
        {
            // Só dígitos: divide pelas larguras; um último nível mais curto é completado com zeros (1111 → 1.1.1.01).
            var digitos = segs[0];
            var partes = new List<string>();
            var pos = 0;
            foreach (var largura in Segmentos)
            {
                if (pos == digitos.Length) break;
                var tamanho = Math.Min(largura, digitos.Length - pos);
                partes.Add(digitos.Substring(pos, tamanho).PadLeft(largura, '0'));
                pos += tamanho;
            }
            if (pos < digitos.Length) return Resultado<string>.Falha($"o código {c} tem mais dígitos que a máscara {Texto}");
            return Resultado<string>.Ok(string.Join('.', partes));
        }

        if (segs.Length > Segmentos.Count)
            return Resultado<string>.Falha($"o código {c} tem {segs.Length} níveis e a máscara {Texto} só {Segmentos.Count}");
        var res = new string[segs.Length];
        for (var i = 0; i < segs.Length; i++)
        {
            var v = segs[i].TrimStart('0');
            if (v.Length == 0) v = "0";
            if (v.Length > Segmentos[i])
                return Resultado<string>.Falha($"no código {c}, o nível {i + 1} ({segs[i]}) não cabe em {Segmentos[i]} dígito(s)");
            res[i] = v.PadLeft(Segmentos[i], '0');
        }
        return Resultado<string>.Ok(string.Join('.', res));
    }

    /// <summary>Sem máscara: mantém os níveis da planilha, só trocando os separadores por ponto.</summary>
    public static Resultado<string> SoNormalizar(string codigo)
    {
        var c = codigo.Trim();
        if (c.Length == 0) return Resultado<string>.Falha("código vazio");
        if (c.Any(char.IsLetter)) return Resultado<string>.Falha($"o código {c} contém letras");
        var segs = c.Split(c.Where(ch => !char.IsAsciiDigit(ch)).Distinct().ToArray(), StringSplitOptions.RemoveEmptyEntries);
        return segs.Length == 0 ? Resultado<string>.Falha($"o código {c} não tem dígitos") : Resultado<string>.Ok(string.Join('.', segs));
    }
}

/// <summary>Conta lida da planilha, já no código final.</summary>
public sealed record ContaImportada(int Linha, string CodigoOriginal, string Codigo, string Descricao, bool Analitica, short Nivel,
                                    string CodNat, IndicadorDC NaturezaSaldo);

public sealed record PlanoImportado(IReadOnlyList<ContaImportada> Contas, IReadOnlyList<string> Erros)
{
    public bool Valido => Erros.Count == 0 && Contas.Count > 0;
}

/// <summary>Colunas da planilha usadas na importação do plano (-1 = não informada; o dado é deduzido).</summary>
public sealed record ColunasPlano(int Codigo, int Descricao, int Natureza = -1, int Tipo = -1, int NaturezaSaldo = -1)
{
    public static ColunasPlano Detectar(TabelaImportada t)
    {
        var cod = t.Coluna("CODIGO", "COD", "CONTA", "CODIGO_CONTA", "CODIGO_DA_CONTA", "COD_CONTA", "CLASSIFICACAO", "CLASSIF", "COD_CTA",
                           "CONTA_CONTABIL", "NUMERO", "ESTRUTURA");
        var desc = t.Coluna("DESCRICAO", "NOME", "DESCRICAO_CONTA", "DESCRICAO_DA_CONTA", "NOME_CONTA", "NOME_DA_CONTA", "TITULO", "HISTORICO");
        if (cod < 0) cod = 0;
        if (desc < 0 || desc == cod) desc = cod == 0 && t.Colunas.Count > 1 ? 1 : 0;
        return new(cod, desc,
            t.Coluna("COD_NAT", "NATUREZA", "NATUREZA_CONTA", "GRUPO", "NAT", "GRUPO_DE_CONTAS"),
            t.Coluna("TIPO", "IND_CTA", "TIPO_CONTA", "TIPO_DA_CONTA", "S_A", "SINTETICA_ANALITICA"),
            t.Coluna("NATUREZA_SALDO", "NATUREZA_DO_SALDO", "SALDO", "D_C", "DC", "DEVEDORA_CREDORA"));
    }
}

/// <summary>
/// Lê um plano de contas de planilha (Excel/CSV) e converte os códigos para a máscara escolhida. Regras:
/// conta superior = código sem o último nível e precisa estar na planilha; quem tem subcontas é sintética;
/// natureza ECD (COD_NAT) e natureza do saldo vêm da planilha ou são deduzidas do grupo (1 Ativo, 2 Passivo/PL,
/// demais Resultado; receitas credoras).
/// </summary>
public static class LeitorPlanoContas
{
    public static PlanoImportado Ler(TabelaImportada t, ColunasPlano colunas, MascaraPlano? mascara)
    {
        var erros = new List<string>();
        var brutas = new List<(int Linha, string Original, string Codigo, string Desc, string? Nat, string? Tipo, IndicadorDC? Saldo)>();
        for (var i = 0; i < t.Linhas.Count; i++)
        {
            var linha = t.NumerosLinha[i];
            var original = t.Valor(i, colunas.Codigo);
            var desc = t.Valor(i, colunas.Descricao);
            if (original.Length == 0 && desc.Length == 0) continue;
            if (original.Length == 0) { erros.Add($"Linha {linha}: código vazio."); continue; }
            if (desc.Length == 0) { erros.Add($"Linha {linha}: descrição vazia (conta {original})."); continue; }
            var conv = mascara is null ? MascaraPlano.SoNormalizar(original) : mascara.Converter(original);
            if (!conv.Sucesso) { erros.Add($"Linha {linha}: {conv.Erros[0]}."); continue; }
            if (conv.Valor.Length > 30) { erros.Add($"Linha {linha}: o código {conv.Valor} passa de 30 caracteres."); continue; }
            brutas.Add((linha, original, conv.Valor, desc.Length > 200 ? desc[..200] : desc,
                        LerNatureza(t.Valor(i, colunas.Natureza)), LerTipo(t.Valor(i, colunas.Tipo)), LerSaldo(t.Valor(i, colunas.NaturezaSaldo))));
        }

        foreach (var g in brutas.GroupBy(b => b.Codigo).Where(g => g.Count() > 1).Take(10))
            erros.Add($"Código {g.Key} repetido (linhas {string.Join(", ", g.Select(b => b.Linha))}).");
        var porCodigo = brutas.GroupBy(b => b.Codigo).ToDictionary(g => g.Key, g => g.First());
        static string? Pai(string cod) => cod.LastIndexOf('.') is var p and > 0 ? cod[..p] : null;
        foreach (var b in brutas.Where(b => Pai(b.Codigo) is { } p && !porCodigo.ContainsKey(p)).Take(20))
            erros.Add($"Linha {b.Linha}: a conta superior {Pai(b.Codigo)} de {b.Codigo} não está na planilha.");
        var comFilhas = brutas.Select(b => Pai(b.Codigo)).OfType<string>().ToHashSet();

        string Natureza(string cod)
        {
            for (var a = cod; a is not null; a = Pai(a))
                if (porCodigo.TryGetValue(a, out var b) && b.Nat is { } nat) return nat;
            for (var a = cod; a is not null; a = Pai(a))
                if (porCodigo.TryGetValue(a, out var b) && TabelaImportada.Normalizar(b.Desc).Contains("PATRIMONIO_LIQUIDO")) return "03";
            return cod.Split('.')[0].TrimStart('0') switch { "1" => "01", "2" => "02", _ => "04" };
        }
        IndicadorDC Saldo(string cod, string nat)
        {
            for (var a = cod; a is not null; a = Pai(a))
                if (porCodigo.TryGetValue(a, out var b) && b.Saldo is { } s) return s;
            if (nat is "02" or "03") return IndicadorDC.C;
            if (nat == "04")
                for (var a = cod; a is not null; a = Pai(a))
                    if (porCodigo.TryGetValue(a, out var b) && TabelaImportada.Normalizar(b.Desc).Contains("RECEITA")) return IndicadorDC.C;
            return IndicadorDC.D;
        }

        var contas = new List<ContaImportada>(brutas.Count);
        foreach (var b in brutas.OrderBy(b => b.Codigo, Comparer<string>.Create(CompararCodigos)))
        {
            var temFilhas = comFilhas.Contains(b.Codigo);
            var nivel = (short)(b.Codigo.Count(ch => ch == '.') + 1);
            if (b.Tipo == "A" && temFilhas) erros.Add($"Linha {b.Linha}: a conta {b.Codigo} está como analítica, mas tem subcontas.");
            var analitica = !temFilhas && nivel > 1 && b.Tipo != "S";
            var nat = Natureza(b.Codigo);
            contas.Add(new ContaImportada(b.Linha, b.Original, b.Codigo, b.Desc, analitica, nivel, nat, Saldo(b.Codigo, nat)));
        }
        if (brutas.Count == 0 && erros.Count == 0) erros.Add("A planilha não tem contas.");
        return new PlanoImportado(contas, erros.Take(50).ToList());
    }

    /// <summary>Ordena nível a nível pelo valor numérico (1.2 antes de 1.10).</summary>
    public static int CompararCodigos(string? a, string? b)
    {
        var sa = (a ?? "").Split('.');
        var sb = (b ?? "").Split('.');
        for (var i = 0; i < Math.Min(sa.Length, sb.Length); i++)
        {
            var c = sa[i].TrimStart('0').Length.CompareTo(sb[i].TrimStart('0').Length);
            if (c == 0) c = string.CompareOrdinal(sa[i].TrimStart('0'), sb[i].TrimStart('0'));
            if (c != 0) return c;
        }
        return sa.Length.CompareTo(sb.Length);
    }

    private static string? LerNatureza(string v)
    {
        var n = TabelaImportada.Normalizar(v);
        if (n.Length == 0) return null;
        if (n.All(char.IsAsciiDigit)) return n.TrimStart('0').PadLeft(2, '0') is var d && d is "01" or "02" or "03" or "04" or "05" or "09" ? d : null;
        if (n.StartsWith("ATIVO")) return "01";
        if (n.StartsWith("PASSIVO")) return "02";
        if (n.StartsWith("PATRIMONIO") || n == "PL") return "03";
        if (n.StartsWith("RESULTADO") || n.StartsWith("RECEITA") || n.StartsWith("DESPESA") || n.StartsWith("CUSTO")) return "04";
        if (n.StartsWith("COMPENSACA")) return "05";
        return null;
    }

    private static string? LerTipo(string v) => TabelaImportada.Normalizar(v) switch
    {
        "A" or "ANALITICA" or "ANALITICO" => "A",
        "S" or "SINTETICA" or "SINTETICO" or "T" or "TOTALIZADORA" => "S",
        _ => null,
    };

    private static IndicadorDC? LerSaldo(string v) => TabelaImportada.Normalizar(v) switch
    {
        "D" or "DEVEDORA" or "DEVEDOR" => IndicadorDC.D,
        "C" or "CREDORA" or "CREDOR" => IndicadorDC.C,
        _ => null,
    };
}
