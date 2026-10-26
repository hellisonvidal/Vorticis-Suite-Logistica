using Vorticis.Application.Contabil;
using Vorticis.Frota;
using Vorticis.Infra.Tests;

namespace Vorticis.Modulos.Tests;

public class RelatoriosFrotaTests
{
    private static readonly Veiculo V1 = new(1, 1, "ABC1D23", null, "CAVALO", "Volvo", "FH", null, null, "DIESEL_S10", null, 110500, 4.5m);
    private static readonly Veiculo V2 = new(2, 2, "DEF4G56", null, "TRUCK", "VW", "Delivery", null, null, "DIESEL_S10", null, 0, null);
    private static readonly Motorista Pedro = new(1, 1, "Pedro", "52998224725", null, null, null, null);
    private static readonly Motorista Ana = new(2, 2, "Ana", "11144477735", null, null, null, null);

    private static Abastecimento Ab(long id, DateOnly data, decimal km, decimal litros, decimal valor, long motorista, string comb = "DIESEL_S10")
        => new(id, 1, motorista, data, km, litros, valor, comb, null);

    private static IReadOnlyList<Abastecimento> Abastecimentos() =>
    [
        Ab(1, new(2026, 3, 1), 1000, 50, 300, 1), Ab(2, new(2026, 3, 5), 1500, 100, 600, 2), Ab(3, new(2026, 3, 10), 2000, 125, 750, 1),
        Ab(4, new(2026, 3, 10), 2000, 10, 50, 1, "ARLA32"),
    ];

    [Fact]
    public void Consumo_por_veiculo_e_por_motorista_confere_litros_valores_e_medias()
    {
        // ciclos: 500 km / 100 L = 5,00 (fechado pelo motorista 2) e 500 km / 125 L = 4,00 (motorista 1); média do veículo 1.000 / 225 = 4,44; Arla não entra
        var porVeiculo = TabelasFrota.ConsumoResumo([V1], [Pedro, Ana], Abastecimentos(), 15m, false);
        Assert.Equal(["Veículo", "Abastecimentos", "Litros", "Valor", "Preço médio do litro", "Km rodado", "Média km/l", "Esperada km/l", "Situação"], porVeiculo.Colunas.Select(c => c.Titulo));
        var l = porVeiculo.Linhas[0].Valores;
        Assert.Equal([3L, 275m, 1650m, 6.000m, 1000m, 4.44m, 4.5m, ""], l.Skip(1));   // 4,44 > 4,5 × 0,85 = 3,825: sem alerta
        Assert.Equal([3L, 275m, 1650m, 6.000m, 1000m, 4.44m], porVeiculo.Linhas[^1].Valores.Skip(1).Take(6));

        var porMotorista = TabelasFrota.ConsumoResumo([V1], [Pedro, Ana], Abastecimentos(), 15m, true);
        Assert.Equal(["Ana", "Pedro", "(sem motorista)"], porMotorista.Linhas.Select(x => x.Valores[0]).Take(2).Append("(sem motorista)"));
        Assert.Equal([1L, 100m, 600m, 6.000m, 500m, 5.00m], porMotorista.Linhas[0].Valores.Skip(1).Take(6));    // Ana
        Assert.Equal([2L, 175m, 1050m, 6.000m, 500m, 4.00m], porMotorista.Linhas[1].Valores.Skip(1).Take(6));   // Pedro
        Assert.Equal(3, porMotorista.Linhas.Count);

        // média abaixo do esperado menos a tolerância destaca a linha
        var exigente = TabelasFrota.ConsumoResumo([V1 with { MediaEsperada = 6m }], [Pedro, Ana], Abastecimentos(), 15m, false);
        Assert.True(exigente.Linhas[0].Destaque);
        Assert.Equal("ABAIXO DO ESPERADO", exigente.Linhas[0].Valores[^1]);
    }

