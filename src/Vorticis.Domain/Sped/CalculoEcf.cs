using Vorticis.Domain.Contabil;

namespace Vorticis.Domain.Sped;

/// <summary>Período de apuração trimestral (K030/L030/M030/N030/P030): <see cref="Codigo"/> = T01 a T04.</summary>
public sealed record PeriodoEcf(int Trimestre, string Codigo, DateOnly Inicio, DateOnly Fim)
{
    public int Meses => (Fim.Year - Inicio.Year) * 12 + Fim.Month - Inicio.Month + 1;
}

/// <summary>Saldo de uma conta patrimonial no período (K155). Valores com sinal: débito positivo; débitos e créditos sem sinal.</summary>
public sealed record SaldoPatrimonialEcf(ContaEcd Conta, decimal Inicial, decimal Debitos, decimal Creditos, decimal Final);

/// <summary>Resultado de uma conta de resultado no período, sem os lançamentos de encerramento (K355). Débito positivo.</summary>
public sealed record SaldoResultadoEcf(ContaEcd Conta, decimal Saldo);

/// <summary>Linha do balanço referencial (L100/P100) ou da DRE referencial (L300/P150). Na DRE só <see cref="Final"/> é usado (resultado do período).</summary>
public sealed record LinhaReferencialEcf(ReferencialEcf Conta, decimal Inicial, decimal Debitos, decimal Creditos, decimal Final);

/// <summary>Linha de uma tabela dinâmica de cálculo (N500, N630, N650, N670, P200, P300, P400, P500). <see cref="Valor"/> com sinal.</summary>
public sealed record LinhaTabelaEcf(string Codigo, string Descricao, decimal Valor);

public sealed record ContaRelacionadaEcf(string CodConta, decimal Valor, char DC);
public sealed record ContaBRelacionadaEcf(string CodContaB, decimal Valor, char DC);

/// <summary>Lançamento da Parte A (M300/M350). <paramref name="Tipo"/>: A adição, E exclusão, P compensação de prejuízo, L lucro; <paramref name="Relacao"/>: IND_RELACAO.</summary>
public sealed record LancamentoParteAEcf(string Codigo, string Descricao, char? Tipo, int? Relacao, decimal Valor, string? Historico,
                                         IReadOnlyList<ContaRelacionadaEcf> Contas, IReadOnlyList<ContaBRelacionadaEcf> ContasB);

/// <summary>Conta da Parte B (M010). <paramref name="Tributo"/>: I (IRPJ) ou C (CSLL). <paramref name="SaldoInicial"/> com a convenção "débito positivo": prejuízo/base negativa a compensar é devedor.</summary>
public sealed record ContaParteBEcf(string Codigo, string Descricao, string CodPbRfb, char Tributo, DateOnly Criacao, decimal SaldoInicial);

/// <summary>Lançamento na Parte B sem reflexo na Parte A (M410): prejuízo do período (PF) ou base de cálculo negativa da CSLL (BC).</summary>
public sealed record LancamentoParteBEcf(string CodContaB, char Tributo, decimal Valor, string Indicador, string Historico);

/// <summary>Controle de saldos da conta da Parte B no período (M500). Valores com sinal, débito positivo.</summary>
public sealed record ControleSaldoEcf(string CodContaB, char Tributo, decimal Inicial, decimal ParteA, decimal ParteB, decimal Final);

/// <summary>Cálculo de um trimestre no Lucro Real (blocos M e N).</summary>
public sealed class ApuracaoRealEcf
{
    public required PeriodoEcf Periodo { get; init; }
    public required IReadOnlyList<LancamentoParteAEcf> M300 { get; init; }
    public required IReadOnlyList<LancamentoParteAEcf> M350 { get; init; }
    public required IReadOnlyList<LancamentoParteBEcf> M410 { get; init; }
    public required IReadOnlyList<ControleSaldoEcf> M500 { get; init; }
    public required IReadOnlyList<LinhaTabelaEcf> N500 { get; init; }
    public required IReadOnlyList<LinhaTabelaEcf> N630 { get; init; }
    public required IReadOnlyList<LinhaTabelaEcf> N650 { get; init; }
    public required IReadOnlyList<LinhaTabelaEcf> N670 { get; init; }
    /// <summary>Lucro líquido antes do IRPJ (M300/2): resultado da DRE referencial menos a provisão da CSLL.</summary>
    public decimal LucroAntesIrpj { get; init; }
    public decimal LucroReal { get; init; }
    public decimal BaseCsll { get; init; }
    public decimal IrpjDevido { get; init; }
    public decimal CsllDevida { get; init; }
    public decimal LucroAntesCsll { get; init; }
}

