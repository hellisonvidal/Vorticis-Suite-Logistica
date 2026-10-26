using System.Reflection;
using System.Text.Json;

namespace Vorticis.Domain.Obrigacoes;

public enum ModuloObrigacao { Todos, Contabil, Fiscal, Folha }
public enum Periodicidade { Mensal, Trimestral, Anual }
public enum TipoRegra { DiaFixo, DiaUtil, UltimoDiaUtil }

/// <summary>Quando o dia fixo não é útil: antecipa (dia útil anterior), posterga (dia útil seguinte) ou mantém.</summary>
public enum AjusteNaoUtil { Antecipa, Posterga, Nenhum }

[Flags]
public enum RegimesObrigacao
{
    Nenhum = 0, Mei = 1, Simples = 2, Presumido = 4, Real = 8, Outros = 16,
    Todos = Mei | Simples | Presumido | Real | Outros,
}

/// <summary>
/// Competência: ano/mês (mensal), ano/último mês do trimestre — 3, 6, 9 ou 12 — (trimestral) ou ano-calendário com mês 0 (anual).
/// </summary>
public readonly record struct Competencia(int Ano, int Mes)
{
    public string Chave => Mes == 0 ? $"{Ano:0000}" : $"{Ano:0000}-{Mes:00}";
    public string Texto(Periodicidade p) => p switch
    {
        Periodicidade.Anual => $"{Ano}",
        Periodicidade.Trimestral => $"{Mes / 3}º tri/{Ano}",
        _ => $"{Mes:00}/{Ano}",
    };
}

/// <summary>Regra de uma obrigação (federal do catálogo ou personalizada da empresa).</summary>
/// <param name="MesesApos">Mensal/trimestral: meses entre o mês da competência e o do vencimento.</param>
/// <param name="MesVencimento">Anual: mês do vencimento (no ano-calendário + <paramref name="AnosApos"/>).</param>
public sealed record DefinicaoObrigacao(
    string Codigo, string Nome, string Orgao, ModuloObrigacao Modulo, Periodicidade Periodicidade, TipoRegra Regra, int Dia,
    int MesesApos, int MesVencimento, int AnosApos, AjusteNaoUtil Ajuste, RegimesObrigacao Regimes, bool DependeEmpregados,
    string? Descricao = null, int? AnoFim = null, bool Personalizada = false, string Esfera = "Federal")
{
    /// <summary>Vencimento da competência já ajustado para dia útil conforme a regra.</summary>
    public DateOnly Vencimento(Competencia c)
    {
        var baseMes = Periodicidade == Periodicidade.Anual
            ? new DateOnly(c.Ano + AnosApos, MesVencimento, 1)
            : new DateOnly(c.Ano, c.Mes, 1).AddMonths(MesesApos);
        switch (Regra)
        {
            case TipoRegra.DiaUtil: return CalendarioDiasUteis.NesimoDiaUtil(baseMes.Year, baseMes.Month, Dia);
            case TipoRegra.UltimoDiaUtil: return CalendarioDiasUteis.UltimoDiaUtil(baseMes.Year, baseMes.Month);
            default:
                var data = new DateOnly(baseMes.Year, baseMes.Month, Math.Min(Dia, DateTime.DaysInMonth(baseMes.Year, baseMes.Month)));
                return Ajuste switch
                {
                    AjusteNaoUtil.Antecipa => CalendarioDiasUteis.DiaUtilAnterior(data),
                    AjusteNaoUtil.Posterga => CalendarioDiasUteis.ProximoDiaUtil(data),
                    _ => data,
                };
        }
    }

    public string RegraTexto
    {
        get
        {
            var quando = Regra switch
            {
                TipoRegra.DiaFixo => $"dia {Dia}",
                TipoRegra.DiaUtil => $"{Dia}º dia útil",
                _ => "último dia útil",
            };
            var onde = Periodicidade == Periodicidade.Anual
                ? $"de {Mes(MesVencimento)}{(AnosApos > 0 ? " do ano seguinte" : "")}"
                : MesesApos == 0 ? "do próprio mês" : MesesApos == 1 ? "do mês seguinte" : $"do {MesesApos}º mês subsequente";
            var aj = Regra == TipoRegra.DiaFixo
                ? Ajuste switch { AjusteNaoUtil.Antecipa => " (antecipa)", AjusteNaoUtil.Posterga => " (posterga)", _ => "" }
                : "";
            return $"{quando} {onde}{aj}";
        }
    }

    private static string Mes(int m) => System.Globalization.CultureInfo.GetCultureInfo("pt-BR").DateTimeFormat.GetMonthName(m);

    /// <summary>Vigente no ano do vencimento.</summary>
    public bool VigenteEm(DateOnly vencimento) => AnoFim is null || vencimento.Year <= AnoFim;

    public bool AplicaA(RegimesObrigacao regime, bool temEmpregados) =>
        (Regimes & regime) != 0 && (!DependeEmpregados || temEmpregados);
}

