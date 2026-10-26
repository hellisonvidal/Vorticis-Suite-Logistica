using Vorticis.Domain.Comum;

namespace Vorticis.Domain.Contabil;

/// <summary>Centro de custo do plano de centros (hierarquia por <see cref="PaiId"/>).</summary>
public sealed record CentroPlano(long Id, string Codigo, string Descricao, long? PaiId, bool Ativo, bool Analitico);

/// <summary>Movimento de uma conta analítica num centro de custo (0 = sem centro) em um mês (primeiro dia do mês).</summary>
public sealed record MovimentoCentro(long ContaId, long CentroId, DateOnly Mes, decimal Debitos, decimal Creditos);

/// <summary>Partida de um lançamento com centro de custo, para detalhar o relatório.</summary>
public sealed record DetalheCentro(long ContaId, long CentroId, DateOnly Data, long Numero, IndicadorDC DC, decimal Valor, string Historico);

public enum ClasseResultado { Nenhuma, Receita, CustoDespesa }
public enum AgrupamentoCustos { PorCentro, PorConta }
public enum ComparacaoPeriodo { Nenhuma, PeriodoAnterior, AnoAnterior }
public enum TipoLinhaGerencial { Total, Grupo, Item, Detalhe }

public sealed record FiltroCentroCusto(DateOnly Inicio, DateOnly Fim, long? CentroId = null, string? ContaPrefixo = null, int NivelMaximo = 0,
                                       bool Detalhar = false);

public sealed record LinhaCentroConta(TipoLinhaGerencial Tipo, string Codigo, string Descricao, int Recuo, decimal Debitos, decimal Creditos,
                                      DateOnly? Data = null, long? Numero = null)
{
    /// <summary>Débitos − créditos (positivo = devedor).</summary>
    public decimal Saldo => Debitos - Creditos;
}

public sealed record FiltroCustos(DateOnly Inicio, DateOnly Fim, AgrupamentoCustos Agrupar = AgrupamentoCustos.PorCentro,
                                  ComparacaoPeriodo Comparar = ComparacaoPeriodo.PeriodoAnterior, long? CentroId = null, string? ContaPrefixo = null,
                                  int NivelMaximo = 0);

public sealed record LinhaCusto(TipoLinhaGerencial Tipo, string Codigo, string Descricao, int Recuo, decimal Valor, decimal Participacao,
                                decimal? Anterior, decimal? Variacao, decimal? VariacaoPercentual);

public sealed record FiltroReceitaCentro(DateOnly Inicio, DateOnly Fim, long? CentroId = null, int NivelMaximo = 0);

public sealed record LinhaReceitaCentro(TipoLinhaGerencial Tipo, string Codigo, string Descricao, int Recuo, IReadOnlyList<decimal> PorMes,
                                        decimal Receita, decimal Participacao, decimal CustosDespesas)
{
    public decimal Margem => Receita - CustosDespesas;
    public decimal? MargemPercentual => Receita == 0 ? null : Math.Round(Margem / Receita * 100, 2);
}

public sealed record ReceitaPorCentro(IReadOnlyList<DateOnly> Meses, IReadOnlyList<LinhaReceitaCentro> Linhas);

/// <summary>Orçamento de uma conta de resultado (e centro, 0 = sem centro) para o ano: 12 valores, índice 0 = janeiro.</summary>
public sealed record LinhaOrcamento(long ContaId, long CentroId, IReadOnlyList<decimal> Valores);

public sealed record FiltroOrcadoRealizado(int Ano, int MesInicial = 1, int MesFinal = 12, long? CentroId = null, string? ContaPrefixo = null,
                                           bool Mensal = false, bool IgnorarCentro = false);

