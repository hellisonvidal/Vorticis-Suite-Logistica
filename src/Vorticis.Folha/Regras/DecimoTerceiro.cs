using Vorticis.Domain.Comum;

namespace Vorticis.Folha.Regras;

/// <summary>13º salário: avos, 1ª parcela (50%, sem descontos) e 2ª parcela (diferença, com INSS e IRRF sobre o valor integral).</summary>
public static class CalculadoraDecimoTerceiro
{
    /// <summary>
    /// Avos do ano: meses em que o empregado trabalhou 15 dias ou mais (admissão e desligamento no ano contam pelos dias
    /// trabalhados no mês; Lei 4.090/62, art. 1º, § 2º).
    /// </summary>
    public static int Avos(DateOnly admissao, DateOnly? desligamento, int ano)
    {
        var avos = 0;
        for (var mes = 1; mes <= 12; mes++)
        {
            var ini = new DateOnly(ano, mes, 1);
            var fim = ini.AddMonths(1).AddDays(-1);
            var de = admissao > ini ? admissao : ini;
            var ate = desligamento is { } d && d < fim ? d : fim;
            if (ate >= de && ate.DayNumber - de.DayNumber + 1 >= 15) avos++;
        }
        return avos;
    }

    private static decimal Fixa(FuncionarioCalc f, ContextoCalculo ctx) => f.SalarioMensal + PeriodosFerias.AdicionalFixoMensal(f, ctx.Tabelas.SalarioMinimo);

    private static ItemCalculo Novo(ContextoCalculo ctx, string codigo, decimal referencia, decimal valor, string memoria)
    {
        var r = ctx.Rubrica(codigo);
        return new ItemCalculo(codigo, r.Descricao, r.Tipo, referencia, Dinheiro.Arredondar(valor), memoria, r.IncInss, r.IncIrrf, r.IncFgts, r.Variavel);
    }

    /// <summary>1ª parcela: metade do 13º proporcional sobre a remuneração fixa (sem médias), sem INSS nem IRRF; há FGTS.</summary>
    public static ResultadoFolha PrimeiraParcela(FuncionarioCalc f, int avos, ContextoCalculo ctx)
    {
        var fixa = Fixa(f, ctx);
        var valor = Dinheiro.Arredondar(fixa / 12m * avos / 2m);
        var aliq = CalculoEncargos.AliquotaFgts(f, ctx.Tabelas.Fgts);
        var fgts = Dinheiro.Arredondar(valor * aliq / 100m);
        var itens = new List<ItemCalculo>
        {
            Novo(ctx, RubricasPadrao.Decimo1, avos, valor, $"50% x ({Formato.Moeda(fixa)} ÷ 12) x {avos}/12 avos (remuneração fixa, sem médias)"),
            Novo(ctx, RubricasPadrao.BaseFgts, 0, valor, "1ª parcela do 13º"),
            Novo(ctx, RubricasPadrao.ValorFgts, aliq, fgts, $"{Formato.Moeda(valor)} x {Formato.Num(aliq)}%"),
        };
        return new ResultadoFolha(itens, 0, 0, valor, 0, 0, fgts, 0, "Sem INSS na 1ª parcela.", "Sem IRRF na 1ª parcela.");
    }

    /// <summary>
    /// 2ª parcela: 13º integral (remuneração + média das variáveis) ÷ 12 x avos; desconta a 1ª parcela, o INSS e o IRRF
    /// calculados sobre o valor integral (tributação exclusiva, tabela do mês). FGTS sobre o que ainda não foi depositado.
    /// </summary>
    public static ResultadoFolha SegundaParcela(FuncionarioCalc f, int avos, decimal mediaVariaveis, decimal primeiraParcelaPaga, ContextoCalculo ctx)
    {
        var fixa = Fixa(f, ctx);
        var total = Dinheiro.Arredondar((fixa + mediaVariaveis) / 12m * avos);
        var itens = new List<ItemCalculo>
        {
            Novo(ctx, RubricasPadrao.Decimo2, avos, total, $"({Formato.Moeda(fixa)} + média de variáveis {Formato.Moeda(mediaVariaveis)}) ÷ 12 x {avos}/12 avos"),
        };
        if (primeiraParcelaPaga > 0)
            itens.Add(Novo(ctx, RubricasPadrao.AdiantamentoDecimo, 0, primeiraParcelaPaga, "1ª parcela paga"));

        decimal inss = 0;
        var memInss = "Estagiário não contribui ao INSS.";
        if (!f.Estagiario) inss = CalculoInss.Calcular(total, ctx.Tabelas.Inss, out memInss);
        var r = CalculoIrrf.Calcular(total, inss, f.DependentesIrrf, 0, ctx.Tabelas.Irrf);
        if (inss > 0) itens.Add(Novo(ctx, RubricasPadrao.InssDecimo, 0, inss, memInss));
        if (r.Imposto > 0) itens.Add(Novo(ctx, RubricasPadrao.IrrfDecimo, 0, r.Imposto, r.Memoria));

        var baseFgts = Math.Max(0, total - primeiraParcelaPaga);
        var aliq = CalculoEncargos.AliquotaFgts(f, ctx.Tabelas.Fgts);
        var fgts = Dinheiro.Arredondar(baseFgts * aliq / 100m);
        itens.Add(Novo(ctx, RubricasPadrao.BaseInss, 0, total, "13º integral"));
        itens.Add(Novo(ctx, RubricasPadrao.BaseIrrf, 0, total, "13º integral"));
        itens.Add(Novo(ctx, RubricasPadrao.BaseFgts, 0, baseFgts, "13º integral menos a 1ª parcela (já com FGTS)"));
        itens.Add(Novo(ctx, RubricasPadrao.ValorFgts, aliq, fgts, $"{Formato.Moeda(baseFgts)} x {Formato.Num(aliq)}%"));
        return new ResultadoFolha(itens, total, total, baseFgts, inss, r.Imposto, fgts, 0, memInss, r.Memoria);
    }
}
