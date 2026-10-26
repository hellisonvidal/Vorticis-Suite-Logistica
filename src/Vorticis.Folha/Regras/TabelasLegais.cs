using System.Text.Json;

namespace Vorticis.Folha.Regras;

/// <summary>Faixa do INSS do empregado: alíquota (%) aplicada à parcela da base até o limite <paramref name="Ate"/>.</summary>
public sealed record FaixaInss(decimal Ate, decimal Aliquota);

public sealed record TabelaInss(IReadOnlyList<FaixaInss> Faixas)
{
    /// <summary>Teto do salário de contribuição (limite da última faixa).</summary>
    public decimal Teto => Faixas[^1].Ate;
}

/// <summary>Faixa da tabela progressiva do IRRF. <paramref name="Ate"/> nulo = faixa final (sem limite).</summary>
public sealed record FaixaIrrf(decimal? Ate, decimal Aliquota, decimal Deduzir);

/// <summary>
/// Redução do IRRF da Lei 15.270/2025: até <see cref="LimiteIsencao"/> de rendimento a redução vai até
/// <see cref="ReducaoMaxima"/> (limitada ao imposto, o que zera o IRRF); acima disso e até <see cref="LimiteSuperior"/>,
/// redução de <see cref="Constante"/> - <see cref="Coeficiente"/> x rendimento tributável.
/// </summary>
public sealed record ReducaoIrrf(decimal LimiteIsencao, decimal ReducaoMaxima, decimal LimiteSuperior, decimal Constante, decimal Coeficiente);

public sealed record TabelaIrrf(IReadOnlyList<FaixaIrrf> Faixas, decimal DeducaoDependente, decimal DescontoSimplificado, ReducaoIrrf? Reducao);

public sealed record TabelaSalarioFamilia(decimal LimiteRemuneracao, decimal Cota);

/// <summary>Alíquotas do FGTS (%): depósito mensal, aprendiz e multa rescisória.</summary>
public sealed record ParametrosFgts(decimal Aliquota, decimal AliquotaAprendiz, decimal MultaSemJustaCausa, decimal MultaAcordo);

/// <summary>Linha de <c>core.parametro_legal</c> (ou do arquivo embutido) de uma chave FOLHA_*.</summary>
public sealed record ParametroLegal(string Chave, DateOnly VigenciaInicio, DateOnly? VigenciaFim, string Valor, string Fundamento);

/// <summary>Conjunto de tabelas legais vigentes em uma data.</summary>
public sealed record TabelasLegais(TabelaInss Inss, TabelaIrrf Irrf, TabelaSalarioFamilia SalarioFamilia, decimal SalarioMinimo, ParametrosFgts Fgts)
{
    public const string ChaveInss = "FOLHA_INSS_EMPREGADO";
    public const string ChaveIrrf = "FOLHA_IRRF_MENSAL";
    public const string ChaveSalarioFamilia = "FOLHA_SALARIO_FAMILIA";
    public const string ChaveSalarioMinimo = "FOLHA_SALARIO_MINIMO";
    public const string ChaveFgts = "FOLHA_FGTS";

    public static readonly string[] Chaves = [ChaveInss, ChaveIrrf, ChaveSalarioFamilia, ChaveSalarioMinimo, ChaveFgts];

    private static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web);

    /// <summary>Escolhe, para cada tabela, a vigência que cobre <paramref name="data"/>.</summary>
    public static TabelasLegais Em(DateOnly data, IEnumerable<ParametroLegal> parametros)
    {
        var lista = parametros.ToList();
        ParametroLegal Vigente(string chave) =>
            lista.Where(p => p.Chave == chave && p.VigenciaInicio <= data && (p.VigenciaFim is null || p.VigenciaFim >= data))
                 .OrderByDescending(p => p.VigenciaInicio).FirstOrDefault()
            ?? throw new InvalidOperationException($"Não há tabela legal {chave} vigente em {data:dd/MM/yyyy}. Cadastre a vigência em core.parametro_legal.");

        var inss = JsonSerializer.Deserialize<DtoInss>(Vigente(ChaveInss).Valor, Opcoes)!;
        var irrf = JsonSerializer.Deserialize<DtoIrrf>(Vigente(ChaveIrrf).Valor, Opcoes)!;
        var sf = JsonSerializer.Deserialize<DtoSalarioFamilia>(Vigente(ChaveSalarioFamilia).Valor, Opcoes)!;
        var min = JsonSerializer.Deserialize<DtoValor>(Vigente(ChaveSalarioMinimo).Valor, Opcoes)!;
        var fgts = JsonSerializer.Deserialize<DtoFgts>(Vigente(ChaveFgts).Valor, Opcoes)!;
        return new TabelasLegais(
            new TabelaInss(inss.Faixas.Select(f => new FaixaInss(f.Ate, f.Aliquota)).ToArray()),
            new TabelaIrrf(irrf.Faixas.Select(f => new FaixaIrrf(f.Ate, f.Aliquota, f.Deduzir)).ToArray(), irrf.DeducaoDependente,
                           irrf.DescontoSimplificado, irrf.Reducao),
            new TabelaSalarioFamilia(sf.LimiteRemuneracao, sf.Cota), min.Valor,
            new ParametrosFgts(fgts.Aliquota, fgts.AliquotaAprendiz, fgts.MultaSemJustaCausa, fgts.MultaAcordo));
    }

    /// <summary>
    /// Tabelas embutidas (Dados/tabelas_legais.json): as mesmas vigências gravadas pela migração 040, usadas em testes
    /// e como referência. O aplicativo lê sempre do banco.
    /// </summary>
    public static IReadOnlyList<ParametroLegal> Embutidas()
    {
        using var s = typeof(TabelasLegais).Assembly.GetManifestResourceStream("Folha.tabelas_legais.json")
                      ?? throw new InvalidOperationException("Recurso tabelas_legais.json não embutido.");
        var itens = JsonSerializer.Deserialize<DtoParametro[]>(s, Opcoes)!;
        return itens.Select(i => new ParametroLegal(i.Chave, DateOnly.Parse(i.VigenciaInicio),
                                                    i.VigenciaFim is null ? null : DateOnly.Parse(i.VigenciaFim), i.Valor.GetRawText(), i.Fundamento)).ToArray();
    }

    private sealed record DtoParametro(string Chave, string VigenciaInicio, string? VigenciaFim, JsonElement Valor, string Fundamento);
    private sealed record DtoFaixaInss(decimal Ate, decimal Aliquota);
    private sealed record DtoInss(DtoFaixaInss[] Faixas);
    private sealed record DtoFaixaIrrf(decimal? Ate, decimal Aliquota, decimal Deduzir);
    private sealed record DtoIrrf(DtoFaixaIrrf[] Faixas, decimal DeducaoDependente, decimal DescontoSimplificado, ReducaoIrrf? Reducao);
    private sealed record DtoSalarioFamilia(decimal LimiteRemuneracao, decimal Cota);
    private sealed record DtoValor(decimal Valor);
    private sealed record DtoFgts(decimal Aliquota, decimal AliquotaAprendiz, decimal MultaSemJustaCausa, decimal MultaAcordo);
}
