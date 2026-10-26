using Vorticis.Domain.Comum;

namespace Vorticis.Folha.Regras;

/// <summary>Período aquisitivo de férias (12 meses a partir da admissão) e o concessivo (os 12 meses seguintes).</summary>
public sealed record PeriodoAquisitivo(int Numero, DateOnly Inicio, DateOnly Fim, DateOnly FimConcessivo)
{
    public bool Completo(DateOnly em) => em > Fim;
    public bool ConcessivoVencido(DateOnly em) => em > FimConcessivo;
}

public static class PeriodosFerias
{
    /// <summary>Períodos aquisitivos iniciados até <paramref name="ate"/> (o último pode estar incompleto).</summary>
    public static IReadOnlyList<PeriodoAquisitivo> Calcular(DateOnly admissao, DateOnly ate)
    {
        var res = new List<PeriodoAquisitivo>();
        for (var n = 1; ; n++)
        {
            var ini = admissao.AddYears(n - 1);
            if (ini > ate) break;
            res.Add(new PeriodoAquisitivo(n, ini, ini.AddYears(1).AddDays(-1), ini.AddYears(2).AddDays(-1)));
        }
        return res;
    }

    /// <summary>
    /// Dias de férias a que o empregado tem direito conforme as faltas injustificadas no período aquisitivo
    /// (CLT, art. 130): até 5 faltas, 30 dias; 6 a 14, 24; 15 a 23, 18; 24 a 32, 12; acima disso, perde o direito.
    /// </summary>
    public static int DiasDireito(int faltasInjustificadas) => faltasInjustificadas switch
    {
        <= 5 => 30,
        <= 14 => 24,
        <= 23 => 18,
        <= 32 => 12,
        _ => 0,
    };

    /// <summary>Média simples das verbas variáveis: soma dos meses considerados dividida pelo número de meses (até 12).</summary>
    public static decimal MediaSimples(decimal somaVariaveis, int meses) => meses <= 0 ? 0 : Dinheiro.Arredondar(somaVariaveis / Math.Min(12, meses));

    /// <summary>Remuneração mensal de referência: salário + adicional de insalubridade/periculosidade + média das variáveis.</summary>
    public static decimal Remuneracao(FuncionarioCalc f, decimal salarioMinimo, decimal mediaVariaveis)
        => Dinheiro.Arredondar(f.SalarioMensal + AdicionalFixoMensal(f, salarioMinimo) + mediaVariaveis);

    /// <summary>Insalubridade (% do salário mínimo) ou periculosidade (30% do salário): o maior, sem acumular.</summary>
    public static decimal AdicionalFixoMensal(FuncionarioCalc f, decimal salarioMinimo)
    {
        var insal = f.InsalubridadePercentual * salarioMinimo / 100m;
        var peric = f.Periculosidade ? f.SalarioMensal * CalculadoraFolhaMensal.PericulosidadePercentual / 100m : 0;
        return Dinheiro.Arredondar(Math.Max(insal, peric));
    }
}

/// <param name="DiasJaUsados">Dias do mesmo período aquisitivo já gozados ou vendidos em outras solicitações (fracionamento).</param>
public sealed record SolicitacaoFerias(DateOnly GozoInicio, int DiasGozo, int AbonoDias, int FaltasInjustificadas, decimal MediaVariaveis, int DiasJaUsados = 0);

public static class CalculadoraFerias
{
    /// <summary>Valida a solicitação; devolve os erros (vazio = válida).</summary>
    public static IReadOnlyList<string> Validar(FuncionarioCalc f, SolicitacaoFerias s)
    {
        var erros = new List<string>();
        var direito = PeriodosFerias.DiasDireito(s.FaltasInjustificadas);
        if (direito == 0) erros.Add("Mais de 32 faltas injustificadas no período aquisitivo: o empregado perdeu o direito às férias (CLT, art. 130).");
        if (s.DiasGozo < 5) erros.Add("O período de gozo deve ter ao menos 5 dias.");
        if (s.AbonoDias < 0 || s.AbonoDias > direito / 3) erros.Add($"O abono pecuniário é de no máximo 1/3 dos dias de direito ({direito / 3} dias).");
        if (direito > 0 && s.DiasGozo + s.AbonoDias + s.DiasJaUsados > direito)
            erros.Add($"Dias de gozo ({s.DiasGozo}) + abono ({s.AbonoDias}) + já utilizados ({s.DiasJaUsados}) passam dos {direito} dias de direito.");
        if (f.Admissao > s.GozoInicio) erros.Add("O gozo não pode começar antes da admissão.");
        return erros;
    }

    /// <summary>
    /// Férias: dias gozados x remuneração/30, mais 1/3 constitucional; abono pecuniário (e seu 1/3) indenizatório, sem INSS,
    /// IRRF nem FGTS. INSS e IRRF incidem sobre as férias gozadas + 1/3; FGTS também.
    /// </summary>
    public static ResultadoFolha Calcular(FuncionarioCalc f, SolicitacaoFerias s, ContextoCalculo ctx)
    {
        var erros = Validar(f, s);
        if (erros.Count > 0) throw new ErroCalculoFolhaException($"{f.Nome}: {string.Join(" ", erros)}");

        var remuneracao = PeriodosFerias.Remuneracao(f, ctx.Tabelas.SalarioMinimo, s.MediaVariaveis);
        var valorDia = remuneracao / 30m;
        var memoRem = $"remuneração {Formato.Moeda(remuneracao)} (salário {Formato.Moeda(f.SalarioMensal)} + adicionais {Formato.Moeda(PeriodosFerias.AdicionalFixoMensal(f, ctx.Tabelas.SalarioMinimo))} + média de variáveis {Formato.Moeda(s.MediaVariaveis)})";

        ItemCalculo Novo(string codigo, decimal referencia, decimal valor, string memoria)
        {
            var r = ctx.Rubrica(codigo);
            return new ItemCalculo(codigo, r.Descricao, r.Tipo, referencia, Dinheiro.Arredondar(valor), memoria, r.IncInss, r.IncIrrf, r.IncFgts, r.Variavel);
        }

        var ferias = Dinheiro.Arredondar(valorDia * s.DiasGozo);
        var itens = new List<ItemCalculo>
        {
            Novo(RubricasPadrao.FeriasGozadas, s.DiasGozo, ferias, $"{s.DiasGozo} dias x ({memoRem} ÷ 30)"),
            Novo(RubricasPadrao.TercoFerias, s.DiasGozo, ferias / 3m, $"1/3 de {Formato.Moeda(ferias)}"),
        };
        if (s.AbonoDias > 0)
        {
            var abono = Dinheiro.Arredondar(valorDia * s.AbonoDias);
            itens.Add(Novo(RubricasPadrao.AbonoPecuniario, s.AbonoDias, abono, $"{s.AbonoDias} dias x ({memoRem} ÷ 30)"));
            itens.Add(Novo(RubricasPadrao.TercoAbono, s.AbonoDias, abono / 3m, $"1/3 de {Formato.Moeda(abono)}"));
        }
        return CalculadoraFolhaMensal.Fechar(f, itens, ctx, Novo);
    }
}
