using System.Globalization;
using System.Text.Json;
using Vorticis.Domain.Comum;
using Vorticis.Fiscal.Comum;

namespace Vorticis.Fiscal.Lucro;

public sealed record PresuncaoAtividade(string Codigo, string Descricao, decimal Irpj, decimal Csll);

/// <summary>Alíquotas, adicional, limite de compensação, PIS/COFINS e percentuais de presunção, por vigência.</summary>
public sealed record ParametrosLucro(
    DateOnly Vigencia, string Fundamento, decimal IrpjAliquota, decimal IrpjAdicionalAliquota, decimal IrpjAdicionalLimiteMensal, decimal CsllAliquota,
    decimal LimiteCompensacao, decimal PisCumulativo, decimal CofinsCumulativo, decimal PisNaoCumulativo, decimal CofinsNaoCumulativo,
    IReadOnlyList<PresuncaoAtividade> Presuncao)
{
    public PresuncaoAtividade? Atividade(string codigo) => Presuncao.FirstOrDefault(p => p.Codigo == codigo);

    public static ParametrosLucro Carregar(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        decimal D(JsonElement e, string nome) => e.GetProperty(nome).GetDecimal();
        var pc = r.GetProperty("pis_cofins");
        return new ParametrosLucro(
            DateOnly.ParseExact(r.GetProperty("vigencia_inicio").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture), r.GetProperty("fundamento").GetString() ?? "",
            D(r.GetProperty("irpj"), "aliquota"), D(r.GetProperty("irpj"), "adicional_aliquota"), D(r.GetProperty("irpj"), "adicional_limite_mensal"),
            D(r.GetProperty("csll"), "aliquota"), D(r, "compensacao_prejuizo_limite"),
            D(pc.GetProperty("cumulativo"), "pis"), D(pc.GetProperty("cumulativo"), "cofins"),
            D(pc.GetProperty("nao_cumulativo"), "pis"), D(pc.GetProperty("nao_cumulativo"), "cofins"),
            r.GetProperty("presuncao").EnumerateArray().Select(p => new PresuncaoAtividade(p.GetProperty("codigo").GetString()!,
                p.GetProperty("descricao").GetString() ?? "", D(p, "irpj"), D(p, "csll"))).ToList());
    }
}

public enum RegimeLucro { Presumido, Real }

public sealed record ReceitaPresumida(string Atividade, decimal Valor);

public sealed record EntradaPresumido(int Ano, int Trimestre, IReadOnlyList<ReceitaPresumida> Receitas, decimal OutrasReceitas, decimal IrrfCompensavel,
                                      decimal CsllRetida, decimal OutrasDeducoesIrpj = 0);

public sealed record EntradaReal(int Ano, int Trimestre, decimal LucroContabil, decimal AdicoesIrpj, decimal ExclusoesIrpj, decimal AdicoesCsll,
                                 decimal ExclusoesCsll, decimal SaldoPrejuizoIrpj, decimal SaldoBaseNegativaCsll, decimal IrrfCompensavel, decimal CsllRetida,
                                 decimal OutrasDeducoesIrpj = 0, decimal ReceitaBruta = 0);

/// <summary>Resultado trimestral de IRPJ e CSLL (Presumido ou Real) com a memória de cálculo.</summary>
public sealed record ResultadoLucro(
    RegimeLucro Regime, int Ano, int Trimestre, DateOnly Inicio, DateOnly Fim, decimal ReceitaBruta, decimal? LucroContabil,
    decimal AdicoesIrpj, decimal ExclusoesIrpj, decimal AdicoesCsll, decimal ExclusoesCsll, decimal CompensacaoIrpj, decimal CompensacaoCsll,
    decimal PrejuizoIrpj, decimal PrejuizoCsll, decimal BaseIrpj, decimal BaseCsll, decimal IrpjNormal, decimal IrpjAdicional, decimal IrpjDeducoes,
    decimal IrpjAPagar, decimal CsllDevida, decimal CsllDeducoes, decimal CsllAPagar, DateOnly Vencimento, IReadOnlyList<string> Avisos,
    IReadOnlyList<string> Memoria)
{
    public decimal IrpjTotal => IrpjNormal + IrpjAdicional;
}

public enum RegimePisCofins { Cumulativo, NaoCumulativo }

public sealed record EntradaPisCofins(DateOnly Competencia, RegimePisCofins Regime, decimal ReceitaBase, decimal BaseCreditos, decimal PisRetido, decimal CofinsRetido);