    [Fact]
    public void Preventivas_mostram_o_que_falta_em_km_e_dias_com_as_vencidas_primeiro()
    {
        var hoje = new DateOnly(2026, 6, 20);
        PlanoManutencao oleo = new(1, 1, "Troca de óleo", 10000, null, 100000, null, true, "ABC1D23", 110500),    // próxima 110.000: passou 500 km
                        freios = new(2, 1, "Freios", 20000, 180, 100000, new DateOnly(2026, 1, 1), true, "ABC1D23", 112000),   // 120.000 km (faltam 8.000) e 30/06 (10 dias)
                        inativo = new(3, 2, "Pneus", 50000, null, 0, null, false, "DEF4G56", 1);
        var t = TabelasFrota.Preventivas([freios, inativo, oleo], hoje, 30);
        Assert.Equal(3, t.Linhas.Count);   // 2 planos ativos + total
        Assert.Equal(["Troca de óleo", 110000m, 110500m, -500m, "VENCIDA"], new object?[] { t.Linhas[0].Valores[1], t.Linhas[0].Valores[4], t.Linhas[0].Valores[5], t.Linhas[0].Valores[6], t.Linhas[0].Valores[9] });
        Assert.True(t.Linhas[0].Destaque);
        Assert.Equal([120000m, 8000m, new DateOnly(2026, 6, 30), 10L, "A VENCER"], new object?[] { t.Linhas[1].Valores[4], t.Linhas[1].Valores[6], t.Linhas[1].Valores[7], t.Linhas[1].Valores[8], t.Linhas[1].Valores[9] });
        Assert.Contains("2 plano(s): 1 vencida(s), 1 a vencer", (string)t.Linhas[^1].Valores[1]!);
    }

    [Fact]
    public void Historico_de_manutencao_agrupa_por_veiculo_e_ignora_canceladas_nos_totais()
    {
        OrdemServico Os(int n, string placa, string tipo, decimal pecas, decimal mao, string sit = "CONCLUIDA")
            => new(n, n, placa == "ABC1D23" ? 1 : 2, null, tipo, new DateOnly(2026, 3, n), 1000 * n, "Serviço " + n, "Oficina", pecas, mao, sit, placa);
        var t = TabelasFrota.HistoricoManutencao([Os(1, "ABC1D23", "PREVENTIVA", 300, 200), Os(2, "ABC1D23", "CORRETIVA", 1000, 500), Os(3, "ABC1D23", "CORRETIVA", 999, 1, "CANCELADA"),
                                                  Os(4, "DEF4G56", "PREVENTIVA", 100, 50)]);
        Assert.Equal(7, t.Linhas.Count);   // 3 + subtotal + 1 + subtotal + total
        Assert.Equal([1300m, 700m, 2000m], t.Linhas[3].Valores.Skip(8));   // subtotal sem a cancelada
        Assert.Equal([100m, 50m, 150m], t.Linhas[5].Valores.Skip(8));
        Assert.Equal([1400m, 750m, 2150m], t.Linhas[^1].Valores.Skip(8));
        Assert.Equal("Preventiva", t.Linhas[0].Valores[3]);
    }

    private static IReadOnlyList<Viagem> Viagens()
    {
        Viagem V(int n, long mot, string nome, string cliente, string destino, decimal kmIni, decimal kmFim, decimal frete, decimal pedagio, decimal dia, decimal outras, string sit = "CONCLUIDA")
            => new(n, n, 1, mot, cliente, "SP", destino, new DateOnly(2026, 3, n), new DateOnly(2026, 3, n + 1), kmIni, kmFim, frete, pedagio, dia, outras, sit, "ABC1D23", nome);
        return [V(1, 1, "Pedro", "Acme", "RJ", 1000, 1430, 5000, 200, 100, 50), V(2, 2, "Ana", "Acme", "RJ", 1430, 1860, 4000, 100, 50, 0), V(3, 1, "Pedro", "Beta", "BH", 0, 600, 3000, 3200, 0, 0),
                V(4, 2, "Ana", "Beta", "BH", 0, 600, 9999, 0, 0, 0, "CANCELADA")];
    }