public sealed record OcorrenciaObrigacao(DefinicaoObrigacao Definicao, Competencia Competencia, DateOnly Vencimento)
{
    public string CompetenciaTexto => Competencia.Texto(Definicao.Periodicidade);
}

public static class CalendarioObrigacoes
{
    /// <summary>Ocorrências cujo vencimento cai em [de, ate], ordenadas por vencimento e nome.</summary>
    public static IReadOnlyList<OcorrenciaObrigacao> Ocorrencias(IEnumerable<DefinicaoObrigacao> definicoes, DateOnly de, DateOnly ate)
    {
        var res = new List<OcorrenciaObrigacao>();
        foreach (var d in definicoes)
        {
            IEnumerable<Competencia> candidatas = d.Periodicidade == Periodicidade.Anual
                ? Enumerable.Range(de.Year - d.AnosApos - 1, ate.Year - de.Year + d.AnosApos + 3).Select(a => new Competencia(a, 0))
                : MesesEntre(de.AddMonths(-d.MesesApos - 1), ate.AddMonths(-d.MesesApos + 1))
                    .Where(m => d.Periodicidade == Periodicidade.Mensal || m.Month % 3 == 0)
                    .Select(m => new Competencia(m.Year, m.Month));
            foreach (var c in candidatas)
            {
                var v = d.Vencimento(c);
                if (v >= de && v <= ate && d.VigenteEm(v)) res.Add(new OcorrenciaObrigacao(d, c, v));
            }
        }
        return res.OrderBy(o => o.Vencimento).ThenBy(o => o.Definicao.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static IEnumerable<DateOnly> MesesEntre(DateOnly de, DateOnly ate)
    {
        for (var m = new DateOnly(de.Year, de.Month, 1); m <= ate; m = m.AddMonths(1)) yield return m;
    }
}

/// <summary>Catálogo de obrigações federais embutido (obrigacoes_federais.json), conferido com a Agenda Tributária da RFB.</summary>
public static class CatalogoObrigacoes
{
    private sealed record Dto(string Codigo, string Nome, string Orgao, string Modulo, string Periodicidade, string Regra, int Dia,
                              int MesesApos, int MesVencimento, int AnosApos, string Ajuste, string[] Regimes, bool DependeEmpregados,
                              string? Descricao, int? AnoFim);

    private static readonly Lazy<IReadOnlyList<DefinicaoObrigacao>> _federais = new(Carregar);

    public static IReadOnlyList<DefinicaoObrigacao> Federais => _federais.Value;

    private static IReadOnlyList<DefinicaoObrigacao> Carregar()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Vorticis.Domain.Obrigacoes.obrigacoes_federais.json")
                      ?? throw new InvalidOperationException("Catálogo de obrigações não encontrado.");
        var dtos = JsonSerializer.Deserialize<List<Dto>>(s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return dtos.Select(d => new DefinicaoObrigacao(d.Codigo, d.Nome, d.Orgao, Enum.Parse<ModuloObrigacao>(d.Modulo), Enum.Parse<Periodicidade>(d.Periodicidade),
                Enum.Parse<TipoRegra>(d.Regra), d.Dia, d.MesesApos, d.MesVencimento, d.AnosApos, Enum.Parse<AjusteNaoUtil>(d.Ajuste),
                d.Regimes.Aggregate(RegimesObrigacao.Nenhum, (a, r) => a | Enum.Parse<RegimesObrigacao>(r)), d.DependeEmpregados, d.Descricao, d.AnoFim))
            .ToList();
    }
}