/// <param name="Mes">Mês da linha (modo mensal); null no modo acumulado.</param>
/// <param name="AcumOrcado">Orçado acumulado desde o mês inicial até o mês da linha (no modo acumulado, igual ao orçado).</param>
public sealed record LinhaOrcadoRealizado(TipoLinhaGerencial Tipo, string Codigo, string Descricao, string Centro, int? Mes, decimal Orcado,
                                          decimal Realizado, decimal AcumOrcado, decimal AcumRealizado)
{
    public decimal Diferenca => Realizado - Orcado;
    public decimal? Percentual => Orcado == 0 ? null : Math.Round(Realizado / Orcado * 100, 2);
    public decimal AcumDiferenca => AcumRealizado - AcumOrcado;
    public decimal? AcumPercentual => AcumOrcado == 0 ? null : Math.Round(AcumRealizado / AcumOrcado * 100, 2);
}

/// <summary>Árvore dos centros de custo para somar analíticos nos sintéticos.</summary>
internal sealed class ArvoreCentros
{
    private readonly Dictionary<long, CentroPlano> _porId;
    private readonly ILookup<long?, CentroPlano> _filhos;
    private readonly Dictionary<long, int> _nivel = [];

    public ArvoreCentros(IEnumerable<CentroPlano> centros)
    {
        var lista = centros.OrderBy(c => c.Codigo, StringComparer.Ordinal).ToList();
        _porId = lista.ToDictionary(c => c.Id);
        _filhos = lista.ToLookup(c => c.PaiId is { } p && _porId.ContainsKey(p) ? p : (long?)null);
        foreach (var c in lista) _nivel[c.Id] = Calcular(c, 0);
    }

    private int Calcular(CentroPlano c, int guarda) =>
        c.PaiId is { } p && guarda < 50 && _porId.TryGetValue(p, out var pai) ? Calcular(pai, guarda + 1) + 1 : 1;

    public CentroPlano? Obter(long id) => _porId.GetValueOrDefault(id);
    public int Nivel(long id) => _nivel.GetValueOrDefault(id, 1);
    public IEnumerable<CentroPlano> Raizes => _filhos[null];
    public IEnumerable<CentroPlano> Filhos(long id) => _filhos[id];

    public HashSet<long> Subarvore(long id)
    {
        var res = new HashSet<long> { id };
        var pilha = new Stack<long>([id]);
        while (pilha.Count > 0)
            foreach (var f in _filhos[pilha.Pop()]) if (res.Add(f.Id)) pilha.Push(f.Id);
        return res;
    }

    /// <summary>O centro é onde o relatório mostra as contas: está no nível máximo pedido ou não tem subcentros a exibir.</summary>
    public bool FolhaDeExibicao(CentroPlano c, int nivelMaximo) => (nivelMaximo > 0 && Nivel(c.Id) >= nivelMaximo) || !_filhos[c.Id].Any();
}

/// <summary>Um centro (ou "sem centro") da estrutura de um relatório, em pré-ordem.</summary>
internal sealed record NoCentro(CentroPlano? Centro, HashSet<long> Ids, int Recuo, bool FolhaDeExibicao);

/// <summary>Relatórios gerenciais: apuração por centro de custo, custos, receitas e orçado x realizado, sobre os movimentos por conta/centro/mês.</summary>
public static class Gerenciais
{
    public const string SemCentro = "Sem centro de custo";

    public static ClasseResultado Classe(ContaPlano c) =>
        c.CodNat != "04" || !c.Analitica ? ClasseResultado.Nenhuma
        : c.NaturezaSaldo == IndicadorDC.C ? ClasseResultado.Receita : ClasseResultado.CustoDespesa;

    /// <summary>Valor do movimento na natureza da conta (devedora: débitos − créditos; credora: créditos − débitos).</summary>
    public static decimal Natural(ContaPlano c, decimal debitos, decimal creditos) =>
        c.NaturezaSaldo == IndicadorDC.D ? debitos - creditos : creditos - debitos;

    /// <summary>Período de comparação: o imediatamente anterior (mesma duração) ou o mesmo período do ano anterior.</summary>
    public static (DateOnly Inicio, DateOnly Fim)? PeriodoComparado(DateOnly inicio, DateOnly fim, ComparacaoPeriodo modo) => modo switch
    {
        ComparacaoPeriodo.PeriodoAnterior => (inicio.AddDays(-(fim.DayNumber - inicio.DayNumber + 1)), inicio.AddDays(-1)),
        ComparacaoPeriodo.AnoAnterior => (inicio.AddYears(-1), fim.AddYears(-1)),
        _ => null,
    };

