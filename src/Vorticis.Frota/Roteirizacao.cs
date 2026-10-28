using System.Globalization;
using System.Text.Json;
using Vorticis.Domain.Comum;

namespace Vorticis.Frota;

/// <summary>Trecho do roteiro entre duas paradas: km estimado (linha reta × fator rodoviário), km da estrada (consulta) e km usado (o do usuário, se digitado).</summary>
public sealed record TrechoRoteiro(ParadaRota De, ParadaRota Para, decimal KmEstimado, decimal? KmEstrada = null, decimal? KmInformado = null)
{
    public decimal Km => KmInformado ?? KmEstrada ?? KmEstimado;
    public string Origem => KmInformado is not null ? "informado" : KmEstrada is not null ? "estrada" : "estimado";
}

/// <summary>Totais do roteiro: km, horas na velocidade média e custo aproximado (km × custo por km + pedágios).</summary>
public sealed record ResumoRoteiro(decimal Km, decimal Horas, decimal CustoKm, decimal Pedagios)
{
    public decimal CustoTotal => Math.Round(Km * CustoKm + Pedagios, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Trajeto pela estrada (serviço de rotas): km e horas totais, km de cada trecho e os pontos do caminho para o mapa.</summary>
public sealed record TrajetoEstrada(decimal Km, decimal Horas, IReadOnlyList<decimal> KmTrechos, IReadOnlyList<(double Lat, double Lon)> Pontos);

/// <summary>
/// Roteirização (sem banco): a ordem de paradas mais curta (vizinho mais próximo e melhoria 2-opt sobre as distâncias estimadas), com a origem fixa
/// e, se pedido, o destino final fixo ou a volta à origem; os trechos, os totais e o custo aproximado.
/// </summary>
public static class RegrasRoteirizacao
{
    private static double D(ParadaRota a, ParadaRota b) =>
        BaseMunicipios.Obter(a.MunicipioIbge) is { } x && BaseMunicipios.Obter(b.MunicipioIbge) is { } y ? RegrasLogistica.Haversine(x, y) : 0;

    /// <summary>Distância total (km em linha reta) de uma ordem de paradas, com a volta à origem se pedido.</summary>
    public static double Distancia(IReadOnlyList<ParadaRota> ordem, bool voltarOrigem)
    {
        var total = 0d;
        for (var i = 1; i < ordem.Count; i++) total += D(ordem[i - 1], ordem[i]);
        return voltarOrigem && ordem.Count > 1 ? total + D(ordem[^1], ordem[0]) : total;
    }

    /// <summary>
    /// A ordem mais curta das paradas. A primeira (origem) fica no lugar; com <paramref name="fixarDestino"/>, a última também; com
    /// <paramref name="voltarOrigem"/>, conta a volta à origem. Até 8 paradas intermediárias a busca é exata; acima, vizinho mais próximo + 2-opt.
    /// </summary>
    public static IReadOnlyList<ParadaRota> Otimizar(IReadOnlyList<ParadaRota> paradas, bool fixarDestino = false, bool voltarOrigem = false)
    {
        if (paradas.Count <= 2) return paradas;
        var inicio = paradas[0];
        var fim = fixarDestino && !voltarOrigem ? paradas[^1] : null;
        var meio = paradas.Skip(1).Take(paradas.Count - (fim is null ? 1 : 2)).ToList();
        IReadOnlyList<ParadaRota> Montar(IEnumerable<ParadaRota> m) => [inicio, .. m, .. fim is null ? Array.Empty<ParadaRota>() : [fim]];
        if (meio.Count <= 8)
        {
            IReadOnlyList<ParadaRota>? melhor = null;
            var menor = double.MaxValue;
            foreach (var p in Permutacoes(meio))
            {
                var ordem = Montar(p);
                var d = Distancia(ordem, voltarOrigem);
                if (d < menor - 1e-9) (menor, melhor) = (d, ordem);
            }
            return melhor!;
        }
        // vizinho mais próximo
        var restantes = meio.ToList();
        var rota = new List<ParadaRota>();
        var atual = inicio;
        while (restantes.Count > 0)
        {
            var proxima = restantes.MinBy(p => D(atual, p))!;
            rota.Add(proxima);
            restantes.Remove(proxima);
            atual = proxima;
        }
        // 2-opt: inverte trechos enquanto encurtar
        var melhorou = true;
        while (melhorou)
        {
            melhorou = false;
            for (var i = 0; i < rota.Count - 1; i++)
                for (var j = i + 1; j < rota.Count; j++)
                {
                    var nova = rota.Take(i).Concat(rota.Skip(i).Take(j - i + 1).Reverse()).Concat(rota.Skip(j + 1)).ToList();
                    if (Distancia(Montar(nova), voltarOrigem) < Distancia(Montar(rota), voltarOrigem) - 1e-6) { rota = nova; melhorou = true; }
                }
        }
        return Montar(rota);
    }

    private static IEnumerable<List<ParadaRota>> Permutacoes(List<ParadaRota> itens)
    {
        if (itens.Count <= 1) { yield return [.. itens]; yield break; }
        for (var i = 0; i < itens.Count; i++)
        {
            var resto = itens.Where((_, k) => k != i).ToList();
            foreach (var p in Permutacoes(resto)) yield return [itens[i], .. p];
        }
    }

    /// <summary>Trechos da ordem (com a volta à origem, se pedido), com o km estimado pelo fator rodoviário.</summary>
    public static IReadOnlyList<TrechoRoteiro> Trechos(IReadOnlyList<ParadaRota> ordem, decimal fator, bool voltarOrigem = false)
    {
        var lista = new List<TrechoRoteiro>();
        var pontos = voltarOrigem && ordem.Count > 1 ? [.. ordem, ordem[0]] : ordem;
        for (var i = 1; i < pontos.Count; i++)
            lista.Add(new TrechoRoteiro(pontos[i - 1], pontos[i], RegrasLogistica.EstimarKm([pontos[i - 1], pontos[i]], fator) ?? 0));
        return lista;
    }

    public static ResumoRoteiro Resumir(IEnumerable<TrechoRoteiro> trechos, decimal velocidadeMedia, decimal custoKm, decimal pedagios)
    {
        var km = trechos.Sum(t => t.Km);
        return new ResumoRoteiro(km, RegrasLogistica.EstimarHoras(km, velocidadeMedia), custoKm, pedagios);
    }

    /// <summary>Custo por km do veículo no período: combustível e manutenção divididos pelo km rodado (maior − menor hodômetro dos abastecimentos).</summary>
    public static decimal? CustoPorKm(IReadOnlyList<Abastecimento> abastecimentos, decimal manutencao)
    {
        if (abastecimentos.Count < 2) return null;
        var km = abastecimentos.Max(a => a.Km) - abastecimentos.Min(a => a.Km);
        return km <= 0 ? null : Math.Round((abastecimentos.Sum(a => a.ValorTotal) + manutencao) / km, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Endereço do trajeto no Google Maps (rota de carro pelas paradas, na ordem).</summary>
    public static string UrlGoogleMaps(IReadOnlyList<ParadaRota> ordem, bool voltarOrigem = false)
    {
        var pontos = (voltarOrigem && ordem.Count > 1 ? [.. ordem, ordem[0]] : ordem)
            .Select(p => BaseMunicipios.Obter(p.MunicipioIbge) is { } m
                ? string.Create(CultureInfo.InvariantCulture, $"{m.Latitude:0.#####},{m.Longitude:0.#####}")
                : Uri.EscapeDataString($"{p.Municipio} {p.Uf}"));
        return "https://www.google.com/maps/dir/" + string.Join('/', pontos) + "/data=!4m2!4m1!3e0";
    }

    /// <summary>Lê a resposta do serviço de rotas (OSRM, com geometria GeoJSON): km e horas totais, km de cada trecho e os pontos do caminho.</summary>
    public static TrajetoEstrada? LerTrajeto(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;
        if (!raiz.TryGetProperty("code", out var code) || code.GetString() != "Ok" || raiz.GetProperty("routes").GetArrayLength() == 0) return null;
        var rota = raiz.GetProperty("routes")[0];
        var trechos = rota.GetProperty("legs").EnumerateArray().Select(l => Math.Round((decimal)l.GetProperty("distance").GetDouble() / 1000m, 1)).ToList();
        var pontos = rota.TryGetProperty("geometry", out var g) && g.ValueKind == JsonValueKind.Object
            ? g.GetProperty("coordinates").EnumerateArray().Select(c => (c[1].GetDouble(), c[0].GetDouble())).ToList()
            : [];
        return new TrajetoEstrada(Math.Round((decimal)rota.GetProperty("distance").GetDouble() / 1000m, 1),
                                  Math.Round((decimal)rota.GetProperty("duration").GetDouble() / 3600m, 2), trechos, pontos);
    }
}