public sealed record ResultadoPisCofins(
    DateOnly Competencia, RegimePisCofins Regime, decimal ReceitaBase, decimal AliquotaPis, decimal AliquotaCofins, decimal PisDebito, decimal CofinsDebito,
    decimal BaseCreditos, decimal PisCredito, decimal CofinsCredito, decimal PisRetido, decimal CofinsRetido, decimal PisAPagar, decimal CofinsAPagar,
    decimal SaldoCredorPis, decimal SaldoCredorCofins, DateOnly Vencimento, IReadOnlyList<string> Avisos, IReadOnlyList<string> Memoria);

/// <summary>
/// IRPJ e CSLL trimestrais (Lei 9.430/1996) e PIS/COFINS mensais. Presumido: base = receita × percentual de presunção da atividade + outras
/// receitas. Real: base = lucro contábil ± LALUR/LACS, com compensação de prejuízos limitada a 30% do lucro. IRPJ 15% + adicional de 10% sobre
/// o que passar de R$ 20 mil por mês do período; CSLL 9%. Pura: percentuais e alíquotas vêm de <see cref="ParametrosLucro"/>.
/// </summary>
public static class CalculadoraLucro
{
    public static (DateOnly Inicio, DateOnly Fim) Trimestre(int ano, int trimestre)
    {
        var inicio = new DateOnly(ano, (trimestre - 1) * 3 + 1, 1);
        return (inicio, inicio.AddMonths(3).AddDays(-1));
    }

    /// <summary>Cota única: último dia útil do mês seguinte ao fim do trimestre.</summary>
    public static DateOnly VencimentoTrimestral(int ano, int trimestre)
    {
        var mes = new DateOnly(ano, trimestre * 3, 1).AddMonths(1);
        return Calendario.UltimoDiaUtilDoMes(mes.Year, mes.Month);
    }

    private static decimal Pct(decimal valor, decimal percentual) => Dinheiro.Arredondar(valor * percentual / 100m);

    /// <summary>IRPJ normal e adicional sobre a base trimestral.</summary>
    public static (decimal Normal, decimal Adicional) Irpj(ParametrosLucro p, decimal baseCalculo)
    {
        if (baseCalculo <= 0) return (0, 0);
        var limite = p.IrpjAdicionalLimiteMensal * 3m;
        return (Pct(baseCalculo, p.IrpjAliquota), baseCalculo > limite ? Pct(baseCalculo - limite, p.IrpjAdicionalAliquota) : 0m);
    }

    public static ResultadoLucro CalcularPresumido(ParametrosLucro p, EntradaPresumido e)
    {
        var (ini, fim) = Trimestre(e.Ano, e.Trimestre);
        var avisos = new List<string>();
        var memoria = new List<string> { $"Lucro Presumido · {e.Trimestre}º trimestre de {e.Ano} ({ini:dd/MM/yyyy} a {fim:dd/MM/yyyy})" };
        decimal baseIrpj = 0, baseCsll = 0, receita = 0;
        foreach (var r in e.Receitas.Where(r => r.Valor != 0).GroupBy(r => r.Atividade))
        {
            var atividade = p.Atividade(r.Key);
            if (atividade is null) { avisos.Add($"Atividade de presunção desconhecida: {r.Key}."); continue; }
            var valor = r.Sum(x => x.Valor);
            var bi = Pct(valor, atividade.Irpj);
            var bc = Pct(valor, atividade.Csll);
            baseIrpj += bi;
            baseCsll += bc;
            receita += valor;
            memoria.Add($"{atividade.Descricao}: receita {Formato.Moeda(valor)} × {atividade.Irpj:0.##}% = base IRPJ {Formato.Moeda(bi)} · × {atividade.Csll:0.##}% = base CSLL {Formato.Moeda(bc)}");
        }
        if (e.OutrasReceitas != 0)
        {
            baseIrpj += e.OutrasReceitas;
            baseCsll += e.OutrasReceitas;
            memoria.Add($"Outras receitas (ganhos de capital, rendimentos financeiros etc.) integram 100% das bases: {Formato.Moeda(e.OutrasReceitas)}");
        }
        if (receita + e.OutrasReceitas <= 0) avisos.Add("Sem receita no trimestre.");

        var (normal, adicional) = Irpj(p, baseIrpj);
        var deducoesIrpj = e.IrrfCompensavel + e.OutrasDeducoesIrpj;
        var irpjAPagar = Math.Max(0, normal + adicional - deducoesIrpj);
        var csll = baseCsll > 0 ? Pct(baseCsll, p.CsllAliquota) : 0m;
        var csllAPagar = Math.Max(0, csll - e.CsllRetida);
        if (deducoesIrpj > normal + adicional && deducoesIrpj > 0) avisos.Add("IRRF e deduções superam o IRPJ devido: o excedente é saldo a compensar.");
        if (e.CsllRetida > csll && e.CsllRetida > 0) avisos.Add("CSLL retida supera a devida: o excedente é saldo a compensar.");
        memoria.Add($"IRPJ: base {Formato.Moeda(baseIrpj)} × {p.IrpjAliquota:0.##}% = {Formato.Moeda(normal)}; adicional {p.IrpjAdicionalAliquota:0.##}% sobre o que passa de {Formato.Moeda(p.IrpjAdicionalLimiteMensal * 3)} = {Formato.Moeda(adicional)}; (−) IRRF e deduções {Formato.Moeda(deducoesIrpj)} = a pagar {Formato.Moeda(irpjAPagar)}");
        memoria.Add($"CSLL: base {Formato.Moeda(baseCsll)} × {p.CsllAliquota:0.##}% = {Formato.Moeda(csll)}; (−) retida {Formato.Moeda(e.CsllRetida)} = a pagar {Formato.Moeda(csllAPagar)}");
        var venc = VencimentoTrimestral(e.Ano, e.Trimestre);
        return new ResultadoLucro(RegimeLucro.Presumido, e.Ano, e.Trimestre, ini, fim, receita, null, 0, 0, 0, 0, 0, 0, 0, 0, baseIrpj, baseCsll, normal, adicional,
            deducoesIrpj, irpjAPagar, csll, e.CsllRetida, csllAPagar, venc, avisos, memoria);
    }

