using Vorticis.Domain.Contabil;

namespace Vorticis.Domain.Sped;

/// <summary>Saldo periódico de uma conta analítica/centro de custo (I155). Valores com sinal: débito positivo.</summary>
public sealed record SaldoPeriodicoEcd(long ContaId, long CentroCustoId, decimal Inicial, decimal Debitos, decimal Creditos, decimal Final);

public sealed record PeriodoEcd(DateOnly Inicio, DateOnly Fim, IReadOnlyList<SaldoPeriodicoEcd> Saldos);

/// <summary>Saldo de conta de resultado antes do encerramento (I355). Débito positivo.</summary>
public sealed record SaldoResultadoEcd(long ContaId, long CentroCustoId, decimal Saldo);

/// <summary>
/// Linha de demonstração do bloco J (J100/J150), montada a partir dos códigos de aglutinação. Valores com sinal (débito positivo).
/// <paramref name="Grupo"/>: J100 → A (ativo) ou P (passivo e PL); J150 → R (receita) ou D (despesa).
/// </summary>
public sealed record LinhaAglutinacaoEcd(string Codigo, bool Totalizador, int Nivel, string? CodigoSuperior, char Grupo, string Descricao,
                                         decimal Inicial, decimal Final, bool TemInicial = true);

/// <summary>Fato contábil que altera o PL (J215). Valor com sinal: crédito (aumento do PL) positivo.</summary>
public sealed record FatoPlEcd(string Codigo, string Descricao, decimal Valor);

/// <summary>J210 (DMPL/DLPA) de um código de aglutinação do PL. Saldos com sinal (débito positivo).</summary>
public sealed record MutacaoPlEcd(string Codigo, string Descricao, decimal Inicial, decimal Final, IReadOnlyList<FatoPlEcd> Fatos);

/// <summary>
/// Cálculos da ECD feitos uma vez a partir de <see cref="DadosEcd"/>: saldos periódicos mensais (I150/I155) a partir dos
/// lançamentos (assim os débitos e créditos de cada mês batem com as partidas I250 por construção), saldos das contas de
/// resultado antes do encerramento (I350/I355), plano reduzido às contas com saldo ou movimento (CTG 2001), códigos de
/// aglutinação (I052) e as linhas das demonstrações (J100, J150, J210/J215) — com as mesmas fórmulas de conferência do PVA.
/// </summary>
public sealed class CalculoEcd
{
    private readonly DadosEcd _d;
    private readonly Dictionary<long, ContaEcd> _porId;
    private readonly Dictionary<long, List<ContaEcd>> _filhas;

    public IReadOnlyList<PeriodoEcd> Periodos { get; }
    /// <summary>Saldo antes do período por conta/centro (débito positivo).</summary>
    public IReadOnlyDictionary<(long Conta, long Centro), decimal> Abertura { get; }
    /// <summary>Saldo no fim do período por conta/centro (débito positivo).</summary>
    public IReadOnlyDictionary<(long Conta, long Centro), decimal> Final { get; }
    /// <summary>Contas analíticas com saldo ou movimento no período (as que vão ao I050, com as sintéticas superiores).</summary>
    public IReadOnlySet<long> ContasAnaliticasUsadas { get; }
    /// <summary>Contas do I050, na ordem do plano (cada conta depois da superior).</summary>
    public IReadOnlyList<ContaEcd> PlanoUsado { get; }
    public IReadOnlySet<long> CentrosUsados { get; }
    /// <summary>Datas dos lançamentos de encerramento (I350) e, em cada uma, os saldos das contas de resultado antes dele (I355).</summary>
    public IReadOnlyList<(DateOnly Data, IReadOnlyList<SaldoResultadoEcd> Saldos)> Encerramentos { get; }
    /// <summary>Código de aglutinação (I052) de cada conta analítica de natureza 01 a 04 usada.</summary>
    public IReadOnlyDictionary<long, string> Aglutinacao { get; }
    public IReadOnlyList<LinhaAglutinacaoEcd> Balanco { get; }
    public IReadOnlyList<LinhaAglutinacaoEcd> Dre { get; }
    public IReadOnlyList<MutacaoPlEcd> MutacoesPl { get; }
    /// <summary>Tipo efetivamente usado no J210 (a DLPA cai para DMPL quando lucros/prejuízos acumulados não têm um único código).</summary>
    public TipoMutacoesEcd TipoMutacoes { get; }

