using System.Net;
using Vorticis.Application.Contabil;
using Vorticis.Frota;
using Vorticis.Infra.Tests;

namespace Vorticis.Modulos.Tests;

public class RegrasLogisticaTests
{
    private static readonly ParadaRota SaoPaulo = new("SP", "3550308", "São Paulo");
    private static readonly ParadaRota Rio = new("RJ", "3304557", "Rio de Janeiro");
    private static readonly ParadaRota Campinas = new("SP", "3509502", "Campinas");

    [Fact]
    public void Haversine_conferido_a_mao_entre_Sao_Paulo_e_Rio()
    {
        // a = sen²(Δφ/2) + cos φ1 · cos φ2 · sen²(Δλ/2), d = 2R · atan2(√a, √(1-a)), R = 6.371 km: (-23,5505; -46,6333) → (-22,9068; -43,1729) = 360,75 km
        Assert.Equal(360.75, RegrasLogistica.Haversine(-23.5505, -46.6333, -22.9068, -43.1729), 0.05);
        Assert.Equal(0, RegrasLogistica.Haversine(-10, -50, -10, -50), 6);
        // meia volta no equador = pi · R
        Assert.Equal(Math.PI * 6371, RegrasLogistica.Haversine(0, 0, 0, 180), 0.01);
    }

    [Fact]
    public void Estimativa_multiplica_pelo_fator_e_soma_os_trechos()
    {
        var reta = RegrasLogistica.EstimarKm([SaoPaulo, Rio], 1m)!.Value;
        Assert.InRange(reta, 355m, 365m);   // cerca de 360 km em linha reta pela base do IBGE
        Assert.InRange(RegrasLogistica.EstimarKm([SaoPaulo, Rio], 1.25m)!.Value, reta * 1.25m - 0.1m, reta * 1.25m + 0.1m);
        var sp_camp = RegrasLogistica.EstimarKm([SaoPaulo, Campinas], 1m)!.Value;
        var camp_rio = RegrasLogistica.EstimarKm([Campinas, Rio], 1m)!.Value;
        Assert.InRange(RegrasLogistica.EstimarKm([SaoPaulo, Campinas, Rio], 1m)!.Value, sp_camp + camp_rio - 0.2m, sp_camp + camp_rio + 0.2m);   // parada intermediária soma os trechos
        Assert.Null(RegrasLogistica.EstimarKm([SaoPaulo], 1.25m));
        Assert.Null(RegrasLogistica.EstimarKm([SaoPaulo, new ParadaRota("SP", "9999999", "Inexistente")], 1.25m));
        Assert.Equal(5.5m, RegrasLogistica.EstimarHoras(330m, 60m));
    }

    [Fact]
    public void Base_de_municipios_traz_os_municipios_do_IBGE_com_UF()
    {
        Assert.True(BaseMunicipios.Todos.Count > 5500);
        Assert.Equal("SP", BaseMunicipios.Obter("3550308")!.Uf);
        Assert.Contains(BaseMunicipios.DaUf("sp"), m => m.Nome == "Campinas");
        Assert.Empty(RegrasLogistica.Validar(new Rota(0, 1, "SP-RJ", 430, "MANUAL", 7, 0, true, [SaoPaulo, Rio])));
        Assert.Contains("ao menos a origem e o destino", RegrasLogistica.Validar(new Rota(0, 1, "x", 0, "ESTIMADA", 0, 0, true, [SaoPaulo]))[0]);
        Assert.Contains(RegrasLogistica.Validar(new Rota(0, 1, "x", 0, "ESTIMADA", 0, 0, true, [SaoPaulo, new("RJ", "3550308", "São Paulo")])), e => e.Contains("Município inválido"));
    }