    [Fact]
    public void Viagens_por_cliente_motorista_e_rota_somam_frete_despesas_e_resultado()
    {
        // Acme: 2 viagens, 860 km, frete 9.000, despesas 350 + 150 = 500, resultado 8.500, 9.000/860 = 10,47 por km, médio 4.500; Beta: 3.000 − 3.200 = −200 (destacada)
        var cli = TabelasFrota.ViagensPor(Viagens(), TabelasFrota.TituloViagensCliente);
        Assert.Equal(["Acme", 2L, 860m, 9000m, 500m, 8500m, 10.47m, 4500m], cli.Linhas[0].Valores);
        Assert.Equal(["Beta", 1L, 600m, 3000m, 3200m, -200m, 5.00m, 3000m], cli.Linhas[1].Valores);
        Assert.True(cli.Linhas[1].Destaque);
        Assert.Equal(["TOTAL", 3L, 1460m, 12000m, 3700m, 8300m, 8.22m, 4000m], cli.Linhas[^1].Valores);   // a cancelada não entra

        var mot = TabelasFrota.ViagensPor(Viagens(), TabelasFrota.TituloViagensMotorista);
        Assert.Equal(["Pedro", 2L, 1030m, 8000m, 3550m, 4450m], mot.Linhas[0].Valores.Take(6));
        Assert.Equal(["Ana", 1L, 430m, 4000m, 150m, 3850m], mot.Linhas[1].Valores.Take(6));

        var rota = TabelasFrota.ViagensPor(Viagens(), TabelasFrota.TituloViagensRota);
        Assert.Equal(["SP → RJ", 2L, 860m, 9000m], rota.Linhas[0].Valores.Take(4));
        Assert.Equal("Rota", rota.Colunas[0].Titulo);
    }

    [Fact]
    public void Coletas_por_cliente_rota_e_motorista()
    {
        Coleta C(int n, string remetente, string rota, string mot, int vol, decimal peso, decimal valor, string sit)
            => new(n, n, new DateTime(2026, 3, n, 8, 0, 0), null, null, "Rua", null, vol, peso, valor, null, null, null, sit, null, remetente, null, null, mot, rota);
        Coleta[] coletas = [C(1, "Acme", "R1", "Pedro", 10, 100.5m, 1000, "COLETADA"), C(2, "Acme", "R1", "Pedro", 5, 50m, 500, "AGENDADA"), C(3, "Beta", "R2", "Ana", 2, 20m, 200, "CANCELADA")];
        var cli = TabelasFrota.ColetasPor(coletas, TabelasFrota.TituloColetasCliente);
        Assert.Equal(["Acme", 2L, 1L, 0L, 15L, 150.5m, 1500m], cli.Linhas[0].Valores);
        Assert.Equal(["Beta", 1L, 0L, 1L, 2L, 20m, 200m], cli.Linhas[1].Valores);
        Assert.Equal(["TOTAL", 3L, 1L, 1L, 17L, 170.5m, 1700m], cli.Linhas[^1].Valores);
        Assert.Equal(["R1", 2L], TabelasFrota.ColetasPor(coletas, TabelasFrota.TituloColetasRota).Linhas[0].Valores.Take(2));
        Assert.Equal(["Ana", "Pedro"], TabelasFrota.ColetasPor(coletas, TabelasFrota.TituloColetasMotorista).Linhas.Take(2).Select(x => x.Valores[0]).Order());
    }

    [Fact]
    public void Diarias_pagas_e_acertadas_destacam_o_que_falta_acertar()
    {
        Diaria D(int n, string sit, decimal qtd, decimal unit, decimal adiant, decimal? gasto)
            => new(n, n, new DateOnly(2026, 3, n), 1, "MOTORISTA", null, null, null, null, "PERNOITE", qtd, unit, adiant, gasto, sit, null, null, null, "Pedro");
        var t = TabelasFrota.DiariasAcerto([D(1, "PAGA", 2, 120, 200, null), D(2, "ACERTADA", 1, 120, 100, 70), D(3, "PAGA", 3, 35, 0, null), D(4, "LANCADA", 1, 100, 0, null)]);
        Assert.Equal(4, t.Linhas.Count);   // 3 pagas + total
        Assert.True(t.Linhas[0].Destaque);
        Assert.Equal("Aguardando acerto", t.Linhas[0].Valores[7]);
        Assert.Equal([100m, 70m, 30m], t.Linhas[1].Valores.Skip(4).Take(3));
        Assert.Equal("Paga (sem acerto)", t.Linhas[2].Valores[7]);
        var total = t.Linhas[^1].Valores;
        Assert.Equal([405m, 70m, 30m], total.Skip(4).Take(3));   // 200 + 100 + 105
        Assert.Contains("200,00", (string)total[7]!);   // a acertar
    }

