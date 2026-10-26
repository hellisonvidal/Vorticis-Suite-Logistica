using Vorticis.Domain.Comum;
using Vorticis.Fiscal.Comum;

namespace Vorticis.Fiscal.Simples;

/// <summary>Atividade da receita, que define o anexo (o Fator R decide entre III e V).</summary>
public enum AtividadeSimples
{
    /// <summary>Revenda de mercadorias (Anexo I).</summary>
    ComercioRevenda,
    /// <summary>Venda de produtos industrializados (Anexo II).</summary>
    IndustriaVenda,
    /// <summary>Serviços do Anexo III (ex.: locação de bens móveis, manutenção, agências de viagem).</summary>
    ServicosAnexoIII,
    /// <summary>Serviços do Anexo IV (construção, vigilância, limpeza, advocacia).</summary>
    ServicosAnexoIV,
    /// <summary>Serviços do Anexo V sem Fator R.</summary>
    ServicosAnexoV,
    /// <summary>Serviços intelectuais sujeitos ao Fator R: Anexo III se folha/RBT12 ≥ 28%, senão Anexo V.</summary>
    ServicosFatorR,
}

/// <summary>Receita com tributação reduzida: o tributo é retirado do percentual do DAS (Res. CGSN 140/2018, art. 25).</summary>
[Flags]
public enum TratamentoSimples
{
    Normal = 0,
    /// <summary>Substituição tributária (ou isenção) do ICMS: exclui a parcela do ICMS.</summary>
    SubstituicaoTributaria = 1,
    /// <summary>Tributação monofásica de PIS/COFINS: exclui as parcelas de PIS e COFINS.</summary>
    Monofasico = 2,
    /// <summary>ISS retido pelo tomador (ou fixo/isento): exclui a parcela do ISS.</summary>
    IssRetido = 4,
    /// <summary>Exportação: exclui ICMS, IPI, ISS, PIS e COFINS (permanecem IRPJ, CSLL e CPP).</summary>
    Exportacao = 8,
}

public sealed record ReceitaSimples(AtividadeSimples Atividade, decimal Valor, TratamentoSimples Tratamento = TratamentoSimples.Normal, string? Descricao = null);

public sealed record EntradaSimples(DateOnly Competencia, IReadOnlyList<ReceitaSimples> Receitas, decimal Rbt12, decimal Folha12);

public sealed record SegmentoSimples(
    AtividadeSimples Atividade, AnexoSimples Anexo, TratamentoSimples Tratamento, decimal Receita, int Faixa, decimal AliquotaNominal,
    decimal ParcelaDeduzir, decimal AliquotaEfetiva, decimal AliquotaAplicada, decimal ValorDas, IReadOnlyDictionary<string, decimal> Tributos);

public sealed record ResultadoSimples(
    DateOnly Competencia, decimal Rbt12, decimal Folha12, decimal FatorR, decimal ReceitaMes, IReadOnlyList<SegmentoSimples> Segmentos,
    decimal ValorDas, DateOnly Vencimento, IReadOnlyList<string> Avisos, IReadOnlyList<string> Memoria)
{
    /// <summary>Total do DAS por tributo (IRPJ, CSLL, COFINS, PIS, CPP, ICMS, ISS, IPI).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyDictionary<string, decimal> TotalPorTributo =>
        Segmentos.SelectMany(s => s.Tributos).GroupBy(t => t.Key).ToDictionary(g => g.Key, g => g.Sum(t => t.Value));

    public bool AbaixoDoMinimo(decimal minimo) => ValorDas > 0 && ValorDas < minimo;
}

/// <summary>
/// Cálculo do DAS do Simples Nacional. Regras (LC 123/2006, art. 18, e Res. CGSN 140/2018):
/// alíquota efetiva = (RBT12 × alíquota nominal − parcela a deduzir) ÷ RBT12, aplicada à receita do mês; o DAS é repartido
/// pelos tributos conforme a faixa, e as receitas com tributação reduzida (ST, monofásico, ISS retido, exportação) perdem as
/// parcelas dos tributos correspondentes. Pura: toda tabela vem de <see cref="TabelaSimples"/>.
/// </summary>
public static class CalculadoraSimples
{
    /// <summary>Casas decimais da alíquota efetiva, em percentual (ex.: 5,3200).</summary>
    public const int CasasAliquotaEfetiva = 4;

