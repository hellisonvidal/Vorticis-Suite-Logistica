using System.Text.Json;
using Vorticis.Domain.Comum;

namespace Vorticis.Folha.Regras;

/// <summary>Alíquotas do pró-labore (core.parametro_legal FOLHA_PRO_LABORE): INSS do contribuinte individual e CPP patronal, em %.</summary>
public sealed record ParametrosProLabore(decimal AliquotaInss, decimal AliquotaPatronal)
{
    public const string Chave = "FOLHA_PRO_LABORE";

    public static ParametrosProLabore Em(DateOnly data, IEnumerable<ParametroLegal> parametros)
    {
        var p = parametros.Where(x => x.Chave == Chave && x.VigenciaInicio <= data && (x.VigenciaFim is null || x.VigenciaFim >= data))
                          .OrderByDescending(x => x.VigenciaInicio).FirstOrDefault()
                ?? throw new InvalidOperationException($"Não há parâmetro legal {Chave} vigente em {data:dd/MM/yyyy}.");
        return JsonSerializer.Deserialize<ParametrosProLabore>(p.Valor, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}

public sealed record ResultadoProLabore(decimal Bruto, decimal Inss, decimal Irrf, decimal Liquido, decimal InssPatronal, string Memoria);

/// <summary>
/// Pró-labore do sócio (contribuinte individual): INSS de 11% sobre o valor, limitado ao que falta para o teto depois do
/// INSS já descontado em outras fontes; IRRF pela tabela mensal (dedução do INSS e dependentes); CPP patronal de 20%
/// (zero no Simples Nacional fora do anexo IV, onde a CPP está no DAS). Sem FGTS.
/// </summary>
public static class CalculoProLabore
{
    public static ResultadoProLabore Calcular(decimal valor, decimal inssOutrasFontes, int dependentes, TabelasLegais tabelas,
                                              ParametrosProLabore p, RegimeEncargos regime)
    {
        if (valor <= 0) return new ResultadoProLabore(0, 0, 0, 0, 0, "Sem pró-labore.");
        // Teto: o contribuinte individual não paga sobre o que ultrapassa o teto, somando todas as fontes.
        var tetoInss = Dinheiro.Arredondar(tabelas.Inss.Teto * p.AliquotaInss / 100m);
        var inss = Math.Min(Dinheiro.Arredondar(valor * p.AliquotaInss / 100m), Math.Max(0, tetoInss - inssOutrasFontes));
        var irrf = CalculoIrrf.Calcular(valor, inss, dependentes, 0, tabelas.Irrf);
        var patronal = regime == RegimeEncargos.SimplesSemCpp ? 0 : Dinheiro.Arredondar(valor * p.AliquotaPatronal / 100m);
        var liquido = valor - inss - irrf.Imposto;
        var memoria = $"INSS {Formato.Num(p.AliquotaInss)}% de {Formato.Moeda(valor)}"
                      + (inssOutrasFontes > 0 ? $" (outras fontes {Formato.Moeda(inssOutrasFontes)}; limite {Formato.Moeda(tetoInss)})" : "")
                      + $" = {Formato.Moeda(inss)}; {irrf.Memoria}; CPP patronal "
                      + (patronal == 0 ? "no DAS (Simples Nacional)" : $"{Formato.Num(p.AliquotaPatronal)}% = {Formato.Moeda(patronal)}");
        return new ResultadoProLabore(valor, inss, irrf.Imposto, liquido, patronal, memoria);
    }
}
