namespace Vorticis.Domain.Imobilizado;

/// <summary>Método de depreciação contábil (NBC TG 27 / CPC 27, item 62).</summary>
public enum MetodoDepreciacao
{
    /// <summary>Quotas constantes: (custo − valor residual) ÷ vida útil.</summary>
    Linear,
    /// <summary>Soma dos dígitos (decrescente): mais depreciação no início da vida útil.</summary>
    SomaDigitos,
}

/// <summary>Dados do bem necessários ao cálculo.</summary>
public sealed record BemParaDepreciar(
    long Id,
    decimal ValorAquisicao,
    decimal ValorResidual,
    int VidaUtilMeses,
    MetodoDepreciacao Metodo,
    DateOnly InicioUso,
    DateOnly? Baixa,
    decimal DepreciacaoAnterior,
    bool Deprecia,
    decimal TaxaAnualFiscal,
    int Turnos,
    bool ProRataDia = false,
    DateOnly? Aquisicao = null)
{
    /// <summary>Data em que a depreciação começa: a aquisição (pro rata dia) ou o início de uso.</summary>
    public DateOnly Inicio => ProRataDia && Aquisicao is { } a ? a : InicioUso;
}

/// <summary>Quota de uma competência: contábil (CPC 27) e fiscal (RFB), e o acumulado depois dela.</summary>
public sealed record QuotaDepreciacao(long BemId, DateOnly Competencia, decimal Contabil, decimal Fiscal, decimal AcumuladoAnterior, decimal AcumuladoApos)
{
    /// <summary>Fiscal − contábil: diferença a controlar no LALUR/e-Lalur (exclusão quando positiva, adição quando negativa).</summary>
    public decimal Diferenca => Fiscal - Contabil;
}

/// <summary>
/// Regras de depreciação.
/// Contábil (NBC TG 27 / CPC 27): começa quando o bem está disponível para uso (mês do início de uso, quota integral),
/// sobre o valor depreciável (custo − valor residual), pela vida útil estimada; para quando o acumulado atinge o
/// valor depreciável ou o bem é baixado (a competência da baixa não é depreciada).
/// Fiscal (RIR/2018 arts. 317–330; IN RFB 1.700/2017, Anexo III): taxa anual do grupo sobre o custo, em quotas mensais,
/// multiplicada por 1,0 / 1,5 / 2,0 para bens móveis usados em 1 / 2 / 3 turnos de 8 horas (art. 323); limitada ao custo.
/// Pro rata dia (opção do bem): começa na data de aquisição; no primeiro mês e no mês da baixa a quota (contábil e fiscal) é
/// proporcional aos dias, e a fração não depreciada no primeiro mês fica para o mês seguinte ao fim da vida útil.
/// </summary>
public static class RegrasDepreciacao
{
    public static DateOnly Competencia(DateOnly data) => new(data.Year, data.Month, 1);

    /// <summary>Meses de uso até a competência, contando a do início (1 = primeiro mês).</summary>
    public static int MesDeUso(BemParaDepreciar b, DateOnly competencia) =>
        (competencia.Year - b.Inicio.Year) * 12 + competencia.Month - b.Inicio.Month + 1;

    /// <summary>
    /// Fração do mês depreciada: 1, ou (pro rata dia) os dias desde a aquisição no primeiro mês, inclusive o dia da aquisição,
    /// e os dias antes da baixa no mês da baixa.
    /// </summary>
    public static decimal FracaoDoMes(BemParaDepreciar b, DateOnly competencia)
    {
        if (!b.ProRataDia) return 1m;
        decimal dias = DateTime.DaysInMonth(competencia.Year, competencia.Month);
        var ini = competencia == Competencia(b.Inicio) ? b.Inicio.Day : 1;
        var fim = b.Baixa is { } baixa && competencia == Competencia(baixa) ? baixa.Day - 1 : (int)dias;
        return Math.Max(0, fim - ini + 1) / dias;
    }

    public static decimal FatorTurnos(int turnos) => turnos switch { 2 => 1.5m, 3 => 2.0m, _ => 1.0m };

    /// <summary>
    /// Quota da competência dado o acumulado contábil e fiscal até o mês anterior.
    /// Retorna null quando o bem não deprecia nessa competência.
    /// </summary>
    public static QuotaDepreciacao? Calcular(BemParaDepreciar b, DateOnly competencia, decimal acumuladoContabil, decimal acumuladoFiscal)
    {
        competencia = Competencia(competencia);
        if (!b.Deprecia || b.VidaUtilMeses <= 0) return null;
        if (competencia < Competencia(b.Inicio)) return null;
        if (b.Baixa is { } baixa && (b.ProRataDia ? competencia > Competencia(baixa) : competencia >= Competencia(baixa))) return null;
        var fracao = FracaoDoMes(b, competencia);
        if (fracao <= 0) return null;
        // Pro rata: a fração não depreciada no primeiro mês é lançada no mês seguinte ao fim da vida útil.
        var ultimoMes = b.VidaUtilMeses + (FracaoDoMes(b with { Baixa = null }, Competencia(b.Inicio)) < 1 ? 1 : 0);

        var depreciavel = b.ValorAquisicao - b.ValorResidual;
        var restante = depreciavel - b.DepreciacaoAnterior - acumuladoContabil;
        var mes = MesDeUso(b, competencia);
        decimal contabil = 0;
        if (restante > 0)
        {
            var n = b.VidaUtilMeses;
            contabil = b.Metodo switch
            {
                MetodoDepreciacao.SomaDigitos when mes <= n => Arredondar(depreciavel * (n - mes + 1) / (n * (n + 1) / 2m) * fracao),
                MetodoDepreciacao.SomaDigitos => restante,
                _ => Arredondar(depreciavel / n * fracao),
            };
            // Último mês da vida útil (ou resíduo de arredondamento): leva o que falta (no mês da baixa, só a fração).
            if ((mes >= ultimoMes && fracao == 1m) || contabil > restante) contabil = restante;
        }

        var restanteFiscal = b.ValorAquisicao - b.DepreciacaoAnterior - acumuladoFiscal;
        var fiscal = restanteFiscal <= 0 ? 0 : Math.Min(restanteFiscal, Arredondar(b.ValorAquisicao * b.TaxaAnualFiscal / 100m * FatorTurnos(b.Turnos) / 12m * fracao));

        if (contabil <= 0 && fiscal <= 0) return null;
        return new QuotaDepreciacao(b.Id, competencia, contabil, fiscal, b.DepreciacaoAnterior + acumuladoContabil,
                                    b.DepreciacaoAnterior + acumuladoContabil + contabil);
    }

    /// <summary>Vida útil fiscal em meses para a taxa anual (ex.: 10% → 120 meses).</summary>
    public static int VidaUtilFiscalMeses(decimal taxaAnual) => taxaAnual <= 0 ? 0 : (int)Math.Round(1200m / taxaAnual);

    private static decimal Arredondar(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
