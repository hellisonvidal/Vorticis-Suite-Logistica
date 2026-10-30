namespace Vorticis.Domain.Frota.Relatorios;

public sealed class LinhaCustoVeiculo
{
    public string VeiculoPlaca { get; init; } = "";
    public string VeiculoModelo { get; init; } = "";

    [Vorticis.Infra.Exportacao.Moeda(2, "R$ ")]
    [Vorticis.Infra.Exportacao.ColunaExcel(Cabecalho = "Combustível", Largura = 18)]
    public decimal Combustivel { get; init; }

    [Vorticis.Infra.Exportacao.Moeda(2, "R$ ")]
    [Vorticis.Infra.Exportacao.ColunaExcel(Cabecalho = "Manutenção", Largura = 18)]
    public decimal Manutencao { get; init; }

    [Vorticis.Infra.Exportacao.Moeda(2, "R$ ")]
    [Vorticis.Infra.Exportacao.ColunaExcel(Cabecalho = "Pneus", Largura = 18)]
    public decimal Pneus { get; init; }

    [Vorticis.Infra.Exportacao.Moeda(2, "R$ ")]
    [Vorticis.Infra.Exportacao.ColunaExcel(Cabecalho = "Seguros", Largura = 18)]
    public decimal Seguros { get; init; }

    [Vorticis.Infra.Exportacao.Moeda(2, "R$ ")]
    [Vorticis.Infra.Exportacao.ColunaExcel(Cabecalho = "IPVA/Licenciamento", Largura = 20)]
    public decimal IpvaLicenciamento { get; init; }

    [Vorticis.Infra.Exportacao.Moeda(2, "R$ ")]
    [Vorticis.Infra.Exportacao.ColunaExcel(Cabecalho = "Multas", Largura = 18)]
    public decimal Multas { get; init; }

    [Vorticis.Infra.Exportacao.Moeda(2, "R$ ")]
    [Vorticis.Infra.Exportacao.ColunaExcel(Cabecalho = "Pedágio", Largura = 18)]
    public decimal Pedagio { get; init; }

    [Vorticis.Infra.Exportacao.Moeda(2, "R$ ")]
    [Vorticis.Infra.Exportacao.ColunaExcel(Cabecalho = "Outros", Largura = 18)]
    public decimal Outros { get; init; }

    [Vorticis.Infra.Exportacao.Moeda(2, "R$ ")]
    [Vorticis.Infra.Exportacao.ColunaExcel(Cabecalho = "Total", Largura = 18)]
    public decimal Total { get; init; }
}