    /// <summary>Compensação de prejuízos: até <paramref name="limite"/>% do lucro (antes da compensação) e até o saldo disponível.</summary>
    public static decimal Compensacao(decimal lucroAntes, decimal saldo, decimal limite) =>
        lucroAntes <= 0 || saldo <= 0 ? 0m : Math.Min(saldo, Pct(lucroAntes, limite));

    public static ResultadoLucro CalcularReal(ParametrosLucro p, EntradaReal e)
    {
        var (ini, fim) = Trimestre(e.Ano, e.Trimestre);
        var avisos = new List<string>();
        var memoria = new List<string> { $"Lucro Real trimestral · {e.Trimestre}º trimestre de {e.Ano} ({ini:dd/MM/yyyy} a {fim:dd/MM/yyyy})" };
        var lucroIrpj = e.LucroContabil + e.AdicoesIrpj - e.ExclusoesIrpj;
        var lucroCsll = e.LucroContabil + e.AdicoesCsll - e.ExclusoesCsll;
        memoria.Add($"Lucro contábil antes do IRPJ/CSLL {Formato.Moeda(e.LucroContabil)}; IRPJ: (+) adições {Formato.Moeda(e.AdicoesIrpj)} (−) exclusões {Formato.Moeda(e.ExclusoesIrpj)} = lucro antes da compensação {Formato.Moeda(lucroIrpj)}");
        var compIrpj = Compensacao(lucroIrpj, e.SaldoPrejuizoIrpj, p.LimiteCompensacao);
        var compCsll = Compensacao(lucroCsll, e.SaldoBaseNegativaCsll, p.LimiteCompensacao);
        var baseIrpj = Math.Max(0, lucroIrpj - compIrpj);
        var baseCsll = Math.Max(0, lucroCsll - compCsll);
        var prejIrpj = lucroIrpj < 0 ? -lucroIrpj : 0m;
        var prejCsll = lucroCsll < 0 ? -lucroCsll : 0m;
        if (e.SaldoPrejuizoIrpj > 0 && lucroIrpj > 0)
            memoria.Add($"Compensação de prejuízos fiscais: saldo {Formato.Moeda(e.SaldoPrejuizoIrpj)}, limite de {p.LimiteCompensacao:0.##}% do lucro = {Formato.Moeda(Pct(lucroIrpj, p.LimiteCompensacao))} → compensado {Formato.Moeda(compIrpj)}");
        if (prejIrpj > 0) memoria.Add($"Prejuízo fiscal do trimestre de {Formato.Moeda(prejIrpj)} (vai para a Parte B do LALUR).");
        memoria.Add($"CSLL: (+) adições {Formato.Moeda(e.AdicoesCsll)} (−) exclusões {Formato.Moeda(e.ExclusoesCsll)} = {Formato.Moeda(lucroCsll)}; base negativa compensada {Formato.Moeda(compCsll)} → base {Formato.Moeda(baseCsll)}");

        var (normal, adicional) = Irpj(p, baseIrpj);
        var deducoes = e.IrrfCompensavel + e.OutrasDeducoesIrpj;
        var irpjAPagar = Math.Max(0, normal + adicional - deducoes);
        var csll = Pct(baseCsll, p.CsllAliquota);
        var csllAPagar = Math.Max(0, csll - e.CsllRetida);
        if (deducoes > normal + adicional && deducoes > 0) avisos.Add("IRRF e deduções superam o IRPJ devido: o excedente é saldo a compensar.");
        memoria.Add($"IRPJ: lucro real {Formato.Moeda(baseIrpj)} × {p.IrpjAliquota:0.##}% = {Formato.Moeda(normal)}; adicional {Formato.Moeda(adicional)}; (−) IRRF e deduções {Formato.Moeda(deducoes)} = a pagar {Formato.Moeda(irpjAPagar)}");
        memoria.Add($"CSLL: {Formato.Moeda(baseCsll)} × {p.CsllAliquota:0.##}% = {Formato.Moeda(csll)}; (−) retida {Formato.Moeda(e.CsllRetida)} = a pagar {Formato.Moeda(csllAPagar)}");
        return new ResultadoLucro(RegimeLucro.Real, e.Ano, e.Trimestre, ini, fim, e.ReceitaBruta, e.LucroContabil, e.AdicoesIrpj, e.ExclusoesIrpj, e.AdicoesCsll,
            e.ExclusoesCsll, compIrpj, compCsll, prejIrpj, prejCsll, baseIrpj, baseCsll, normal, adicional, deducoes, irpjAPagar, csll, e.CsllRetida, csllAPagar,
            VencimentoTrimestral(e.Ano, e.Trimestre), avisos, memoria);
    }

