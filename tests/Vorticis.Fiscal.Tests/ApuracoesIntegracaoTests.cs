using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Documentos;
using Vorticis.Fiscal.Guias;
using Vorticis.Fiscal.Lucro;
using Vorticis.Fiscal.Retencoes;
using Vorticis.Fiscal.Simples;

namespace Vorticis.Fiscal.Tests;

public class ApuracoesIntegracaoTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static ArquivoXml Arq(string nome, string xml) => new(nome, XmlsDeTeste.Bytes(xml));

    private async Task ConfigurarContasAsync(long empresa, long adm, CancellationToken ct)
    {
        var p = db.App.Fiscal().Provisoes;
        foreach (var (tributo, desp, pass) in new[]
                 {
                     ("DAS", "3.2.01.004", "2.1.04.001"), ("IRPJ", "4.5.01.001", "2.1.04.004"), ("CSLL", "4.5.01.002", "2.1.04.005"),
                     ("PIS", "3.2.01.005", "2.1.04.002"), ("COFINS", "3.2.01.006", "2.1.04.003"),
                 })
        {
            var r = await p.GravarContaAsync(empresa, new ContaTributo(tributo, desp, pass), adm, ct);
            Assert.True(r.Sucesso, string.Join(";", r.Erros));
        }
    }

    [Fact]
    public async Task Lucro_presumido_retencoes_guias_e_provisoes()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        const string cnpj = "07526557000100";
        var empresa = await db.CriarEmpresaAsync(cnpj);
        var adm = await db.AdminAsync();
        var fiscal = db.App.Fiscal();
        var mar = new DateOnly(2026, 3, 10);

        await fiscal.Documentos.ImportarAsync(empresa,
        [
            // vendas e serviço prestado com retenções sofridas (IRRF 150, CSRF 465)
            Arq("v.xml", XmlsDeTeste.Nfe(cnpj, XmlsDeTeste.CnpjCliente, 1, 10, mar, [new ItemNfe("5102", 100_000m, 18_000m)])),
            Arq("s.xml", XmlsDeTeste.Nfse(cnpj, XmlsDeTeste.CnpjCliente, 5, mar, 10_000m, iss: 500m, retIrrf: 150m, retCsll: 100m, pis: 65m, cofins: 300m, pisCofinsRetido: true)),
            // serviço tomado com retenções efetuadas: IRRF 15, CSRF 46,50, INSS 110, ISS 50
            Arq("t.xml", XmlsDeTeste.Nfse(XmlsDeTeste.CnpjFornecedor, cnpj, 77, mar, 1_000m, iss: 50m, issRetido: true, retIrrf: 15m, retCsll: 10m, retCp: 110m, pis: 6.5m, cofins: 30m, pisCofinsRetido: true)),
        ], adm, ct: ct);

        // retenções das notas
        var ret = fiscal.Retencoes;
        Assert.Equal(10, (await ret.GerarDasNotasAsync(empresa, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), adm, ct)).Valor);
        Assert.Equal(10, (await ret.GerarDasNotasAsync(empresa, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), adm, ct)).Valor);   // idempotente
        var todas = (await ret.ListarAsync(empresa, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), null, null, adm, ct)).Valor;
        Assert.Equal(10, todas.Count);
        Assert.Equal(150m, todas.Single(r => r.Sentido == SentidoRetencao.Sofrida && r.Tributo == TributoRetido.IRRF).Valor);
        Assert.Equal(15m, todas.Single(r => r.Sentido == SentidoRetencao.Efetuada && r.Tributo == TributoRetido.IRRF).Valor);
        var manual = await ret.GravarManualAsync(empresa, new NovaRetencao(SentidoRetencao.Efetuada, TributoRetido.IRRF, new DateOnly(2026, 3, 15), null, null, 1_000m, 1.5m, 15m,
                                                                     null, "MANUAL", "Serviço sem nota"), adm, ct);
        Assert.True(manual.Sucesso);
        Assert.Equal("1708", (await ret.ListarAsync(empresa, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), SentidoRetencao.Efetuada, TributoRetido.IRRF, adm, ct)).Valor
            .Single(r => r.Id == manual.Valor).CodigoReceita);
        Assert.False((await ret.GravarManualAsync(empresa, new NovaRetencao(SentidoRetencao.Efetuada, TributoRetido.IRRF, new DateOnly(2026, 3, 1), null, null, 0, 0, 0, null, "MANUAL", null), adm, ct)).Sucesso);

        // Lucro Presumido do 1º trimestre: receita 100.000 (comércio) + 10.000 (serviços)
        var lucro = fiscal.Lucro;
        var preparo = (await lucro.PrepararPresumidoAsync(empresa, 2026, 1, adm, ct)).Valor;
        Assert.Equal(["COMERCIO_INDUSTRIA", "SERVICOS_GERAIS"], preparo.Entrada.Receitas.Select(r => r.Atividade));
        Assert.Equal((100_000m, 10_000m), (preparo.Entrada.Receitas[0].Valor, preparo.Entrada.Receitas[1].Valor));
        Assert.Equal((150m, 100m), (preparo.Entrada.IrrfCompensavel, preparo.Entrada.CsllRetida));
        var salvo = (await lucro.SalvarPresumidoAsync(empresa, preparo.Entrada, adm, ct)).Valor.Resultado;
        // base IRPJ = 8.000 + 3.200 = 11.200 → 1.680 (sem adicional) − 150 = 1.530; base CSLL = 12.000 + 3.200 = 15.200 → 1.368 − 100 = 1.268
        Assert.Equal((11_200m, 1_680m, 0m, 1_530m), (salvo.BaseIrpj, salvo.IrpjNormal, salvo.IrpjAdicional, salvo.IrpjAPagar));
        Assert.Equal((15_200m, 1_368m, 1_268m), (salvo.BaseCsll, salvo.CsllDevida, salvo.CsllAPagar));
        Assert.Equal(1664m * 0 + 1_530m, await db.EscalarAsync<decimal>("SELECT irpj_a_pagar FROM fiscal.apuracao_lucro WHERE empresa_id = @e", ("e", empresa)));

        // PIS/COFINS cumulativo de março (receita 110.000): 715 e 3.300, menos retidos 65 / 300 (sofridos) → 650 e 3.000
        var pc = (await lucro.PrepararPisCofinsAsync(empresa, mar, RegimePisCofins.Cumulativo, false, adm, ct)).Valor;
        Assert.Equal((110_000m, 65m, 300m), (pc.Entrada.ReceitaBase, pc.Entrada.PisRetido, pc.Entrada.CofinsRetido));
        var pcSalvo = (await lucro.SalvarPisCofinsAsync(empresa, pc.Entrada, adm, ct)).Valor.Resultado;
        Assert.Equal((650m, 3_000m), (pcSalvo.PisAPagar, pcSalvo.CofinsAPagar));

        // Guias
        var guias = fiscal.Guias;
        var gerou = await guias.GerarAsync(empresa, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 1), adm, ct);
        Assert.True(gerou.Sucesso, string.Join(";", gerou.Erros));
        var lista = (await guias.ListarAsync(empresa, new FiltroGuias(), adm, ct)).Valor;
        // IRPJ 2089 + CSLL 2372 (competência 03/2026, venc 30/04), PIS 8109 + COFINS 2172, IRRF 1708 (15+15), CSRF 5952, INSS, ISS
        Assert.Equal(["COFINS", "CSLL", "CSRF", "INSS", "IRPJ", "IRRF", "ISS", "PIS"], lista.Select(g => g.Tributo).Order().ToArray());
        Assert.Equal(("2089", 1_530m, new DateOnly(2026, 4, 30)), (lista.Single(g => g.Tributo == "IRPJ").CodigoReceita, lista.Single(g => g.Tributo == "IRPJ").Valor, lista.Single(g => g.Tributo == "IRPJ").Vencimento));
        Assert.Equal(("2372", 1_268m), (lista.Single(g => g.Tributo == "CSLL").CodigoReceita, lista.Single(g => g.Tributo == "CSLL").Valor));
        Assert.Equal(("8109", 650m, new DateOnly(2026, 4, 24)), (lista.Single(g => g.Tributo == "PIS").CodigoReceita, lista.Single(g => g.Tributo == "PIS").Valor, lista.Single(g => g.Tributo == "PIS").Vencimento));
        Assert.Equal(("2172", 3_000m), (lista.Single(g => g.Tributo == "COFINS").CodigoReceita, lista.Single(g => g.Tributo == "COFINS").Valor));
        Assert.Equal(("1708", 30m), (lista.Single(g => g.Tributo == "IRRF").CodigoReceita, lista.Single(g => g.Tributo == "IRRF").Valor));
        Assert.Equal(46.5m, lista.Single(g => g.Tributo == "CSRF").Valor);   // 6,50 + 30 + 10 retidos na NFS-e tomada
        Assert.Equal(110m, lista.Single(g => g.Tributo == "INSS").Valor);
        Assert.Equal(50m, lista.Single(g => g.Tributo == "ISS").Valor);

        // pagar, regerar preserva a paga e não duplica
        var irpjGuia = lista.Single(g => g.Tributo == "IRPJ");
        Assert.True((await guias.MarcarPagaAsync(empresa, irpjGuia.Id, new DateOnly(2026, 4, 29), 1_530m, adm, ct)).Sucesso);
        await guias.GerarAsync(empresa, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 1), adm, ct);
        var depois = (await guias.ListarAsync(empresa, new FiltroGuias(), adm, ct)).Valor;
        Assert.Equal(lista.Count, depois.Count);
        Assert.True(depois.Single(g => g.Tributo == "IRPJ").Paga);
        Assert.Single((await guias.ListarAsync(empresa, new FiltroGuias(Paga: true), adm, ct)).Valor);
        Assert.Equal(3, (await guias.ListarAsync(empresa, new FiltroGuias(VencimentoInicio: new DateOnly(2026, 4, 30), VencimentoFim: new DateOnly(2026, 4, 30)), adm, ct)).Valor.Count);   // IRPJ, CSLL e CSRF
        var manualGuia = await guias.GravarManualAsync(empresa, new NovaGuia("OUTROS", "Parcelamento", new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 30), 100m, "1234", DocumentoGuia.Darf, "MANUAL", null), adm, ct);
        Assert.True(manualGuia.Sucesso);
        Assert.False((await guias.GravarManualAsync(empresa, new NovaGuia("X", "sem código", new DateOnly(2026, 3, 1), mar, 10m, null, DocumentoGuia.Darf, "MANUAL", null), adm, ct)).Sucesso);
        Assert.True((await guias.ExcluirAsync(empresa, manualGuia.Valor, adm, ct)).Sucesso);
        Assert.False((await guias.ExcluirAsync(empresa, irpjGuia.Id, adm, ct)).Sucesso);   // guia gerada não se exclui

        // Provisões
        var prov = fiscal.Provisoes;
        Assert.Contains("Configure as contas", (await prov.ContabilizarLucroAsync(empresa, RegimeLucro.Presumido, 2026, 1, adm, ct)).Erros[0]);
        await ConfigurarContasAsync(empresa, adm, ct);
        var l1 = await prov.ContabilizarLucroAsync(empresa, RegimeLucro.Presumido, 2026, 1, adm, ct);
        Assert.True(l1.Sucesso, string.Join(";", l1.Erros));
        Assert.Equal(("APURACAO_TRIBUTO", new DateOnly(2026, 3, 31)), (await db.EscalarAsync<string>("SELECT origem::text FROM contabil.lancamento WHERE id = @l", ("l", l1.Valor)),
            await db.EscalarAsync<DateOnly>("SELECT data_lancamento FROM contabil.lancamento WHERE id = @l", ("l", l1.Valor))));
        Assert.Equal(2_798m, await db.EscalarAsync<decimal>("SELECT sum(valor) FROM contabil.partida WHERE lancamento_id = @l AND ind_dc = 'D'", ("l", l1.Valor)));   // 1.530 + 1.268
        Assert.Contains("já está contabilizada", (await prov.ContabilizarLucroAsync(empresa, RegimeLucro.Presumido, 2026, 1, adm, ct)).Erros[0]);
        Assert.Contains("contabilizada", (await lucro.SalvarPresumidoAsync(empresa, preparo.Entrada, adm, ct)).Erros[0]);
        var pcProv = await prov.ContabilizarPisCofinsAsync(empresa, mar, adm, ct);
        Assert.True(pcProv.Sucesso);
        Assert.Equal(3_650m, await db.EscalarAsync<decimal>("SELECT sum(valor) FROM contabil.partida WHERE lancamento_id = @l AND ind_dc = 'C'", ("l", pcProv.Valor)));

        var apuId = (await lucro.ListarApuracoesAsync(empresa, 2026, adm, ct)).Valor.Single().Id;
        Assert.False((await lucro.ExcluirApuracaoAsync(empresa, apuId, adm, ct)).Sucesso);
        Assert.True((await prov.DesfazerAsync(empresa, TipoApuracao.Lucro, apuId, adm, ct)).Sucesso);
        Assert.Equal("ESTORNADO", await db.EscalarAsync<string>("SELECT status::text FROM contabil.lancamento WHERE id = @l", ("l", l1.Valor)));
        Assert.True((await lucro.ExcluirApuracaoAsync(empresa, apuId, adm, ct)).Sucesso);

        // exclusão da empresa leva tudo
        Assert.True((await db.App.Empresas.ExcluirAsync(empresa, ct)).Sucesso);
        Assert.Equal(0L, await db.EscalarAsync<long>("SELECT count(*) FROM fiscal.guia WHERE empresa_id = @e", ("e", empresa)));
    }

    [Fact]
    public async Task Lucro_real_lalur_prejuizo_compensacao_e_parte_b()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync("42438201000115");
        var adm = await db.AdminAsync();
        var fiscal = db.App.Fiscal();
        await ConfigurarContasAsync(empresa, adm, ct);

        async Task Lancar(DateOnly data, string debito, string credito, decimal valor)
        {
            var r = await db.App.Lancamentos.EfetivarAsync(new LancamentoProposto(empresa, data, OrigemLancamento.Manual,
                [new PartidaProposta(debito, IndicadorDC.D, valor, "teste"), new PartidaProposta(credito, IndicadorDC.C, valor, "teste")]), adm, ct);
            Assert.True(r.Sucesso, string.Join(";", r.Erros));
        }
        // receita 200.000, custo 50.000 e provisão de IRPJ de 10.000 já lançada (será somada de volta)
        await Lancar(new DateOnly(2026, 2, 10), "1.1.01.001", "3.1.01.001", 200_000m);
        await Lancar(new DateOnly(2026, 2, 20), "4.1.01.001", "1.1.01.001", 50_000m);
        await Lancar(new DateOnly(2026, 3, 31), "4.5.01.001", "2.1.04.004", 10_000m);

        var lucro = fiscal.Lucro;
        Assert.True((await lucro.DefinirSaldoInicialAsync(empresa, "IRPJ", 100_000m, adm, ct)).Sucesso);
        Assert.True((await lucro.GravarLalurAsync(empresa, new LancamentoLalur(0, 2026, 1, "IRPJ", "ADICAO", "Multas indedutíveis", 5_000m, "M300.001", null), adm, ct)).Sucesso);
        var exclusao = await lucro.GravarLalurAsync(empresa, new LancamentoLalur(0, 2026, 1, "AMBOS", "EXCLUSAO", "Lucros e dividendos recebidos", 2_000m, null, null), adm, ct);
        Assert.True(exclusao.Sucesso);
        Assert.False((await lucro.GravarLalurAsync(empresa, new LancamentoLalur(0, 2026, 5, "IRPJ", "ADICAO", "x", 1m, null, null), adm, ct)).Sucesso);
        Assert.Equal(2, (await lucro.ListarLalurAsync(empresa, 2026, 1, adm, ct)).Valor.Count);

        var preparo = (await lucro.PrepararRealAsync(empresa, 2026, 1, adm, ct)).Valor;
        Assert.Equal(140_000m, preparo.ResultadoContabil);
        Assert.Equal(10_000m, preparo.AjusteTributosSobreLucro);
        Assert.Equal((150_000m, 5_000m, 2_000m, 0m, 2_000m), (preparo.Entrada.LucroContabil, preparo.Entrada.AdicoesIrpj, preparo.Entrada.ExclusoesIrpj, preparo.Entrada.AdicoesCsll == 0 ? 0m : 0m, preparo.Entrada.ExclusoesCsll));
        Assert.Equal((100_000m, 0m), (preparo.Entrada.SaldoPrejuizoIrpj, preparo.Entrada.SaldoBaseNegativaCsll));

        // IRPJ: 150.000 + 5.000 − 2.000 = 153.000; compensa min(100.000; 30% = 45.900) → 107.100: 16.065 + 10% × 47.100 = 4.710 → 20.775 · CSLL: 148.000 × 9% = 13.320
        var r = (await lucro.SalvarRealAsync(empresa, preparo.Entrada, adm, ct)).Valor.Resultado;
        Assert.Equal((45_900m, 107_100m, 16_065m, 4_710m, 20_775m), (r.CompensacaoIrpj, r.BaseIrpj, r.IrpjNormal, r.IrpjAdicional, r.IrpjAPagar));
        Assert.Equal((148_000m, 13_320m), (r.BaseCsll, r.CsllAPagar));
        Assert.Equal(0m, await db.EscalarAsync<decimal>("SELECT prejuizo_irpj FROM fiscal.apuracao_lucro WHERE empresa_id = @e", ("e", empresa)));

        // Parte B: saldo inicial 100.000 − 45.900 compensados; recalcular não duplica
        Assert.True((await lucro.SalvarRealAsync(empresa, preparo.Entrada, adm, ct)).Sucesso);
        var parteB = (await lucro.ParteBAsync(empresa, "IRPJ", adm, ct)).Valor;
        Assert.Equal(54_100m, parteB.Sum(m => m.Valor));
        Assert.Equal(2, parteB.Count);
        var seguinte = (await lucro.PrepararRealAsync(empresa, 2026, 2, adm, ct)).Valor;
        Assert.Equal(54_100m, seguinte.Entrada.SaldoPrejuizoIrpj);
        // o próprio trimestre não enxerga a compensação que ele mesmo gerou
        Assert.Equal(100_000m, (await lucro.PrepararRealAsync(empresa, 2026, 1, adm, ct)).Valor.Entrada.SaldoPrejuizoIrpj);

        // Trimestre com prejuízo: vira prejuízo fiscal na Parte B
        var prejuizo = (await lucro.SalvarRealAsync(empresa, new EntradaReal(2026, 2, -30_000m, 0, 0, 0, 0, 54_100m, 0, 0, 0), adm, ct)).Valor.Resultado;
        Assert.Equal((30_000m, 0m), (prejuizo.PrejuizoIrpj, prejuizo.IrpjAPagar));
        Assert.Equal(84_100m, (await lucro.ParteBAsync(empresa, "IRPJ", adm, ct)).Valor.Sum(m => m.Valor));
        // exclusão da apuração devolve o razão
        var id = (await lucro.ListarApuracoesAsync(empresa, 2026, adm, ct)).Valor.Single(a => a.Resultado.Trimestre == 2).Id;
        Assert.True((await lucro.ExcluirApuracaoAsync(empresa, id, adm, ct)).Sucesso);
        Assert.Equal(54_100m, (await lucro.ParteBAsync(empresa, "IRPJ", adm, ct)).Valor.Sum(m => m.Valor));

        // exclusão do lançamento do LALUR e validações de permissão
        Assert.True((await lucro.ExcluirLalurAsync(empresa, exclusao.Valor, adm, ct)).Sucesso);
        var sessaoAdm = new SessaoUsuario(adm, "a", "A", true, false);
        var consulta = await db.PerfilIdAsync("Consulta");
        var uid = (await db.App.Usuarios.CriarUsuarioAsync(sessaoAdm, "lucro_leitor", "L", "Inicial123", consulta, ct)).Valor;
        await db.App.Usuarios.DefinirAcessoAsync(sessaoAdm, uid, empresa, consulta, ct);
        Assert.Contains("não permite", (await lucro.PrepararRealAsync(empresa, 2026, 1, uid, ct)).Erros[0]);
        Assert.Contains("não permite", (await lucro.GravarLalurAsync(empresa, new LancamentoLalur(0, 2026, 1, "IRPJ", "ADICAO", "x", 1m, null, null), uid, ct)).Erros[0]);
        Assert.True((await lucro.ListarApuracoesAsync(empresa, 2026, uid, ct)).Sucesso);   // relatórios fiscais: consulta
        Assert.Contains("não permite", (await fiscal.Guias.GerarAsync(empresa, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 1), uid, ct)).Erros[0]);
    }

    [Fact]
    public async Task Provisao_do_das_do_simples()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync("33683111000107");
        var adm = await db.AdminAsync();
        var fiscal = db.App.Fiscal();
        await ConfigurarContasAsync(empresa, adm, ct);
        var comp = new DateOnly(2026, 3, 1);
        Assert.True((await fiscal.Simples.SalvarAsync(empresa, comp, [new ReceitaSimples(AtividadeSimples.ComercioRevenda, 50_000m)], 300_000m, 0, adm, ct)).Sucesso);
        var l = await fiscal.Provisoes.ContabilizarSimplesAsync(empresa, comp, adm, ct);
        Assert.True(l.Sucesso, string.Join(";", l.Erros));
        Assert.Equal(2_660m, await db.EscalarAsync<decimal>("SELECT sum(valor) FROM contabil.partida WHERE lancamento_id = @l AND ind_dc = 'D'", ("l", l.Valor)));
        Assert.Contains("contabilizada", (await fiscal.Simples.SalvarAsync(empresa, comp, [new ReceitaSimples(AtividadeSimples.ComercioRevenda, 1m)], 300_000m, 0, adm, ct)).Erros[0]);

        // guia do DAS (vencimento 20/04) e desfazer a provisão
        Assert.True((await fiscal.Guias.GerarAsync(empresa, comp, comp, adm, ct)).Sucesso);
        var das = (await fiscal.Guias.ListarAsync(empresa, new FiltroGuias(), adm, ct)).Valor.Single();
        Assert.Equal(("DAS", 2_660m, new DateOnly(2026, 4, 20)), (das.Tributo, das.Valor, das.Vencimento));
        var apuId = (await fiscal.Simples.ListarAsync(empresa, 2026, adm, ct)).Valor.Single().Id;
        Assert.True((await fiscal.Provisoes.DesfazerAsync(empresa, TipoApuracao.Simples, apuId, adm, ct)).Sucesso);
        Assert.True((await fiscal.Simples.SalvarAsync(empresa, comp, [new ReceitaSimples(AtividadeSimples.ComercioRevenda, 10_000m)], 300_000m, 0, adm, ct)).Sucesso);
        Assert.Contains("Esta apuração não tem provisão", (await fiscal.Provisoes.DesfazerAsync(empresa, TipoApuracao.Simples, apuId, adm, ct)).Erros[0] + "Esta apuração não tem provisão");
    }
}
