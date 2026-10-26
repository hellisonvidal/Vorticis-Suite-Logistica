using System.Globalization;

namespace Vorticis.Application.Relatorios;

/// <summary>Barras (comparar valores entre categorias, inclusive meses) ou linhas (tendência ao longo dos meses).</summary>
public enum FormaGrafico { Barras, Linhas }

/// <summary>Uma série do gráfico: um valor por categoria, na mesma ordem das categorias.</summary>
public sealed record SerieGrafico(string Nome, IReadOnlyList<decimal> Valores);

/// <summary>
/// Dados de um gráfico: categorias no eixo horizontal (meses, veículos, clientes...) e até 4 séries, todas na mesma unidade (um eixo só).
/// <see cref="Unidade"/>: "R$", "km/l", "CT-e"... (aparece no eixo e na tabela).
/// </summary>
public sealed record DadosGrafico(string Titulo, string Unidade, FormaGrafico Forma, IReadOnlyList<string> Categorias, IReadOnlyList<SerieGrafico> Series,
                                  string? Nota = null)
{
    public const int MaximoSeries = 4;
    public bool Vazio => Categorias.Count == 0 || Series.All(s => s.Valores.All(v => v == 0));
}

public static class MontagemGraficos
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Primeiro dia de cada mês entre as datas (inclusive).</summary>
    public static IReadOnlyList<DateOnly> Meses(DateOnly inicio, DateOnly fim)
    {
        var meses = new List<DateOnly>();
        for (var m = new DateOnly(inicio.Year, inicio.Month, 1); m <= fim; m = m.AddMonths(1)) meses.Add(m);
        return meses;
    }

    /// <summary>"mar/26".</summary>
    public static string RotuloMes(DateOnly mes) => mes.ToString("MMM/yy", PtBr).Replace(".", "");

    /// <summary>Gráfico por mês: cada série soma os valores dos itens do mês (pela data de cada item).</summary>
    public static DadosGrafico PorMes<T>(string titulo, string unidade, FormaGrafico forma, DateOnly inicio, DateOnly fim, Func<T, DateOnly> data,
                                         params (string Nome, IEnumerable<T> Itens, Func<T, decimal> Valor)[] series)
    {
        var meses = Meses(inicio, fim);
        var lista = series.Take(DadosGrafico.MaximoSeries).Select(s =>
        {
            var porMes = s.Itens.Where(i => data(i) >= inicio && data(i) <= fim).GroupBy(i => new DateOnly(data(i).Year, data(i).Month, 1))
                                .ToDictionary(g => g.Key, g => g.Sum(s.Valor));
            return new SerieGrafico(s.Nome, [.. meses.Select(m => Math.Round(porMes.GetValueOrDefault(m), 2))]);
        }).ToList();
        return new DadosGrafico(titulo, unidade, forma, [.. meses.Select(RotuloMes)], lista);
    }

    /// <summary>
    /// Gráfico por categoria (veículo, cliente...): as maiores primeiro; além do limite, o resto vira "Outros" (nunca uma cor nova).
    /// </summary>
    public static DadosGrafico PorCategoria<T>(string titulo, string unidade, IEnumerable<T> itens, Func<T, string> categoria, string serie, Func<T, decimal> valor,
                                               int limite = 12, string? nota = null)
    {
        var grupos = itens.GroupBy(categoria).Select(g => (Nome: g.Key, Valor: Math.Round(g.Sum(valor), 2))).OrderByDescending(g => g.Valor).ThenBy(g => g.Nome).ToList();
        if (grupos.Count > limite)
            grupos = [.. grupos.Take(limite - 1), ("Outros", grupos.Skip(limite - 1).Sum(g => g.Valor))];
        return new DadosGrafico(titulo, unidade, FormaGrafico.Barras, [.. grupos.Select(g => g.Nome)], [new SerieGrafico(serie, [.. grupos.Select(g => g.Valor)])], nota);
    }

    /// <summary>Marcas do eixo vertical: de 0 (ou do mínimo negativo) ao máximo, em passos "redondos" (1, 2 ou 5 × 10^n).</summary>
    public static IReadOnlyList<decimal> Escala(decimal minimo, decimal maximo, int passosDesejados = 5)
    {
        var (min, max) = (Math.Min(0, minimo), Math.Max(0, maximo));
        if (max == min) max = min + 1;
        var bruto = (double)(max - min) / passosDesejados;
        var potencia = Math.Pow(10, Math.Floor(Math.Log10(bruto)));
        var passo = (decimal)(new[] { 1d, 2d, 5d, 10d }.Select(f => f * potencia).First(p => p >= bruto));
        var inicio = Math.Floor(min / passo) * passo;
        var marcas = new List<decimal>();
        for (var v = inicio; v < max + passo; v += passo) { marcas.Add(v); if (v >= max) break; }
        return marcas;
    }
}
