namespace Vorticis.Domain.Comum;

public static class Dinheiro
{
    /// <summary>Arredondamento comercial (meio para longe do zero), padrão em documentos fiscais.</summary>
    public static decimal Arredondar(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);

    public static bool TemNoMaximoDuasCasas(decimal valor) => valor == Math.Round(valor, 2);

    /// <summary>
    /// Divide <paramref name="total"/> proporcionalmente aos pesos, garantindo que a soma das
    /// parcelas seja exatamente o total (método do maior resto). Use em rateios de centro de custo,
    /// parcelas e apropriações para não gerar lançamentos desbalanceados por centavos.
    /// </summary>
    public static IReadOnlyList<decimal> Ratear(decimal total, IReadOnlyList<decimal> pesos)
    {
        if (!TemNoMaximoDuasCasas(total)) throw new ArgumentException("Total deve ter no máximo 2 casas decimais.", nameof(total));
        if (pesos.Count == 0) throw new ArgumentException("Informe ao menos um peso.", nameof(pesos));
        if (pesos.Any(p => p < 0)) throw new ArgumentException("Pesos não podem ser negativos.", nameof(pesos));
        var somaPesos = pesos.Sum();
        if (somaPesos == 0) throw new ArgumentException("A soma dos pesos deve ser positiva.", nameof(pesos));

        long totalCentavos = (long)(total * 100);
        var exatos = pesos.Select(p => totalCentavos * p / somaPesos).ToArray();
        var parcelas = exatos.Select(e => (long)Math.Floor(e)).ToArray();
        long sobra = totalCentavos - parcelas.Sum();

        // Distribui os centavos restantes para os maiores restos (empate: ordem original).
        foreach (var i in exatos.Select((e, i) => (resto: e - Math.Floor(e), i))
                                .OrderByDescending(x => x.resto).ThenBy(x => x.i)
                                .Take((int)Math.Abs(sobra)).Select(x => x.i))
            parcelas[i] += Math.Sign(sobra);

        return parcelas.Select(c => c / 100m).ToArray();
    }
}