    [Fact]
    public void Diaria_calcula_total_e_saldo_do_acerto()
    {
        var d = new Diaria(0, 1, new DateOnly(2026, 3, 2), 1, "MOTORISTA", null, null, null, null, "PERNOITE", 2.5m, 120.40m, 200m, null, "LANCADA", null, null, null);
        Assert.Equal(301m, d.Total);
        Assert.Equal(200m, d.ValorPagamento);
        Assert.Null(d.Saldo);
        Assert.Equal(50m, (d with { ValorGasto = 150m }).Saldo);     // sobrou: o colaborador devolve
        Assert.Equal(-30m, (d with { ValorGasto = 230m }).Saldo);    // faltou: a empresa paga
        Assert.Equal(301m, (d with { Adiantamento = 0 }).ValorPagamento);
        Assert.Empty(RegrasLogistica.Validar(d));
        Assert.Contains("quantidade", RegrasLogistica.Validar(d with { Quantidade = 0 })[0]);
    }

    [Fact]
    public void Coleta_anda_agendada_em_coleta_coletada_e_cancela()
    {
        Assert.True(RegrasLogistica.TransicaoColetaValida("AGENDADA", "EM_COLETA"));
        Assert.True(RegrasLogistica.TransicaoColetaValida("EM_COLETA", "COLETADA"));
        Assert.True(RegrasLogistica.TransicaoColetaValida("EM_COLETA", "CANCELADA"));
        Assert.False(RegrasLogistica.TransicaoColetaValida("COLETADA", "AGENDADA"));
        Assert.False(RegrasLogistica.TransicaoColetaValida("CANCELADA", "EM_COLETA"));
        Assert.False(RegrasLogistica.ColetaEditavel("COLETADA"));
    }

    private sealed class Falso(Func<HttpRequestMessage, HttpResponseMessage> resposta) : HttpMessageHandler
    {
        public Uri? Chamada { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Chamada = request.RequestUri;
            return Task.FromResult(resposta(request));
        }
    }

    [Fact]
    public async Task Servico_online_devolve_km_e_horas_e_trata_as_falhas_sem_excecao()
    {
        var ct = TestContext.Current.CancellationToken;
        var ok = new Falso(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"code":"Ok","routes":[{"distance":429800.4,"duration":19800}]}""") });
        var r = await ServicoRotasOnline.ConsultarAsync(new HttpClient(ok), [SaoPaulo, Rio], "http://osrm.teste", ct);
        Assert.True(r.Sucesso);
        Assert.Equal((429.8m, 5.5m), (r.Valor.Km, r.Valor.Horas));
        Assert.StartsWith("http://osrm.teste/route/v1/driving/-46.6395,-23.5329;-43.2003,-22.9129", ok.Chamada!.ToString());

        // sem internet, erro do servidor, resposta inválida e rota não encontrada: falha com mensagem, sem exceção
        var semRede = new HttpClient(new Falso(_ => throw new HttpRequestException("sem rede")));
        var f1 = await ServicoRotasOnline.ConsultarAsync(semRede, [SaoPaulo, Rio], null, ct);
        Assert.False(f1.Sucesso);
        Assert.Contains("estimada foi mantida", f1.Erros[0]);
        Assert.False((await ServicoRotasOnline.ConsultarAsync(new HttpClient(new Falso(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))), [SaoPaulo, Rio], null, ct)).Sucesso);
        Assert.False((await ServicoRotasOnline.ConsultarAsync(new HttpClient(new Falso(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("não é json") })), [SaoPaulo, Rio], null, ct)).Sucesso);
        Assert.False((await ServicoRotasOnline.ConsultarAsync(new HttpClient(new Falso(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"code":"NoRoute","routes":[]}""") })), [SaoPaulo, Rio], null, ct)).Sucesso);
        Assert.False((await ServicoRotasOnline.ConsultarAsync(new HttpClient(ok), [SaoPaulo], null, ct)).Sucesso);
    }

    [Fact]
    public void Ordem_de_coleta_gera_PDF()
    {
        var c = new Coleta(1, 7, new DateTime(2026, 3, 2, 8, 30, 0), null, null, "Rua A, 10 - Campinas/SP", "Peças", 12, 340.5m, 8000m, null, null, null, "AGENDADA", null);
        var pdf = ServicoFrota.OrdemColetaPdf(c, "Transportadora Teste", "Ana");
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }
}

public class LogisticaFrotaIntegracaoTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    [Fact]
    public async Task Frotas_vinculam_veiculos_e_centros_de_custo()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("19131243000197");
        var svc = ServicosFrota.De(db.App);

        Assert.False((await svc.GravarFrotaAsync(empresa, new FrotaCadastro(0, 1, " ", "PROPRIA", null, null), adm, ct)).Sucesso);
        Assert.False((await svc.GravarFrotaAsync(empresa, new FrotaCadastro(0, 1, "X", "NAVIO", null, null), adm, ct)).Sucesso);
        var frota = (await svc.GravarFrotaAsync(empresa, new FrotaCadastro(0, 1, "Frota própria", "PROPRIA", "Carlos", null), adm, ct)).Valor;
        var agregada = (await svc.GravarFrotaAsync(empresa, new FrotaCadastro(0, 2, "Agregados", "AGREGADA", null, null), adm, ct)).Valor;
        var veiculo = (await svc.GravarVeiculoAsync(empresa, new Veiculo(0, 1, "BRA2E19", null, "CAVALO", null, null, null, null, "DIESEL_S10", null, 0, null, FrotaId: frota), adm, ct)).Valor;
        Assert.Equal(frota, (await svc.VeiculosAsync(empresa, adm, ct)).Single(v => v.Id == veiculo).FrotaId);
        var lista = await svc.FrotasAsync(empresa, adm, ct);
        Assert.Equal([1, 0], lista.Select(f => f.Veiculos));

        // centro de custo com o campo Frota: o centro passa a representar a frota
        Assert.True((await db.App.PlanoContas.IncluirCentroAsync(empresa, "1", "Frota própria", "", adm, ct: ct)).Sucesso);
        var centro = (await db.App.PlanoContas.ListarCentrosAsync(empresa, adm, ct)).Single();
        Assert.Contains((await db.App.PlanoContas.ListarFrotasAsync(empresa, adm, ct)), f => f.Id == frota);
        Assert.True((await db.App.PlanoContas.DefinirFrotaDoCentroAsync(empresa, centro.Id, frota, adm, ct)).Sucesso);
        Assert.Equal(frota, (await db.App.PlanoContas.ListarCentrosAsync(empresa, adm, ct)).Single().FrotaId);
        Assert.False((await db.App.PlanoContas.DefinirFrotaDoCentroAsync(empresa, centro.Id, 999999, adm, ct)).Sucesso);
        Assert.Contains((await svc.CentrosCustoAsync(empresa, adm, ct)), c => c.Id == centro.Id && c.FrotaId == frota);

        // excluir a frota solta os vínculos
        Assert.True((await svc.ExcluirFrotaAsync(empresa, frota, adm, ct)).Sucesso);
        Assert.Null((await svc.VeiculosAsync(empresa, adm, ct)).Single(v => v.Id == veiculo).FrotaId);
        Assert.Null((await db.App.PlanoContas.ListarCentrosAsync(empresa, adm, ct)).Single().FrotaId);
        _ = agregada;
    }

    [Fact]
    public async Task Diarias_lancam_pagam_geram_conta_a_pagar_pendencia_e_acertam()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("63810427000175");
        var svc = ServicosFrota.De(db.App);
        var hoje = DateOnly.FromDateTime(DateTime.Today);

        var frota = (await svc.GravarFrotaAsync(empresa, new FrotaCadastro(0, 1, "Própria", "PROPRIA", null, null), adm, ct)).Valor;
        Assert.True((await db.App.PlanoContas.IncluirCentroAsync(empresa, "1", "Frota própria", "", adm, ct: ct)).Sucesso);
        var centro = (await db.App.PlanoContas.ListarCentrosAsync(empresa, adm, ct)).Single().Id;
        Assert.True((await db.App.PlanoContas.DefinirFrotaDoCentroAsync(empresa, centro, frota, adm, ct)).Sucesso);
        var motorista = (await svc.GravarMotoristaAsync(empresa, new Motorista(0, 1, "Pedro Alves", "52998224725", null, null, null, null), adm, ct)).Valor;
        var ajudante = (await svc.GravarMotoristaAsync(empresa, new Motorista(0, 2, "Mateus Lima", "11144477735", null, null, null, null), adm, ct)).Valor;

        // valores por tipo (Parâmetros) e valor unitário sugerido
        Assert.True((await svc.GravarValoresDiariaAsync(empresa, new Dictionary<string, decimal> { ["PERNOITE"] = 120m, ["REFEICAO"] = 35m }, adm, ct)).Sucesso);
        Assert.Equal(120m, (await svc.ValoresDiariaAsync(empresa, ct))["PERNOITE"]);
        Assert.Equal(0m, (await svc.ValoresDiariaAsync(empresa, ct))["OUTRA"]);

        var nova = new Diaria(0, 0, hoje, motorista, "MOTORISTA", null, null, null, null, "PERNOITE", 2, 0, 100m, null, "LANCADA", centro, null, null);
        var numero = (await svc.GravarDiariaAsync(empresa, nova, adm, ct)).Valor;
        var d = (await svc.DiariasAsync(empresa, hoje, hoje, null, null, adm, ct)).Single();
        Assert.Equal((1, 120m, 240m, frota), (numero, d.ValorUnitario, d.Total, d.FrotaId ?? 0));   // valor da tabela; frota vem do centro de custo
        Assert.False((await svc.GravarDiariaAsync(empresa, nova with { Tipo = "OUTRA" }, adm, ct)).Sucesso);   // sem valor na tabela e sem informar

        // sem permissão não grava
        Assert.False((await svc.GravarDiariaAsync(empresa, nova, 999999, ct)).Sucesso);

        // contabilização on-line: gera só a conta a pagar (adiantamento de 100)
        var pago = await svc.PagarDiariaAsync(empresa, d.Id, hoje.AddDays(5), adm, ct);
        Assert.True(pago.Sucesso, string.Join(" ", pago.Erros));
        Assert.Equal((100m, false), (pago.Valor.Valor, pago.Valor.PendenciaRegistrada));
        Assert.Equal(100m, await db.EscalarAsync<decimal>("SELECT valor FROM fiscal.duplicata WHERE id = @i AND tipo = 'PAGAR' AND saldo = valor", ("i", pago.Valor.DuplicataId)));
        Assert.False((await svc.PagarDiariaAsync(empresa, d.Id, hoje, adm, ct)).Sucesso);   // já paga
        Assert.False((await svc.GravarDiariaAsync(empresa, nova with { Id = d.Id, Numero = numero, Quantidade = 3 }, adm, ct)).Sucesso);   // paga: não altera
        Assert.False((await svc.ExcluirDiariaAsync(empresa, d.Id, adm, ct)).Sucesso);
        Assert.Equal(0, await db.EscalarAsync<int>("SELECT count(*)::int FROM contabil.pendencia_contabilizacao WHERE empresa_id = @e", ("e", empresa)));

        // acerto: gastou 70 de 100 adiantados → 30 a devolver
        Assert.Equal(30m, (await svc.AcertarDiariaAsync(empresa, d.Id, 70m, adm, ct)).Valor);
        var acertada = (await svc.DiariasAsync(empresa, hoje, hoje, null, null, adm, ct)).Single();
        Assert.Equal(("ACERTADA", 30m), (acertada.Situacao, acertada.Saldo!.Value));
        Assert.False((await svc.AcertarDiariaAsync(empresa, d.Id, 70m, adm, ct)).Sucesso);

        // sem adiantamento: paga o total; não há o que acertar. Contabilização em lote: registra a pendência da Frota
        Assert.True((await db.App.Pendencias.GravarModoAsync(empresa, ModoContabilizacao.Lote, adm, ct)).Sucesso);
        await svc.GravarDiariaAsync(empresa, nova with { Funcao = "AJUDANTE", MotoristaId = ajudante, Adiantamento = 0, Tipo = "REFEICAO", Quantidade = 3 }, adm, ct);
        var d2 = (await svc.DiariasAsync(empresa, hoje, hoje, ajudante, null, adm, ct)).Single();
        Assert.Equal(105m, d2.Total);
        var pago2 = await svc.PagarDiariaAsync(empresa, d2.Id, hoje, adm, ct);
        Assert.Equal((105m, true), (pago2.Valor.Valor, pago2.Valor.PendenciaRegistrada));
        var pend = (await db.App.Pendencias.ListarAsync(empresa, new FiltroPendencias(OrigensPendencia.Frota), adm, ct)).Valor.Single();
        Assert.Equal((105m, $"DIARIA:{d2.Id}", SituacaoPendencia.Pendente), (pend.Valor, pend.Referencia, pend.Situacao));
        Assert.Contains("sem adiantamento", (await svc.AcertarDiariaAsync(empresa, d2.Id, 50m, adm, ct)).Erros[0].Replace("não teve adiantamento", "sem adiantamento"));

        // diária lançada pode ser excluída; relatórios por colaborador e por frota
        await svc.GravarDiariaAsync(empresa, nova with { Tipo = "REFEICAO", Adiantamento = 0 }, adm, ct);
        var lancada = (await svc.DiariasAsync(empresa, hoje, hoje, null, null, adm, ct)).First(x => x.Situacao == "LANCADA");
        Assert.True((await svc.ExcluirDiariaAsync(empresa, lancada.Id, adm, ct)).Sucesso);
        Assert.Single(await svc.DiariasAsync(empresa, hoje, hoje, null, frota, adm, ct), x => x.MotoristaId == motorista);
        var porColab = (await svc.RelatorioAsync(empresa, TabelasFrota.TituloDiariasColaborador, hoje, hoje, adm, ct)).Valor;
        Assert.Equal(240m + 105m, porColab.Linhas[^1].Valores[6]);
        var porFrota = (await svc.RelatorioAsync(empresa, TabelasFrota.TituloDiariasFrota, hoje, hoje, adm, ct)).Valor;
        Assert.All(porFrota.Linhas, l => Assert.Equal(porFrota.Colunas.Count, l.Valores.Count));
        var custos = (await svc.RelatorioAsync(empresa, TabelasFrota.TituloCustosFrota, hoje, hoje, adm, ct)).Valor;
        Assert.Equal(240m + 105m, custos.Linhas[^1].Valores[5]);   // total de diárias da coluna "Diárias"
    }

    [Fact]
    public async Task Rotas_estimam_por_Haversine_aceitam_manual_e_mantem_a_estimativa_se_o_servico_online_falha()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("27183946000197");
        var svc = ServicosFrota.De(db.App);
        ParadaRota sp = new("SP", "3550308", "São Paulo"), rj = new("RJ", "3304557", "Rio de Janeiro"), camp = new("SP", "3509502", "Campinas");

        Assert.False((await svc.GravarConfiguracaoAsync(empresa, new ConfiguracaoFrota(FatorRodoviario: 0.5m), adm, ct)).Sucesso);
        Assert.Equal(1.25m, (await svc.ConfiguracaoAsync(empresa, ct)).FatorRodoviario);

        // estimada: distância e tempo calculados com o fator padrão 1,25 e 60 km/h
        var reta = RegrasLogistica.EstimarKm([sp, rj], 1m)!.Value;
        var id = (await svc.GravarRotaAsync(empresa, new Rota(0, 1, "SP - RJ", 9999, "ESTIMADA", 0, 120m, true, [sp, rj]), adm, ct)).Valor;
        var rota = (await svc.RotasAsync(empresa, adm, ct)).Single();
        Assert.Equal(RegrasLogistica.EstimarKm([sp, rj], 1.25m), rota.DistanciaKm);
        Assert.Equal(RegrasLogistica.EstimarHoras(rota.DistanciaKm, 60m), rota.TempoHoras);
        Assert.Equal("São Paulo/SP → Rio de Janeiro/RJ", rota.Trajeto);

        // fator configurável
        Assert.True((await svc.GravarConfiguracaoAsync(empresa, new ConfiguracaoFrota(FatorRodoviario: 1.4m, VelocidadeMedia: 50m), adm, ct)).Sucesso);
        await svc.GravarRotaAsync(empresa, rota with { Paradas = [sp, camp, rj] }, adm, ct);
        rota = (await svc.RotasAsync(empresa, adm, ct)).Single();
        Assert.Equal(["São Paulo", "Campinas", "Rio de Janeiro"], rota.Paradas.Select(p => p.Municipio));
        Assert.Equal(RegrasLogistica.EstimarKm([sp, camp, rj], 1.4m), rota.DistanciaKm);
        Assert.Equal(RegrasLogistica.EstimarHoras(rota.DistanciaKm, 50m), rota.TempoHoras);

        // edição manual: a distância digitada é mantida
        await svc.GravarRotaAsync(empresa, rota with { DistanciaKm = 520m, DistanciaOrigem = "MANUAL", TempoHoras = 9m }, adm, ct);
        rota = (await svc.RotasAsync(empresa, adm, ct)).Single();
        Assert.Equal((520m, 9m, "MANUAL"), (rota.DistanciaKm, rota.TempoHoras, rota.DistanciaOrigem));
        Assert.False((await svc.GravarRotaAsync(empresa, rota with { Codigo = 2, Paradas = [sp] }, adm, ct)).Sucesso);

        // serviço online simulado: sucesso grava como rodoviária; falha devolve erro e nada muda
        var ok = new HttpClient(new RespostaFixa("""{"code":"Ok","routes":[{"distance":437500,"duration":22000}]}"""));
        var r = await svc.BuscarDistanciaRodoviariaAsync(empresa, rota.Paradas, adm, ok, "http://osrm.teste", ct);
        Assert.Equal(437.5m, r.Valor.Km);
        await svc.GravarRotaAsync(empresa, rota with { DistanciaKm = r.Valor.Km, TempoHoras = r.Valor.Horas, DistanciaOrigem = "RODOVIARIA" }, adm, ct);
        Assert.Equal(("RODOVIARIA", 437.5m), ((await svc.RotasAsync(empresa, adm, ct)).Single().DistanciaOrigem, (await svc.RotasAsync(empresa, adm, ct)).Single().DistanciaKm));
        var falha = await svc.BuscarDistanciaRodoviariaAsync(empresa, rota.Paradas, adm, new HttpClient(new RespostaFixa(null)), "http://osrm.teste", ct);
        Assert.False(falha.Sucesso);
        Assert.Equal(437.5m, (await svc.RotasAsync(empresa, adm, ct)).Single().DistanciaKm);
        Assert.False((await svc.BuscarDistanciaRodoviariaAsync(empresa, rota.Paradas, 999999, ok, null, ct)).Sucesso);   // exige permissão

        Assert.True((await svc.ExcluirRotaAsync(empresa, id, adm, ct)).Sucesso);
        Assert.Empty(await svc.RotasAsync(empresa, adm, ct));
    }

    private sealed class RespostaFixa(string? json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => json is null ? throw new HttpRequestException("sem internet") : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }

    [Fact]
    public async Task Coletas_numeram_andam_filtram_e_travam_depois_de_coletadas()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("48731625000150");
        var svc = ServicosFrota.De(db.App);
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var rem = await db.EscalarAsync<long>("INSERT INTO core.participante (empresa_id, codigo, nome) VALUES (@e, 'C1', 'Indústria Alfa') RETURNING id", ("e", empresa));
        var dest = await db.EscalarAsync<long>("INSERT INTO core.participante (empresa_id, codigo, nome) VALUES (@e, 'C2', 'Comércio Beta') RETURNING id", ("e", empresa));
        var motorista = (await svc.GravarMotoristaAsync(empresa, new Motorista(0, 1, "Pedro Alves", "52998224725", null, null, null, null), adm, ct)).Valor;
        var veiculo = (await svc.GravarVeiculoAsync(empresa, new Veiculo(0, 1, "BRA2E19", null, "TRUCK", null, null, null, null, "DIESEL_S10", null, 0, null), adm, ct)).Valor;
        Assert.Equal(2, (await svc.ParticipantesAsync(empresa, adm, ct)).Count);

        var agenda = hoje.ToDateTime(new TimeOnly(8, 30));
        var c = new Coleta(0, 0, agenda, rem, dest, "Rua A, 10 - Campinas/SP", "Peças", 12, 340.5m, 8000m, veiculo, motorista, null, "AGENDADA", null);
        Assert.False((await svc.GravarColetaAsync(empresa, c with { EnderecoColeta = " " }, adm, ct)).Sucesso);
        Assert.False((await svc.GravarColetaAsync(empresa, c with { Situacao = "COLETADA" }, adm, ct)).Sucesso);   // coleta nova começa agendada
        Assert.False((await svc.GravarColetaAsync(empresa, c, 999999, ct)).Sucesso);
        Assert.Equal(1, (await svc.GravarColetaAsync(empresa, c, adm, ct)).Valor);
        Assert.Equal(2, (await svc.GravarColetaAsync(empresa, c with { EnderecoColeta = "Av. B, 20" }, adm, ct)).Valor);   // sequencial

        var lista = await svc.ColetasAsync(empresa, hoje, hoje, null, adm, ct);
        Assert.Equal([2, 1], lista.Select(x => x.Numero));
        Assert.Equal(("Indústria Alfa", "Comércio Beta", "BRA2E19", "Pedro Alves"), (lista[1].Remetente, lista[1].Destinatario, lista[1].Placa, lista[1].MotoristaNome));
        var primeira = lista[1];
        Assert.True((await svc.GravarColetaAsync(empresa, primeira with { Situacao = "EM_COLETA" }, adm, ct)).Sucesso);
        Assert.Single(await svc.ColetasAsync(empresa, hoje, hoje, "EM_COLETA", adm, ct));
        Assert.True((await svc.GravarColetaAsync(empresa, primeira with { Situacao = "COLETADA" }, adm, ct)).Sucesso);
        Assert.False((await svc.GravarColetaAsync(empresa, primeira with { Situacao = "AGENDADA" }, adm, ct)).Sucesso);   // coletada: travada
        Assert.False((await svc.ExcluirColetaAsync(empresa, primeira.Id, adm, ct)).Sucesso);
        var segunda = lista[0];
        Assert.False((await svc.GravarColetaAsync(empresa, segunda with { Situacao = "COLETADA", Volumes = -1 }, adm, ct)).Sucesso);
        Assert.True((await svc.GravarColetaAsync(empresa, segunda with { Situacao = "CANCELADA" }, adm, ct)).Sucesso);
        Assert.True((await svc.ExcluirColetaAsync(empresa, segunda.Id, adm, ct)).Sucesso);
        Assert.Single(await svc.ColetasAsync(empresa, hoje, hoje, null, adm, ct));
        Assert.Empty(await svc.ColetasAsync(empresa, hoje.AddDays(1), hoje.AddDays(2), null, adm, ct));
    }
}
