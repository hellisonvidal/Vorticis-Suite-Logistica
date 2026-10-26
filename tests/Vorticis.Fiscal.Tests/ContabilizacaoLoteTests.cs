using Vorticis.Application.Contabil;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Contabilizacao;
using Vorticis.Fiscal.Documentos;
using Vorticis.Fiscal.Duplicatas;

namespace Vorticis.Fiscal.Tests;

/// <summary>Contabilização em lote (Contábil › Parâmetros › Tipo de contabilização): os módulos registram pendências e a tela de lote gera os mesmos lançamentos.</summary>
public class ContabilizacaoLoteTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static readonly DateOnly Mar = new(2026, 3, 10);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ArquivoXml Arq(string nome, string xml) => new(nome, XmlsDeTeste.Bytes(xml));

    private static ConfiguracaoDuplicatas Contas() => new(ContaFornecedores: "2.1.01.001", ContaClientes: "1.1.02.001", ContaJurosPagos: "4.3.01.001", ContaMultaPaga: "4.3.01.001",
        ContaDescontoObtido: "3.3.01.003", ContaJurosRecebidos: "3.3.01.002", ContaMultaRecebida: "3.3.01.002", ContaDescontoConcedido: "4.3.01.004");

    private async Task<decimal> SaldoAsync(long empresa, string conta) => await db.EscalarAsync<decimal>("""
        SELECT COALESCE(SUM(CASE p.ind_dc WHEN 'D' THEN p.valor ELSE -p.valor END), 0)
          FROM contabil.partida p JOIN contabil.lancamento l ON l.id = p.lancamento_id JOIN contabil.plano_contas c ON c.id = p.conta_id
         WHERE l.empresa_id = @e AND c.codigo = @c AND l.status <> 'ESTORNADO' AND l.origem <> 'ESTORNO'
        """, ("e", empresa), ("c", conta));

    private async Task<long> LancamentosAsync(long empresa) => await db.EscalarAsync<long>("SELECT count(*) FROM contabil.lancamento WHERE empresa_id = @e", ("e", empresa));

    [Fact]
    public async Task Modo_padrao_e_online_e_so_quem_pode_alterar_grava()
    {
        BancoDeTeste.ExigirBanco();
        var empresa = await db.CriarEmpresaAsync(CnpjsDeTeste.Valido(7101));
        var adm = await db.AdminAsync();
        Assert.Equal(ModoContabilizacao.Online, (await db.App.Pendencias.ModoAsync(empresa, adm, Ct)).Valor);
        Assert.True((await db.App.Pendencias.GravarModoAsync(empresa, ModoContabilizacao.Lote, adm, Ct)).Sucesso);
        Assert.Equal(ModoContabilizacao.Lote, (await db.App.Pendencias.ModoAsync(empresa, adm, Ct)).Valor);
        var semAcesso = await db.EscalarAsync<long>("INSERT INTO core.usuario (login, nome, senha_hash, administrador) VALUES ('sem_acesso_lote', 'Sem acesso', 'x', FALSE) RETURNING id");
        Assert.Contains("acesso", (await db.App.Pendencias.GravarModoAsync(empresa, ModoContabilizacao.Online, semAcesso, Ct)).Erros[0]);
        Assert.False((await db.App.Pendencias.ModoAsync(empresa, semAcesso, Ct)).Sucesso);
    }

    [Fact]
    public async Task Baixa_em_lote_registra_pendencia_e_o_lote_gera_o_mesmo_lancamento_e_desfaz()
    {
        BancoDeTeste.ExigirBanco();
        var cnpj = CnpjsDeTeste.Valido(7102);
        var empresa = await db.CriarEmpresaAsync(cnpj);
        var adm = await db.AdminAsync();
        var fiscal = db.App.Fiscal();
        var dup = fiscal.Duplicatas;
        await fiscal.Documentos.ImportarAsync(empresa,
            [Arq("compra.xml", XmlsDeTeste.Nfe(XmlsDeTeste.CnpjFornecedor, cnpj, 1, 810, Mar, [new ItemNfe("1102", 1000m)], nomeEmit: "Fornecedor Alfa",
                cobranca: [("001", new DateOnly(2026, 4, 10), 1000m)]))], adm, ct: Ct);
        Assert.True((await dup.GravarConfiguracaoAsync(empresa, Contas(), adm, Ct)).Sucesso);
        var pagar = (await dup.ConsultarAsync(empresa, new FiltroDuplicatas(TipoDuplicata.Pagar), adm, Ct)).Valor.Single();
        var banco = "1.1.01.002";
        var abril = new DateOnly(2026, 4, 12);
        var pend = db.App.Pendencias;
        Assert.True((await pend.GravarModoAsync(empresa, ModoContabilizacao.Lote, adm, Ct)).Sucesso);

        // em lote: a baixa é gravada (o saldo da duplicata cai), mas não há lançamento — só a pendência
        var antes = await LancamentosAsync(empresa);
        var baixa = await dup.BaixarAsync(empresa, new NovaBaixa(pagar.Id, abril, 400m, 20m, 10m, 5m, banco), adm, Ct);
        Assert.True(baixa.Sucesso, string.Join(";", baixa.Erros));
        Assert.Equal(antes, await LancamentosAsync(empresa));
        var b = (await dup.BaixasAsync(empresa, [pagar.Id], adm, Ct)).Valor.Single();
        Assert.Null(b.LancamentoId);
        Assert.Equal(600m, (await dup.ConsultarAsync(empresa, new FiltroDuplicatas(TipoDuplicata.Pagar), adm, Ct)).Valor.Single().Saldo);
        var pendentes = (await pend.ListarAsync(empresa, new FiltroPendencias(), adm, Ct)).Valor;
        var p = Assert.Single(pendentes);
        Assert.Equal((OrigensPendencia.Duplicatas, abril, 425m), (p.Origem, p.Periodo, p.Valor));

        // filtros por origem e período
        Assert.Empty((await pend.ListarAsync(empresa, new FiltroPendencias(OrigensPendencia.Folha), adm, Ct)).Valor);
        Assert.Empty((await pend.ListarAsync(empresa, new FiltroPendencias(Inicio: abril.AddDays(1)), adm, Ct)).Valor);
        Assert.Single((await pend.ListarAsync(empresa, new FiltroPendencias(OrigensPendencia.Duplicatas, abril, abril), adm, Ct)).Valor);

        // prévia mostra as partidas sem lançar
        var previa = await pend.PreviaAsync(empresa, [p.Id], adm, Ct);
        Assert.True(previa.Sucesso);
        Assert.Contains(previa.Valor, l => l.Contains("2.1.01.001"));
        Assert.Equal(antes, await LancamentosAsync(empresa));

        // contabilizar selecionadas: os mesmos saldos da baixa on-line
        var lote = (await pend.ContabilizarAsync(empresa, [p.Id], adm, Ct)).Valor;
        Assert.Equal((1, 425m, 0), (lote.Contabilizadas, lote.Valor, lote.Falhas.Count));
        Assert.Equal(400m, await SaldoAsync(empresa, "2.1.01.001"));
        Assert.Equal(30m, await SaldoAsync(empresa, "4.3.01.001"));
        Assert.Equal(-5m, await SaldoAsync(empresa, "3.3.01.003"));
        Assert.Equal(-425m, await SaldoAsync(empresa, banco));
        var contabilizada = (await dup.BaixasAsync(empresa, [pagar.Id], adm, Ct)).Valor.Single();
        Assert.NotNull(contabilizada.LancamentoId);
        Assert.Equal("Pagamento NF 810/001 - Fornecedor Alfa", await db.EscalarAsync<string>("SELECT max(historico) FROM contabil.partida WHERE lancamento_id = @l", ("l", contabilizada.LancamentoId!.Value)));
        Assert.Empty((await pend.ListarAsync(empresa, new FiltroPendencias(), adm, Ct)).Valor);
        Assert.Contains("já foram contabilizadas", (await pend.ContabilizarAsync(empresa, [p.Id], adm, Ct)).Erros[0]);

        // desfazer lote: estorna, devolve a pendência e a baixa fica sem lançamento
        var desfeito = await pend.DesfazerLoteAsync(empresa, lote.LoteId!.Value, adm, Ct);
        Assert.True(desfeito.Sucesso, string.Join(";", desfeito.Erros));
        Assert.Equal(1, desfeito.Valor.Contabilizadas);
        Assert.Equal(0m, await SaldoAsync(empresa, banco));
        Assert.Null((await dup.BaixasAsync(empresa, [pagar.Id], adm, Ct)).Valor.Single().LancamentoId);
        Assert.Single((await pend.ListarAsync(empresa, new FiltroPendencias(), adm, Ct)).Valor);
        Assert.True((await pend.ListarLotesAsync(empresa, adm, Ct)).Valor.Single(l => l.Id == lote.LoteId).Desfeito);
        Assert.Contains("já foi desfeito", (await pend.DesfazerLoteAsync(empresa, lote.LoteId.Value, adm, Ct)).Erros[0]);

        // contabiliza de novo; depois estorna a própria baixa (o estorno da baixa acompanha o lançamento do lote)
        var lote2 = (await pend.ContabilizarAsync(empresa, [p.Id], adm, Ct)).Valor;
        Assert.Equal(1, lote2.Contabilizadas);
        Assert.Equal(-425m, await SaldoAsync(empresa, banco));
        Assert.True((await dup.EstornarBaixaAsync(empresa, baixa.Valor, null, adm, Ct)).Sucesso);
        Assert.Equal(0m, await SaldoAsync(empresa, banco));

        // pendência de baixa que foi estornada antes do lote é cancelada, sem lançar
        var segunda = await dup.BaixarAsync(empresa, new NovaBaixa(pagar.Id, abril, 100m, 0, 0, 0, banco), adm, Ct);
        Assert.True(segunda.Sucesso);
        Assert.True((await dup.EstornarBaixaAsync(empresa, segunda.Valor, null, adm, Ct)).Sucesso);
        var orfa = (await pend.ListarAsync(empresa, new FiltroPendencias(), adm, Ct)).Valor.Single();
        var r = (await pend.ContabilizarAsync(empresa, [orfa.Id], adm, Ct)).Valor;
        Assert.Equal((0, 1), (r.Contabilizadas, r.Falhas.Count));
        Assert.Empty((await pend.ListarAsync(empresa, new FiltroPendencias(), adm, Ct)).Valor);
        Assert.Equal(0m, await SaldoAsync(empresa, banco));

        // de volta ao on-line: lança na hora, sem pendência (comportamento de sempre)
        Assert.True((await pend.GravarModoAsync(empresa, ModoContabilizacao.Online, adm, Ct)).Sucesso);
        var online = await dup.BaixarAsync(empresa, new NovaBaixa(pagar.Id, abril, 50m, 0, 0, 0, banco), adm, Ct);
        Assert.True(online.Sucesso);
        Assert.Equal(-50m, await SaldoAsync(empresa, banco));
        Assert.Empty((await pend.ListarAsync(empresa, new FiltroPendencias(), adm, Ct)).Valor);
    }

    private async Task<long> EmpresaComRegraAsync(int semente, ModoContabilizacao modo)
    {
        var cnpj = CnpjsDeTeste.Valido(semente);
        var empresa = await db.CriarEmpresaAsync(cnpj);
        var adm = await db.AdminAsync();
        var fiscal = db.App.Fiscal();
        Assert.True((await db.App.Pendencias.GravarModoAsync(empresa, modo, adm, Ct)).Sucesso);
        Assert.True((await fiscal.Contabilizacao.GravarRegraAsync(empresa, new RegraContabilizacao(0, 100, null, "5102", null, null, "1.1.02.001", "3.1.01.001", "Venda NF {DOCUMENTO}"), adm, Ct)).Sucesso);
        await fiscal.Documentos.ImportarAsync(empresa,
            [Arq("venda.xml", XmlsDeTeste.Nfe(cnpj, XmlsDeTeste.CnpjCliente, 1, 820, Mar, [new ItemNfe("5102", 500m)]))], adm, ct: Ct);
        return empresa;
    }

    private async Task<List<string>> PartidasAsync(long empresa) => (await db.EscalarAsync<string>("""
        SELECT COALESCE(string_agg(c.codigo || ':' || p.ind_dc || ':' || p.valor::text, ',' ORDER BY c.codigo, p.ind_dc), '')
          FROM contabil.partida p JOIN contabil.lancamento l ON l.id = p.lancamento_id JOIN contabil.plano_contas c ON c.id = p.conta_id
         WHERE l.empresa_id = @e AND l.origem <> 'ESTORNO' AND l.status <> 'ESTORNADO'
        """, ("e", empresa))).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

    [Fact]
    public async Task Nota_em_lote_vira_pendencia_e_o_lote_gera_o_mesmo_lancamento_do_modo_online()
    {
        BancoDeTeste.ExigirBanco();
        var adm = await db.AdminAsync();
        var fiscal = db.App.Fiscal();
        var pend = db.App.Pendencias;

        // referência: a nota contabilizada pela função de sempre
        var referencia = await EmpresaComRegraAsync(7103, ModoContabilizacao.Online);
        Assert.Equal(1, (await fiscal.Contabilizacao.ContabilizarAsync(referencia, Mar, Mar, null, null, adm, Ct)).Valor.Contabilizados);
        var esperado = await PartidasAsync(referencia);
        Assert.NotEmpty(esperado);

        var empresa = await EmpresaComRegraAsync(7104, ModoContabilizacao.Lote);
        var registro = await fiscal.Contabilizacao.ContabilizarSeOnlineAsync(empresa, Mar, Mar, null, adm, Ct);
        Assert.Equal(1, registro!.Valor.Registradas);
        Assert.Equal(0, registro.Valor.Contabilizados);
        Assert.Equal(0L, await LancamentosAsync(empresa));
        Assert.Equal(1L, await db.EscalarAsync<long>("SELECT count(*) FROM fiscal.documento WHERE empresa_id = @e AND lancamento_id IS NULL", ("e", empresa)));
        // registrar de novo não duplica a pendência
        Assert.Equal(0, (await fiscal.Contabilizacao.ContabilizarSeOnlineAsync(empresa, Mar, Mar, null, adm, Ct))!.Valor.Registradas);
        var p = (await pend.ListarAsync(empresa, new FiltroPendencias(OrigensPendencia.Fiscal), adm, Ct)).Valor.Single();
        Assert.Contains("NF 820", p.Descricao);
        Assert.Equal(Mar, p.Periodo);

        var lote = (await pend.ContabilizarAsync(empresa, [p.Id], adm, Ct)).Valor;
        Assert.Equal(1, lote.Contabilizadas);
        Assert.Equal(esperado, await PartidasAsync(empresa));
        Assert.Equal(1L, await db.EscalarAsync<long>("SELECT count(*) FROM fiscal.documento WHERE empresa_id = @e AND lancamento_id IS NOT NULL", ("e", empresa)));

        Assert.True((await pend.DesfazerLoteAsync(empresa, lote.LoteId!.Value, adm, Ct)).Sucesso);
        Assert.Empty(await PartidasAsync(empresa));
        Assert.Equal(1L, await db.EscalarAsync<long>("SELECT count(*) FROM fiscal.documento WHERE empresa_id = @e AND lancamento_id IS NULL", ("e", empresa)));
        Assert.Single((await pend.ListarAsync(empresa, new FiltroPendencias(), adm, Ct)).Valor);
    }
}
