using Vorticis.Domain.Comum;
using Vorticis.Fiscal.Comum;
using Vorticis.Fiscal.Lucro;
using Vorticis.Fiscal.Retencoes;
using Vorticis.Fiscal.Simples;

namespace Vorticis.Fiscal.Guias;

/// <summary>Documento de arrecadação: DAS (Simples), DARF (códigos de receita), GPS/DCTFWeb (INSS, informativo) ou guia municipal do ISS.</summary>
public enum DocumentoGuia { Das, Darf, Gps, DctfWeb, Iss }

public sealed record NovaGuia(
    string Tributo, string Descricao, DateOnly Competencia, DateOnly Vencimento, decimal Valor, string? CodigoReceita, DocumentoGuia Documento, string Origem,
    string? Referencia, string? Observacao = null);

public sealed record Guia(
    long Id, string Tributo, string Descricao, DateOnly Competencia, DateOnly Vencimento, decimal Valor, string? CodigoReceita, DocumentoGuia Documento, string Origem,
    string? Referencia, bool Paga, DateOnly? DataPagamento, decimal? ValorPago, string? Observacao)
{
    public string RotuloDocumento => Documento switch { DocumentoGuia.Das => "DAS", DocumentoGuia.Darf => "DARF", DocumentoGuia.Gps => "GPS", DocumentoGuia.DctfWeb => "DCTFWeb", _ => "ISS" };
}

public sealed record FiltroGuias(DateOnly? CompetenciaInicio = null, DateOnly? CompetenciaFim = null, DateOnly? VencimentoInicio = null, DateOnly? VencimentoFim = null,
                                 bool? Paga = null);

/// <summary>Monta as guias de recolhimento a partir das apurações e das retenções efetuadas. Pura: vencimentos e códigos vêm dos parâmetros.</summary>
public static class GeradorGuias
{
    private static readonly Dictionary<TributoRetido, string> RotuloRetencao = new()
    {
        [TributoRetido.IRRF] = "IRRF", [TributoRetido.PIS] = "PIS retido", [TributoRetido.COFINS] = "COFINS retida", [TributoRetido.CSLL] = "CSLL retida",
        [TributoRetido.INSS] = "INSS retido (11%)", [TributoRetido.ISS] = "ISS retido",
    };

    public static IEnumerable<NovaGuia> DoSimples(ApuracaoSimplesSalva a)
    {
        if (a.ValorDas <= 0) yield break;
        yield return new NovaGuia("DAS", $"DAS do Simples Nacional · {a.Competencia:MM/yyyy}", a.Competencia, a.Vencimento, a.ValorDas, null, DocumentoGuia.Das, "SIMPLES",
            $"SIMPLES:{a.Competencia:yyyy-MM}", a.Resultado.Avisos.Any(x => x.Contains("abaixo do mínimo", StringComparison.Ordinal)) ? "Abaixo de R$ 10,00: soma-se ao DAS do mês seguinte." : null);
    }

    public static IEnumerable<NovaGuia> DoLucro(ApuracaoLucroSalva a, ParametrosGuias p)
    {
        var r = a.Resultado;
        var real = r.Regime == RegimeLucro.Real;
        var comp = new DateOnly(r.Fim.Year, r.Fim.Month, 1);
        var sufixo = $"{r.Ano}T{r.Trimestre}";
        if (r.IrpjAPagar > 0)
            yield return new NovaGuia("IRPJ", $"IRPJ {(real ? "Lucro Real" : "Lucro Presumido")} · {r.Trimestre}º tri/{r.Ano}", comp, r.Vencimento, r.IrpjAPagar,
                p.Codigo(real ? "IRPJ_REAL_TRIMESTRAL" : "IRPJ_PRESUMIDO"), DocumentoGuia.Darf, "LUCRO", $"LUCRO:{r.Regime}:{sufixo}:IRPJ", Minimo(r.IrpjAPagar, p));
        if (r.CsllAPagar > 0)
            yield return new NovaGuia("CSLL", $"CSLL {(real ? "Lucro Real" : "Lucro Presumido")} · {r.Trimestre}º tri/{r.Ano}", comp, r.Vencimento, r.CsllAPagar,
                p.Codigo(real ? "CSLL_REAL_TRIMESTRAL" : "CSLL_PRESUMIDO"), DocumentoGuia.Darf, "LUCRO", $"LUCRO:{r.Regime}:{sufixo}:CSLL", Minimo(r.CsllAPagar, p));
    }

