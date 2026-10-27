using Vorticis.Frota;
using Vorticis.Infra.Tests;

namespace Vorticis.Modulos.Tests;

public class RegrasContratosTests
{
    private static ContratoFinanceiro Financiamento(string sistema = "PRICE", decimal taxa = 1.5m, int parcelas = 48) =>
        new(0, RegrasContratos.Financiamento, "F1", "Caminhão", null, new(2026, 1, 10), 300000, 60000, 0, taxa, sistema, 0, 0, 0, parcelas, new(2026, 2, 10));

    [Fact]
    public void Price_tem_parcelas_iguais_e_quita_o_financiado()
    {
        var p = RegrasContratos.Calcular(Financiamento());
        Assert.Equal(48, p.Count);
        Assert.Equal(240000m, p.Sum(x => x.Amortizacao));
        Assert.Equal(0m, p[^1].Saldo);
        Assert.Equal(3600m, p[0].Juros);                                              // 1,5% de 240.000
        Assert.Single(p.Take(47).Select(x => x.Valor).Distinct());                   // parcelas iguais (a última acerta os centavos)
        Assert.InRange(p[^1].Valor - p[0].Valor, -0.5m, 0.5m);
        Assert.Equal(new DateOnly(2030, 1, 10), p[^1].Vencimento);
    }

    [Fact]
    public void Sac_amortiza_igual_e_juros_caem()
    {
        var p = RegrasContratos.Calcular(Financiamento("SAC", 1, 24) with { ValorBem = 120000, Entrada = 20000 });
        Assert.Equal(4166.67m, p[0].Amortizacao);
        Assert.Equal(4166.59m, p[^1].Amortizacao);                                   // acerto dos centavos
        Assert.Equal(1000m, p[0].Juros);
        Assert.True(p[1].Juros < p[0].Juros);
        // sem juros: só a amortização
        Assert.All(RegrasContratos.Calcular(Financiamento(taxa: 0, parcelas: 10)), x => Assert.Equal(0m, x.Juros));
    }

    [Fact]
    public void Consorcio_tem_fundo_comum_taxa_fundo_de_reserva_e_seguro()
    {
        var c = new ContratoFinanceiro(0, RegrasContratos.Consorcio, "G1/C2", "Carreta", null, new(2026, 1, 5), 100000, 0, 0, 0, "PRICE", 15, 2, 50, 10, new(2026, 2, 5));
        var p = RegrasContratos.Calcular(c);
        Assert.Equal((10000m, 1500m, 200m, 50m, 11750m), (p[0].Amortizacao, p[0].Juros, p[0].Outros, p[0].Seguro, p[0].Valor));
        Assert.Equal(100000m, p.Sum(x => x.Amortizacao));
        // sem contemplação não há dívida no passivo; contemplado, tudo vence em menos de 12 meses (curto prazo)
        Assert.Equal(new DivisaoPrazos(0, 0), RegrasContratos.Dividir(c, p, c.DataContrato));
        Assert.Equal(new DivisaoPrazos(100000, 0), RegrasContratos.Dividir(c with { DataContemplacao = c.DataContrato }, p, c.DataContrato));
    }

    [Fact]
    public void Curto_prazo_sao_as_parcelas_dos_proximos_12_meses()
    {
        var c = Financiamento("SAC", 1, 24) with { ValorBem = 120000, Entrada = 20000 };
        var p = RegrasContratos.Calcular(c).ToList();
        Assert.Equal(new DivisaoPrazos(50000.04m, 49999.96m), RegrasContratos.Dividir(c, p, c.DataContrato));
        p[0] = p[0] with { PagoEm = new(2026, 2, 10), ValorPago = 1 };
        Assert.Equal(new DivisaoPrazos(50000.04m, 45833.29m), RegrasContratos.Dividir(c, p, new(2026, 2, 28)));   // a 14ª passou para o curto prazo
    }

