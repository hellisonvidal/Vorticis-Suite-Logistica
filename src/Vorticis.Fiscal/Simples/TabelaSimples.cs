using System.Globalization;
using System.Text.Json;

namespace Vorticis.Fiscal.Simples;

public enum AnexoSimples { I, II, III, IV, V }

/// <summary>Faixa de receita bruta dos 12 meses (RBT12): alíquota nominal (%), parcela a deduzir (R$) e repartição (% por tributo).</summary>
public sealed record FaixaSimples(decimal Ate, decimal Aliquota, decimal Deduzir, IReadOnlyDictionary<string, decimal> Reparticao);

/// <summary>Tabelas do Simples Nacional (LC 123/2006) por vigência; os valores vêm dos parâmetros legais, nunca do código.</summary>
public sealed record TabelaSimples(
    DateOnly VigenciaInicio, string Fundamento, decimal LimiteRbt12, decimal SublimiteIcmsIss, decimal LimiteFatorR, decimal MinimoDas,
    IReadOnlyDictionary<AnexoSimples, IReadOnlyList<FaixaSimples>> Anexos)
{
    public FaixaSimples Faixa(AnexoSimples anexo, decimal rbt12)
    {
        var faixas = Anexos[anexo];
        return faixas.FirstOrDefault(f => rbt12 <= f.Ate) ?? faixas[^1];
    }

    public int NumeroFaixa(AnexoSimples anexo, decimal rbt12)
    {
        var faixas = Anexos[anexo];
        var i = faixas.ToList().FindIndex(f => rbt12 <= f.Ate);
        return (i < 0 ? faixas.Count : i + 1);
    }

    public static TabelaSimples Carregar(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        decimal D(JsonElement e) => e.GetDecimal();
        var anexos = new Dictionary<AnexoSimples, IReadOnlyList<FaixaSimples>>();
        foreach (var a in r.GetProperty("anexos").EnumerateObject())
        {
            var anexo = Enum.Parse<AnexoSimples>(a.Name);
            anexos[anexo] = a.Value.EnumerateArray().Select(f => new FaixaSimples(D(f.GetProperty("ate")), D(f.GetProperty("aliquota")), D(f.GetProperty("deduzir")),
                f.GetProperty("reparticao").EnumerateObject().ToDictionary(p => p.Name, p => D(p.Value)))).OrderBy(f => f.Ate).ToList();
        }
        return new TabelaSimples(DateOnly.ParseExact(r.GetProperty("vigencia_inicio").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            r.GetProperty("fundamento").GetString() ?? "", D(r.GetProperty("limite_rbt12")), D(r.GetProperty("sublimite_icms_iss")),
            D(r.GetProperty("limite_fator_r")), D(r.GetProperty("minimo_das")), anexos);
    }
}