    [Fact]
    public void Resumo_compara_o_periodo_com_o_anterior()
    {
        var ini = new DateOnly(2026, 3, 1);
        var fim = new DateOnly(2026, 3, 31);   // 31 dias: anterior 29/01 a 28/02
        Viagem V(int n, DateOnly saida, decimal frete, decimal km, decimal desp) => new(n, n, 1, 1, "Acme", "SP", "RJ", saida, saida, 0, km, frete, desp, 0, 0, "CONCLUIDA");
        OrdemServico Os(int n, DateOnly d, decimal valor, string sit = "CONCLUIDA") => new(n, n, 1, null, "CORRETIVA", d, null, "x", null, valor, 0, sit);
        Coleta C(int n, string sit) => new(n, n, new DateTime(2026, 3, n, 8, 0, 0), null, null, "Rua", null, 1, 1, 1, null, null, null, sit, null);
        var diaria = new Diaria(1, 1, new DateOnly(2026, 3, 5), 1, "MOTORISTA", null, null, null, null, "PERNOITE", 2, 120, 0, null, "PAGA", null, null, null);
        var t = TabelasFrota.Resumo(ini, fim, 5, [Ab(1, new(2026, 3, 1), 1000, 50, 300, 1), Ab(2, new(2026, 3, 5), 1500, 100, 600, 1)],
                                    [Os(1, new(2026, 3, 10), 1000), Os(2, new(2026, 2, 15), 500), Os(3, new(2026, 3, 11), 7777, "CANCELADA")],
                                    [V(1, new(2026, 3, 5), 5000, 430, 350), V(2, new(2026, 2, 10), 4000, 400, 300)], [diaria], [C(1, "COLETADA"), C(2, "AGENDADA")], 2, 3);
        string[] Linha(int i) => [.. t.Linhas[i].Valores.Select(v => v?.ToString() ?? "")];
        Assert.Equal(["Viagens (sem canceladas)", "1", "1", "0,00%"], Linha(0));
        Assert.Equal(["Frete das viagens (R$)", "5.000,00", "4.000,00", "25,00%"], Linha(2));
        Assert.Equal(["Combustível abastecido (R$)", "900,00", "0,00", ""], Linha(4));
        Assert.Equal("5,00", Linha(6)[1]);   // 500 km / 100 L
        Assert.Equal(["Manutenção – ordens do período (R$)", "1.000,00", "500,00", "100,00%"], Linha(7));
        Assert.Equal(["Custo total: combustível + manutenção + despesas + diárias (R$)", "2.490,00", "800,00", "211,25%"], Linha(9));   // 900 + 1.000 + 350 + 240
        Assert.Equal("2.510,00", Linha(10)[1]);   // 5.000 − 2.490
        Assert.Equal(["Custo por km rodado (R$)", "5,79", "2,00", "189,50%"], Linha(11));
        Assert.Equal(["Coletas agendadas", "2", "0", ""], Linha(12));
        Assert.Equal("1", Linha(13)[1]);
        Assert.Equal(["Veículos ativos", "5"], Linha(15).Take(2));
        Assert.Equal("3", Linha(17)[1]);
        Assert.True(t.Linhas[17].Destaque);
    }

    [Fact]
    public void Titulos_e_filtros_dos_relatorios()
    {
        Assert.True(TabelasFrota.SemPeriodo(TabelasFrota.TituloPreventivas));
        Assert.False(TabelasFrota.SemPeriodo(TabelasFrota.TituloResumo));
        Assert.True(TabelasFrota.UsaFiltros(TabelasFrota.TituloViagensCliente));
        Assert.False(TabelasFrota.UsaFiltros(TabelasFrota.TituloResumo));
    }
}

public class ContabilizacaoDiariasTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private const string Despesa = "4.2.01.008", Credito = "2.1.06.003";

    private async Task<(long Empresa, long Adm, long Motorista, long Centro, long Frota, ServicoFrota Svc)> PrepararAsync(string cnpj, bool contas = true)
    {
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync(cnpj);
        var svc = ServicosFrota.De(db.App);
        var frota = (await svc.GravarFrotaAsync(empresa, new FrotaCadastro(0, 1, "Própria", "PROPRIA", null, null), adm, ct)).Valor;
        Assert.True((await db.App.PlanoContas.IncluirCentroAsync(empresa, "1", "Frota própria", "", adm, ct: ct)).Sucesso);
        var centro = (await db.App.PlanoContas.ListarCentrosAsync(empresa, adm, ct)).Single().Id;
        Assert.True((await db.App.PlanoContas.DefinirFrotaDoCentroAsync(empresa, centro, frota, adm, ct)).Sucesso);
        var motorista = (await svc.GravarMotoristaAsync(empresa, new Motorista(0, 1, "Pedro Alves", "52998224725", null, null, null, null), adm, ct)).Valor;
        Assert.True((await svc.GravarValoresDiariaAsync(empresa, new Dictionary<string, decimal> { ["PERNOITE"] = 120m, ["REFEICAO"] = 35m }, adm, ct)).Sucesso);
        if (contas) Assert.True((await svc.GravarConfiguracaoAsync(empresa, new ConfiguracaoFrota(ContaDiariaDespesa: Despesa, ContaDiariaCredito: Credito), adm, ct)).Sucesso);
        return (empresa, adm, motorista, centro, frota, svc);
    }

    private static Diaria Nova(long motorista, long centro, string tipo, decimal qtd, decimal adiantamento = 0)
        => new(0, 0, new DateOnly(2026, 3, 10), motorista, "MOTORISTA", null, null, null, null, tipo, qtd, 0, adiantamento, null, "LANCADA", centro, null, null);

    [Fact]
    public async Task Configuracao_das_contas_valida_as_contas()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var (empresa, adm, _, _, _, svc) = await PrepararAsync("51234567000163", contas: false);
        Assert.False((await svc.GravarConfiguracaoAsync(empresa, new ConfiguracaoFrota(ContaDiariaDespesa: Despesa), adm, ct)).Sucesso);   // só uma conta
        Assert.False((await svc.GravarConfiguracaoAsync(empresa, new ConfiguracaoFrota(ContaDiariaDespesa: Despesa, ContaDiariaCredito: Despesa), adm, ct)).Sucesso);   // iguais
        Assert.False((await svc.GravarConfiguracaoAsync(empresa, new ConfiguracaoFrota(ContaDiariaDespesa: "9.9.9.999", ContaDiariaCredito: Credito), adm, ct)).Sucesso);   // inexistente
        Assert.False((await svc.GravarConfiguracaoAsync(empresa, new ConfiguracaoFrota(ContaDiariaDespesa: "4.2.01", ContaDiariaCredito: Credito), adm, ct)).Sucesso);   // sintética
        Assert.True((await svc.GravarConfiguracaoAsync(empresa, new ConfiguracaoFrota(ContaDiariaDespesa: Despesa, ContaDiariaCredito: Credito), adm, ct)).Sucesso);
        Assert.True((await svc.ConfiguracaoAsync(empresa, ct)).ContabilizaDiarias);
        Assert.True((await svc.GravarConfiguracaoAsync(empresa, new ConfiguracaoFrota(), adm, ct)).Sucesso);   // limpar
        Assert.False((await svc.ConfiguracaoAsync(empresa, ct)).ContabilizaDiarias);
    }

    [Fact]
    public async Task Diaria_paga_gera_lancamento_on_line_e_em_lote_pela_contabilizacao()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var (empresa, adm, motorista, centro, _, svc) = await PrepararAsync("61234567000117");
        var data = new DateOnly(2026, 3, 10);

        // on-line: 2 pernoites × 120 = 240, sem adiantamento → D despesa (com o centro de custo da frota) / C contas a pagar
        await svc.GravarDiariaAsync(empresa, Nova(motorista, centro, "PERNOITE", 2), adm, ct);
        var d1 = (await svc.DiariasAsync(empresa, data, data, null, null, adm, ct)).Single();
        var pago = await svc.PagarDiariaAsync(empresa, d1.Id, data.AddDays(5), adm, ct);
        Assert.True(pago.Sucesso, string.Join(" ", pago.Erros));
        Assert.False(pago.Valor.PendenciaRegistrada);
        Assert.Null(pago.Valor.Aviso);
        var lanc = pago.Valor.LancamentoId!.Value;
        Assert.Equal(lanc, await db.EscalarAsync<long>("SELECT lancamento_id FROM frota.diaria WHERE id = @i", ("i", d1.Id)));
        Assert.Equal(("FROTA", "EFETIVADO"), (await db.EscalarAsync<string>("SELECT origem::text FROM contabil.lancamento WHERE id = @l", ("l", lanc)),
                                             await db.EscalarAsync<string>("SELECT status::text FROM contabil.lancamento WHERE id = @l", ("l", lanc))));
        Assert.Equal(240m, await db.EscalarAsync<decimal>("""
            SELECT p.valor FROM contabil.partida p JOIN contabil.plano_contas c ON c.id = p.conta_id JOIN contabil.centro_custo cc ON cc.id = p.centro_custo_id
             WHERE p.lancamento_id = @l AND p.ind_dc = 'D' AND c.codigo = '4.2.01.008' AND cc.codigo = '1'
            """, ("l", lanc)));
        Assert.Equal(240m, await db.EscalarAsync<decimal>("SELECT p.valor FROM contabil.partida p JOIN contabil.plano_contas c ON c.id = p.conta_id WHERE p.lancamento_id = @l AND p.ind_dc = 'C' AND c.codigo = '2.1.06.003'", ("l", lanc)));
        Assert.Equal(240m, await db.EscalarAsync<decimal>("SELECT valor FROM fiscal.duplicata WHERE id = @i", ("i", pago.Valor.DuplicataId)));

        // em lote: só a pendência; a prévia e a contabilização usam as mesmas contas; não contabiliza duas vezes
        Assert.True((await db.App.Pendencias.GravarModoAsync(empresa, ModoContabilizacao.Lote, adm, ct)).Sucesso);
        await svc.GravarDiariaAsync(empresa, Nova(motorista, centro, "REFEICAO", 3, adiantamento: 80m), adm, ct);
        var d2 = (await svc.DiariasAsync(empresa, data, data, null, null, adm, ct)).Single(x => x.Tipo == "REFEICAO");
        var pago2 = await svc.PagarDiariaAsync(empresa, d2.Id, data, adm, ct);
        Assert.Equal((true, null), (pago2.Valor.PendenciaRegistrada, pago2.Valor.LancamentoId));
        var pend = (await db.App.Pendencias.ListarAsync(empresa, new FiltroPendencias(OrigensPendencia.Frota), adm, ct)).Valor.Single();
        Assert.Equal(80m, pend.Valor);   // o adiantamento é o que sai no pagamento
        var previa = await db.App.Pendencias.PreviaAsync(empresa, [pend.Id], adm, ct);
        Assert.Contains(previa.Valor, l => l.Contains("D 4.2.01.008") && l.Contains("80,00") && l.Contains("C.C. 1"));
        Assert.Contains(previa.Valor, l => l.Contains("C 2.1.06.003"));

        var lote = await db.App.Pendencias.ContabilizarAsync(empresa, [pend.Id], adm, ct);
        Assert.True(lote.Sucesso, string.Join(" ", lote.Erros));
        Assert.Equal((1, 80m, 0), (lote.Valor.Contabilizadas, lote.Valor.Valor, lote.Valor.Falhas.Count));
        var lanc2 = await db.EscalarAsync<long>("SELECT lancamento_id FROM frota.diaria WHERE id = @i", ("i", d2.Id));
        Assert.Equal(80m, await db.EscalarAsync<decimal>("SELECT sum(valor) FROM contabil.partida WHERE lancamento_id = @l AND ind_dc = 'D'", ("l", lanc2)));
        Assert.False((await db.App.Pendencias.ContabilizarAsync(empresa, [pend.Id], adm, ct)).Sucesso);   // já contabilizada

        // desfazer o lote estorna o lançamento e libera a diária; a pendência volta e pode ser contabilizada de novo
        var desfeito = await db.App.Pendencias.DesfazerLoteAsync(empresa, lote.Valor.LoteId!.Value, adm, ct);
        Assert.True(desfeito.Sucesso, string.Join(" ", desfeito.Erros));
        Assert.Equal("ESTORNADO", await db.EscalarAsync<string>("SELECT status::text FROM contabil.lancamento WHERE id = @l", ("l", lanc2)));
        Assert.Equal(0, await db.EscalarAsync<int>("SELECT count(*)::int FROM frota.diaria WHERE id = @i AND lancamento_id IS NOT NULL", ("i", d2.Id)));
        var de_novo = await db.App.Pendencias.ContabilizarAsync(empresa, [pend.Id], adm, ct);
        Assert.Equal(1, de_novo.Valor.Contabilizadas);

    }

    [Fact]
    public async Task Sem_as_contas_o_online_avisa_e_o_lote_mantem_a_pendencia_com_o_motivo()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var (empresa, adm, motorista, centro, _, svc) = await PrepararAsync("41234567000100", contas: false);
        var data = new DateOnly(2026, 3, 10);
        await svc.GravarDiariaAsync(empresa, Nova(motorista, centro, "PERNOITE", 1), adm, ct);
        var d = (await svc.DiariasAsync(empresa, data, data, null, null, adm, ct)).Single();
        var online = await svc.PagarDiariaAsync(empresa, d.Id, data, adm, ct);
        Assert.True(online.Sucesso);
        Assert.Null(online.Valor.LancamentoId);
        Assert.Contains("Parâmetros", online.Valor.Aviso);

        Assert.True((await db.App.Pendencias.GravarModoAsync(empresa, ModoContabilizacao.Lote, adm, ct)).Sucesso);
        await svc.GravarDiariaAsync(empresa, Nova(motorista, centro, "REFEICAO", 2), adm, ct);
        var d2 = (await svc.DiariasAsync(empresa, data, data, null, null, adm, ct)).Single(x => x.Tipo == "REFEICAO");
        await svc.PagarDiariaAsync(empresa, d2.Id, data, adm, ct);
        var pend = (await db.App.Pendencias.ListarAsync(empresa, new FiltroPendencias(OrigensPendencia.Frota), adm, ct)).Valor.Single();
        var r = await db.App.Pendencias.ContabilizarAsync(empresa, [pend.Id], adm, ct);
        Assert.Equal((0, 1), (r.Valor.Contabilizadas, r.Valor.Falhas.Count));
        Assert.Contains("conta de despesa", r.Valor.Falhas[0].Erros[0]);
        Assert.Equal(SituacaoPendencia.Pendente, (await db.App.Pendencias.ListarAsync(empresa, new FiltroPendencias(OrigensPendencia.Frota, Situacao: null), adm, ct)).Valor.Single().Situacao);
    }

    [Fact]
    public async Task Relatorios_novos_rodam_no_banco_com_filtros()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var (empresa, adm, motorista, centro, _, svc) = await PrepararAsync("11444777000161", contas: false);
        var data = new DateOnly(2026, 3, 10);
        await svc.GravarDiariaAsync(empresa, Nova(motorista, centro, "PERNOITE", 2, adiantamento: 100m), adm, ct);
        var d = (await svc.DiariasAsync(empresa, data, data, null, null, adm, ct)).Single();
        await svc.PagarDiariaAsync(empresa, d.Id, data, adm, ct);

        var acerto = await svc.RelatorioAsync(empresa, TabelasFrota.TituloDiariasAcerto, data, data, adm, ct);
        Assert.Equal(100m, acerto.Valor.Linhas[^1].Valores[4]);
        Assert.Equal(2, (await svc.RelatorioAsync(empresa, TabelasFrota.TituloDiariasAcerto, data, data, adm, ct, new FiltroRelatorioFrota(MotoristaId: motorista))).Valor.Linhas.Count);
        Assert.Single((await svc.RelatorioAsync(empresa, TabelasFrota.TituloDiariasAcerto, data, data, adm, ct, new FiltroRelatorioFrota(MotoristaId: 999999))).Valor.Linhas);   // só o total

        foreach (var titulo in new[] { TabelasFrota.TituloConsumoVeiculo, TabelasFrota.TituloConsumoMotorista, TabelasFrota.TituloPreventivas, TabelasFrota.TituloHistoricoManutencao,
                                       TabelasFrota.TituloViagensMotorista, TabelasFrota.TituloViagensCliente, TabelasFrota.TituloViagensRota, TabelasFrota.TituloColetasCliente,
                                       TabelasFrota.TituloColetasRota, TabelasFrota.TituloColetasMotorista, TabelasFrota.TituloResumo })
        {
            var r = await svc.RelatorioAsync(empresa, titulo, data, data, adm, ct);
            Assert.True(r.Sucesso, titulo + ": " + string.Join(" ", r.Erros));
            Assert.All(r.Valor.Linhas, l => Assert.Equal(r.Valor.Colunas.Count, l.Valores.Count));
        }
        Assert.False((await svc.RelatorioAsync(empresa, TabelasFrota.TituloResumo, data, data, 999999, ct)).Sucesso);   // sem permissão
    }
}