    private static bool Prefixo(ContaPlano c, string? prefixo) => string.IsNullOrWhiteSpace(prefixo) || c.Codigo.StartsWith(prefixo.Trim(), StringComparison.Ordinal);

    private static DateOnly Mes(DateOnly d) => new(d.Year, d.Month, 1);

    /// <summary>Centros a exibir (pré-ordem), com o conjunto de ids da subárvore; "sem centro" ao final quando não há filtro de centro.</summary>
    private static List<NoCentro> Estrutura(ArvoreCentros arvore, long? centroId, int nivelMaximo, Func<HashSet<long>, bool> temMovimento)
    {
        var res = new List<NoCentro>();
        void Visitar(CentroPlano c, int recuo)
        {
            var ids = arvore.Subarvore(c.Id);
            if (!temMovimento(ids)) return;
            var folha = arvore.FolhaDeExibicao(c, nivelMaximo);
            res.Add(new NoCentro(c, ids, recuo, folha));
            if (!folha) foreach (var f in arvore.Filhos(c.Id)) Visitar(f, recuo + 1);
        }
        if (centroId is { } id)
        {
            if (arvore.Obter(id) is { } c) Visitar(c, 0);
        }
        else
        {
            foreach (var r in arvore.Raizes) Visitar(r, 0);
            HashSet<long> sem = [0];
            if (temMovimento(sem)) res.Add(new NoCentro(null, sem, 0, true));
        }
        return res;
    }

    private static string Cod(NoCentro n) => n.Centro?.Codigo ?? "";
    private static string Desc(NoCentro n) => n.Centro?.Descricao ?? SemCentro;

    // ---------------------------------------------------------------------------------------------------------------
    // 1. Contabilização por centro de custo
    // ---------------------------------------------------------------------------------------------------------------