    public static IEnumerable<NovaGuia> DePisCofins(ApuracaoPisCofinsSalva a, ParametrosGuias p)
    {
        var r = a.Resultado;
        var cum = r.Regime == RegimePisCofins.Cumulativo;
        if (r.PisAPagar > 0)
            yield return new NovaGuia("PIS", $"PIS {(cum ? "cumulativo" : "não cumulativo")} · {r.Competencia:MM/yyyy}", r.Competencia, r.Vencimento, r.PisAPagar,
                p.Codigo(cum ? "PIS_CUMULATIVO" : "PIS_NAO_CUMULATIVO"), DocumentoGuia.Darf, "PIS_COFINS", $"PISCOFINS:{r.Competencia:yyyy-MM}:PIS", Minimo(r.PisAPagar, p));
        if (r.CofinsAPagar > 0)
            yield return new NovaGuia("COFINS", $"COFINS {(cum ? "cumulativa" : "não cumulativa")} · {r.Competencia:MM/yyyy}", r.Competencia, r.Vencimento, r.CofinsAPagar,
                p.Codigo(cum ? "COFINS_CUMULATIVO" : "COFINS_NAO_CUMULATIVO"), DocumentoGuia.Darf, "PIS_COFINS", $"PISCOFINS:{r.Competencia:yyyy-MM}:COFINS", Minimo(r.CofinsAPagar, p));
    }

    private static string? Minimo(decimal valor, ParametrosGuias p) => valor < p.MinimoDarf ? $"Abaixo de {Formato.Moeda(p.MinimoDarf)}: o DARF só é emitido quando o acumulado atingir o mínimo." : null;

    /// <summary>
    /// Retenções efetuadas por competência: IRRF por código de receita; CSRF (PIS + COFINS + CSLL, código 5952) numa só guia; INSS 11% (DCTFWeb,
    /// informativo); ISS retido (guia do município). Vencimentos: IRRF e INSS dia 20 (antecipa), CSRF último dia útil do mês seguinte, ISS no dia parametrizado.
    /// </summary>
    public static IEnumerable<NovaGuia> DasRetencoes(IEnumerable<Retencao> retencoes, ParametrosGuias p)
    {
        foreach (var grupo in retencoes.Where(r => r.Sentido == SentidoRetencao.Efetuada).GroupBy(r => (r.Competencia, Grupo: GrupoGuia(r))))
        {
            var comp = grupo.Key.Competencia;
            var valor = grupo.Sum(r => r.Valor);
            var mesSeguinte = comp.AddMonths(1);
            var referencia = $"RET:{grupo.Key.Grupo}:{comp:yyyy-MM}";
            switch (grupo.Key.Grupo)
            {
                case "IRRF":
                    foreach (var porCodigo in grupo.GroupBy(r => r.CodigoReceita ?? p.CodigoIrrfServicos))
                        yield return new NovaGuia("IRRF", $"IRRF retido · {comp:MM/yyyy} · cód. {porCodigo.Key}", comp, Calendario.DiaUtilAnterior(Calendario.DiaDoMesSeguinte(comp, 20)),
                            porCodigo.Sum(r => r.Valor), porCodigo.Key, DocumentoGuia.Darf, "RETENCAO", $"{referencia}:{porCodigo.Key}", Minimo(porCodigo.Sum(r => r.Valor), p));
                    break;
                case "CSRF":
                    yield return new NovaGuia("CSRF", $"CSRF (PIS/COFINS/CSLL retidos) · {comp:MM/yyyy}", comp, Calendario.UltimoDiaUtilDoMes(mesSeguinte.Year, mesSeguinte.Month), valor,
                        p.CodigoCsrf, DocumentoGuia.Darf, "RETENCAO", referencia, Minimo(valor, p));
                    break;
                case "INSS":
                    yield return new NovaGuia("INSS", $"INSS retido 11% · {comp:MM/yyyy} (DCTFWeb/GPS informativo)", comp, Calendario.DiaUtilAnterior(Calendario.DiaDoMesSeguinte(comp, 20)), valor,
                        p.CodigoInss, DocumentoGuia.DctfWeb, "RETENCAO", referencia, "Informativo: o recolhimento é feito pela DCTFWeb/MIT.");
                    break;
                default:
                    yield return new NovaGuia("ISS", $"ISS retido · {comp:MM/yyyy}", comp, Calendario.ProximoDiaUtil(Calendario.DiaDoMesSeguinte(comp, p.DiaVencimentoIss)), valor, null,
                        DocumentoGuia.Iss, "RETENCAO", referencia, "Vencimento e guia conforme a legislação do município.");
                    break;
            }
        }
    }

    private static string GrupoGuia(Retencao r) => r.Tributo switch
    {
        TributoRetido.IRRF => "IRRF",
        TributoRetido.PIS or TributoRetido.COFINS or TributoRetido.CSLL => "CSRF",
        TributoRetido.INSS => "INSS",
        _ => "ISS",
    };
}