/// <summary>Cálculo de um trimestre no Lucro Presumido (bloco P).</summary>
public sealed class ApuracaoPresumidoEcf
{
    public required PeriodoEcf Periodo { get; init; }
    public required IReadOnlyList<LinhaTabelaEcf> P200 { get; init; }
    public required IReadOnlyList<LinhaTabelaEcf> P300 { get; init; }
    public required IReadOnlyList<LinhaTabelaEcf> P400 { get; init; }
    public required IReadOnlyList<LinhaTabelaEcf> P500 { get; init; }
    public decimal BaseIrpj { get; init; }
    public decimal BaseCsll { get; init; }
    public decimal IrpjDevido { get; init; }
    public decimal CsllDevida { get; init; }
}

/// <summary>
/// Cálculos da ECF feitos uma vez a partir de <see cref="DadosEcf"/>: períodos trimestrais, saldos das contas por período (K155/K355),
/// balanço e DRE referenciais (L100/L300 ou P100/P150) e as tabelas dinâmicas de apuração do Lucro Real (M300/M350/M410/M500,
/// N500/N630/N650/N670) ou do Lucro Presumido (P200/P300/P400/P500), com as fórmulas das Tabelas Dinâmicas do leiaute 12.
/// </summary>
public sealed class CalculoEcf
{
    public const string ContaPrejuizo = "PF_IRPJ";
    public const string ContaBaseNegativa = "BC_CSLL";
    public const string RefProvisaoCsll = "3.02.01.01.01.01";
    public const string RefResultadoAntesTributos = "3.01";
    private const decimal Aliquota = 0.15m, Adicional = 0.10m, LimiteMensal = 20000m, AliquotaCsll = 0.09m;

    private readonly DadosEcf _d;
    private readonly Dictionary<long, ContaEcd> _contas;
    private readonly Dictionary<string, ReferencialEcf> _ref;

    public IReadOnlyList<PeriodoEcf> Periodos { get; }
    /// <summary>Contas com saldo ou movimento (balanço ou resultado) em algum período, com os registros K155/K355 de cada período.</summary>
    public IReadOnlyList<IReadOnlyList<SaldoPatrimonialEcf>> Patrimoniais { get; }
    public IReadOnlyList<IReadOnlyList<SaldoResultadoEcf>> Resultados { get; }
    public IReadOnlyList<IReadOnlyList<LinhaReferencialEcf>> Balanco { get; }
    public IReadOnlyList<IReadOnlyList<LinhaReferencialEcf>> Dre { get; }
    /// <summary>Contas do J050, na ordem do plano (cada conta depois da superior).</summary>
    public IReadOnlyList<ContaEcd> PlanoUsado { get; }
    public IReadOnlyList<ContaEcd> SemReferencial { get; }
    public IReadOnlyList<string> ReferenciaInexistente { get; }
    public IReadOnlyList<ApuracaoRealEcf> Real { get; }
    public IReadOnlyList<ApuracaoPresumidoEcf> Presumido { get; }
    public IReadOnlyList<ContaParteBEcf> ContasParteB { get; }
    /// <summary>Compensação de prejuízo/base negativa que excede o saldo disponível na Parte B (código da conta, trimestre, excesso).</summary>
    public IReadOnlyList<(string Conta, int Trimestre, decimal Excesso)> CompensacoesSemSaldo { get; }
    /// <summary>Total do ativo no fim do período e receita bruta do ano (Y672).</summary>
    public decimal TotalAtivoFinal { get; }