    public static IReadOnlyList<LinhaCentroConta> PorCentroCusto(IReadOnlyList<ContaPlano> plano, IReadOnlyList<CentroPlano> centros,
                                                                 IReadOnlyList<MovimentoCentro> movimentos, IReadOnlyList<DetalheCentro> detalhes,
                                                                 FiltroCentroCusto filtro)
    {
        var contas = plano.ToDictionary(c => c.Id);
        var movs = movimentos.Where(m => contas.TryGetValue(m.ContaId, out var c) && Prefixo(c, filtro.ContaPrefixo)).ToList();
        var arvore = new ArvoreCentros(centros);
        var linhas = new List<LinhaCentroConta>();
        decimal totalD = 0, totalC = 0;

        foreach (var no in Estrutura(arvore, filtro.CentroId, filtro.NivelMaximo, ids => movs.Any(m => ids.Contains(m.CentroId))))
        {
            var doCentro = movs.Where(m => no.Ids.Contains(m.CentroId)).ToList();
            var d = doCentro.Sum(m => m.Debitos);
            var c = doCentro.Sum(m => m.Creditos);
            linhas.Add(new(TipoLinhaGerencial.Grupo, Cod(no), Desc(no), no.Recuo, d, c));
            if (no.Recuo == 0) { totalD += d; totalC += c; }
            if (!no.FolhaDeExibicao) continue;
            foreach (var g in doCentro.GroupBy(m => m.ContaId).OrderBy(g => contas[g.Key].Codigo, StringComparer.Ordinal))
            {
                var conta = contas[g.Key];
                linhas.Add(new(TipoLinhaGerencial.Item, conta.Codigo, conta.Descricao, no.Recuo + 1, g.Sum(m => m.Debitos), g.Sum(m => m.Creditos)));
                if (!filtro.Detalhar) continue;
                foreach (var l in detalhes.Where(x => x.ContaId == g.Key && no.Ids.Contains(x.CentroId)).OrderBy(x => x.Data).ThenBy(x => x.Numero))
                    linhas.Add(new(TipoLinhaGerencial.Detalhe, "", l.Historico, no.Recuo + 2, l.DC == IndicadorDC.D ? l.Valor : 0,
                                   l.DC == IndicadorDC.C ? l.Valor : 0, l.Data, l.Numero));
            }
        }
        if (linhas.Count > 0) linhas.Add(new(TipoLinhaGerencial.Total, "", "Total", 0, totalD, totalC));
        return linhas;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // 3. Relação de custos
    // ---------------------------------------------------------------------------------------------------------------

    public static IReadOnlyList<LinhaCusto> RelacaoCustos(IReadOnlyList<ContaPlano> plano, IReadOnlyList<CentroPlano> centros,
                                                          IReadOnlyList<MovimentoCentro> atual, IReadOnlyList<MovimentoCentro>? anterior,
                                                          FiltroCustos filtro)
    {
        var contas = plano.ToDictionary(c => c.Id);
        bool Custo(MovimentoCentro m) => contas.TryGetValue(m.ContaId, out var c) && Classe(c) == ClasseResultado.CustoDespesa && Prefixo(c, filtro.ContaPrefixo);
        var ma = atual.Where(Custo).ToList();
        var mp = anterior?.Where(Custo).ToList();
        decimal Soma(IEnumerable<MovimentoCentro> ms) => ms.Sum(m => m.Debitos - m.Creditos);
        var total = Soma(ma);

        LinhaCusto Linha(TipoLinhaGerencial tipo, string cod, string desc, int recuo, IEnumerable<MovimentoCentro> a, IEnumerable<MovimentoCentro>? p)
        {
            var v = Soma(a);
            decimal? ant = p is null ? null : Soma(p);
            decimal? var = ant is null ? null : v - ant;
            decimal? varPct = ant is null or 0 ? null : Math.Round((v - ant.Value) / Math.Abs(ant.Value) * 100, 2);
            return new(tipo, cod, desc, recuo, v, total == 0 ? 0 : Math.Round(v / total * 100, 2), ant, var, varPct);
        }

        var linhas = new List<LinhaCusto>();
        if (filtro.Agrupar == AgrupamentoCustos.PorCentro)
        {
            var arvore = new ArvoreCentros(centros);
            bool Tem(HashSet<long> ids) => ma.Any(m => ids.Contains(m.CentroId)) || (mp?.Any(m => ids.Contains(m.CentroId)) ?? false);
            foreach (var no in Estrutura(arvore, filtro.CentroId, filtro.NivelMaximo, Tem))
            {
                var a = ma.Where(m => no.Ids.Contains(m.CentroId)).ToList();
                var p = mp?.Where(m => no.Ids.Contains(m.CentroId)).ToList();
                linhas.Add(Linha(TipoLinhaGerencial.Grupo, Cod(no), Desc(no), no.Recuo, a, p));
                if (!no.FolhaDeExibicao) continue;
                var ids = a.Select(m => m.ContaId).Concat(p?.Select(m => m.ContaId) ?? []).Distinct().OrderBy(i => contas[i].Codigo, StringComparer.Ordinal);
                foreach (var cid in ids)
                    linhas.Add(Linha(TipoLinhaGerencial.Item, contas[cid].Codigo, contas[cid].Descricao, no.Recuo + 1,
                                     a.Where(m => m.ContaId == cid), p?.Where(m => m.ContaId == cid)));
            }
        }
        else
        {
            var sub = filtro.CentroId is { } cid0 ? new ArvoreCentros(centros).Subarvore(cid0) : null;
            if (sub is not null)
            {
                ma = ma.Where(m => sub.Contains(m.CentroId)).ToList();
                mp = mp?.Where(m => sub.Contains(m.CentroId)).ToList();
                total = Soma(ma);
            }
            var porId = centros.ToDictionary(c => c.Id);
            var contasIds = ma.Select(m => m.ContaId).Concat(mp?.Select(m => m.ContaId) ?? []).Distinct().OrderBy(i => contas[i].Codigo, StringComparer.Ordinal);
            foreach (var conta in contasIds)
            {
                var a = ma.Where(m => m.ContaId == conta).ToList();
                var p = mp?.Where(m => m.ContaId == conta).ToList();
                linhas.Add(Linha(TipoLinhaGerencial.Grupo, contas[conta].Codigo, contas[conta].Descricao, 0, a, p));
                var cs = a.Select(m => m.CentroId).Concat(p?.Select(m => m.CentroId) ?? []).Distinct()
                          .OrderBy(i => porId.TryGetValue(i, out var c) ? c.Codigo : "~", StringComparer.Ordinal);
                foreach (var c in cs)
                    linhas.Add(Linha(TipoLinhaGerencial.Item, porId.TryGetValue(c, out var cc) ? cc.Codigo : "", porId.TryGetValue(c, out var c2) ? c2.Descricao : SemCentro,
                                     1, a.Where(m => m.CentroId == c), p?.Where(m => m.CentroId == c)));
            }
        }
        if (linhas.Count > 0)
        {
            decimal? ant = mp is null ? null : Soma(mp);
            // o total geral considera todos os custos do filtro (centro filtrado já aplicado no agrupamento por conta)
            var totalAtual = Soma(ma);
            linhas.Add(new(TipoLinhaGerencial.Total, "", "Total de custos e despesas", 0, totalAtual, totalAtual == 0 ? 0 : 100m, ant,
                           ant is null ? null : totalAtual - ant, ant is null or 0 ? null : Math.Round((totalAtual - ant.Value) / Math.Abs(ant.Value) * 100, 2)));
        }
        return linhas;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // 4. Receita por centro de custo
    // ---------------------------------------------------------------------------------------------------------------

    public static IReadOnlyList<DateOnly> MesesDoPeriodo(DateOnly inicio, DateOnly fim)
    {
        var res = new List<DateOnly>();
        for (var m = Mes(inicio); m <= fim; m = m.AddMonths(1)) res.Add(m);
        return res;
    }

    public static ReceitaPorCentro ReceitaPorCentroCusto(IReadOnlyList<ContaPlano> plano, IReadOnlyList<CentroPlano> centros,
                                                         IReadOnlyList<MovimentoCentro> movimentos, FiltroReceitaCentro filtro)
    {
        var contas = plano.ToDictionary(c => c.Id);
        var meses = MesesDoPeriodo(filtro.Inicio, filtro.Fim);
        var receitas = new List<MovimentoCentro>();
        var custos = new List<MovimentoCentro>();
        foreach (var m in movimentos)
        {
            if (!contas.TryGetValue(m.ContaId, out var c)) continue;
            switch (Classe(c))
            {
                case ClasseResultado.Receita: receitas.Add(m); break;
                case ClasseResultado.CustoDespesa: custos.Add(m); break;
            }
        }
        decimal Rec(IEnumerable<MovimentoCentro> ms) => ms.Sum(m => m.Creditos - m.Debitos);
        decimal Cus(IEnumerable<MovimentoCentro> ms) => ms.Sum(m => m.Debitos - m.Creditos);
        var arvore = new ArvoreCentros(centros);
        var nos = Estrutura(arvore, filtro.CentroId, filtro.NivelMaximo,
                            ids => receitas.Any(m => ids.Contains(m.CentroId)) || custos.Any(m => ids.Contains(m.CentroId)));
        var totalReceita = nos.Where(n => n.Recuo == 0).Sum(n => Rec(receitas.Where(m => n.Ids.Contains(m.CentroId))));
        var linhas = new List<LinhaReceitaCentro>();
        foreach (var no in nos)
        {
            var r = receitas.Where(m => no.Ids.Contains(m.CentroId)).ToList();
            var v = Rec(r);
            linhas.Add(new(TipoLinhaGerencial.Grupo, Cod(no), Desc(no), no.Recuo, meses.Select(mes => Rec(r.Where(m => m.Mes == mes))).ToList(), v,
                           totalReceita == 0 ? 0 : Math.Round(v / totalReceita * 100, 2), Cus(custos.Where(m => no.Ids.Contains(m.CentroId)))));
        }
        if (linhas.Count > 0)
        {
            var topo = nos.Where(n => n.Recuo == 0).ToList();
            var rt = receitas.Where(m => topo.Any(n => n.Ids.Contains(m.CentroId))).ToList();
            var ct = custos.Where(m => topo.Any(n => n.Ids.Contains(m.CentroId))).ToList();
            linhas.Add(new(TipoLinhaGerencial.Total, "", "Total", 0, meses.Select(mes => Rec(rt.Where(m => m.Mes == mes))).ToList(), Rec(rt),
                           totalReceita == 0 ? 0 : 100m, Cus(ct)));
        }
        return new ReceitaPorCentro(meses, linhas);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // 2. Orçado x realizado
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>Orçamento reajustado: cada valor multiplicado por (1 + percentual/100), com centavos.</summary>
    public static IReadOnlyList<LinhaOrcamento> Reajustar(IEnumerable<LinhaOrcamento> origem, decimal percentual) =>
        origem.Select(l => new LinhaOrcamento(l.ContaId, l.CentroId, l.Valores.Select(v => Math.Round(v * (1 + percentual / 100m), 2, MidpointRounding.AwayFromZero)).ToList()))
              .ToList();

    /// <summary>Realizado de um ano como base de orçamento (valores na natureza da conta, só contas de resultado), com reajuste.</summary>
    public static IReadOnlyList<LinhaOrcamento> RealizadoComoOrcamento(IReadOnlyList<ContaPlano> plano, IEnumerable<MovimentoCentro> movimentos,
                                                                       int ano, decimal percentual)
    {
        var contas = plano.ToDictionary(c => c.Id);
        var res = new Dictionary<(long, long), decimal[]>();
        foreach (var m in movimentos)
        {
            if (m.Mes.Year != ano || !contas.TryGetValue(m.ContaId, out var c) || Classe(c) == ClasseResultado.Nenhuma) continue;
            if (!res.TryGetValue((m.ContaId, m.CentroId), out var v)) res[(m.ContaId, m.CentroId)] = v = new decimal[12];
            v[m.Mes.Month - 1] += Natural(c, m.Debitos, m.Creditos);
        }
        var linhas = res.Where(kv => kv.Value.Any(x => x != 0))
                        .OrderBy(kv => contas[kv.Key.Item1].Codigo, StringComparer.Ordinal).ThenBy(kv => kv.Key.Item2)
                        .Select(kv => new LinhaOrcamento(kv.Key.Item1, kv.Key.Item2, kv.Value)).ToList();
        return Reajustar(linhas, percentual);
    }

    public static IReadOnlyList<LinhaOrcadoRealizado> OrcadoRealizado(IReadOnlyList<ContaPlano> plano, IReadOnlyList<CentroPlano> centros,
                                                                      IReadOnlyList<LinhaOrcamento> orcamento, IReadOnlyList<MovimentoCentro> movimentos,
                                                                      FiltroOrcadoRealizado f)
    {
        var contas = plano.ToDictionary(c => c.Id);
        var porId = centros.ToDictionary(c => c.Id);
        var sub = f.CentroId is { } cid ? new ArvoreCentros(centros).Subarvore(cid) : null;
        bool Ok(long contaId, long centroId) =>
            contas.TryGetValue(contaId, out var c) && Classe(c) != ClasseResultado.Nenhuma && Prefixo(c, f.ContaPrefixo)
            && (sub is null || sub.Contains(centroId));
        long Centro(long id) => f.IgnorarCentro ? 0 : id;

        // (conta, centro) -> 12 orçados e 12 realizados
        var chaves = new Dictionary<(long, long), (decimal[] Orc, decimal[] Real)>();
        (decimal[] Orc, decimal[] Real) Obter(long conta, long centro)
        {
            if (!chaves.TryGetValue((conta, centro), out var v)) chaves[(conta, centro)] = v = (new decimal[12], new decimal[12]);
            return v;
        }
        foreach (var o in orcamento)
        {
            if (!Ok(o.ContaId, o.CentroId)) continue;
            var v = Obter(o.ContaId, Centro(o.CentroId));
            for (var i = 0; i < 12 && i < o.Valores.Count; i++) v.Orc[i] += o.Valores[i];
        }
        foreach (var m in movimentos)
        {
            if (m.Mes.Year != f.Ano || !Ok(m.ContaId, m.CentroId)) continue;
            Obter(m.ContaId, Centro(m.CentroId)).Real[m.Mes.Month - 1] += Natural(contas[m.ContaId], m.Debitos, m.Creditos);
        }

        var mi = Math.Clamp(f.MesInicial, 1, 12);
        var mf = Math.Clamp(f.MesFinal, mi, 12);
        string NomeCentro(long id) => id == 0 ? "" : porId.TryGetValue(id, out var c) ? $"{c.Codigo} {c.Descricao}" : "";
        var ordenadas = chaves.OrderBy(kv => contas[kv.Key.Item1].Codigo, StringComparer.Ordinal).ThenBy(kv => porId.TryGetValue(kv.Key.Item2, out var c) ? c.Codigo : "")
                              .Where(kv => Enumerable.Range(mi - 1, mf - mi + 1).Any(i => kv.Value.Orc[i] != 0 || kv.Value.Real[i] != 0)).ToList();
        var linhas = new List<LinhaOrcadoRealizado>();
        foreach (var kv in ordenadas)
        {
            var conta = contas[kv.Key.Item1];
            var (orc, real) = kv.Value;
            if (f.Mensal)
            {
                decimal ao = 0, ar = 0;
                for (var i = mi - 1; i < mf; i++)
                {
                    ao += orc[i]; ar += real[i];
                    linhas.Add(new(TipoLinhaGerencial.Item, conta.Codigo, conta.Descricao, NomeCentro(kv.Key.Item2), i + 1, orc[i], real[i], ao, ar));
                }
            }
            else
            {
                var o = Enumerable.Range(mi - 1, mf - mi + 1).Sum(i => orc[i]);
                var r = Enumerable.Range(mi - 1, mf - mi + 1).Sum(i => real[i]);
                linhas.Add(new(TipoLinhaGerencial.Item, conta.Codigo, conta.Descricao, NomeCentro(kv.Key.Item2), null, o, r, o, r));
            }
        }
        if (!f.Mensal && linhas.Count > 0)
        {
            LinhaOrcadoRealizado Soma(string desc, ClasseResultado classe)
            {
                var itens = linhas.Where(l => Classe(contas.Values.First(c => c.Codigo == l.Codigo)) == classe).ToList();
                return new(TipoLinhaGerencial.Total, "", desc, "", null, itens.Sum(l => l.Orcado), itens.Sum(l => l.Realizado),
                           itens.Sum(l => l.Orcado), itens.Sum(l => l.Realizado));
            }
            var rec = Soma("Total das receitas", ClasseResultado.Receita);
            var cus = Soma("Total de custos e despesas", ClasseResultado.CustoDespesa);
            linhas.Add(rec);
            linhas.Add(cus);
            linhas.Add(new(TipoLinhaGerencial.Total, "", "Resultado (receitas − custos e despesas)", "", null, rec.Orcado - cus.Orcado, rec.Realizado - cus.Realizado,
                           rec.Orcado - cus.Orcado, rec.Realizado - cus.Realizado));
        }
        return linhas;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Importação de planilha de orçamento
    // ---------------------------------------------------------------------------------------------------------------

    private static readonly string[][] NomesMeses =
    [
        ["jan", "janeiro", "01", "1", "mes_1"], ["fev", "fevereiro", "02", "2", "mes_2"], ["mar", "marco", "03", "3", "mes_3"],
        ["abr", "abril", "04", "4", "mes_4"], ["mai", "maio", "05", "5", "mes_5"], ["jun", "junho", "06", "6", "mes_6"],
        ["jul", "julho", "07", "7", "mes_7"], ["ago", "agosto", "08", "8", "mes_8"], ["set", "setembro", "09", "9", "mes_9"],
        ["out", "outubro", "10", "10", "mes_10"], ["nov", "novembro", "11", "11", "mes_11"], ["dez", "dezembro", "12", "12", "mes_12"],
    ];

    /// <summary>
    /// Lê a planilha de orçamento. Colunas: "Conta" (código), "Centro de custo" (opcional, código) e uma coluna por mês (Jan a Dez);
    /// ou, em formato longo, "Conta", "Centro de custo" (opcional), "Mês" (1 a 12) e "Valor". Linhas repetidas da mesma conta/centro somam.
    /// </summary>
    public static Resultado<IReadOnlyList<LinhaOrcamento>> LerPlanilhaOrcamento(TabelaImportada t, IReadOnlyList<ContaPlano> plano, IReadOnlyList<CentroPlano> centros)
    {
        static int Col(TabelaImportada t, params string[] nomes) => t.Coluna(nomes.Select(n => n.ToUpperInvariant()).ToArray());
        var iConta = Col(t, "conta", "codigo", "codigo_da_conta", "conta_contabil", "cod_conta");
        if (iConta < 0) return Resultado<IReadOnlyList<LinhaOrcamento>>.Falha("A planilha precisa ter a coluna \"Conta\" com o código da conta.");
        var iCentro = Col(t, "centro_de_custo", "centro_custo", "centro", "codigo_do_centro", "cc");
        var iMesLongo = Col(t, "mes", "competencia");
        var iValor = Col(t, "valor", "orcado", "valor_orcado");
        var iMeses = NomesMeses.Select(n => Col(t, n)).ToArray();
        var longo = iMesLongo >= 0 && iValor >= 0;
        if (!longo && iMeses.All(i => i < 0))
            return Resultado<IReadOnlyList<LinhaOrcamento>>.Falha("Não encontrei as colunas dos meses (Jan a Dez) nem as colunas \"Mês\" e \"Valor\".");

        var contas = plano.Where(c => c.Analitica).ToDictionary(c => c.Codigo, StringComparer.OrdinalIgnoreCase);
        var cs = centros.ToDictionary(c => c.Codigo, StringComparer.OrdinalIgnoreCase);
        var res = new Dictionary<(long, long), decimal[]>();
        var erros = new List<string>();
        for (var i = 0; i < t.Linhas.Count; i++)
        {
            var linha = t.NumerosLinha[i];
            var cod = t.Valor(i, iConta);
            if (cod.Length == 0) continue;
            if (!contas.TryGetValue(cod, out var conta) || Classe(conta) == ClasseResultado.Nenhuma)
            {
                erros.Add($"Linha {linha}: a conta {cod} não existe, não é analítica ou não é de resultado (receita, custo ou despesa).");
                continue;
            }
            long centro = 0;
            var codCentro = iCentro >= 0 ? t.Valor(i, iCentro) : "";
            if (codCentro.Length > 0)
            {
                if (!cs.TryGetValue(codCentro, out var cc)) { erros.Add($"Linha {linha}: o centro de custo {codCentro} não existe."); continue; }
                centro = cc.Id;
            }
            if (!res.TryGetValue((conta.Id, centro), out var v)) res[(conta.Id, centro)] = v = new decimal[12];
            if (longo)
            {
                if (!int.TryParse(t.Valor(i, iMesLongo), out var mes) || mes is < 1 or > 12) { erros.Add($"Linha {linha}: mês inválido (use 1 a 12)."); continue; }
                if (Formato.LerValor(t.Valor(i, iValor)) is not { } valor) { erros.Add($"Linha {linha}: valor inválido."); continue; }
                v[mes - 1] += valor;
            }
            else
            {
                for (var m = 0; m < 12; m++)
                {
                    if (iMeses[m] < 0 || t.Valor(i, iMeses[m]) is not { Length: > 0 } txt) continue;
                    if (Formato.LerValor(txt) is not { } valor) { erros.Add($"Linha {linha}: valor inválido em {NomesMeses[m][1]}."); continue; }
                    v[m] += valor;
                }
            }
        }
        if (erros.Count > 0) return Resultado<IReadOnlyList<LinhaOrcamento>>.Falha(erros);
        if (res.Count == 0) return Resultado<IReadOnlyList<LinhaOrcamento>>.Falha("A planilha não tem linhas de orçamento.");
        return Resultado<IReadOnlyList<LinhaOrcamento>>.Ok(res.Select(kv => new LinhaOrcamento(kv.Key.Item1, kv.Key.Item2, kv.Value)).ToList());
    }
}
