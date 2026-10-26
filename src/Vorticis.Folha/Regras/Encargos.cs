using Vorticis.Domain.Comum;

namespace Vorticis.Folha.Regras;

/// <summary>
/// Como a empresa paga a contribuição patronal: <see cref="Geral"/> (20% + RAT x FAP + terceiros), Simples Nacional
/// sem CPP (anexos I, II, III e V: a CPP está no DAS) e Simples Nacional com CPP (anexo IV: 20% + RAT, sem terceiros).
/// </summary>
public enum RegimeEncargos { Geral, SimplesSemCpp, SimplesComCpp }

public static class RegimeEncargosExtensoes
{
    /// <summary>Regime de encargos a partir do regime tributário (texto do banco) e do anexo do Simples.</summary>
    public static RegimeEncargos De(string? regimeTributario, string? anexoSimples) => regimeTributario switch
    {
        "SIMPLES_NACIONAL" => anexoSimples == "IV" ? RegimeEncargos.SimplesComCpp : RegimeEncargos.SimplesSemCpp,
        "MEI" => RegimeEncargos.SimplesSemCpp,
        _ => RegimeEncargos.Geral,
    };
}

/// <param name="RatAliquota">Alíquota do RAT em % (1, 2 ou 3).</param>
/// <param name="Fap">Fator Acidentário de Prevenção (0,5 a 2,0).</param>
/// <param name="TerceirosAliquota">Soma das contribuições a terceiros em % (FPAS 515 = 5,8).</param>
public sealed record ParametrosEncargos(decimal RatAliquota, decimal Fap, decimal TerceirosAliquota, RegimeEncargos Regime)
{
    public const decimal AliquotaPatronal = 20m;

    public decimal AliquotaInssPatronal => Regime == RegimeEncargos.SimplesSemCpp ? 0 : AliquotaPatronal;
    public decimal AliquotaRatAjustada => Regime == RegimeEncargos.SimplesSemCpp ? 0 : RatAliquota * Fap;
    public decimal AliquotaTerceiros => Regime == RegimeEncargos.Geral ? TerceirosAliquota : 0;
}

public sealed record EncargosPatronais(decimal InssPatronal, decimal Rat, decimal Terceiros, decimal Fgts)
{
    public decimal Previdenciarios => InssPatronal + Rat + Terceiros;
    public decimal Total => Previdenciarios + Fgts;
    public static readonly EncargosPatronais Zero = new(0, 0, 0, 0);
    public static EncargosPatronais operator +(EncargosPatronais a, EncargosPatronais b)
        => new(a.InssPatronal + b.InssPatronal, a.Rat + b.Rat, a.Terceiros + b.Terceiros, a.Fgts + b.Fgts);
}

public static class CalculoEncargos
{
    /// <summary>Encargos patronais sobre a base previdenciária; o FGTS é informado já calculado (depende do vínculo).</summary>
    public static EncargosPatronais Calcular(decimal baseInss, decimal fgts, ParametrosEncargos p, bool contribui = true)
    {
        if (!contribui || baseInss <= 0) return new EncargosPatronais(0, 0, 0, fgts);
        return new EncargosPatronais(
            Dinheiro.Arredondar(baseInss * p.AliquotaInssPatronal / 100m),
            Dinheiro.Arredondar(baseInss * p.AliquotaRatAjustada / 100m),
            Dinheiro.Arredondar(baseInss * p.AliquotaTerceiros / 100m),
            fgts);
    }

    public static decimal AliquotaFgts(FuncionarioCalc f, ParametrosFgts p) => f.Estagiario ? 0 : f.Aprendiz ? p.AliquotaAprendiz : p.Aliquota;
}

/// <summary>Provisão mensal de férias (1/12 + 1/3) e de 13º salário (1/12) com encargos.</summary>
public sealed record ProvisaoMensal(decimal Base, decimal Ferias, decimal Decimo, EncargosPatronais EncargosFerias, EncargosPatronais EncargosDecimo)
{
    public decimal Total => Ferias + Decimo + EncargosFerias.Total + EncargosDecimo.Total;
}

public static class CalculoProvisoes
{
    /// <summary>
    /// <paramref name="remuneracaoBase"/>: salário + adicionais fixos + média das variáveis. <paramref name="fracaoMes"/>: 1 para
    /// mês completo; 0 quando o funcionário trabalhou menos de 15 dias no mês (não forma avo).
    /// </summary>
    public static ProvisaoMensal Calcular(FuncionarioCalc funcionario, decimal remuneracaoBase, decimal fracaoMes, ParametrosEncargos encargos, ParametrosFgts fgts)
    {
        var ferias = Dinheiro.Arredondar(remuneracaoBase / 12m * 4m / 3m * fracaoMes);
        var decimo = Dinheiro.Arredondar(remuneracaoBase / 12m * fracaoMes);
        var aliqFgts = CalculoEncargos.AliquotaFgts(funcionario, fgts);
        var contribui = !funcionario.Estagiario;
        return new ProvisaoMensal(Dinheiro.Arredondar(remuneracaoBase), ferias, decimo,
            CalculoEncargos.Calcular(ferias, Dinheiro.Arredondar(ferias * aliqFgts / 100m), encargos, contribui),
            CalculoEncargos.Calcular(decimo, Dinheiro.Arredondar(decimo * aliqFgts / 100m), encargos, contribui));
    }
}
