namespace Vorticis.Folha.Regras;

/// <summary>Calendário para DSR e proporcionalidades: domingos e feriados nacionais.</summary>
public static class Calendario
{
    /// <summary>Domingo de Páscoa (algoritmo de Meeus/Jones/Butcher).</summary>
    public static DateOnly Pascoa(int ano)
    {
        int a = ano % 19, b = ano / 100, c = ano % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30, i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = (a + 11 * h + 22 * l) / 451;
        int mes = (h + l - 7 * m + 114) / 31, dia = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(ano, mes, dia);
    }

    /// <summary>Feriados nacionais do ano: fixos (incl. Consciência Negra desde 2024) e Sexta-feira Santa.</summary>
    public static IReadOnlySet<DateOnly> FeriadosNacionais(int ano)
    {
        var f = new HashSet<DateOnly>
        {
            new(ano, 1, 1), new(ano, 4, 21), new(ano, 5, 1), new(ano, 9, 7), new(ano, 10, 12), new(ano, 11, 2), new(ano, 11, 15),
            new(ano, 12, 25), Pascoa(ano).AddDays(-2),
        };
        if (ano >= 2024) f.Add(new DateOnly(ano, 11, 20));
        return f;
    }

    /// <summary>
    /// Dias úteis (segunda a sábado que não são feriado) e dias de descanso (domingos e feriados em outros dias) do mês.
    /// <paramref name="feriadosExtras"/>: feriados estaduais/municipais da empresa.
    /// </summary>
    public static (int DiasUteis, int DiasDescanso) DiasUteisEDescanso(int ano, int mes, IEnumerable<DateOnly>? feriadosExtras = null)
    {
        var feriados = FeriadosNacionais(ano).Concat(feriadosExtras ?? []).ToHashSet();
        int dias = DateTime.DaysInMonth(ano, mes), uteis = 0, descanso = 0;
        for (var d = 1; d <= dias; d++)
        {
            var data = new DateOnly(ano, mes, d);
            if (data.DayOfWeek == DayOfWeek.Sunday || feriados.Contains(data)) descanso++;
            else uteis++;
        }
        return (uteis, descanso);
    }

    /// <summary>Dias trabalhados no mês na base comercial de 30 dias (admissão ou desligamento no mês).</summary>
    public static int DiasTrabalhadosComerciais(DateOnly competencia, DateOnly admissao, DateOnly? desligamento)
    {
        var inicioMes = new DateOnly(competencia.Year, competencia.Month, 1);
        var fimMes = inicioMes.AddMonths(1).AddDays(-1);
        var ini = admissao > inicioMes ? admissao : inicioMes;
        var fim = desligamento is { } d && d < fimMes ? d : fimMes;
        if (fim < ini) return 0;
        var diaIni = Math.Min(ini.Day, 30);
        var diaFim = fim == fimMes ? 30 : Math.Min(fim.Day, 30);
        return Math.Max(0, Math.Min(30, diaFim - diaIni + 1));
    }
}
