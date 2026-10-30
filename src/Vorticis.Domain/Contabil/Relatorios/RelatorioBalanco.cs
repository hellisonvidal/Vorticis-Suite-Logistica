namespace Vorticis.Domain.Contabil.Relatorios;

public sealed class LinhaBalanco
{
    public string ContaCodigo { get; init; } = "";
    public string ContaNome { get; init; } = "";

    [Vorticis.Infra.Exportacao.Moeda(2, "R$ ")]
    [Vorticis.Infra.Exportacao.ColunaExcel(Cabecalho = "Saldo Atual", Largura = 18)]
    public decimal SaldoAtual { get; init; }

    [Vorticis.Infra.Exportacao.Moeda(2, "R$ ")]
    [Vorticis.Infra.Exportacao.ColunaExcel(Cabecalho = "Saldo Anterior", Largura = 18)]
    public decimal SaldoAnterior { get; init; }

    [Vorticis.Infra.Exportacao.Moeda(2, "R$ ")]
    [Vorticis.Infra.Exportacao.ColunaExcel(Cabecalho = "Variação", Largura = 18)]
    public decimal Variacao { get; init; }

    [Vorticis.Infra.Exportacao.ColunaExcel(Cabecalho = "Variação %", Formato = "0.00%")]
    public decimal VariacaoPercentual { get; init; }

    public bool EhTotalizador { get; init; }
    public int Nivel { get; init; }
}