    public CalculoEcf(DadosEcf d)
    {
        _d = d;
        _contas = d.Plano.ToDictionary(c => c.Id);
        _ref = d.Referencial.GroupBy(r => r.Codigo).ToDictionary(g => g.Key, g => g.First());
        var p = d.Parametros;

        var periodos = new List<PeriodoEcf>();
        for (var t = 1; t <= 4; t++)
        {
            var ini = new DateOnly(p.Ano, t * 3 - 2, 1);
            var fim = ini.AddMonths(3).AddDays(-1);
            if (ini < p.Inicio) ini = p.Inicio;
            if (fim > p.Fim) fim = p.Fim;
            if (ini <= fim) periodos.Add(new PeriodoEcf(t, $"T{t:00}", ini, fim));
        }
        Periodos = periodos;

        // Saldos por conta e período (centros de custo somados: o mapeamento referencial é por conta).
        var corrente = new Dictionary<long, decimal>();
        foreach (var s in d.SaldosAbertura.Where(s => s.Saldo != 0)) corrente[s.ContaId] = corrente.GetValueOrDefault(s.ContaId) + s.Saldo;
        var patrimoniais = new List<IReadOnlyList<SaldoPatrimonialEcf>>();
        var resultados = new List<IReadOnlyList<SaldoResultadoEcf>>();
        var usadas = new HashSet<long>();
        foreach (var per in periodos)
        {
            var deb = new Dictionary<long, decimal>();
            var cred = new Dictionary<long, decimal>();
            var resultado = new Dictionary<long, decimal>();
            foreach (var l in d.Lancamentos.Where(l => l.Data >= per.Inicio && l.Data <= per.Fim))
                foreach (var pt in l.Partidas)
                {
                    if (pt.DC == IndicadorDC.D) deb[pt.ContaId] = deb.GetValueOrDefault(pt.ContaId) + pt.Valor;
                    else cred[pt.ContaId] = cred.GetValueOrDefault(pt.ContaId) + pt.Valor;
                    if (l.Indicador != IndicadorLancamento.Encerramento)
                        resultado[pt.ContaId] = resultado.GetValueOrDefault(pt.ContaId) + pt.DC.ComSinal(pt.Valor);
                }
            var pat = new List<SaldoPatrimonialEcf>();
            foreach (var id in corrente.Keys.Union(deb.Keys).Union(cred.Keys).Distinct()
                                    .OrderBy(id => Codigo(id), StringComparer.Ordinal))
            {
                if (!_contas.TryGetValue(id, out var conta)) continue;
                var ini = corrente.GetValueOrDefault(id);
                var dd = deb.GetValueOrDefault(id);
                var cc = cred.GetValueOrDefault(id);
                var fin = ini + dd - cc;
                corrente[id] = fin;
                if (conta.CodNat is "01" or "02" or "03")
                {
                    if (ini == 0 && dd == 0 && cc == 0 && fin == 0) continue;
                    pat.Add(new SaldoPatrimonialEcf(conta, ini, dd, cc, fin));
                    usadas.Add(id);
                }
            }
            var res = resultado.Where(kv => kv.Value != 0 && _contas.TryGetValue(kv.Key, out var c) && c.CodNat == "04")
                               .OrderBy(kv => Codigo(kv.Key), StringComparer.Ordinal)
                               .Select(kv => new SaldoResultadoEcf(_contas[kv.Key], kv.Value)).ToList();
            foreach (var r in res) usadas.Add(r.Conta.Id);
            patrimoniais.Add(pat);
            resultados.Add(res);
        }
        Patrimoniais = patrimoniais;
        Resultados = resultados;
        foreach (var l in d.Lalur.Where(l => l.ContaId is not null)) if (_contas.ContainsKey(l.ContaId!.Value)) usadas.Add(l.ContaId!.Value);
        PlanoUsado = MontarPlanoUsado(usadas);
        SemReferencial = usadas.Select(id => _contas[id]).Where(c => c.CodigoReferencial is null && c.CodNat is "01" or "02" or "03" or "04")
                               .OrderBy(c => c.Codigo, StringComparer.Ordinal).ToList();
        ReferenciaInexistente = usadas.Select(id => _contas[id].CodigoReferencial).Where(c => c is not null && !_ref.ContainsKey(c)).Distinct().Cast<string>()
                                      .OrderBy(c => c, StringComparer.Ordinal).ToList();

        Balanco = patrimoniais.Select((pat, i) => Rolagem(pat.Where(s => s.Conta.CodigoReferencial is not null)
            .Select(s => (s.Conta.CodigoReferencial!, s.Inicial, s.Debitos, s.Creditos, s.Final)))).ToList();
        Dre = resultados.Select(res => Rolagem(res.Where(s => s.Conta.CodigoReferencial is not null)
            .Select(s => (s.Conta.CodigoReferencial!, 0m, 0m, 0m, s.Saldo)))).ToList();
        TotalAtivoFinal = patrimoniais.Count == 0 ? 0 : patrimoniais[^1].Where(s => s.Conta.CodNat == "01").Sum(s => s.Final);

        var compSemSaldo = new List<(string, int, decimal)>();
        var contasB = new List<ContaParteBEcf>();
        var real = new List<ApuracaoRealEcf>();
        var presumido = new List<ApuracaoPresumidoEcf>();
        if (p.Forma == FormaTributacaoEcf.Real) CalcularReal(real, contasB, compSemSaldo);
        else CalcularPresumido(presumido);
        Real = real;
        Presumido = presumido;
        ContasParteB = contasB;
        CompensacoesSemSaldo = compSemSaldo;
    }

