using Vorticis.Domain.Contabil;

namespace Vorticis.Domain.Tests;

public class MutacoesTests
{
    private static readonly ContaPlano[] Plano =
    [
        new(1, "1.1", "Caixa", 2, true, null, IndicadorDC.D, "01"),
        new(2, "2.1", "Dividendos a pagar", 2, true, null, IndicadorDC.C, "02"),
        new(10, "2.3", "PATRIMÔNIO LÍQUIDO", 1, false, null, IndicadorDC.C, "03"),
        new(11, "2.3.01", "CAPITAL SOCIAL", 2, false, 10, IndicadorDC.C, "03"),
        new(12, "2.3.01.001", "Capital Subscrito", 3, true, 11, IndicadorDC.C, "03"),
        new(13, "2.3.02", "RESERVAS", 2, false, 10, IndicadorDC.C, "03"),
        new(14, "2.3.02.001", "Reserva Legal", 3, true, 13, IndicadorDC.C, "03"),
        new(15, "2.3.03", "LUCROS OU PREJUÍZOS ACUMULADOS", 2, false, 10, IndicadorDC.C, "03"),
        new(16, "2.3.03.001", "Lucros Acumulados", 3, true, 15, IndicadorDC.C, "03"),
        new(20, "3.1", "Vendas", 2, true, null, IndicadorDC.C, "04"),
        new(21, "3.9", "Apuração do resultado", 2, true, null, IndicadorDC.C, "04"),
    ];

    private static PartidaMutacao P(long lanc, long conta, IndicadorDC dc, decimal v, bool enc = false) => new(lanc, conta, dc, v, enc);

    [Fact]
    public void Classifica_contas_do_pl_pelas_descricoes()
    {
        var porId = Plano.ToDictionary(c => c.Id);
        Assert.Equal(ColunaPl.CapitalSocial, Mutacoes.Classificar(porId[12], porId));
        Assert.Equal(ColunaPl.ReservasLucros, Mutacoes.Classificar(porId[14], porId));
        Assert.Equal(ColunaPl.LucrosAcumulados, Mutacoes.Classificar(porId[16], porId));
        Assert.Null(Mutacoes.Classificar(porId[1], porId));
    }

    [Fact]
    public void Dmpl_e_dlpa_conferem_com_o_saldo_final()
    {
        var saldos = new Dictionary<long, decimal> { [12] = -10_000m, [16] = -2_000m };
        IReadOnlyList<PartidaMutacao> partidas =
        [
            P(1, 1, IndicadorDC.D, 5_000m), P(1, 12, IndicadorDC.C, 5_000m),          // integralização em dinheiro
            P(2, 16, IndicadorDC.D, 100m), P(2, 14, IndicadorDC.C, 100m),             // reserva legal
            P(3, 16, IndicadorDC.D, 800m), P(3, 2, IndicadorDC.C, 800m),              // dividendos propostos
            P(4, 21, IndicadorDC.D, 3_000m, true), P(4, 16, IndicadorDC.C, 3_000m, true),   // encerramento: só transfere
        ];
        var d = Mutacoes.Montar(new(2026, 1, 1), new(2026, 12, 31), Plano, saldos, partidas, lucroLiquido: 3_000m);

        Assert.Equal("Saldo inicial em 31/12/2025", d.Dmpl[0].Descricao);
        Assert.Equal(12_000m, d.Dmpl[0].Total);
        Assert.Equal(5_000m, d.Dmpl.Single(l => l.Descricao == Mutacoes.AumentoCapital).Valor(ColunaPl.CapitalSocial));
        var reservas = d.Dmpl.Single(l => l.Descricao == Mutacoes.ConstituicaoReservas);
        Assert.Equal((100m, -100m, 0m), (reservas.Valor(ColunaPl.ReservasLucros), reservas.Valor(ColunaPl.LucrosAcumulados), reservas.Total));
        Assert.Equal(-800m, d.Dmpl.Single(l => l.Descricao == Mutacoes.Dividendos).Total);
        var final = d.Dmpl[^1];
        Assert.Equal((15_000m, 100m, 4_100m), (final.Valor(ColunaPl.CapitalSocial), final.Valor(ColunaPl.ReservasLucros), final.Valor(ColunaPl.LucrosAcumulados)));
        Assert.Equal(19_200m, final.Total);                                           // 12.000 + 5.000 + 3.000 − 800
        Assert.DoesNotContain(ColunaPl.AcoesTesouraria, d.Colunas);

        // DLPA: 2.000 + 3.000 − 100 − 800 = 4.100
        Assert.Equal(2_000m, d.Dlpa[0].Valor);
        Assert.Equal(5_000m, d.Dlpa.Single(l => l.Descricao == "Saldo à disposição").Valor);
        Assert.Equal(-100m, d.Dlpa.Single(l => l.Descricao.Contains("Transferências para reservas")).Valor);
        Assert.Equal(4_100m, d.Dlpa[^1].Valor);
    }

