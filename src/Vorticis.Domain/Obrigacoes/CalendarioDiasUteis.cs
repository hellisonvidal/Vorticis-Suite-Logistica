namespace Vorticis.Domain.Obrigacoes;

public sealed record Feriado(DateOnly Data, string Nome);

/// <summary>
/// Dias úteis para fins de prazos de obrigações federais. Segue o critério da Receita Federal/rede bancária: não são dias úteis sábados,
/// domingos, feriados nacionais e os pontos facultativos em que não há expediente bancário (segunda e terça de Carnaval, Sexta-feira
/// Santa e Corpus Christi). A Quarta-feira de Cinzas conta como dia útil. Feriados estaduais e municipais não entram.
/// </summary>
public static class CalendarioDiasUteis
{
    /// <summary>Domingo de Páscoa (algoritmo de Meeus/Jones/Butcher, calendário gregoriano).</summary>
    public static DateOnly Pascoa(int ano)
    {
        int a = ano % 19, b = ano / 100, c = ano % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30, i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = (a + 11 * h + 22 * l) / 451;
        int mes = (h + l - 7 * m + 114) / 31, dia = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(ano, mes, dia);
    }

    /// <summary>Feriados e pontos facultativos sem expediente bancário do ano, em ordem de data.</summary>
    public static IReadOnlyList<Feriado> Feriados(int ano)
    {
        var p = Pascoa(ano);
        var lista = new List<Feriado>
        {
            new(new(ano, 1, 1), "Confraternização Universal"),
            new(p.AddDays(-48), "Carnaval (segunda-feira)"),
            new(p.AddDays(-47), "Carnaval (terça-feira)"),
            new(p.AddDays(-2), "Sexta-feira Santa"),
            new(new(ano, 4, 21), "Tiradentes"),
            new(new(ano, 5, 1), "Dia do Trabalhador"),
            new(p.AddDays(60), "Corpus Christi"),
            new(new(ano, 9, 7), "Independência do Brasil"),
            new(new(ano, 10, 12), "Nossa Senhora Aparecida"),
            new(new(ano, 11, 2), "Finados"),
            new(new(ano, 11, 15), "Proclamação da República"),
            new(new(ano, 12, 25), "Natal"),
        };
        if (ano >= 2024) lista.Add(new(new(ano, 11, 20), "Dia da Consciência Negra")); // Lei 14.759/2023
        return lista.OrderBy(f => f.Data).ToList();
    }

    public static bool EhFeriado(DateOnly d) => Feriados(d.Year).Any(f => f.Data == d);

    public static bool EhDiaUtil(DateOnly d) => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !EhFeriado(d);

    /// <summary>A própria data, se for dia útil; senão o primeiro dia útil seguinte.</summary>
    public static DateOnly ProximoDiaUtil(DateOnly d)
    {
        while (!EhDiaUtil(d)) d = d.AddDays(1);
        return d;
    }

    /// <summary>A própria data, se for dia útil; senão o último dia útil anterior.</summary>
    public static DateOnly DiaUtilAnterior(DateOnly d)
    {
        while (!EhDiaUtil(d)) d = d.AddDays(-1);
        return d;
    }

    /// <summary>N-ésimo dia útil do mês (1 = primeiro).</summary>
    public static DateOnly NesimoDiaUtil(int ano, int mes, int n)
    {
        if (n < 1) throw new ArgumentOutOfRangeException(nameof(n));
        var d = new DateOnly(ano, mes, 1);
        var contados = 0;
        while (true)
        {
            if (EhDiaUtil(d) && ++contados == n) return d;
            d = d.AddDays(1);
        }
    }

    public static DateOnly UltimoDiaUtil(int ano, int mes) => DiaUtilAnterior(new DateOnly(ano, mes, DateTime.DaysInMonth(ano, mes)));
}