    public ContaEcd Conta(long id) => _contas[id];
    public string Codigo(long id) => _contas.TryGetValue(id, out var c) ? c.Codigo : id.ToString();
    private static decimal R(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private List<ContaEcd> MontarPlanoUsado(HashSet<long> usadas)
    {
        var incluir = new HashSet<long>();
        foreach (var id in usadas)
            for (long? atual = id; atual is long a && _contas.TryGetValue(a, out var c) && incluir.Add(a);) atual = c.ContaPaiId;
        var filhas = _d.Plano.Where(c => c.ContaPaiId is not null && incluir.Contains(c.Id)).GroupBy(c => c.ContaPaiId!.Value)
                            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Codigo, StringComparer.Ordinal).ToList());
        var res = new List<ContaEcd>();
        void Visitar(ContaEcd c)
        {
            if (!incluir.Contains(c.Id)) return;
            res.Add(c);
            foreach (var f in filhas.GetValueOrDefault(c.Id) ?? []) Visitar(f);
        }
        foreach (var raiz in _d.Plano.Where(c => incluir.Contains(c.Id) && (c.ContaPaiId is null || !_contas.ContainsKey(c.ContaPaiId.Value)))
                                     .OrderBy(c => c.Codigo, StringComparer.Ordinal))
            Visitar(raiz);
        return res;
    }

    /// <summary>Soma os valores das contas analíticas mapeadas em cada conta referencial e nas superiores, em pré-ordem (L100/L300/P100/P150).</summary>
    private List<LinhaReferencialEcf> Rolagem(IEnumerable<(string Codigo, decimal Ini, decimal Deb, decimal Cred, decimal Fin)> itens)
    {
        var acc = new Dictionary<string, (decimal Ini, decimal Deb, decimal Cred, decimal Fin)>();
        foreach (var (cod, ini, deb, cred, fin) in itens)
            for (string? atual = cod; atual is not null && _ref.TryGetValue(atual, out var r); atual = r.CodigoPai)
            {
                var v = acc.GetValueOrDefault(atual);
                acc[atual] = (v.Ini + ini, v.Deb + deb, v.Cred + cred, v.Fin + fin);
                if (r.CodigoPai == atual) break;
            }
        var filhos = acc.Keys.Select(k => _ref[k]).Where(r => r.CodigoPai is not null && acc.ContainsKey(r.CodigoPai))
                        .GroupBy(r => r.CodigoPai!).ToDictionary(g => g.Key, g => g.OrderBy(r => r.Codigo, StringComparer.Ordinal).ToList());
        var res = new List<LinhaReferencialEcf>();
        void Visitar(ReferencialEcf r)
        {
            var v = acc[r.Codigo];
            res.Add(new LinhaReferencialEcf(r, v.Ini, v.Deb, v.Cred, v.Fin));
            foreach (var f in filhos.GetValueOrDefault(r.Codigo) ?? []) Visitar(f);
        }
        foreach (var raiz in acc.Keys.Select(k => _ref[k]).Where(r => r.CodigoPai is null || !acc.ContainsKey(r.CodigoPai)).OrderBy(r => r.Codigo, StringComparer.Ordinal))
            Visitar(raiz);
        return res;
    }

    // ------------------------------------------------------------------------------------------------ Lucro Real

    private ContaEcd? PlanoPorId(long? id) => id is long i && _contas.TryGetValue(i, out var c) ? c : null;

    private static string Historico(IEnumerable<string> textos) =>
        FormatoSped.Texto(string.Join("; ", textos.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct()), 500);

    private IReadOnlyList<LancamentoParteAEcf> ParteA(IEnumerable<LancamentoLalurEcf> lancamentos, IReadOnlyDictionary<string, (string Descricao, bool Adicao)> tabela,
                                                      char tipoAdicao = 'A')
    {
        var res = new List<LancamentoParteAEcf>();
        foreach (var g in lancamentos.Where(l => l.CodigoEcf is not null).GroupBy(l => (l.CodigoEcf!, l.Adicao)).OrderBy(g => g.Key.Item2 ? 0 : 1)
                                     .ThenBy(g => decimal.TryParse(g.Key.Item1, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0))
        {
            var (codigo, adicao) = g.Key;
            var todasComConta = g.All(l => PlanoPorId(l.ContaId) is not null);
            var contas = todasComConta
                ? g.GroupBy(l => l.ContaId!.Value).Select(x =>
                {
                    var conta = _contas[x.Key];
                    var resultado = conta.CodNat == "04";
                    // Adição: resultado a débito / patrimonial a crédito; exclusão: o inverso (conversão de sinais do M300).
                    var dc = adicao == resultado ? 'D' : 'C';
                    return new ContaRelacionadaEcf(conta.Codigo, R(x.Sum(l => l.Valor)), dc);
                }).ToList()
                : [];
            res.Add(new LancamentoParteAEcf(codigo, tabela.TryGetValue(codigo, out var t) ? t.Descricao : codigo, adicao ? tipoAdicao : 'E',
                todasComConta ? 2 : 4, R(g.Sum(l => l.Valor)), Historico(g.Select(l => l.Descricao)), contas, []));
        }
        return res;
    }

    private void CalcularReal(List<ApuracaoRealEcf> real, List<ContaParteBEcf> contasB, List<(string, int, decimal)> semSaldo)
    {
        var p = _d.Parametros;
        // Saldo inicial da Parte B em 1º de janeiro: saldo informado + movimentos dos anos anteriores (prejuízo + , compensação −).
        decimal SaldoInicial(string tributo) => _d.ParteB.Where(m => m.Tributo == tributo && (m.Tipo == "SALDO_INICIAL" || (m.Ano is int a && a < p.Ano))).Sum(m => m.Valor);
        var saldoIrpj = SaldoInicial("IRPJ");
        var saldoCsll = SaldoInicial("CSLL");
        var saldoIniIrpj = saldoIrpj;
        var saldoIniCsll = saldoCsll;
        var movimentou = new[] { false, false };
        var primeiroTrimestre = new int?[2];

        for (var i = 0; i < Periodos.Count; i++)
        {
            var per = Periodos[i];
            var ap = _d.Apuracoes.FirstOrDefault(a => a.Trimestre == per.Trimestre && a.Regime == FormaTributacaoEcf.Real);
            var dre = Dre[i].ToDictionary(l => l.Conta.Codigo, l => l.Final);
            var vAntes = dre.GetValueOrDefault(RefResultadoAntesTributos);
            var vCsll = dre.GetValueOrDefault(RefProvisaoCsll);
            var lancamentos = _d.Lalur.Where(l => l.Trimestre == per.Trimestre).ToList();

            // ---------------- M300 (e-Lalur Parte A)
            var l2 = R(-(vAntes + vCsll));
            var l9 = vCsll > 0 ? R(vCsll) : 0m;
            var m300 = ParteA(lancamentos.Where(l => l.Irpj), TabelasEcfLalur.M300A).ToList();
            var l93 = R(m300.Where(x => x.Tipo == 'A').Sum(x => x.Valor) + l9);
            var l168 = R(m300.Where(x => x.Tipo == 'E').Sum(x => x.Valor));
            var l169 = R(l2 + l93 - l168);
            var l171 = l169;
            var compIrpj = R(Math.Min(ap?.CompensacaoIrpj ?? 0, Math.Max(0, l171)));
            var disponivelIrpj = saldoIrpj;
            if (compIrpj > disponivelIrpj) { semSaldo.Add((ContaPrejuizo, per.Trimestre, compIrpj - Math.Max(0, disponivelIrpj))); compIrpj = Math.Max(0, disponivelIrpj); }
            var l175 = R(l171 - compIrpj);
            var linhas300 = new List<LancamentoParteAEcf> { new("2", "Lucro Líquido Antes do IRPJ", null, null, l2, null, [], []) };
            if (l9 > 0) linhas300.Add(new("9", "Contribuição Social Sobre o Lucro Líquido - CSLL", 'A', null, l9, null, [], []));
            linhas300.AddRange(m300.Where(x => x.Tipo == 'A'));
            linhas300.Add(new("93", "SOMA DAS ADIÇÕES (IRPJ)", null, null, l93, null, [], []));
            linhas300.AddRange(m300.Where(x => x.Tipo == 'E'));
            linhas300.Add(new("168", "SOMA DAS EXCLUSÕES (IRPJ)", null, null, l168, null, [], []));
            linhas300.Add(new("169", "LUCRO REAL ANTES DA COMPENSAÇÃO DE PREJUÍZOS DO PRÓPRIO PERÍODO DE APURAÇÃO", null, null, l169, null, [], []));
            linhas300.Add(new("171", "LUCRO REAL APÓS A COMPENSAÇÃO DOS PREJUÍZOS DO PRÓPRIO PERÍODO DE APURAÇÃO", null, null, l171, null, [], []));
            if (compIrpj > 0)
                linhas300.Add(new("173", "(-) Compensação de Prejuízos Fiscais de Períodos Anteriores - Atividades em Geral", 'P', 1, compIrpj,
                    "Compensação de prejuízo fiscal de períodos anteriores", [], [new(ContaPrejuizo, compIrpj, 'C')]));
            linhas300.Add(new("175", "LUCRO REAL", null, null, l175, null, [], []));

            // ---------------- M350 (e-Lacs Parte A)
            var c2 = R(-vAntes);
            var m350 = ParteA(lancamentos.Where(l => l.Csll), TabelasEcfLalur.M350A).ToList();
            var c93 = R(m350.Where(x => x.Tipo == 'A').Sum(x => x.Valor));
            var c168 = R(m350.Where(x => x.Tipo == 'E').Sum(x => x.Valor));
            var c169 = R(c2 + c93 - c168);
            var compCsll = R(Math.Min(ap?.CompensacaoCsll ?? 0, Math.Max(0, c169)));
            if (compCsll > saldoCsll) { semSaldo.Add((ContaBaseNegativa, per.Trimestre, compCsll - Math.Max(0, saldoCsll))); compCsll = Math.Max(0, saldoCsll); }
            var c175 = R(c169 - compCsll);
            var linhas350 = new List<LancamentoParteAEcf> { new("2", "Lucro Antes da CSLL", null, null, c2, null, [], []) };
            linhas350.AddRange(m350.Where(x => x.Tipo == 'A'));
            linhas350.Add(new("93", "SOMA DAS ADIÇÕES (CSLL)", null, null, c93, null, [], []));
            linhas350.AddRange(m350.Where(x => x.Tipo == 'E'));
            linhas350.Add(new("168", "SOMA DAS EXCLUSÕES (CSLL)", null, null, c168, null, [], []));
            linhas350.Add(new("169", "BASE DE CÁLCULO ANTES DA COMPENSAÇÃO DE BC NEGATIVA DO PRÓPRIO PERÍODO DE APURAÇÃO", null, null, c169, null, [], []));
            linhas350.Add(new("171", "BASE DE CÁLCULO ANTES DA COMPENSAÇÃO DE BC NEGATIVA DE PERÍODOS ANTERIORES", null, null, c169, null, [], []));
            if (compCsll > 0)
                linhas350.Add(new("173", "(-) Compensação de Base de Cálculo Negativa da CSLL de Períodos Anteriores - Atividades em Geral", 'P', 1, compCsll,
                    "Compensação de base de cálculo negativa de períodos anteriores", [], [new(ContaBaseNegativa, compCsll, 'C')]));
            linhas350.Add(new("175", "BASE DE CÁLCULO DA CSLL", null, null, c175, null, [], []));

            // ---------------- Parte B: prejuízo do período (M410) e controle de saldos (M500)
            var m410 = new List<LancamentoParteBEcf>();
            var prejIrpj = l175 < 0 ? -l175 : 0m;
            var prejCsll = c175 < 0 ? -c175 : 0m;
            if (prejIrpj > 0) m410.Add(new(ContaPrejuizo, 'I', prejIrpj, "PF", "Prejuízo fiscal apurado no período"));
            if (prejCsll > 0) m410.Add(new(ContaBaseNegativa, 'C', prejCsll, "BC", "Base de cálculo negativa da CSLL apurada no período"));
            var ctl1 = new ControleSaldoEcf(ContaPrejuizo, 'I', saldoIrpj, -compIrpj, prejIrpj, saldoIrpj - compIrpj + prejIrpj);
            var ctl2 = new ControleSaldoEcf(ContaBaseNegativa, 'C', saldoCsll, -compCsll, prejCsll, saldoCsll - compCsll + prejCsll);
            if (compIrpj > 0 || prejIrpj > 0) { movimentou[0] = true; primeiroTrimestre[0] ??= per.Trimestre; }
            if (compCsll > 0 || prejCsll > 0) { movimentou[1] = true; primeiroTrimestre[1] ??= per.Trimestre; }
            saldoIrpj = ctl1.Final;
            saldoCsll = ctl2.Final;

            // ---------------- N500 / N630 (IRPJ) e N650 / N670 (CSLL)
            var n3 = l175 > 0 ? R(l175 * Aliquota) : 0m;
            var limite = LimiteMensal * per.Meses;
            var n4 = l175 > limite ? R((l175 - limite) * Adicional) : 0m;
            var irrf = R(ap?.IrpjDeducoes ?? 0);
            var n26 = R(n3 + n4 - irrf);
            var n500 = new List<LinhaTabelaEcf> { new("1", "Valor da base de cálculo do IRPJ", l175) };
            var n630 = new List<LinhaTabelaEcf> { new("1", "BASE DE CÁLCULO DO IRPJ", l175), new("3", "À Alíquota de 15%", n3), new("4", "Adicional", n4) };
            if (irrf != 0) n630.Add(new("20", "(-) Imposto de Renda Retido na Fonte", irrf));
            n630.Add(new("26", "IMPOSTO DE RENDA A PAGAR", n26));
            var n2 = c175 > 0 ? R(c175 * AliquotaCsll) : 0m;
            var ret = R(ap?.CsllDeducoes ?? 0);
            var n21 = R(n2 - ret);
            var n650 = new List<LinhaTabelaEcf> { new("1", "Valor da Base de Cálculo da CSLL", c175) };
            var n670 = new List<LinhaTabelaEcf> { new("1", "BASE DE CÁLCULO DA CSLL", c175), new("2", "Contribuição Social sobre o Lucro Líquido por Atividade", n2),
                                                   new("4", "TOTAL DA CONTRIBUIÇÃO SOCIAL SOBRE O LUCRO LÍQUIDO", n2) };
            if (ret != 0) n670.Add(new("17", "(-) CSLL Retida na Fonte por Pessoas Jurídicas de Direito Privado (Lei n° 10.833/2003, art. 30)", ret));
            n670.Add(new("21", "CSLL A PAGAR", n21));

            real.Add(new ApuracaoRealEcf
            {
                Periodo = per, M300 = linhas300, M350 = linhas350, M410 = m410, M500 = [ctl1, ctl2], N500 = n500, N630 = n630, N650 = n650, N670 = n670,
                LucroAntesIrpj = l2, LucroReal = l175, BaseCsll = c175, IrpjDevido = R(n3 + n4), CsllDevida = n2, LucroAntesCsll = c2,
            });
        }

        // Contas da Parte B (M010): com saldo inicial ou movimento no ano.
        DateOnly Criacao(int idx, decimal inicial) => inicial != 0 || primeiroTrimestre[idx] is null
            ? new DateOnly(p.Ano - 1, 12, 31)
            : Periodos.First(x => x.Trimestre == primeiroTrimestre[idx]).Fim;
        if (saldoIniIrpj != 0 || movimentou[0])
            contasB.Add(new ContaParteBEcf(ContaPrejuizo, "Prejuízo Fiscal Operacional - Atividade Geral", "1000", 'I', Criacao(0, saldoIniIrpj), saldoIniIrpj));
        if (saldoIniCsll != 0 || movimentou[1])
            contasB.Add(new ContaParteBEcf(ContaBaseNegativa, "Base de Cálculo Negativa da CSLL - Atividade Geral", "1003", 'C', Criacao(1, saldoIniCsll), saldoIniCsll));
    }

    // ------------------------------------------------------------------------------------------------ Lucro Presumido

    private void CalcularPresumido(List<ApuracaoPresumidoEcf> lista)
    {
        foreach (var per in Periodos)
        {
            var ap = _d.Apuracoes.FirstOrDefault(a => a.Trimestre == per.Trimestre && a.Regime == FormaTributacaoEcf.Presumido);
            var receitas = ap?.Receitas ?? [];
            decimal Soma(Func<ReceitaPresumidaEcf, bool> f) => R(receitas.Where(f).Sum(r => r.Valor));
            bool Perc(decimal valor, decimal alvo) => Math.Abs(valor - alvo) < 0.005m;

            var p200 = new List<LinhaTabelaEcf>();
            var r16 = Soma(r => Perc(r.PercentualIrpj, 1.6m));
            var r8 = Soma(r => Perc(r.PercentualIrpj, 8m));
            var r16b = Soma(r => Perc(r.PercentualIrpj, 16m));
            var r32 = Soma(r => Perc(r.PercentualIrpj, 32m));
            var r384 = Soma(r => Perc(r.PercentualIrpj, 38.4m));
            void Add(List<LinhaTabelaEcf> l, string c, string d, decimal v) { if (v != 0) l.Add(new(c, d, v)); }
            Add(p200, "2", "Receita Bruta Sujeita ao Percentual de 1,6%", r16);
            Add(p200, "4", "Receita Bruta Sujeita ao Percentual de 8%", r8);
            Add(p200, "6", "Receita Bruta Sujeita ao Percentual de 16%", r16b);
            Add(p200, "8", "Receita Bruta Sujeita ao Percentual de 32%", r32);
            Add(p200, "9", "Receita Bruta Sujeita ao Percentual de 38,4%", r384);
            var l10 = R(r16 * 0.016m + r8 * 0.08m + r16b * 0.16m + r32 * 0.32m + r384 * 0.384m);
            p200.Add(new("10", "RESULTADO DA APLICAÇÃO DOS PERCENTUAIS SOBRE A RECEITA BRUTA AJUSTADO", l10));
            var outras = R(ap?.OutrasReceitas ?? 0);
            Add(p200, "20", "Demais Receitas e Ganhos de Capital", outras);
            var baseIrpj = R(Math.Max(0, l10 + outras));
            p200.Add(new("26", "BASE DE CÁLCULO DO IMPOSTO SOBRE O LUCRO PRESUMIDO", baseIrpj));

            var p3 = baseIrpj > 0 ? R(baseIrpj * Aliquota) : 0m;
            var limite = LimiteMensal * per.Meses;
            var p4 = baseIrpj > limite ? R((baseIrpj - limite) * Adicional) : 0m;
            var irrf = R(ap?.IrpjDeducoes ?? 0);
            var p300 = new List<LinhaTabelaEcf> { new("1", "BASE DE CÁLCULO DO IMPOSTO SOBRE O LUCRO PRESUMIDO", baseIrpj), new("3", "À Alíquota de 15%", p3), new("4", "Adicional", p4) };
            if (irrf != 0) p300.Add(new("10", "(-) Imposto de Renda Retido na Fonte", irrf));
            p300.Add(new("15", "IMPOSTO DE RENDA A PAGAR", R(p3 + p4 - irrf)));

            var p400 = new List<LinhaTabelaEcf>();
            var q12 = Soma(r => Perc(r.PercentualCsll, 12m));
            var q32 = Soma(r => Perc(r.PercentualCsll, 32m));
            var q384 = Soma(r => Perc(r.PercentualCsll, 38.4m));
            Add(p400, "2", "Receita Bruta Sujeita ao Percentual de 12%", q12);
            Add(p400, "4", "Receita Bruta Sujeita ao Percentual de 32%", q32);
            Add(p400, "5", "Receita Bruta Sujeita ao Percentual de 38,4%", q384);
            var p6 = R(q12 * 0.12m + q32 * 0.32m + q384 * 0.384m);
            p400.Add(new("6", "RESULTADO DA APLICAÇÃO DOS PERCENTUAIS SOBRE A RECEITA BRUTA AJUSTADO", p6));
            Add(p400, "16", "Demais Receitas e Ganhos de Capital", outras);
            var baseCsll = R(Math.Max(0, p6 + outras));
            p400.Add(new("21", "BASE DE CÁLCULO DA CSLL", baseCsll));

            var csll = baseCsll > 0 ? R(baseCsll * AliquotaCsll) : 0m;
            var ret = R(ap?.CsllDeducoes ?? 0);
            var p500 = new List<LinhaTabelaEcf> { new("1", "BASE DE CÁLCULO DA CSLL", baseCsll), new("2", "CSLL Apurada", csll), new("4", "TOTAL DA CONTRIBUIÇÃO SOCIAL SOBRE O LUCRO LÍQUIDO", csll) };
            if (ret != 0) p500.Add(new("11", "(-) CSLL Retida na Fonte por Pessoas Jurídicas de Direito Privado (Lei n° 10.833/2003, art. 30)", ret));
            p500.Add(new("13", "CSLL A PAGAR", R(csll - ret)));

            lista.Add(new ApuracaoPresumidoEcf
            {
                Periodo = per, P200 = p200, P300 = p300, P400 = p400, P500 = p500, BaseIrpj = baseIrpj, BaseCsll = baseCsll, IrpjDevido = R(p3 + p4), CsllDevida = csll,
            });
        }
    }
}