    public CalculoEcd(DadosEcd dados)
    {
        _d = dados;
        _porId = dados.Plano.ToDictionary(c => c.Id);
        _filhas = dados.Plano.Where(c => c.ContaPaiId is not null).GroupBy(c => c.ContaPaiId!.Value)
                             .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Codigo, StringComparer.Ordinal).ToList());
        var p = dados.Parametros;

        var abertura = new Dictionary<(long, long), decimal>();
        foreach (var s in dados.SaldosAbertura.Where(s => s.Saldo != 0))
            abertura[(s.ContaId, s.CentroCustoId)] = abertura.GetValueOrDefault((s.ContaId, s.CentroCustoId)) + s.Saldo;
        Abertura = abertura;

        var lancamentos = dados.Lancamentos.Where(l => l.Data >= p.Inicio && l.Data <= p.Fim).ToList();

        // Saldos periódicos mensais
        var periodos = new List<PeriodoEcd>();
        var corrente = new Dictionary<(long, long), decimal>(abertura);
        var usadas = new HashSet<long>();
        var centros = new HashSet<long>();
        for (var mes = new DateOnly(p.Inicio.Year, p.Inicio.Month, 1); mes <= p.Fim; mes = mes.AddMonths(1))
        {
            var ini = mes < p.Inicio ? p.Inicio : mes;
            var fimMes = mes.AddMonths(1).AddDays(-1);
            var fim = fimMes > p.Fim ? p.Fim : fimMes;
            var mov = new Dictionary<(long, long), (decimal D, decimal C)>();
            foreach (var l in lancamentos.Where(l => l.Data >= ini && l.Data <= fim))
                foreach (var pt in l.Partidas)
                {
                    var k = (pt.ContaId, pt.CentroCustoId ?? 0);
                    var (dd, cc) = mov.GetValueOrDefault(k);
                    mov[k] = pt.DC == IndicadorDC.D ? (dd + pt.Valor, cc) : (dd, cc + pt.Valor);
                }
            var linhas = new List<SaldoPeriodicoEcd>();
            foreach (var k in corrente.Keys.Union(mov.Keys).Distinct().OrderBy(k => Codigo(k.Item1), StringComparer.Ordinal).ThenBy(k => k.Item2))
            {
                var inicial = corrente.GetValueOrDefault(k);
                var (deb, cred) = mov.GetValueOrDefault(k);
                var final = inicial + deb - cred;
                corrente[k] = final;
                if (inicial == 0 && deb == 0 && cred == 0 && final == 0) continue;
                linhas.Add(new SaldoPeriodicoEcd(k.Item1, k.Item2, inicial, deb, cred, final));
                usadas.Add(k.Item1);
                if (k.Item2 != 0) centros.Add(k.Item2);
            }
            periodos.Add(new PeriodoEcd(ini, fim, linhas));
        }
        Periodos = periodos;
        Final = corrente.Where(kv => kv.Value != 0).ToDictionary(kv => kv.Key, kv => kv.Value);
        ContasAnaliticasUsadas = usadas;
        CentrosUsados = centros;
        PlanoUsado = MontarPlanoUsado(usadas);

        // Saldos das contas de resultado antes de cada encerramento (I350/I355)
        var encerramentos = new List<(DateOnly, IReadOnlyList<SaldoResultadoEcd>)>();
        foreach (var data in lancamentos.Where(l => l.Indicador == IndicadorLancamento.Encerramento).Select(l => l.Data).Distinct().Order())
        {
            var saldo = new Dictionary<(long, long), decimal>();
            foreach (var (k, v) in abertura) if (Resultado(k.Item1)) saldo[k] = v;
            var comEncerramento = new HashSet<(long, long)>();
            foreach (var l in lancamentos.Where(l => l.Data <= data))
                foreach (var pt in l.Partidas.Where(pt => Resultado(pt.ContaId)))
                {
                    var k = (pt.ContaId, pt.CentroCustoId ?? 0);
                    if (l.Indicador == IndicadorLancamento.Encerramento && l.Data == data) { comEncerramento.Add(k); continue; }
                    saldo[k] = saldo.GetValueOrDefault(k) + pt.DC.ComSinal(pt.Valor);
                }
            var linhas = saldo.Keys.Union(comEncerramento).Distinct()
                .Where(k => saldo.GetValueOrDefault(k) != 0 || comEncerramento.Contains(k))
                .OrderBy(k => Codigo(k.Item1), StringComparer.Ordinal).ThenBy(k => k.Item2)
                .Select(k => new SaldoResultadoEcd(k.Item1, k.Item2, saldo.GetValueOrDefault(k))).ToList();
            encerramentos.Add((data, linhas));
        }
        Encerramentos = encerramentos;

        // Aglutinação e demonstrações
        var nivel = Math.Max((short)2, p.NivelAglutinacao);
        var agl = new Dictionary<long, string>();
        var noDe = new Dictionary<long, ContaEcd>();
        foreach (var id in usadas)
        {
            var c = _porId[id];
            if (c.CodNat is not ("01" or "02" or "03" or "04")) continue;
            var no = c;
            while (no.Nivel > nivel && no.ContaPaiId is long pai && _porId.TryGetValue(pai, out var sup)) no = sup;
            noDe[id] = no;
            agl[id] = no.Codigo;
        }
        Aglutinacao = agl;

        var movimentoNaoEncerramento = new Dictionary<long, decimal>();
        foreach (var l in lancamentos.Where(l => l.Indicador != IndicadorLancamento.Encerramento))
            foreach (var pt in l.Partidas)
                movimentoNaoEncerramento[pt.ContaId] = movimentoNaoEncerramento.GetValueOrDefault(pt.ContaId) + pt.DC.ComSinal(pt.Valor);

        decimal SomaAbertura(long conta) => abertura.Where(kv => kv.Key.Item1 == conta).Sum(kv => kv.Value);
        decimal SomaFinal(long conta) => corrente.Where(kv => kv.Key.Item1 == conta).Sum(kv => kv.Value);

        var contasBalanco = noDe.Where(kv => _porId[kv.Key].CodNat is "01" or "02" or "03").ToList();
        Balanco = MontarBalanco(contasBalanco, SomaAbertura, SomaFinal);

        var contasResultado = noDe.Where(kv => _porId[kv.Key].CodNat == "04").ToList();
        Dre = MontarDre(contasResultado, c => movimentoNaoEncerramento.GetValueOrDefault(c), p.ResultadoAnterior);

        var contasPl = noDe.Where(kv => _porId[kv.Key].CodNat == "03").ToList();
        (MutacoesPl, TipoMutacoes) = MontarMutacoes(contasPl, lancamentos, SomaAbertura, SomaFinal, p.Mutacoes);
    }

    public ContaEcd Conta(long id) => _porId[id];
    public string Codigo(long contaId) => _porId.TryGetValue(contaId, out var c) ? c.Codigo : contaId.ToString();
    private bool Resultado(long contaId) => _porId.TryGetValue(contaId, out var c) && c.CodNat == "04";

    private List<ContaEcd> MontarPlanoUsado(HashSet<long> usadas)
    {
        var incluir = new HashSet<long>();
        foreach (var id in usadas)
            for (long? atual = id; atual is long a && _porId.TryGetValue(a, out var c) && incluir.Add(a);) atual = c.ContaPaiId;
        var res = new List<ContaEcd>();
        void Visitar(ContaEcd c)
        {
            if (!incluir.Contains(c.Id)) return;
            res.Add(c);
            foreach (var f in _filhas.GetValueOrDefault(c.Id) ?? []) Visitar(f);
        }
        foreach (var raiz in _d.Plano.Where(c => c.ContaPaiId is null || !_porId.ContainsKey(c.ContaPaiId.Value))
                                     .OrderBy(c => c.Codigo, StringComparer.Ordinal))
            Visitar(raiz);
        return res;
    }

    /// <summary>Árvore de aglutinação: os nós de detalhe e as contas sintéticas acima deles, em pré-ordem.</summary>
    private sealed class No(string codigo, string descricao, ContaEcd? conta)
    {
        public string Codigo { get; } = codigo;
        public string Descricao { get; } = descricao;
        public ContaEcd? Conta { get; } = conta;
        public bool Detalhe { get; set; }
        public List<No> Filhos { get; } = [];
        public decimal Inicial { get; set; }
        public decimal Final { get; set; }
        public decimal? Anterior { get; set; }
    }

    private List<No> Arvore(IEnumerable<KeyValuePair<long, ContaEcd>> contas, Action<No, long> acumular)
    {
        var nos = new Dictionary<long, No>();
        No Obter(ContaEcd c) => nos.TryGetValue(c.Id, out var n) ? n : nos[c.Id] = new No(c.Codigo, c.Descricao, c);
        var raizes = new List<No>();
        foreach (var (analitica, detalhe) in contas)
        {
            var no = Obter(detalhe);
            no.Detalhe = true;
            acumular(no, analitica);
        }
        foreach (var no in nos.Values.ToList())
        {
            var atual = no;
            while (true)
            {
                if (atual.Conta?.ContaPaiId is long pai && _porId.TryGetValue(pai, out var sup))
                {
                    var existia = nos.ContainsKey(sup.Id);
                    var n = Obter(sup);
                    if (!n.Filhos.Contains(atual)) n.Filhos.Add(atual);
                    if (existia) break;
                    atual = n;
                }
                else
                {
                    if (!raizes.Contains(atual)) raizes.Add(atual);
                    break;
                }
            }
        }
        void Ordenar(No n)
        {
            n.Filhos.Sort((a, b) => string.CompareOrdinal(a.Codigo, b.Codigo));
            n.Filhos.ForEach(Ordenar);
        }
        raizes.Sort((a, b) => string.CompareOrdinal(a.Codigo, b.Codigo));
        raizes.ForEach(Ordenar);
        // Totalizadores: soma dos filhos (os detalhes já acumularam as contas analíticas).
        void Somar(No n)
        {
            if (n.Detalhe) return;
            n.Filhos.ForEach(Somar);
            n.Inicial = n.Filhos.Sum(f => f.Inicial);
            n.Final = n.Filhos.Sum(f => f.Final);
            n.Anterior = n.Filhos.Any(f => f.Anterior is not null) ? n.Filhos.Sum(f => f.Anterior ?? 0) : null;
        }
        raizes.ForEach(Somar);
        return raizes;
    }

    private string CodigoLivre(string preferido)
    {
        var usados = _d.Plano.Select(c => c.Codigo).ToHashSet();
        var codigo = preferido;
        for (int i = 2; usados.Contains(codigo); i++) codigo = $"{preferido}{i}";
        return codigo;
    }

    private No Agrupar(List<No> raizes, string codigo, string descricao)
    {
        if (raizes.Count == 1 && !raizes[0].Detalhe) return raizes[0];
        var raiz = new No(CodigoLivre(codigo), descricao, null);
        raiz.Filhos.AddRange(raizes);
        raiz.Inicial = raizes.Sum(r => r.Inicial);
        raiz.Final = raizes.Sum(r => r.Final);
        raiz.Anterior = raizes.Any(r => r.Anterior is not null) ? raizes.Sum(r => r.Anterior ?? 0) : null;
        return raiz;
    }

    private IReadOnlyList<LinhaAglutinacaoEcd> MontarBalanco(List<KeyValuePair<long, ContaEcd>> contas, Func<long, decimal> abertura,
                                                             Func<long, decimal> final)
    {
        var raizes = Arvore(contas, (no, conta) => { no.Inicial += abertura(conta); no.Final += final(conta); });
        var ativo = Agrupar(raizes.Where(r => GrupoBalanco(r) == 'A').ToList(), "A", "ATIVO");
        var passivo = Agrupar(raizes.Where(r => GrupoBalanco(r) == 'P').ToList(), "P", "PASSIVO E PATRIMÔNIO LÍQUIDO");
        var linhas = new List<LinhaAglutinacaoEcd>();
        void Emitir(No n, int nivel, string? superior, char grupo)
        {
            linhas.Add(new LinhaAglutinacaoEcd(n.Codigo, !n.Detalhe, nivel, superior, grupo, n.Descricao, n.Inicial, n.Final));
            foreach (var f in n.Filhos) Emitir(f, nivel + 1, n.Codigo, grupo);
        }
        Emitir(ativo, 1, null, 'A');
        Emitir(passivo, 1, null, 'P');
        return linhas;
    }

    private static char GrupoBalanco(No raiz) => raiz.Conta?.CodNat == "01" ? 'A' : 'P';

    private IReadOnlyList<LinhaAglutinacaoEcd> MontarDre(List<KeyValuePair<long, ContaEcd>> contas, Func<long, decimal> resultado,
                                                         IReadOnlyDictionary<long, decimal>? anterior)
    {
        var raizes = Arvore(contas, (no, conta) =>
        {
            no.Final += resultado(conta);
            if (anterior is not null) no.Anterior = (no.Anterior ?? 0) + anterior.GetValueOrDefault(conta);
        });
        var raiz = Agrupar(raizes, "RES", "RESULTADO LÍQUIDO DO EXERCÍCIO");
        var linhas = new List<LinhaAglutinacaoEcd>();
        void Emitir(No n, int nivel, string? superior)
        {
            var grupo = n.Conta is { } c ? (c.Natureza == IndicadorDC.C ? 'R' : 'D') : (n.Final <= 0 ? 'R' : 'D');
            linhas.Add(new LinhaAglutinacaoEcd(n.Codigo, !n.Detalhe, nivel, superior, grupo, n.Descricao, n.Anterior ?? 0, n.Final,
                                               n.Anterior is not null));
            foreach (var f in n.Filhos) Emitir(f, nivel + 1, n.Codigo);
        }
        if (contas.Count > 0) Emitir(raiz, 1, null);
        return linhas;
    }

    private (IReadOnlyList<MutacaoPlEcd>, TipoMutacoesEcd) MontarMutacoes(List<KeyValuePair<long, ContaEcd>> contasPl, List<LancamentoEcd> lancamentos,
                                                                          Func<long, decimal> abertura, Func<long, decimal> final, TipoMutacoesEcd tipo)
    {
        if (tipo == TipoMutacoesEcd.Nenhuma || contasPl.Count == 0) return ([], TipoMutacoesEcd.Nenhuma);
        var porNo = contasPl.GroupBy(kv => kv.Value.Codigo).ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToHashSet());
        var descricao = contasPl.GroupBy(kv => kv.Value.Codigo).ToDictionary(g => g.Key, g => g.First().Value.Descricao);

        IEnumerable<string> codigos = porNo.Keys.OrderBy(c => c, StringComparer.Ordinal);
        if (tipo == TipoMutacoesEcd.Dlpa)
        {
            var plano = _d.Plano.Select(c => new ContaPlano(c.Id, c.Codigo, c.Descricao, c.Nivel, c.Analitica, c.ContaPaiId, c.Natureza, c.CodNat)).ToList();
            var porId = plano.ToDictionary(c => c.Id);
            var lucros = contasPl.Where(kv => Mutacoes.Classificar(porId[kv.Key], porId) == ColunaPl.LucrosAcumulados)
                                 .Select(kv => kv.Value.Codigo).Distinct().ToList();
            if (lucros.Count == 1) codigos = lucros;
            else tipo = TipoMutacoesEcd.Dmpl;
        }

        var todasPl = contasPl.Select(kv => kv.Key).ToHashSet();
        var res = new List<MutacaoPlEcd>();
        foreach (var codigo in codigos)
        {
            var contas = porNo[codigo];
            var fatos = new SortedDictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var l in lancamentos)
            {
                var efeito = l.Partidas.Where(pt => contas.Contains(pt.ContaId)).Sum(pt => pt.DC == IndicadorDC.C ? pt.Valor : -pt.Valor);
                if (efeito == 0) continue;
                var fora = l.Partidas.Where(pt => !todasPl.Contains(pt.ContaId))
                                     .Select(pt => _porId.TryGetValue(pt.ContaId, out var c) ? c.CodNat : "").ToList();
                var categoria = l.Indicador == IndicadorLancamento.Encerramento ? "1"
                    : l.SaldoInicial ? "2"
                    : fora.Count == 0 ? "5"
                    : efeito > 0 && fora.All(n => n == "01") ? "3"
                    : efeito < 0 && fora.All(n => n == "02") ? "4"
                    : "6";
                fatos[categoria] = fatos.GetValueOrDefault(categoria) + efeito;
            }
            var ini = contas.Sum(abertura);
            var fin = contas.Sum(final);
            res.Add(new MutacaoPlEcd(codigo, descricao[codigo], ini, fin,
                fatos.Where(f => f.Value != 0).Select(f => new FatoPlEcd(f.Key, DescricaoFato(f.Key), f.Value)).ToList()));
        }
        return (res, tipo);
    }

    public static string DescricaoFato(string categoria) => categoria switch
    {
        "1" => "RESULTADO DO EXERCÍCIO (LUCRO OU PREJUÍZO) TRANSFERIDO AO PATRIMÔNIO LÍQUIDO",
        "2" => "SALDOS DE IMPLANTAÇÃO DA ESCRITURAÇÃO",
        "3" => "AUMENTO DE CAPITAL (INTEGRALIZAÇÃO)",
        "4" => "DIVIDENDOS E LUCROS DISTRIBUÍDOS",
        "5" => "TRANSFERÊNCIAS ENTRE CONTAS DO PATRIMÔNIO LÍQUIDO",
        _ => "OUTRAS MUTAÇÕES DO PATRIMÔNIO LÍQUIDO",
    };
}
