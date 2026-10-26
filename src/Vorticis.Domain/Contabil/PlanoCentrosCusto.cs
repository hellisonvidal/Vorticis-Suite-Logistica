using Vorticis.Domain.Comum;

namespace Vorticis.Domain.Contabil;

/// <summary>Posição de um código no plano de centros de custo: nível (1 = raiz) e código do centro superior.</summary>
public sealed record PosicaoCentro(short Nivel, string? CodigoPai);

/// <summary>
/// Regras do plano de centros de custo hierárquico (grupos sintéticos e centros analíticos), no mesmo molde do plano
/// de contas: código em níveis separados por ponto (1, 1.01, 1.01.001), definido pela máscara da empresa
/// (<see cref="MascaraPlano"/>, ex.: "9.99.999"). Sem máscara vale o cadastro livre anterior: nível = nº de segmentos.
/// </summary>
public static class PlanoCentrosCusto
{
    public const int MaximoCodigo = 30;

    /// <summary>Código do centro superior: o código sem o último segmento (null na raiz).</summary>
    public static string? CodigoPaiDe(string codigo)
    {
        var i = codigo.LastIndexOf('.');
        return i < 0 ? null : codigo[..i];
    }

    /// <summary>
    /// Confere o código pela máscara (só dígitos; cada nível com exatamente a largura da máscara; no máximo os níveis dela)
    /// e devolve o nível e o código do superior. Sem máscara, aceita código livre e calcula o nível pelos pontos.
    /// </summary>
    public static Resultado<PosicaoCentro> Validar(string codigo, MascaraPlano? mascara)
    {
        var c = (codigo ?? "").Trim();
        if (c.Length is 0 or > MaximoCodigo) return Resultado<PosicaoCentro>.Falha($"Informe o código (até {MaximoCodigo} caracteres).");
        var segs = c.Split('.');
        if (segs.Any(s => s.Length == 0)) return Resultado<PosicaoCentro>.Falha($"Código {c} inválido: níveis vazios.");
        if (mascara is not null)
        {
            if (segs.Any(s => !s.All(char.IsAsciiDigit)))
                return Resultado<PosicaoCentro>.Falha($"Código {c} inválido: use apenas números separados por ponto, conforme a máscara {mascara.Texto}.");
            if (segs.Length > mascara.Segmentos.Count)
                return Resultado<PosicaoCentro>.Falha($"Código {c} tem {segs.Length} níveis e a máscara {mascara.Texto} só {mascara.Segmentos.Count}.");
            for (var i = 0; i < segs.Length; i++)
                if (segs[i].Length != mascara.Segmentos[i])
                    return Resultado<PosicaoCentro>.Falha($"Código {c} fora da máscara {mascara.Texto}: o nível {i + 1} deve ter {mascara.Segmentos[i]} dígito(s).");
        }
        return Resultado<PosicaoCentro>.Ok(new PosicaoCentro((short)segs.Length, CodigoPaiDe(c)));
    }

    /// <summary>
    /// Tipo do novo centro. Com máscara de vários níveis o nível 1 é sempre grupo (sintético) e o último nível da máscara
    /// sempre analítico; nos demais vale a escolha do usuário. Sem máscara, vale a escolha (padrão: analítico).
    /// </summary>
    public static Resultado<bool> DefinirAnalitico(short nivel, bool sintetico, MascaraPlano? mascara)
    {
        if (mascara is null) return Resultado<bool>.Ok(!sintetico);
        var ultimo = nivel == mascara.Segmentos.Count;
        if (mascara.Segmentos.Count > 1 && nivel == 1) return Resultado<bool>.Ok(false);
        if (ultimo && sintetico) return Resultado<bool>.Falha($"O último nível da máscara ({mascara.Texto}) só admite centros analíticos.");
        return Resultado<bool>.Ok(!sintetico);
    }
}
