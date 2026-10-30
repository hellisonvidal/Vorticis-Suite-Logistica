namespace Vorticis.Domain.Erp.Relatorios;

public sealed class LinhaFluxoCaixa
{
    public string Grupo { get; init; } = "";
    public string Descricao { get; init; } = "";

    [Vorticis.Infra.Exportacao.Moeda(2, "R$ ")]
    [Vorticis.Infra.Exportacao.ColunaExcel(Cabecalho = "Previsto", Largura = 18)]
    public decimal Previsto { get; init; }

    [Vorticis.Infra.Exportacao.Moeda(2, "R$ ")]
    [Vorticis.Infra.Exportacao.ColunaExcel(Cabecalho = "Realizado", Largura = 18)]
    public decimal Realizado { get; init; }

    [Vorticis.Infra.Exportacao.Moeda(2, "R$ ")]
    [Vorticis.Infra.Exportacao.ColunaExcel(Cabecalho = "Diferença", Largura = 18)]
    public decimal Diferenca { get; init; }

    [Vorticis.Infra.Exportacao.ColunaExcel(Cabecalho = "% Realizado", Formato = "0.00%")]
    public decimal PercentualRealizado { get; init; }

    public bool EhTotalizador { get; init; }
    public int Nivel { get; init; }
}

public sealed class LinhaDre
{
    public string ContaCodigo { get; init; } = "";
    public string ContaNome { get; init; } = "";

    [Vorticis.Infra.Exportacao.Moeda(2, "R$ ")]
    [Vorticis.Infra.Exportacao.ColunaExcel(Cabecalho = "Valor", Largura = 18)]
    public decimal Valor { get; init; }

    [Vorticis.Infra.Exportacao.ColunaExcel(Cabecalho = "% Receita", Formato = "0.00%")]
    public decimal PercentualReceita { get; init; }

    public bool EhTotalizador { get; init; }
    public int Nivel { get; init; }
}