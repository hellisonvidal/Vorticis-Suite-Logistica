namespace Vorticis.Fiscal.Comum;

/// <summary>Dias úteis para vencimentos de tributos (fins de semana e feriados nacionais; sem feriados estaduais e municipais).</summary>
public static class Calendario
{
    public static bool EhFeriadoNacional(DateOnly d)
    {
        if ((d.Month, d.Day) is (1, 1) or (4, 21) or (5, 1) or (9, 7) or (10, 12) or (11, 2) or (11, 15) or (11, 20) or (12, 25)) return true;
        return d == SextaFeiraSanta(d.Year);
    }

    /// <summary>Sexta-feira da Paixão (dois dias antes da Páscoa; algoritmo gregoriano anônimo).</summary>
    public static DateOnly SextaFeiraSanta(int ano)
    {
        int a = ano % 19, b = ano / 100, c = ano % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30, i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = (a + 11 * h + 22 * l) / 451;
        int mes = (h + l - 7 * m + 114) / 31, dia = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(ano, mes, dia).AddDays(-2);
    }

    public static bool EhDiaUtil(DateOnly d) => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !EhFeriadoNacional(d);

    /// <summary>Prorroga para o primeiro dia útil seguinte (regra do DAS do Simples Nacional).</summary>
    public static DateOnly ProximoDiaUtil(DateOnly d)
    {
        while (!EhDiaUtil(d)) d = d.AddDays(1);
        return d;
    }

    /// <summary>Antecipa para o dia útil anterior (regra dos DARF federais).</summary>
    public static DateOnly DiaUtilAnterior(DateOnly d)
    {
        while (!EhDiaUtil(d)) d = d.AddDays(-1);
        return d;
    }

    public static DateOnly UltimoDiaUtilDoMes(int ano, int mes) => DiaUtilAnterior(new DateOnly(ano, mes, DateTime.DaysInMonth(ano, mes)));

    /// <summary>Dia <paramref name="dia"/> do mês seguinte à competência.</summary>
    public static DateOnly DiaDoMesSeguinte(DateOnly competencia, int dia)
    {
        var m = new DateOnly(competencia.Year, competencia.Month, 1).AddMonths(1);
        return new DateOnly(m.Year, m.Month, Math.Min(dia, DateTime.DaysInMonth(m.Year, m.Month)));
    }
}