    private static DemonstracaoMutacoes Exemplo()
    {
        var saldos = new Dictionary<long, decimal> { [12] = -10_000m, [14] = -500m, [16] = -2_000m };
        IReadOnlyList<PartidaMutacao> partidas =
        [
            P(1, 1, IndicadorDC.D, 5_000m), P(1, 12, IndicadorDC.C, 5_000m),          // integralização em dinheiro
            P(2, 16, IndicadorDC.D, 100m), P(2, 14, IndicadorDC.C, 100m),             // reserva legal
            P(3, 16, IndicadorDC.D, 800m), P(3, 2, IndicadorDC.C, 800m),              // dividendos propostos
            P(4, 14, IndicadorDC.D, 200m), P(4, 16, IndicadorDC.C, 200m),             // reversão de reserva
        ];
        return Mutacoes.Montar(new(2026, 1, 1), new(2026, 12, 31), Plano, saldos, partidas, lucroLiquido: 3_000m);
    }

    [Fact]
    public void Dmpl_segue_o_modelo_do_cpc_26_com_colunas_e_linhas_fixas()
    {
        var d = Exemplo();
        // uma coluna por componente do PL, na ordem do CPC 26 (mesmo as sem saldo); o total vem na tabela
        Assert.Equal([ColunaPl.CapitalSocial, ColunaPl.ReservasCapital, ColunaPl.ReservasLucros, ColunaPl.AjustesAvaliacao, ColunaPl.LucrosAcumulados], d.Colunas);
        Assert.Equal(["Capital social", "Reservas de capital", "Reservas de lucros", "Ajustes de avaliação patrimonial", "Lucros ou prejuízos acumulados"],
                     d.Colunas.Select(DemonstracaoMutacoes.NomeColuna));

        Assert.Equal(["Saldo inicial em 31/12/2025", Mutacoes.Outras, Mutacoes.AumentoCapital, Mutacoes.ReversaoReservas, Mutacoes.LucroLiquido,
                      Mutacoes.ConstituicaoReservas, Mutacoes.Dividendos, "Saldo final em 31/12/2026"], d.Dmpl.Select(l => l.Descricao));
        Assert.True(d.Dmpl[0].Saldo && d.Dmpl[^1].Saldo);
        // 12.500 de saldo inicial + 5.000 de capital + 3.000 de resultado − 800 de dividendos
        Assert.Equal(19_700m, d.Dmpl[^1].Total);
        Assert.Equal(d.Dmpl[0].Total + d.Dmpl.Where(l => !l.Saldo).Sum(l => l.Total), d.Dmpl[^1].Total);   // saldo inicial + mutações = saldo final
    }

    [Fact]
    public void Dmpl_sem_movimento_mantem_as_linhas_fixas_zeradas()
    {
        var d = Mutacoes.Montar(new(2026, 1, 1), new(2026, 12, 31), Plano, new Dictionary<long, decimal> { [12] = -1_000m }, [], lucroLiquido: 0m);
        Assert.Equal(1_000m, d.Dmpl[0].Total);
        Assert.Contains(d.Dmpl, l => l.Descricao == Mutacoes.Outras && l.Total == 0);
        Assert.Contains(d.Dmpl, l => l.Descricao == Mutacoes.Dividendos && l.Total == 0);
        Assert.DoesNotContain(d.Dmpl, l => l.Descricao == Mutacoes.ReversaoReservas);          // só quando há reversão
        Assert.Equal(1_000m, d.Dmpl[^1].Total);
    }

    [Fact]
    public void Dlpa_segue_o_art_186_da_lei_6404()
    {
        var d = Exemplo();
        Assert.Equal(["Saldo no início do período (01/01/2026)", "(+/-) Ajustes de exercícios anteriores e outras mutações", "(+) Reversão de reservas",
                      "(+/-) Lucro líquido (prejuízo) do exercício", "Saldo à disposição", "Destinações:", "(-) Transferências para reservas",
                      "(-) Dividendos e lucros distribuídos", "(-) Incorporação ao capital", "Saldo no fim do período (31/12/2026)"],
                     d.Dlpa.Select(l => l.Descricao));
        Assert.True(d.Dlpa.Single(l => l.Descricao == "Destinações:").SemValor);
        Assert.Equal(200m, d.Dlpa.Single(l => l.Descricao == "(+) Reversão de reservas").Valor);
        // 2.000 + 200 (reversão) + 3.000 (lucro) = 5.200 à disposição; − 100 (reserva) − 800 (dividendos) = 4.300
        Assert.Equal(5_200m, d.Dlpa.Single(l => l.Descricao == "Saldo à disposição").Valor);
        Assert.Equal(4_300m, d.Dlpa[^1].Valor);
        Assert.Equal(d.Dmpl[^1].Valor(ColunaPl.LucrosAcumulados), d.Dlpa[^1].Valor);              // DLPA e DMPL fecham no mesmo saldo
    }

    [Fact]
    public void Tabelas_da_dmpl_e_da_dlpa_sao_independentes()
    {
        var d = Exemplo();
        var dmpl = Application.Relatorios.TabelasMutacoes.Dmpl(d);
        Assert.Equal(7, dmpl.Colunas.Count);                                                    // descrição + 5 componentes + total
        Assert.Equal("Total", dmpl.Colunas[^1].Titulo);
        Assert.All(dmpl.Linhas, l => Assert.Equal(7, l.Valores.Count));
        Assert.Equal(d.Dmpl[^1].Total, dmpl.Linhas[^1].Valores[^1]);

        var dlpa = Application.Relatorios.TabelasMutacoes.Dlpa(d);
        Assert.Equal(2, dlpa.Colunas.Count);
        Assert.Null(dlpa.Linhas.Single(l => (string?)l.Valores[0] == "Destinações:").Valores[1]);
        Assert.NotEqual(dmpl.Titulo, dlpa.Titulo);
    }
}
