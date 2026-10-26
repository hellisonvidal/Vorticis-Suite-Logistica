using Vorticis.Domain.Comum;

namespace Vorticis.Folha.Regras;

/// <summary>INSS do empregado: alíquotas progressivas por faixa, limitado ao teto do salário de contribuição.</summary>
public static class CalculoInss
{
    public static decimal Calcular(decimal baseCalculo, TabelaInss tabela) => Calcular(baseCalculo, tabela, out _);

    public static decimal Calcular(decimal baseCalculo, TabelaInss tabela, out string memoria)
    {
        if (baseCalculo <= 0) { memoria = "Sem base de cálculo."; return 0; }
        var baseLimitada = Math.Min(baseCalculo, tabela.Teto);
        decimal total = 0, anterior = 0;
        var partes = new List<string>();
        foreach (var f in tabela.Faixas)
        {
            if (baseLimitada <= anterior) break;
            var parcela = Math.Min(baseLimitada, f.Ate) - anterior;
            var contribuicao = parcela * f.Aliquota / 100m;
            total += contribuicao;
            partes.Add($"{Formato.Moeda(parcela)} x {Formato.Num(f.Aliquota)}%");
            anterior = f.Ate;
        }
        var valor = Dinheiro.Arredondar(total);
        memoria = $"Base {Formato.Moeda(baseCalculo)}" + (baseCalculo > tabela.Teto ? $" (limitada ao teto {Formato.Moeda(tabela.Teto)})" : "")
                  + $": {string.Join(" + ", partes)} = {Formato.Moeda(valor)}";
        return valor;
    }
}

public sealed record ResultadoIrrf(decimal Rendimento, decimal DeducaoLegal, decimal DescontoSimplificado, bool UsouSimplificado, decimal BaseCalculo,
                                   decimal ImpostoTabela, decimal Reducao, decimal Imposto, string Memoria);

/// <summary>
/// IRRF mensal: usa o melhor entre as deduções legais (INSS, dependentes, pensão alimentícia) e o desconto simplificado,
/// aplica a tabela progressiva e a redução da Lei 15.270/2025 (calculada sobre o rendimento tributável, antes das deduções).
/// </summary>
public static class CalculoIrrf
{
    public static ResultadoIrrf Calcular(decimal rendimento, decimal inss, int dependentes, decimal pensaoAlimenticia, TabelaIrrf tabela)
    {
        rendimento = Math.Max(0, rendimento);
        var legal = inss + dependentes * tabela.DeducaoDependente + pensaoAlimenticia;
        var usaSimplificado = tabela.DescontoSimplificado > legal;
        var deducao = usaSimplificado ? tabela.DescontoSimplificado : legal;
        var baseCalculo = Math.Max(0, Dinheiro.Arredondar(rendimento - deducao));

        var faixa = tabela.Faixas.First(f => f.Ate is null || baseCalculo <= f.Ate);
        var impostoTabela = Math.Max(0, Dinheiro.Arredondar(baseCalculo * faixa.Aliquota / 100m - faixa.Deduzir));

        decimal reducao = 0;
        string textoReducao = "";
        if (tabela.Reducao is { } r && impostoTabela > 0)
        {
            if (rendimento <= r.LimiteIsencao)
            {
                reducao = Math.Min(r.ReducaoMaxima, impostoTabela);
                textoReducao = $"; redução (rendimento até {Formato.Moeda(r.LimiteIsencao)}) {Formato.Moeda(reducao)}";
            }
            else if (rendimento <= r.LimiteSuperior)
            {
                reducao = Math.Min(impostoTabela, Math.Max(0, Dinheiro.Arredondar(r.Constante - r.Coeficiente * rendimento)));
                textoReducao = $"; redução {Formato.Moeda(r.Constante)} - {r.Coeficiente} x {Formato.Moeda(rendimento)} = {Formato.Moeda(reducao)}";
            }
        }
        var imposto = Math.Max(0, impostoTabela - reducao);
        var memoria = $"Rendimento {Formato.Moeda(rendimento)}; " + (usaSimplificado
            ? $"desconto simplificado {Formato.Moeda(tabela.DescontoSimplificado)} (maior que as deduções legais {Formato.Moeda(legal)})"
            : $"deduções legais {Formato.Moeda(legal)} (INSS {Formato.Moeda(inss)}, {dependentes} dependente(s), pensão {Formato.Moeda(pensaoAlimenticia)})")
            + $"; base {Formato.Moeda(baseCalculo)} x {Formato.Num(faixa.Aliquota)}% - {Formato.Moeda(faixa.Deduzir)} = {Formato.Moeda(impostoTabela)}"
            + textoReducao + $"; IRRF {Formato.Moeda(imposto)}";
        return new ResultadoIrrf(rendimento, legal, tabela.DescontoSimplificado, usaSimplificado, baseCalculo, impostoTabela, reducao, imposto, memoria);
    }
}
