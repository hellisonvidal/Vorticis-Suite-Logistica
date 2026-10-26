namespace Vorticis.Domain.Contabil;

/// <summary>Colunas da DMPL (modelo do CFC / NBC TG 26, Lei 6.404 art. 186 §2º).</summary>
public enum ColunaPl { CapitalSocial, ReservasCapital, AjustesAvaliacao, ReservasLucros, AcoesTesouraria, LucrosAcumulados }

/// <summary>Partida de um lançamento do período, para classificar as mutações do PL.</summary>
/// <param name="SaldoInicial">Implantação de saldos: compõe o saldo de abertura, não é mutação.</param>
public sealed record PartidaMutacao(long LancamentoId, long ContaId, IndicadorDC DC, decimal Valor, bool Encerramento, bool SaldoInicial = false);

/// <summary>Linha da DMPL: um valor por coluna (aumento do PL positivo) e o total.</summary>
public sealed record LinhaMutacao(string Descricao, IReadOnlyDictionary<ColunaPl, decimal> Valores, bool Saldo = false)
{
    public decimal Total => Valores.Values.Sum();
    public decimal Valor(ColunaPl c) => Valores.GetValueOrDefault(c);
}

/// <param name="SemValor">Linha de título (ex.: "Destinações:"), sem valor.</param>
public sealed record LinhaDlpa(string Descricao, decimal Valor, bool Destaque = false, bool SemValor = false);

public sealed record DemonstracaoMutacoes(DateOnly Inicio, DateOnly Fim, IReadOnlyList<ColunaPl> Colunas, IReadOnlyList<LinhaMutacao> Dmpl,
                                          IReadOnlyList<LinhaDlpa> Dlpa)
{
    /// <summary>Ordem das colunas da DMPL (CPC 26): capital, reservas de capital, reservas de lucros, ajustes de avaliação patrimonial,
    /// ações em tesouraria (só se houver) e lucros ou prejuízos acumulados; o total vem depois.</summary>
    public static IReadOnlyList<ColunaPl> OrdemDmpl { get; } =
        [ColunaPl.CapitalSocial, ColunaPl.ReservasCapital, ColunaPl.ReservasLucros, ColunaPl.AjustesAvaliacao, ColunaPl.AcoesTesouraria,
         ColunaPl.LucrosAcumulados];

    public static string NomeColuna(ColunaPl c) => c switch
    {
        ColunaPl.CapitalSocial => "Capital social",
        ColunaPl.ReservasCapital => "Reservas de capital",
        ColunaPl.AjustesAvaliacao => "Ajustes de avaliação patrimonial",
        ColunaPl.ReservasLucros => "Reservas de lucros",
        ColunaPl.AcoesTesouraria => "Ações/quotas em tesouraria",
        _ => "Lucros ou prejuízos acumulados",
    };
}

/// <summary>
/// DMPL e DLPA (modelos do CFC; Lei 6.404 art. 186; NBC TG 26). As contas do PL (COD_NAT 03) são enquadradas nas colunas
/// pela descrição (própria e dos grupos superiores). As mutações do período vêm dos lançamentos que movimentam o PL,
/// classificados pela contrapartida: integralização de capital (contra ativo), dividendos (contra passivo), constituição
/// e reversão de reservas, aumento de capital com reservas/lucros (entre contas do PL). O lucro líquido é o da DRE
/// (sem os lançamentos de encerramento, que só o transferem ao PL), de modo que o saldo final bate com o Balanço.
/// </summary>
public static class Mutacoes
{
    public const string AumentoCapital = "Aumento de capital (integralização)";
    public const string CapitalComReservas = "Aumento de capital com reservas e lucros";
    public const string ConstituicaoReservas = "Destinação: constituição de reservas";
    public const string ReversaoReservas = "Reversão de reservas";
    public const string Dividendos = "Destinação: dividendos e lucros distribuídos";
    public const string LucroLiquido = "Resultado do exercício (lucro líquido ou prejuízo)";
    public const string Outras = "Ajustes de exercícios anteriores e outras mutações";