    [Fact]
    public void Validacao_e_rateio_por_placa()
    {
        var c = Financiamento();
        Assert.Empty(RegrasContratos.Validar(c, []));
        Assert.Contains("O rateio por placa deve somar 100%.", RegrasContratos.Validar(c, [new(1, 60), new(2, 30)]));
        Assert.Contains("A entrada deve ser menor que o valor do bem.", RegrasContratos.Validar(c with { Entrada = 300000 }, []));
        Assert.NotEmpty(RegrasContratos.Validar(c with { Contabiliza = true }, []));                                  // contas obrigatórias
        Assert.Equal([(1L, 60.01m), (2L, 39.99m)], RegrasContratos.Ratear(100m, [new(1, 60.005m), new(2, 39.995m)]).Select(x => (x.VeiculoId!.Value, x.Valor)));
        Assert.Equal([((long?)null, 50m)], RegrasContratos.Ratear(50m, []));
    }
}

public class ContratosIntegracaoTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private const string Bem = "1.2.03.003", Banco = "1.1.01.002", Curto = "2.1.05.001", Longo = "2.2.01.001", Juros = "4.3.01.001", Consorcio = "1.1.02.003";

    private async Task<List<(string Conta, string DC, decimal Valor, string? Centro)>> PartidasAsync(long empresa, long contrato, string tipo)
    {
        var lista = new List<(string, string, decimal, string?)>();
        await using var con = await db.App.DataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new Npgsql.NpgsqlCommand("""
            SELECT pc.codigo, p.ind_dc::text, p.valor, cc.codigo FROM frota.contrato_lancamento l JOIN contabil.partida p ON p.lancamento_id = l.lancamento_id
              JOIN contabil.plano_contas pc ON pc.id = p.conta_id LEFT JOIN contabil.centro_custo cc ON cc.id = p.centro_custo_id
             WHERE l.contrato_id = @c AND l.tipo = @t ORDER BY p.sequencia
            """, con);
        cmd.Parameters.AddWithValue("c", contrato);
        cmd.Parameters.AddWithValue("t", tipo);
        await using var r = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await r.ReadAsync(TestContext.Current.CancellationToken)) lista.Add((r.GetString(0), r.GetString(1), r.GetDecimal(2), r.IsDBNull(3) ? null : r.GetString(3)));
        return lista;
    }

    private async Task<(long Empresa, long Adm, long V1, long V2)> PrepararAsync(string cnpj)
    {
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync(cnpj);
        var svc = ServicosFrota.De(db.App);
        Assert.True((await db.App.PlanoContas.IncluirCentroAsync(empresa, "7", "Frota pesada", "", adm, ct: ct)).Sucesso);
        var centro = (await db.App.PlanoContas.ListarCentrosAsync(empresa, adm, ct)).Single().Id;
        var frota = (await svc.GravarFrotaAsync(empresa, new FrotaCadastro(0, 1, "Pesada", "PROPRIA", null, centro), adm, ct)).Valor;
        var v1 = (await svc.GravarVeiculoAsync(empresa, new Veiculo(0, 1, "ABC1D23", null, "CAVALO", null, null, null, null, "DIESEL_S10", null, 0, null, FrotaId: frota), adm, ct)).Valor;
        var v2 = (await svc.GravarVeiculoAsync(empresa, new Veiculo(0, 2, "XYZ9A87", null, "CARRETA", null, null, null, null, "NENHUM", null, 0, null), adm, ct)).Valor;
        return (empresa, adm, v1, v2);
    }

    [Fact]
    public async Task Financiamento_contabiliza_contratacao_pagamento_e_reclassificacao()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var (empresa, adm, v1, v2) = await PrepararAsync("73461825000187");
        var svc = ServicosFrota.De(db.App);
        var c = new ContratoFinanceiro(0, RegrasContratos.Financiamento, "FIN-1", "Cavalo mecânico", null, new(2026, 1, 10), 120000, 20000, 0, 1, "SAC", 0, 0, 0, 24,
                                       new(2026, 2, 10), Contabiliza: true, ContaBem: Bem, ContaPagamento: Banco, ContaCurtoPrazo: Curto, ContaLongoPrazo: Longo,
                                       ContaDespesa: Juros);
        Assert.False((await svc.GravarContratoAsync(empresa, c, [new(v1, 60)], adm, ct)).Sucesso);                 // rateio não soma 100%
        var r = await svc.GravarContratoAsync(empresa, c, [new(v1, 60), new(v2, 40)], adm, ct);
        Assert.True(r.Sucesso, string.Join(" ", r.Erros));
        var id = r.Valor;
        Assert.Contains("já existe", (await svc.GravarContratoAsync(empresa, c, [], adm, ct)).Erros[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(24, (await svc.ParcelasContratoAsync(empresa, id, adm, ct)).Count);

        Assert.Contains("Contabilize a contratação", (await svc.PagarParcelaAsync(empresa, id, 1, new(2026, 2, 10), 5166.67m, adm, ct)).Erros[0]);
        var rc = await svc.ContabilizarContratacaoAsync(empresa, id, adm, ct);
        Assert.True(rc.Sucesso, string.Join(" ", rc.Erros));
        Assert.Equal([(Bem, "D", 120000m, (string?)null), (Banco, "C", 20000m, null), (Curto, "C", 50000.04m, null), (Longo, "C", 49999.96m, null)],
                     await PartidasAsync(empresa, id, "CONTRATACAO"));
        Assert.False((await svc.ContabilizarContratacaoAsync(empresa, id, adm, ct)).Sucesso);                       // não contabiliza duas vezes

        // pagamento com R$ 33,33 de juros de atraso: a despesa é rateada 60/40 e a placa da frota leva o centro de custo dela
        var rp = await svc.PagarParcelaAsync(empresa, id, 1, new(2026, 2, 10), 5200m, adm, ct);
        Assert.True(rp.Sucesso, string.Join(" ", rp.Erros));
        Assert.Equal([(Curto, "D", 4166.67m, (string?)null), (Juros, "D", 620m, "7"), (Juros, "D", 413.33m, null), (Banco, "C", 5200m, null)],
                     await PartidasAsync(empresa, id, "PAGAMENTO"));
        Assert.False((await svc.PagarParcelaAsync(empresa, id, 1, new(2026, 2, 10), 5200m, adm, ct)).Sucesso);       // já paga
        // valores não mudam depois de pago
        Assert.False((await svc.GravarContratoAsync(empresa, c with { Id = id, TaxaJuros = 2 }, [new(v1, 60), new(v2, 40)], adm, ct)).Sucesso);
        Assert.True((await svc.GravarContratoAsync(empresa, c with { Id = id, Descricao = "Cavalo Scania" }, [new(v1, 100)], adm, ct)).Sucesso);

        // reclassificação no fim do mês: a 14ª parcela passa do longo para o curto prazo
        var rr = await svc.ReclassificarAsync(empresa, id, new(2026, 2, 28), adm, ct);
        Assert.True(rr.Sucesso, string.Join(" ", rr.Erros));
        Assert.Equal(4166.67m, rr.Valor);
        Assert.Equal(new SaldosContabeisContrato(50000.04m, 45833.29m, 0, true), await svc.SaldosContabeisAsync(id, ct));
        Assert.False((await svc.ReclassificarAsync(empresa, id, new(2026, 2, 28), adm, ct)).Sucesso);              // nada mais a reclassificar

        // posição (Imobilizado › Bens financiados): saldo devedor e prazos
        var pos = Assert.Single(await svc.PosicaoContratosAsync(empresa, new(2026, 2, 28), adm, ct));
        Assert.Equal((1, 95833.33m, "ABC1D23"), (pos.Pagas, pos.SaldoDevedor, pos.Placas));

        // bem do imobilizado gerado pelo contrato (grupo de veículos de carga)
        var grupo = (await svc.GruposImobilizadoAsync(ct)).Single(g => g.Codigo == "06");
        var rb = await svc.GerarBemAsync(empresa, id, grupo.Id, adm, ct);
        Assert.True(rb.Sucesso, string.Join(" ", rb.Erros));
        var bem = (await db.App.Imobilizado.ListarBensAsync(empresa, adm, ct)).Single();
        Assert.Equal((120000m, new DateOnly(2026, 1, 10), "ABC1D23"), (bem.Valor, bem.DataAquisicao, bem.Plaqueta));
        Assert.StartsWith("1 – ", (await svc.ContratosAsync(empresa, RegrasContratos.Financiamento, adm, ct)).Single().Bem);

        // estornos: pagamento (com lançamento) e depois a contratação só sem outros movimentos
        Assert.Contains("estorne", (await svc.DesfazerContratacaoAsync(empresa, id, adm, ct)).Erros[0]);
        Assert.True((await svc.EstornarPagamentoAsync(empresa, id, 1, adm, ct)).Sucesso);
        Assert.False((await svc.ParcelasContratoAsync(empresa, id, adm, ct))[0].Paga);
        Assert.False((await svc.ExcluirContratoAsync(empresa, id, adm, ct)).Sucesso);                              // ainda há lançamentos
    }

    [Fact]
    public async Task Consorcio_vai_ao_ativo_ate_a_contemplacao_e_depois_ao_passivo()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var (empresa, adm, v1, _) = await PrepararAsync("58204739000103");
        var svc = ServicosFrota.De(db.App);
        var c = new ContratoFinanceiro(0, RegrasContratos.Consorcio, "G10/C25", "Carreta", null, new(2026, 1, 5), 100000, 0, 0, 0, "PRICE", 15, 2, 50, 10,
                                       new(2026, 2, 5), Contabiliza: true, ContaBem: Bem, ContaPagamento: Banco, ContaCurtoPrazo: Curto, ContaLongoPrazo: Longo,
                                       ContaDespesa: Juros, ContaConsorcio: Consorcio);
        var id = (await svc.GravarContratoAsync(empresa, c, [new(v1, 100)], adm, ct)).Valor;
        Assert.False((await svc.ContabilizarContratacaoAsync(empresa, id, adm, ct)).Sucesso);                       // consórcio não lança na adesão

        Assert.True((await svc.PagarParcelaAsync(empresa, id, 1, new(2026, 2, 5), 11750m, adm, ct)).Sucesso);
        Assert.Equal([(Consorcio, "D", 10200m, (string?)null), (Juros, "D", 1550m, "7"), (Banco, "C", 11750m, null)], await PartidasAsync(empresa, id, "PAGAMENTO"));

        var rc = await svc.RegistrarContemplacaoAsync(empresa, id, new(2026, 3, 1), adm, ct);
        Assert.True(rc.Sucesso, string.Join(" ", rc.Erros));
        Assert.Equal([(Bem, "D", 100000m, (string?)null), (Consorcio, "C", 10000m, null), (Curto, "C", 90000m, null)], await PartidasAsync(empresa, id, "CONTEMPLACAO"));
        Assert.Equal(new SaldosContabeisContrato(90000m, 0, 200m, true), await svc.SaldosContabeisAsync(id, ct));   // no ativo fica o fundo de reserva

        Assert.True((await svc.PagarParcelaAsync(empresa, id, 2, new(2026, 3, 5), 11750m, adm, ct)).Sucesso);
        Assert.Equal(new SaldosContabeisContrato(80000m, 0, 400m, true), await svc.SaldosContabeisAsync(id, ct));
        Assert.Contains("contemplação", (await svc.EstornarPagamentoAsync(empresa, id, 1, adm, ct)).Erros[0]);       // paga antes da contemplação contabilizada
        Assert.True((await svc.EstornarPagamentoAsync(empresa, id, 2, adm, ct)).Sucesso);
    }
}