    public static AnexoSimples Anexo(AtividadeSimples atividade, decimal fatorR, decimal limiteFatorR) => atividade switch
    {
        AtividadeSimples.ComercioRevenda => AnexoSimples.I,
        AtividadeSimples.IndustriaVenda => AnexoSimples.II,
        AtividadeSimples.ServicosAnexoIII => AnexoSimples.III,
        AtividadeSimples.ServicosAnexoIV => AnexoSimples.IV,
        AtividadeSimples.ServicosAnexoV => AnexoSimples.V,
        _ => fatorR >= limiteFatorR ? AnexoSimples.III : AnexoSimples.V,
    };

    public static decimal FatorR(decimal folha12, decimal rbt12) => rbt12 > 0 ? Math.Round(folha12 / rbt12, 4, MidpointRounding.AwayFromZero) : 0m;

    /// <summary>Alíquota efetiva em percentual; sem RBT12 (início de atividade sem receita), vale a nominal da primeira faixa.</summary>
    public static decimal AliquotaEfetiva(FaixaSimples faixa, decimal rbt12) => rbt12 <= 0
        ? faixa.Aliquota
        : Math.Round((rbt12 * faixa.Aliquota / 100m - faixa.Deduzir) / rbt12 * 100m, CasasAliquotaEfetiva, MidpointRounding.AwayFromZero);

    public static IReadOnlySet<string> TributosExcluidos(TratamentoSimples t, bool acimaSublimite)
    {
        var s = new HashSet<string>();
        if (t.HasFlag(TratamentoSimples.SubstituicaoTributaria)) s.Add("ICMS");
        if (t.HasFlag(TratamentoSimples.Monofasico)) { s.Add("PIS"); s.Add("COFINS"); }
        if (t.HasFlag(TratamentoSimples.IssRetido)) s.Add("ISS");
        if (t.HasFlag(TratamentoSimples.Exportacao)) { s.Add("ICMS"); s.Add("IPI"); s.Add("ISS"); s.Add("PIS"); s.Add("COFINS"); }
        if (acimaSublimite) { s.Add("ICMS"); s.Add("ISS"); }   // acima do sublimite, ICMS e ISS são recolhidos fora do DAS
        return s;
    }