    public static ColunaPl? Classificar(ContaPlano conta, IReadOnlyDictionary<long, ContaPlano> porId)
    {
        if (conta.CodNat != "03") return null;
        var textos = new List<string>();
        for (var c = conta; c is not null; c = c.ContaPaiId is long p && porId.TryGetValue(p, out var pai) ? pai : null)
            if (c.CodNat == "03") textos.Add(Normalizar(c.Descricao));
        bool Tem(string s) => textos.Any(t => t.Contains(s));
        bool Propria(string s) => textos[0].Contains(s);
        if (Tem("TESOURARIA")) return ColunaPl.AcoesTesouraria;
        if (Tem("AJUSTE") && Tem("AVALIAC")) return ColunaPl.AjustesAvaliacao;
        if (Propria("SUPERAVIT") || Propria("DEFICIT")) return ColunaPl.LucrosAcumulados;   // entidades sem fins lucrativos (ITG 2002)
        if (Propria("LUCRO") || Propria("PREJUIZO")) return Propria("RESERVA") ? ColunaPl.ReservasLucros : ColunaPl.LucrosAcumulados;
        if (Tem("RESERVA") && (Tem("RESERVA DE CAPITAL") || Tem("RESERVAS DE CAPITAL") || Propria("AGIO") || Propria("DOACAO")))
            return ColunaPl.ReservasCapital;
        if (Tem("RESERVA")) return ColunaPl.ReservasLucros;
        if (Tem("CAPITAL") || Tem("PATRIMONIO SOCIAL")) return ColunaPl.CapitalSocial;
        return Tem("LUCRO") || Tem("PREJUIZO") || Tem("SUPERAVIT") || Tem("DEFICIT") ? ColunaPl.LucrosAcumulados : ColunaPl.ReservasLucros;
    }