    public static ResultadoPisCofins CalcularPisCofins(ParametrosLucro p, EntradaPisCofins e)
    {
        var cumulativo = e.Regime == RegimePisCofins.Cumulativo;
        var aliqPis = cumulativo ? p.PisCumulativo : p.PisNaoCumulativo;
        var aliqCofins = cumulativo ? p.CofinsCumulativo : p.CofinsNaoCumulativo;
        var pisDeb = Pct(e.ReceitaBase, aliqPis);
        var cofinsDeb = Pct(e.ReceitaBase, aliqCofins);
        var pisCred = cumulativo ? 0m : Pct(e.BaseCreditos, aliqPis);
        var cofinsCred = cumulativo ? 0m : Pct(e.BaseCreditos, aliqCofins);
        var pis = pisDeb - pisCred - e.PisRetido;
        var cofins = cofinsDeb - cofinsCred - e.CofinsRetido;
        var memoria = new List<string>
        {
            $"PIS/COFINS {(cumulativo ? "cumulativo" : "não cumulativo")} · competência {e.Competencia:MM/yyyy}",
            $"Receita da base {Formato.Moeda(e.ReceitaBase)} × PIS {aliqPis:0.00##}% = {Formato.Moeda(pisDeb)} · × COFINS {aliqCofins:0.00##}% = {Formato.Moeda(cofinsDeb)}",
        };
        if (!cumulativo)
            memoria.Add($"Créditos sobre {Formato.Moeda(e.BaseCreditos)}: PIS {Formato.Moeda(pisCred)} · COFINS {Formato.Moeda(cofinsCred)}");
        memoria.Add($"Retenções na fonte: PIS {Formato.Moeda(e.PisRetido)} · COFINS {Formato.Moeda(e.CofinsRetido)} → a pagar PIS {Formato.Moeda(Math.Max(0, pis))} · COFINS {Formato.Moeda(Math.Max(0, cofins))}");
        var avisos = new List<string>();
        if (pis < 0 || cofins < 0) avisos.Add("Há saldo credor de PIS/COFINS no mês: transporte-o para o mês seguinte (a compensação não é controlada aqui).");
        return new ResultadoPisCofins(e.Competencia, e.Regime, e.ReceitaBase, aliqPis, aliqCofins, pisDeb, cofinsDeb, e.BaseCreditos, pisCred, cofinsCred, e.PisRetido,
            e.CofinsRetido, Math.Max(0, pis), Math.Max(0, cofins), Math.Max(0, -pis), Math.Max(0, -cofins), VencimentoPisCofins(e.Competencia), avisos, memoria);
    }

    /// <summary>Dia 25 do mês seguinte; se não for dia útil, antecipa para o dia útil anterior.</summary>
    public static DateOnly VencimentoPisCofins(DateOnly competencia) => Calendario.DiaUtilAnterior(Calendario.DiaDoMesSeguinte(competencia, 25));
}
