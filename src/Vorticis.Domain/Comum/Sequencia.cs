namespace Vorticis.Domain.Comum;

/// <summary>Sequências de códigos de cadastro: o próximo código é o menor número livre (preenche lacunas de exclusões).</summary>
public static class Sequencia
{
    /// <summary>Menor inteiro positivo que não está em <paramref name="usados"/>.</summary>
    public static int MenorLivre(IEnumerable<int> usados)
    {
        var set = usados.Where(n => n > 0).ToHashSet();
        var livre = 1;
        while (set.Contains(livre)) livre++;
        return livre;
    }

    /// <summary>Menor número livre entre códigos textuais (códigos não numéricos são ignorados).</summary>
    public static int MenorLivre(IEnumerable<string> codigos) =>
        MenorLivre(codigos.Select(c => int.TryParse(c?.Trim(), out var n) ? n : 0));
}