    /// <param name="saldosIniciais">Saldo (débito positivo) de cada conta analítica no início do período.</param>
    /// <param name="partidas">Partidas dos lançamentos do período (todas, de todas as contas).</param>
    /// <param name="lucroLiquido">Resultado do período pela DRE (lucro positivo).</param>
    public static DemonstracaoMutacoes Montar(DateOnly inicio, DateOnly fim, IReadOnlyList<ContaPlano> plano,
                                              IReadOnlyDictionary<long, decimal> saldosIniciais, IReadOnlyList<PartidaMutacao> partidas,
                                              decimal lucroLiquido)
    {
        var porId = plano.ToDictionary(c => c.Id);
        var colunaDe = plano.Where(c => c.Analitica).Select(c => (c.Id, Col: Classificar(c, porId)))
                            .Where(x => x.Col is not null).ToDictionary(x => x.Id, x => x.Col!.Value);
        Dictionary<ColunaPl, decimal> Novo() => Enum.GetValues<ColunaPl>().ToDictionary(c => c, _ => 0m);

        var inicial = Novo();
        foreach (var (conta, col) in colunaDe) inicial[col] -= saldosIniciais.GetValueOrDefault(conta);   // crédito aumenta o PL
        foreach (var p in partidas.Where(p => p.SaldoInicial && colunaDe.ContainsKey(p.ContaId)))
            inicial[colunaDe[p.ContaId]] += p.DC == IndicadorDC.C ? p.Valor : -p.Valor;

        var mutacoes = new Dictionary<string, Dictionary<ColunaPl, decimal>>();
        foreach (var lanc in partidas.Where(p => !p.Encerramento && !p.SaldoInicial).GroupBy(p => p.LancamentoId))
        {
            var pl = lanc.Where(p => colunaDe.ContainsKey(p.ContaId)).ToList();
            if (pl.Count == 0) continue;
            var efeito = Novo();
            foreach (var p in pl) efeito[colunaDe[p.ContaId]] += p.DC == IndicadorDC.C ? p.Valor : -p.Valor;
            var fora = lanc.Where(p => !colunaDe.ContainsKey(p.ContaId)).Select(p => porId.TryGetValue(p.ContaId, out var c) ? c.CodNat : null).ToList();
            var linha = Tipo(efeito, fora);
            if (!mutacoes.TryGetValue(linha, out var acumulado)) mutacoes[linha] = acumulado = Novo();
            foreach (var (c, v) in efeito) acumulado[c] += v;
        }

        var lucro = Novo();
        lucro[ColunaPl.LucrosAcumulados] = lucroLiquido;
        var final = Novo();
        foreach (var c in Enum.GetValues<ColunaPl>())
            final[c] = inicial[c] + mutacoes.Values.Sum(m => m[c]) + lucro[c];

        var ordem = new[] { Outras, AumentoCapital, CapitalComReservas, ReversaoReservas, LucroLiquido, ConstituicaoReservas, Dividendos };
        var dmpl = new List<LinhaMutacao> { new($"Saldo inicial em {inicio.AddDays(-1):dd/MM/yyyy}", inicial, Saldo: true) };
        // Linhas do modelo do CPC 26 que aparecem sempre (ajustes, aumento de capital, resultado, reservas e dividendos);
        // as demais (capital com reservas, reversões) só quando há movimento.
        var fixas = new[] { Outras, AumentoCapital, LucroLiquido, ConstituicaoReservas, Dividendos };
        foreach (var nome in ordem)
        {
            if (nome == LucroLiquido) dmpl.Add(new(LucroLiquido, lucro));
            else if (mutacoes.TryGetValue(nome, out var m) && (fixas.Contains(nome) || m.Values.Any(v => v != 0))) dmpl.Add(new(nome, m));
            else if (fixas.Contains(nome)) dmpl.Add(new(nome, Novo()));
        }
        dmpl.Add(new($"Saldo final em {fim:dd/MM/yyyy}", final, Saldo: true));

                // Pelo CPC 26 ficam sempre capital, reservas de capital, reservas de lucros, ajustes de avaliação e lucros acumulados.
        var colunas = DemonstracaoMutacoes.OrdemDmpl.Where(c => c != ColunaPl.AcoesTesouraria || dmpl.Any(l => l.Valor(c) != 0)).ToList();

        // DLPA: a coluna de lucros ou prejuízos acumulados, com as destinações em destaque.
        decimal Lpa(string nome) => mutacoes.TryGetValue(nome, out var m) ? m[ColunaPl.LucrosAcumulados] : 0;
        var dlpa = new List<LinhaDlpa>
        {
            new($"Saldo no início do período ({inicio:dd/MM/yyyy})", inicial[ColunaPl.LucrosAcumulados], true),
            new("(+/-) Ajustes de exercícios anteriores e outras mutações", Lpa(Outras)),
            new("(+) Reversão de reservas", Lpa(ReversaoReservas)),
            new("(+/-) Lucro líquido (prejuízo) do exercício", lucroLiquido),
            new("Saldo à disposição", inicial[ColunaPl.LucrosAcumulados] + Lpa(Outras) + Lpa(ReversaoReservas) + lucroLiquido, true),
            new("Destinações:", 0, true, SemValor: true),
            new("(-) Transferências para reservas", Lpa(ConstituicaoReservas)),
            new("(-) Dividendos e lucros distribuídos", Lpa(Dividendos)),
            new("(-) Incorporação ao capital", Lpa(CapitalComReservas) + Lpa(AumentoCapital)),
            new($"Saldo no fim do período ({fim:dd/MM/yyyy})", final[ColunaPl.LucrosAcumulados], true),
        };
        return new DemonstracaoMutacoes(inicio, fim, colunas, dmpl, dlpa);
    }

    /// <summary>Tipo da mutação pelo efeito nas colunas do PL e pela natureza das contrapartidas fora do PL.</summary>
    private static string Tipo(Dictionary<ColunaPl, decimal> efeito, IReadOnlyList<string?> naturezasFora)
    {
        bool Aumenta(ColunaPl c) => efeito[c] > 0;
        bool Diminui(ColunaPl c) => efeito[c] < 0;
        var reservas = new[] { ColunaPl.ReservasCapital, ColunaPl.ReservasLucros };
        if (naturezasFora.Count == 0)
        {
            if (Aumenta(ColunaPl.CapitalSocial)) return CapitalComReservas;
            if (reservas.Any(Aumenta) && Diminui(ColunaPl.LucrosAcumulados)) return ConstituicaoReservas;
            if (reservas.Any(Diminui) && Aumenta(ColunaPl.LucrosAcumulados)) return ReversaoReservas;
            return Outras;
        }
        if (Aumenta(ColunaPl.CapitalSocial) && naturezasFora.All(n => n == "01")) return AumentoCapital;
        if (Diminui(ColunaPl.LucrosAcumulados) && naturezasFora.All(n => n == "02")) return Dividendos;
        return Outras;
    }

    private static string Normalizar(string s)
    {
        var d = s.ToUpperInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(d.Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
    }
}