    public static ResultadoSimples Calcular(TabelaSimples tabela, EntradaSimples e)
    {
        var avisos = new List<string>();
        var memoria = new List<string>();
        var rbt12 = e.Rbt12;
        var fatorR = FatorR(e.Folha12, rbt12);
        var acimaSublimite = rbt12 > tabela.SublimiteIcmsIss;
        if (rbt12 > tabela.LimiteRbt12)
            avisos.Add($"RBT12 de {Formato.Moeda(rbt12)} excede o limite do Simples Nacional ({Formato.Moeda(tabela.LimiteRbt12)}): a empresa deve comunicar a exclusão do regime.");
        if (acimaSublimite && rbt12 <= tabela.LimiteRbt12)
            avisos.Add($"RBT12 acima do sublimite de {Formato.Moeda(tabela.SublimiteIcmsIss)}: ICMS e ISS devem ser recolhidos fora do DAS (aqui excluídos). Apure-os separadamente.");
        if (e.Receitas.Any(r => r.Valor < 0)) avisos.Add("Há receita negativa (devoluções/cancelamentos): confira a segregação.");

        memoria.Add($"Competência {e.Competencia:MM/yyyy} · RBT12 {Formato.Moeda(rbt12)} · folha dos 12 meses {Formato.Moeda(e.Folha12)} · Fator R {(fatorR * 100):0.00}%");

        var segmentos = new List<SegmentoSimples>();
        var grupos = e.Receitas.Where(r => r.Valor != 0)
            .GroupBy(r => (Anexo: Anexo(r.Atividade, fatorR, tabela.LimiteFatorR), r.Atividade, r.Tratamento))
            .OrderBy(g => g.Key.Anexo).ThenBy(g => g.Key.Tratamento);
        foreach (var g in grupos)
        {
            var receita = g.Sum(r => r.Valor);
            var faixa = tabela.Faixa(g.Key.Anexo, rbt12);
            var numeroFaixa = tabela.NumeroFaixa(g.Key.Anexo, rbt12);
            var efetiva = AliquotaEfetiva(faixa, rbt12);
            var excluidos = TributosExcluidos(g.Key.Tratamento, acimaSublimite);
            var incluidos = faixa.Reparticao.Where(x => !excluidos.Contains(x.Key)).ToList();
            var soma = incluidos.Sum(x => x.Value);
            var aplicada = efetiva * soma / 100m;
            // DAS do segmento arredondado uma vez; a repartição entre os tributos fecha exatamente nesse valor (maior resto).
            var das = Dinheiro.Arredondar(Math.Abs(receita) * aplicada / 100m);
            var partes = soma > 0 ? Dinheiro.Ratear(das, incluidos.Select(x => x.Value).ToList()) : incluidos.Select(_ => 0m).ToList();
            var sinal = receita < 0 ? -1m : 1m;
            var tributos = new Dictionary<string, decimal>();
            for (var i = 0; i < incluidos.Count; i++) tributos[incluidos[i].Key] = partes[i] * sinal;
            das *= sinal;
            segmentos.Add(new SegmentoSimples(g.Key.Atividade, g.Key.Anexo, g.Key.Tratamento, receita, numeroFaixa, faixa.Aliquota, faixa.Deduzir, efetiva,
                Math.Round(aplicada, CasasAliquotaEfetiva + 2, MidpointRounding.AwayFromZero), das, tributos));
            var trat = g.Key.Tratamento == TratamentoSimples.Normal ? "" : $" [{g.Key.Tratamento}]";
            memoria.Add($"Anexo {g.Key.Anexo}{trat}: receita {Formato.Moeda(receita)} · {numeroFaixa}ª faixa ({faixa.Aliquota:0.00}% − {Formato.Moeda(faixa.Deduzir)}) · " +
                        $"alíquota efetiva {efetiva:0.0000}% · aplicada {aplicada:0.0000}% · DAS {Formato.Moeda(das)}");
            if (g.Key.Atividade == AtividadeSimples.ServicosFatorR)
                memoria.Add($"  Fator R {(fatorR * 100):0.00}% {(fatorR >= tabela.LimiteFatorR ? "≥" : "<")} {(tabela.LimiteFatorR * 100):0}%: Anexo {g.Key.Anexo}.");
        }

        var total = segmentos.Sum(s => s.ValorDas);
        var venc = Calendario.ProximoDiaUtil(Calendario.DiaDoMesSeguinte(e.Competencia, 20));
        memoria.Add($"DAS total {Formato.Moeda(total)} · vencimento {venc:dd/MM/yyyy}");
        if (total > 0 && total < tabela.MinimoDas)
            avisos.Add($"DAS de {Formato.Moeda(total)} abaixo do mínimo de {Formato.Moeda(tabela.MinimoDas)}: o valor é somado ao do mês seguinte.");
        return new ResultadoSimples(e.Competencia, rbt12, e.Folha12, fatorR, e.Receitas.Sum(r => r.Valor), segmentos, total, venc, avisos, memoria);
    }

    // ------------------------------------------------------------------ RBT12 e folha dos 12 meses

    public sealed record Acumulado(decimal Valor, bool Proporcional, IReadOnlyList<DateOnly> MesesSemDado);

    /// <summary>
    /// Soma dos 12 meses anteriores à competência. Empresa com menos de 12 meses de atividade: média dos meses de atividade × 12
    /// (art. 2º, §§ 1º e 2º da Res. CGSN 140/2018); no primeiro mês, o valor do próprio mês × 12. Meses esperados sem lançamento
    /// em <paramref name="serie"/> contam como zero e são devolvidos para aviso.
    /// </summary>
    public static Acumulado Acumular12Meses(DateOnly competencia, DateOnly inicioAtividade, IReadOnlyDictionary<DateOnly, decimal> serie, decimal valorDoMes)
    {
        var comp = new DateOnly(competencia.Year, competencia.Month, 1);
        var inicio = new DateOnly(inicioAtividade.Year, inicioAtividade.Month, 1);
        var mesesAtividade = Math.Clamp((comp.Year - inicio.Year) * 12 + comp.Month - inicio.Month, 0, 12);
        if (mesesAtividade == 0) return new Acumulado(Dinheiro.Arredondar(valorDoMes * 12m), true, []);

        var meses = Enumerable.Range(1, mesesAtividade).Select(i => comp.AddMonths(-i)).ToList();
        var semDado = meses.Where(m => !serie.ContainsKey(m)).OrderBy(m => m).ToList();
        var soma = meses.Sum(m => serie.TryGetValue(m, out var v) ? v : 0m);
        return mesesAtividade < 12
            ? new Acumulado(Dinheiro.Arredondar(soma / mesesAtividade * 12m), true, semDado)
            : new Acumulado(soma, false, semDado);
    }
}
