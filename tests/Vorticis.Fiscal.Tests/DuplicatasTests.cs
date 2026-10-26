using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Contabilizacao;
using Vorticis.Fiscal.Documentos;
using Vorticis.Fiscal.Duplicatas;
using Vorticis.Fiscal.Relatorios;
using Vorticis.Infra.Financeiro;

namespace Vorticis.Fiscal.Tests;

/// <summary>Regras puras das duplicatas: parcelas, aging e sugestão de conciliação.</summary>
public class DuplicatasRegrasTests
{
    private static Duplicata Dup(long id, TipoDuplicata tipo, string doc, DateOnly venc, decimal saldo, string? part = "Forn", SituacaoDuplicata sit = SituacaoDuplicata.Aberta) =>
        new(id, null, null, 1, "1", part, tipo, "001", doc, venc.AddDays(-30), venc, saldo, 0, 0, 0, saldo, sit, null, "MANUAL");

    [Fact]
    public void Gera_parcelas_iguais_com_a_diferenca_na_ultima()
    {
        var p = GeradorParcelas.Gerar(100m, 3, new DateOnly(2026, 3, 31), 30);
        Assert.Equal(["001", "002", "003"], p.Select(x => x.Numero));
        Assert.Equal([33.33m, 33.33m, 33.34m], p.Select(x => x.Valor));
        Assert.Equal([new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 30), new DateOnly(2026, 5, 30)], p.Select(x => x.Vencimento));
        Assert.Equal(100m, p.Sum(x => x.Valor));
        Assert.Empty(GeradorParcelas.Gerar(0m, 3, new DateOnly(2026, 3, 31), 30));
    }

    [Fact]
    public void Aging_separa_as_faixas_e_ignora_liquidadas_e_canceladas()
    {
        var hoje = new DateOnly(2026, 6, 30);
        var lista = new[]
        {
            Dup(1, TipoDuplicata.Pagar, "1", hoje.AddDays(5), 100m),              // a vencer
            Dup(2, TipoDuplicata.Pagar, "2", hoje, 10m),                          // vence hoje: ainda a vencer
            Dup(3, TipoDuplicata.Pagar, "3", hoje.AddDays(-1), 20m),              // 1 a 30
            Dup(4, TipoDuplicata.Pagar, "4", hoje.AddDays(-31), 30m),             // 31 a 60
            Dup(5, TipoDuplicata.Pagar, "5", hoje.AddDays(-61), 40m),             // 61 a 90
            Dup(6, TipoDuplicata.Pagar, "6", hoje.AddDays(-91), 50m),             // acima de 90
            Dup(7, TipoDuplicata.Pagar, "7", hoje.AddDays(-200), 60m, sit: SituacaoDuplicata.Liquidada),
            Dup(8, TipoDuplicata.Pagar, "8", hoje.AddDays(-200), 70m, sit: SituacaoDuplicata.Cancelada),
            Dup(9, TipoDuplicata.Pagar, "9", hoje.AddDays(-10), 5m, part: "Outro"),
        };
        var linhas = Aging.Posicao(lista, hoje);
        Assert.Equal(["Forn", "Outro"], linhas.Select(l => l.Participante));
        Assert.Equal([110m, 20m, 30m, 40m, 50m], linhas[0].Faixas);
        Assert.Equal([0m, 5m, 0m, 0m, 0m], linhas[1].Faixas);
        Assert.Equal(255m, linhas.Sum(l => l.Total));
        var tabela = TabelasFiscais.PosicaoPorVencimento(TipoDuplicata.Pagar, linhas, hoje);
        Assert.Equal(7, tabela.Colunas.Count);
        Assert.Equal(255m, tabela.Linhas[^1].Valores[^1]);
    }

    private static MovimentoExtrato Mov(long id, DateOnly data, IndicadorDC dc, decimal valor, string hist = "PIX", string? doc = null, bool contraConciliada = false) =>
        new(id, id, data, dc, valor, hist, doc, "1.1.01.002", id + 1000, "1.1.01.001", contraConciliada);

    [Fact]
    public void Sugere_por_valor_e_data_proxima_ou_documento_sem_repetir_movimento()
    {
        var venc = new DateOnly(2026, 4, 10);
        var d1 = Dup(1, TipoDuplicata.Pagar, "1234", venc, 400m);
        var d2 = Dup(2, TipoDuplicata.Pagar, "9999", venc, 400m);
        var receber = Dup(3, TipoDuplicata.Receber, "55", venc, 400m, "Cliente");
        var movs = new[]
        {
            Mov(1, venc.AddDays(1), IndicadorDC.C, 400m),                              // saída: casa com a pagar (1 dia)
            Mov(2, venc.AddDays(30), IndicadorDC.C, 400m, "PAGTO NF 1234 ALFA"),      // fora da data, mas cita o documento da d1
            Mov(3, venc, IndicadorDC.D, 400m),                                        // entrada: só casa com a receber
            Mov(4, venc, IndicadorDC.C, 123m),                                        // valor diferente
            Mov(5, venc, IndicadorDC.C, 400m, contraConciliada: true),                // contrapartida já conciliada
        };
        var s = ConciliadorDuplicatas.Sugerir([d1, d2, receber], movs, toleranciaDias: 5);
        // d1 tem dois candidatos (mov 1: 35 pontos; mov 2 pelo documento: 60); d2 fica com o outro movimento
        Assert.Equal(3, s.Count);
        Assert.Equal((1L, 2L), (s[0].Duplicata.Id, s[0].Movimento.PartidaBancoId));
        Assert.Contains("documento", s[0].Motivo);
        Assert.Equal(3L, s.Single(x => x.Duplicata.Id == 3).Movimento.PartidaBancoId);
        Assert.Equal(1L, s.Single(x => x.Duplicata.Id == 2).Movimento.PartidaBancoId);
        Assert.DoesNotContain(s, x => x.Movimento.PartidaBancoId is 4 or 5);
        Assert.Empty(ConciliadorDuplicatas.Sugerir([d2], [Mov(6, venc.AddDays(20), IndicadorDC.C, 400m)], 5));
    }

    [Fact]
    public void Resumo_de_vendas_soma_so_as_notas_autorizadas_por_nota_cliente_ou_mes()
    {
        var baseDoc = new DocumentoFiscal(1, ModeloDocumento.NFe55, new string('1', 44), TipoOperacao.Saida, "1", "1", new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 9), null,
            SituacaoDocumento.Autorizada, new ParteDocumento("11222333000181", "Eu", "SP"), new ParteDocumento("12345678909", "Cliente A", "RJ"), 5, "7",
            new TotaisDocumento(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0, 0, 0, 0, 0), null, false);
        DocumentoFiscal[] docs =
        [
            baseDoc with { Totais = baseDoc.Totais with { Total = 100m, Icms = 18m } },
            baseDoc with { Id = 2, Numero = "2", Emissao = new DateOnly(2026, 4, 2), DataLivro = new DateOnly(2026, 4, 2), Totais = baseDoc.Totais with { Total = 50m } },
            baseDoc with { Id = 3, Numero = "3", Situacao = SituacaoDocumento.Cancelada, Totais = baseDoc.Totais with { Total = 999m } },
            baseDoc with { Id = 4, Numero = "4", Operacao = TipoOperacao.Entrada, Totais = baseDoc.Totais with { Total = 70m } },
        ];
        var porNota = TabelasFiscais.ResumoNotas(docs, TipoOperacao.Saida, 0);
        Assert.Equal("Resumo de vendas", porNota.Titulo);
        Assert.Equal(3, porNota.Linhas.Count);                       // 2 notas + total (cancelada e entrada ficam de fora)
        Assert.Equal(150m, porNota.Linhas[^1].Valores[4]);
        Assert.Equal(18m, porNota.Linhas[^1].Valores[5]);
        Assert.Equal(2, TabelasFiscais.ResumoNotas(docs, TipoOperacao.Saida, 1).Linhas.Count);   // um cliente + total
        Assert.Equal(3, TabelasFiscais.ResumoNotas(docs, TipoOperacao.Saida, 2).Linhas.Count);   // março, abril + total
        Assert.Equal(70m, TabelasFiscais.ResumoNotas(docs, TipoOperacao.Entrada, 0).Linhas[^1].Valores[4]);
        Assert.All(porNota.Linhas, l => Assert.Equal(porNota.Colunas.Count, l.Valores.Count));
    }

    [Fact]
    public void Motor_usa_a_conta_do_participante_como_contrapartida_da_nota_com_duplicatas()
    {
        var doc = new DocumentoFiscal(10, ModeloDocumento.NFe55, new string('1', 44), TipoOperacao.Entrada, "321", "1", new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 10), null,
            SituacaoDocumento.Autorizada, new ParteDocumento("1", "Forn", "SP"), new ParteDocumento("2", "Eu", "RJ"), 5, "7",
            new TotaisDocumento(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0, 0, 0, 0, 0), null, false);
        var item = new ItemFiscal(1, null, null, null, null, "1102", null, 1, 100m, 100m, 0, 0, 0, 0, 100m, null, null, 0, 0, 0, 0, 0, null, 0, 0, 0, null, 0, 0, 0, null, 0, 0, 0, 0, 0, 0);
        var regras = new[] { new RegraContabilizacao(1, 100, null, "1102", null, null, "1.1.04.001", "2.1.99.999", "NF {DOCUMENTO}") };
        var comConta = MotorContabilizacao.Montar(1, doc, [item], regras, contaParticipante: "2.1.01.001").Lancamento!;
        Assert.Equal(("1.1.04.001", "2.1.01.001"), (comConta.Partidas[0].CodigoConta, comConta.Partidas[1].CodigoConta));
        var semConta = MotorContabilizacao.Montar(1, doc, [item], regras).Lancamento!;
        Assert.Equal("2.1.99.999", semConta.Partidas[1].CodigoConta);
        // regra específica do participante ou conta definida na própria nota têm preferência
        var porParticipante = MotorContabilizacao.Montar(1, doc, [item], [regras[0] with { ParticipanteId = 5 }], contaParticipante: "2.1.01.001").Lancamento!;
        Assert.Equal("2.1.99.999", porParticipante.Partidas[1].CodigoConta);
        var naNota = MotorContabilizacao.Montar(1, doc with { ContaCredito = "2.1.77.001" }, [item], regras, contaParticipante: "2.1.01.001").Lancamento!;
        Assert.Equal("2.1.77.001", naNota.Partidas[1].CodigoConta);
    }
}

public class DuplicatasIntegracaoTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static readonly DateOnly Mar = new(2026, 3, 10);

    private static ArquivoXml Arq(string nome, string xml) => new(nome, XmlsDeTeste.Bytes(xml));

    private static ConfiguracaoDuplicatas Contas() => new(ContaFornecedores: "2.1.01.001", ContaClientes: "1.1.02.001", ContaJurosPagos: "4.3.01.001", ContaMultaPaga: "4.3.01.001",
        ContaDescontoObtido: "3.3.01.003", ContaJurosRecebidos: "3.3.01.002", ContaMultaRecebida: "3.3.01.002", ContaDescontoConcedido: "4.3.01.004");

    private async Task<decimal> SaldoAsync(long empresa, string conta) => await db.EscalarAsync<decimal>("""
        SELECT COALESCE(SUM(CASE p.ind_dc WHEN 'D' THEN p.valor ELSE -p.valor END), 0)
          FROM contabil.partida p JOIN contabil.lancamento l ON l.id = p.lancamento_id JOIN contabil.plano_contas c ON c.id = p.conta_id
         WHERE l.empresa_id = @e AND c.codigo = @c AND l.status <> 'ESTORNADO' AND l.origem <> 'ESTORNO'
        """, ("e", empresa), ("c", conta));

    private static ItemFiscal Item(string cfop, decimal valor) =>
        new(0, null, "Mercadoria", null, null, cfop, "UN", 1, valor, 0, 0, 0, 0, 0, 0, "00", "0", 0, 0, 0, 0, 0, null, 0, 0, 0, null, 0, 0, 0, null, 0, 0, 0, 0, 0, 0);

    [Fact]
    public async Task Importacao_gera_duplicatas_da_cobranca_e_a_vista_conforme_a_configuracao()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var cnpj = CnpjsDeTeste.Valido(701);
        var empresa = await db.CriarEmpresaAsync(cnpj);
        var adm = await db.AdminAsync();
        var fiscal = db.App.Fiscal();
        var venc1 = new DateOnly(2026, 4, 10);
        var venc2 = new DateOnly(2026, 5, 10);

        var r = await fiscal.Documentos.ImportarAsync(empresa,
        [
            Arq("compra.xml", XmlsDeTeste.Nfe(XmlsDeTeste.CnpjFornecedor, cnpj, 1, 500, Mar, [new ItemNfe("1102", 1000m)], nomeEmit: "Fornecedor Alfa",
                cobranca: [("001", venc1, 400m), ("002", venc2, 600m)])),
            Arq("venda.xml", XmlsDeTeste.Nfe(cnpj, XmlsDeTeste.CnpjCliente, 1, 600, Mar, [new ItemNfe("5102", 250m)])),                     // sem cobrança: à vista
            Arq("nfce.xml", XmlsDeTeste.Nfe(cnpj, null, 1, 601, Mar, [new ItemNfe("5102", 30m)], modelo: "65")),                            // NFC-e sem cobrança: nada
        ], adm, ct: ct);
        Assert.Equal(3, r.Valor.Importados);

        var dup = fiscal.Duplicatas;
        var pagar = (await dup.ConsultarAsync(empresa, new FiltroDuplicatas(TipoDuplicata.Pagar), adm, ct)).Valor;
        Assert.Equal(["001", "002"], pagar.Select(d => d.Parcela));
        Assert.Equal([400m, 600m], pagar.Select(d => d.Valor));
        Assert.Equal([venc1, venc2], pagar.Select(d => d.Vencimento));
        Assert.All(pagar, d =>
        {
            Assert.Equal(("500", "Fornecedor Alfa", SituacaoDuplicata.Aberta, "NOTA"), (d.NumeroDocumento, d.ParticipanteNome, d.Situacao, d.Origem));
            Assert.Equal(d.Valor, d.Saldo);
        });
        var receber = (await dup.ConsultarAsync(empresa, new FiltroDuplicatas(TipoDuplicata.Receber), adm, ct)).Valor;
        var aVista = Assert.Single(receber);
        Assert.Equal(("600", Mar, Mar, 250m), (aVista.NumeroDocumento, aVista.Emissao, aVista.Vencimento, aVista.Valor));

        // configuração: vencimento à vista em 15 dias; depois, sem duplicata à vista
        Assert.True((await dup.GravarConfiguracaoAsync(empresa, Contas() with { AVistaDias = 15 }, adm, ct)).Sucesso);
        await fiscal.Documentos.ImportarAsync(empresa, [Arq("v2.xml", XmlsDeTeste.Nfe(cnpj, XmlsDeTeste.CnpjCliente, 1, 602, Mar, [new ItemNfe("5102", 80m)]))], adm, ct: ct);
        Assert.Equal(Mar.AddDays(15), (await dup.ConsultarAsync(empresa, new FiltroDuplicatas(TipoDuplicata.Receber), adm, ct)).Valor.Single(d => d.NumeroDocumento == "602").Vencimento);
        Assert.True((await dup.GravarConfiguracaoAsync(empresa, Contas() with { GerarAVista = false }, adm, ct)).Sucesso);
        await fiscal.Documentos.ImportarAsync(empresa, [Arq("v3.xml", XmlsDeTeste.Nfe(cnpj, XmlsDeTeste.CnpjCliente, 1, 603, Mar, [new ItemNfe("5102", 90m)]))], adm, ct: ct);
        Assert.DoesNotContain((await dup.ConsultarAsync(empresa, new FiltroDuplicatas(), adm, ct)).Valor, d => d.NumeroDocumento == "603");

        // configuração inválida
        var ruim = await dup.GravarConfiguracaoAsync(empresa, Contas() with { ContaFornecedores = "9.9.9", HistoricoPagamento = "x {XYZ}" }, adm, ct);
        Assert.Contains(ruim.Erros, e => e.Contains("não encontrada"));
        Assert.Contains(ruim.Erros, e => e.Contains("Variável desconhecida"));
    }

    [Fact]
    public void Cfop_de_imobilizado_so_vale_para_nota_de_entrada()
    {
        Assert.True(CfopImobilizado.Eh("1.551"));
        Assert.True(CfopImobilizado.Eh("2406"));
        Assert.False(CfopImobilizado.Eh("1102"));
        ItemFiscal[] itens = [Item("1551", 5000m), Item("1102", 100m), Item("1102", 50m) with { CfopOriginal = "2551" }];
        Assert.Equal(2, CfopImobilizado.Itens(TipoOperacao.Entrada, itens).Count);
        Assert.Empty(CfopImobilizado.Itens(TipoOperacao.Saida, itens));
    }

    [Fact]
    public void Forma_de_pagamento_divide_o_total_nos_prazos_e_confere_o_formato()
    {
        var f = new FormaPagamento(0, 1, "Boleto 30/60/90", MeioPagamento.Boleto, " 30 / 60 / 90 ", null);
        Assert.Empty(f.Validar());
        Assert.Equal([30, 60, 90], f.Dias);
        Assert.Equal("3x (30/60/90 dias)", f.CondicaoTexto);
        var p = f.GerarParcelas(100m, Mar);
        Assert.Equal(["1/3", "2/3", "3/3"], p.Select(x => x.Numero));
        Assert.Equal([33.33m, 33.33m, 33.34m], p.Select(x => x.Valor));
        Assert.Equal([Mar.AddDays(30), Mar.AddDays(60), Mar.AddDays(90)], p.Select(x => x.Vencimento));
        var vista = new FormaPagamento(0, 2, "Dinheiro", MeioPagamento.Dinheiro, "0", null);
        Assert.True(vista.AVista);
        Assert.Equal(("1", Mar, 250m), (vista.GerarParcelas(250m, Mar)[0].Numero, vista.GerarParcelas(250m, Mar)[0].Vencimento, vista.GerarParcelas(250m, Mar)[0].Valor));
        Assert.NotEmpty((f with { Prazos = "30/abc" }).Validar());
        Assert.NotEmpty((f with { Prazos = "60/30" }).Validar());
        Assert.NotEmpty((f with { Descricao = " " }).Validar());
        Assert.Equal("30/60", FormaPagamento.NormalizarPrazos("30;60"));
        // parcelas, 1º vencimento e intervalo montam os prazos
        Assert.Equal("30/60/90", FormaPagamento.MontarPrazos(3, 30, 30));
        Assert.Equal("0", FormaPagamento.MontarPrazos(1, 0, 30));
        Assert.Equal(30, f.Intervalo);
        Assert.Null((f with { Prazos = "0/30/45" }).Intervalo);
    }

    [Fact]
    public async Task Cadastros_financeiros_e_forma_de_pagamento_na_nota_digitada()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync(CnpjsDeTeste.Valido(791));
        var adm = await db.AdminAsync();
        var svc = db.App.CadastrosFinanceiros();

        // conta bancária: formato e conta contábil conferidos
        Assert.Contains(ServicoCadastrosFinanceiros.Validar(new ContaBancaria(0, 1, "BB", "1", "12a", "x", null)), e => e.StartsWith("Banco"));
        Assert.Contains("não existe no plano", (await svc.GravarContaAsync(empresa, new ContaBancaria(0, 1, "BB", "001", "1234-5", "12345-6", "9.9.99"), adm, ct)).Erros[0]);
        var conta = await svc.GravarContaAsync(empresa, new ContaBancaria(0, 1, "Banco do Brasil", "001", "1234-5", "12345-6", null), adm, ct);
        Assert.True(conta.Sucesso, string.Join("; ", conta.Erros));
        Assert.False((await svc.GravarContaAsync(empresa, new ContaBancaria(0, 2, "Repetida", "001", "1234-5", "12345-6", null), adm, ct)).Sucesso);

        // forma de pagamento ligada à conta
        var forma = await svc.GravarFormaAsync(empresa, new FormaPagamento(0, 1, "Boleto 30/60", MeioPagamento.Boleto, "30/60", conta.Valor), adm, ct);
        Assert.True(forma.Sucesso, string.Join("; ", forma.Erros));
        var lida = Assert.Single(await svc.FormasAsync(empresa, adm, ct));
        Assert.Equal((MeioPagamento.Boleto, "30/60", (long?)conta.Valor), (lida.Meio, lida.Prazos, lida.ContaBancariaId));
        Assert.True((await svc.GravarFormaAsync(empresa, lida with { Meio = MeioPagamento.CartaoCredito }, adm, ct)).Sucesso);
        Assert.Equal(MeioPagamento.CartaoCredito, (await svc.FormasAsync(empresa, adm, ct))[0].Meio);

        // nota digitada com a forma: as parcelas vêm dos prazos e a forma fica gravada
        var part = await db.EscalarAsync<long>("INSERT INTO core.participante (empresa_id, codigo, nome, cpf_cnpj, uf) VALUES (@e, '10', 'Fornecedor Gama', @d, 'SP') RETURNING id",
            ("e", empresa), ("d", XmlsDeTeste.CnpjFornecedor));
        var itens = new[] { Item("1102", 300m) };
        var t = CalculadoraNota.Totalizar(itens, new TotaisDocumento(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0, 0, 0, 0, 0));
        var parcelas = lida.GerarParcelas(t.Total, Mar).Select(p => new ParcelaDuplicata(p.Numero, p.Vencimento, p.Valor)).ToList();
        var fiscal = db.App.Fiscal();
        var id = await fiscal.Digitacao.GravarAsync(empresa, new DocumentoDigitado(0, TipoOperacao.Entrada, ModeloDocumento.NFe55, "1", "880", null, Mar, Mar, "Compra", part, null, t,
            itens, Parcelas: parcelas, FormaPagamentoId: lida.Id), adm, ct);
        Assert.True(id.Sucesso, string.Join("; ", id.Erros));
        Assert.Equal(lida.Id, (await fiscal.Digitacao.ObterAsync(empresa, id.Valor, adm, ct)).Valor.FormaPagamentoId);
        var dups = (await fiscal.Duplicatas.ConsultarAsync(empresa, new FiltroDuplicatas(DocumentoId: id.Valor), adm, ct)).Valor;
        Assert.Equal([Mar.AddDays(30), Mar.AddDays(60)], dups.Select(d => d.Vencimento));
        Assert.Equal(300m, dups.Sum(d => d.Valor));

        // excluir a conta deixa a forma sem conta; excluir a forma deixa a nota sem forma
        Assert.True((await svc.ExcluirContaAsync(empresa, conta.Valor, adm, ct)).Sucesso);
        Assert.Null((await svc.FormasAsync(empresa, adm, ct))[0].ContaBancariaId);
        Assert.True((await svc.ExcluirFormaAsync(empresa, lida.Id, adm, ct)).Sucesso);
        Assert.Null((await fiscal.Digitacao.ObterAsync(empresa, id.Valor, adm, ct)).Valor.FormaPagamentoId);
    }

    [Fact]
    public async Task Digitacao_com_parcelas_valida_a_soma_e_gera_a_vista_na_nota_nova()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync(CnpjsDeTeste.Valido(702));
        var adm = await db.AdminAsync();
        var fiscal = db.App.Fiscal();
        var part = await db.EscalarAsync<long>("INSERT INTO core.participante (empresa_id, codigo, nome, cpf_cnpj, uf) VALUES (@e, '10', 'Fornecedor Beta', @d, 'SP') RETURNING id",
            ("e", empresa), ("d", XmlsDeTeste.CnpjFornecedor));

        DocumentoDigitado Nota(string numero, IReadOnlyList<ParcelaDuplicata>? parcelas, long id = 0)
        {
            var itens = new[] { Item("1102", 300m) };
            var t = CalculadoraNota.Totalizar(itens, new TotaisDocumento(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0, 0, 0, 0, 0));
            return new DocumentoDigitado(id, TipoOperacao.Entrada, ModeloDocumento.NFe55, "1", numero, null, Mar, Mar, "Compra", part, null, t, itens, Parcelas: parcelas);
        }

        // soma diferente do valor da nota
        var ruim = await fiscal.Digitacao.GravarAsync(empresa, Nota("700", GeradorParcelas.Gerar(200m, 2, Mar.AddDays(30), 30)), adm, ct);
        Assert.Contains("difere do valor da nota", ruim.Erros[0]);
        var antes = await fiscal.Digitacao.GravarAsync(empresa, Nota("700", [new ParcelaDuplicata("001", Mar.AddDays(-1), 300m)]), adm, ct);
        Assert.Contains("anterior à emissão", antes.Erros[0]);

        // três parcelas iguais a cada 30 dias
        var id = (await fiscal.Digitacao.GravarAsync(empresa, Nota("701", GeradorParcelas.Gerar(300m, 3, Mar.AddDays(30), 30)), adm, ct)).Valor;
        var lista = (await fiscal.Duplicatas.ConsultarAsync(empresa, new FiltroDuplicatas(DocumentoId: id), adm, ct)).Valor;
        Assert.Equal([100m, 100m, 100m], lista.Select(d => d.Valor));
        Assert.Equal([Mar.AddDays(30), Mar.AddDays(60), Mar.AddDays(90)], lista.Select(d => d.Vencimento));
        Assert.All(lista, d => Assert.Equal((TipoDuplicata.Pagar, part), (d.Tipo, d.ParticipanteId)));

        // alterar as parcelas substitui as duplicatas; null não mexe
        Assert.True((await fiscal.Digitacao.GravarAsync(empresa, Nota("701", [new ParcelaDuplicata("001", Mar.AddDays(10), 120m), new ParcelaDuplicata("002", Mar.AddDays(40), 180m)], id), adm, ct)).Sucesso);
        Assert.Equal([120m, 180m], (await fiscal.Duplicatas.ConsultarAsync(empresa, new FiltroDuplicatas(DocumentoId: id), adm, ct)).Valor.Select(d => d.Valor));
        Assert.True((await fiscal.Digitacao.GravarAsync(empresa, Nota("701", null, id), adm, ct)).Sucesso);
        Assert.Equal(2, (await fiscal.Duplicatas.ConsultarAsync(empresa, new FiltroDuplicatas(DocumentoId: id), adm, ct)).Valor.Count);

        // nota nova sem parcelas: uma duplicata à vista
        var id2 = (await fiscal.Digitacao.GravarAsync(empresa, Nota("702", null), adm, ct)).Valor;
        var aVista = Assert.Single((await fiscal.Duplicatas.ConsultarAsync(empresa, new FiltroDuplicatas(DocumentoId: id2), adm, ct)).Valor);
        Assert.Equal((Mar, 300m), (aVista.Vencimento, aVista.Valor));

        // com baixa, as parcelas só podem ser repetidas como estão
        Assert.True((await fiscal.Duplicatas.GravarConfiguracaoAsync(empresa, Contas(), adm, ct)).Sucesso);
        Assert.True((await fiscal.Duplicatas.BaixarAsync(empresa, new NovaBaixa(aVista.Id, Mar, 300m, 0, 0, 0, "1.1.01.002"), adm, ct)).Sucesso);
        var troca = await fiscal.Digitacao.GravarAsync(empresa, Nota("702", [new ParcelaDuplicata("001", Mar.AddDays(5), 300m)], id2), adm, ct);
        Assert.Contains("duplicata baixada", troca.Erros[0]);
        Assert.True((await fiscal.Digitacao.GravarAsync(empresa, Nota("702", [new ParcelaDuplicata("001", Mar, 300m)], id2), adm, ct)).Sucesso);
    }

    [Fact]
    public async Task Baixa_parcial_e_total_geram_lancamentos_e_o_estorno_os_desfaz()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var cnpj = CnpjsDeTeste.Valido(703);
        var empresa = await db.CriarEmpresaAsync(cnpj);
        var adm = await db.AdminAsync();
        var fiscal = db.App.Fiscal();
        var dup = fiscal.Duplicatas;
        await fiscal.Documentos.ImportarAsync(empresa,
        [
            Arq("compra.xml", XmlsDeTeste.Nfe(XmlsDeTeste.CnpjFornecedor, cnpj, 1, 800, Mar, [new ItemNfe("1102", 1000m)], nomeEmit: "Fornecedor Alfa",
                cobranca: [("001", new DateOnly(2026, 4, 10), 1000m)])),
            Arq("venda.xml", XmlsDeTeste.Nfe(cnpj, XmlsDeTeste.CnpjCliente, 1, 801, Mar, [new ItemNfe("5102", 500m)], cobranca: [("001", new DateOnly(2026, 4, 15), 500m)])),
        ], adm, ct: ct);
        var pagar = (await dup.ConsultarAsync(empresa, new FiltroDuplicatas(TipoDuplicata.Pagar), adm, ct)).Valor.Single();
        var receber = (await dup.ConsultarAsync(empresa, new FiltroDuplicatas(TipoDuplicata.Receber), adm, ct)).Valor.Single();
        var banco = "1.1.01.002";
        var abril = new DateOnly(2026, 4, 12);

        // sem as contas dos Parâmetros não há baixa
        var semConta = await dup.BaixarAsync(empresa, new NovaBaixa(pagar.Id, abril, 400m, 0, 0, 0, banco), adm, ct);
        Assert.Contains("Parâmetros do Fiscal", semConta.Erros[0]);
        Assert.True((await dup.GravarConfiguracaoAsync(empresa, Contas(), adm, ct)).Sucesso);

        // validações
        Assert.Contains("maior que o saldo", (await dup.BaixarAsync(empresa, new NovaBaixa(pagar.Id, abril, 1500m, 0, 0, 0, banco), adm, ct)).Erros[0]);
        Assert.Contains("maior que zero", (await dup.BaixarAsync(empresa, new NovaBaixa(pagar.Id, abril, 0m, 0, 0, 0, banco), adm, ct)).Erros[0]);
        Assert.Contains("conta do banco", (await dup.BaixarAsync(empresa, new NovaBaixa(pagar.Id, abril, 100m, 0, 0, 0, ""), adm, ct)).Erros[0]);
        Assert.Contains("não encontrada", string.Join(";", (await dup.BaixarAsync(empresa, new NovaBaixa(pagar.Id, abril, 100m, 0, 0, 0, "9.9.9"), adm, ct)).Erros));

        // pagamento parcial: 400 de principal + 20 de juros + 10 de multa - 5 de desconto = 425 do banco
        var b1 = (await dup.BaixarAsync(empresa, new NovaBaixa(pagar.Id, abril, 400m, 20m, 10m, 5m, banco), adm, ct));
        Assert.True(b1.Sucesso, string.Join(";", b1.Erros));
        var d1 = (await dup.ConsultarAsync(empresa, new FiltroDuplicatas(TipoDuplicata.Pagar), adm, ct)).Valor.Single();
        Assert.Equal((SituacaoDuplicata.Parcial, 600m, 20m, 10m, 5m), (d1.Situacao, d1.Saldo, d1.Juros, d1.Multa, d1.Desconto));
        var baixa1 = (await dup.BaixasAsync(empresa, [pagar.Id], adm, ct)).Valor.Single();
        Assert.Equal(425m, baixa1.Liquido);
        var lanc = baixa1.LancamentoId!.Value;
        Assert.Equal(abril, await db.EscalarAsync<DateOnly>("SELECT data_lancamento FROM contabil.lancamento WHERE id = @l", ("l", lanc)));
        Assert.Equal("800/001", await db.EscalarAsync<string>("SELECT max(num_arquivo) FROM contabil.partida WHERE lancamento_id = @l", ("l", lanc)));
        Assert.Equal("Pagamento NF 800/001 - Fornecedor Alfa", await db.EscalarAsync<string>("SELECT max(historico) FROM contabil.partida WHERE lancamento_id = @l", ("l", lanc)));
        Assert.Equal(3L, await db.EscalarAsync<long>("SELECT count(*) FROM contabil.partida WHERE lancamento_id = @l AND ind_dc = 'D'", ("l", lanc)));   // fornecedor, juros e multa
        Assert.Equal(400m, await SaldoAsync(empresa, "2.1.01.001"));     // fornecedor debitado pelo principal
        Assert.Equal(30m, await SaldoAsync(empresa, "4.3.01.001"));      // juros + multa (despesa)
        Assert.Equal(-5m, await SaldoAsync(empresa, "3.3.01.003"));      // desconto obtido (receita)
        Assert.Equal(-425m, await SaldoAsync(empresa, banco));

        // baixa do saldo: liquidada; nova baixa é recusada
        Assert.True((await dup.BaixarAsync(empresa, new NovaBaixa(pagar.Id, abril.AddDays(5), 600m, 0, 0, 0, banco), adm, ct)).Sucesso);
        var liquidada = (await dup.ConsultarAsync(empresa, new FiltroDuplicatas(TipoDuplicata.Pagar), adm, ct)).Valor.Single();
        Assert.Equal((SituacaoDuplicata.Liquidada, 0m), (liquidada.Situacao, liquidada.Saldo));
        Assert.Contains("liquidada", (await dup.BaixarAsync(empresa, new NovaBaixa(pagar.Id, abril, 1m, 0, 0, 0, banco), adm, ct)).Erros[0]);

        // recebimento em lote: débito no banco, crédito no cliente
        var lote = (await dup.BaixarLoteAsync(empresa, [receber.Id, 999999], abril, banco, adm, ct)).Valor;
        Assert.Equal((1, 500m, 1), (lote.Baixadas, lote.Valor, lote.Falhas.Count));
        Assert.Equal(-500m, await SaldoAsync(empresa, "1.1.02.001"));
        Assert.Equal(-425m - 600m + 500m, await SaldoAsync(empresa, banco));
        Assert.Equal("Recebimento NF 801/001 - Cliente Teste Ltda", await db.EscalarAsync<string>(
            "SELECT max(p.historico) FROM contabil.partida p JOIN fiscal.duplicata_baixa b ON b.lancamento_id = p.lancamento_id WHERE b.duplicata_id = @d", ("d", receber.Id)));

        // estorno da baixa parcial: lançamento estornado e valor devolvido ao saldo
        var baixas = (await dup.BaixasAsync(empresa, [pagar.Id], adm, ct)).Valor;
        Assert.True((await dup.EstornarBaixaAsync(empresa, baixas[0].Id, null, adm, ct)).Sucesso);
        Assert.Equal("ESTORNADO", await db.EscalarAsync<string>("SELECT status::text FROM contabil.lancamento WHERE id = @l", ("l", baixas[0].LancamentoId!.Value)));
        var d2 = (await dup.ConsultarAsync(empresa, new FiltroDuplicatas(TipoDuplicata.Pagar), adm, ct)).Valor.Single();
        Assert.Equal((SituacaoDuplicata.Parcial, 400m, 0m), (d2.Situacao, d2.Saldo, d2.Juros));
        Assert.Equal(-100m, await SaldoAsync(empresa, banco));   // só sobram o pagamento de 600 e o recebimento de 500
        Assert.Contains("não encontrada", (await dup.EstornarBaixaAsync(empresa, baixas[0].Id, null, adm, ct)).Erros[0]);
    }

    [Fact]
    public async Task Nota_cancelada_cancela_duplicatas_abertas_e_nota_com_baixa_nao_e_excluida()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var cnpj = CnpjsDeTeste.Valido(704);
        var empresa = await db.CriarEmpresaAsync(cnpj);
        var adm = await db.AdminAsync();
        var fiscal = db.App.Fiscal();
        var dup = fiscal.Duplicatas;
        Assert.True((await dup.GravarConfiguracaoAsync(empresa, Contas(), adm, ct)).Sucesso);
        var chaveA = XmlsDeTeste.Chave(901);
        await fiscal.Documentos.ImportarAsync(empresa,
        [
            Arq("a.xml", XmlsDeTeste.Nfe(XmlsDeTeste.CnpjFornecedor, cnpj, 1, 901, Mar, [new ItemNfe("1102", 200m)], chave: chaveA, cobranca: [("001", Mar.AddDays(30), 100m), ("002", Mar.AddDays(60), 100m)])),
            Arq("b.xml", XmlsDeTeste.Nfe(XmlsDeTeste.CnpjFornecedor, cnpj, 1, 902, Mar, [new ItemNfe("1102", 300m)], cobranca: [("001", Mar.AddDays(30), 300m)])),
        ], adm, ct: ct);
        var todas = (await dup.ConsultarAsync(empresa, new FiltroDuplicatas(), adm, ct)).Valor;
        var b = todas.Single(d => d.NumeroDocumento == "902");
        Assert.True((await dup.BaixarAsync(empresa, new NovaBaixa(b.Id, Mar.AddDays(30), 300m, 0, 0, 0, "1.1.01.002"), adm, ct)).Sucesso);

        // cancelamento da nota A (evento) cancela as duplicatas abertas
        var canc = await fiscal.Documentos.ImportarAsync(empresa, [Arq("ca.xml", XmlsDeTeste.CancelamentoNfe(chaveA))], adm, ct: ct);
        Assert.Equal(1, canc.Valor.Cancelamentos);
        todas = (await dup.ConsultarAsync(empresa, new FiltroDuplicatas(), adm, ct)).Valor;
        Assert.All(todas.Where(d => d.NumeroDocumento == "901"), d => Assert.Equal(SituacaoDuplicata.Cancelada, d.Situacao));
        Assert.Equal(SituacaoDuplicata.Liquidada, todas.Single(d => d.NumeroDocumento == "902").Situacao);
        Assert.Empty((await dup.ConsultarAsync(empresa, new FiltroDuplicatas(SomenteAbertas: true), adm, ct)).Valor);
        Assert.Contains("já está cancelada", (await dup.CancelarAsync(empresa, todas.First(d => d.NumeroDocumento == "901").Id, adm, ct)).Erros[0]);
        Assert.Contains("já está liquidada", (await dup.BaixarAsync(empresa, new NovaBaixa(b.Id, Mar, 1m, 0, 0, 0, "1.1.01.002"), adm, ct)).Erros[0]);

        // excluir a nota B (duplicata baixada) é bloqueado; estornando a baixa, a exclusão libera e as duplicatas somem
        var docB = (await fiscal.Documentos.ConsultarAsync(empresa, new FiltroDocumentos(Texto: "902"), adm, ct)).Valor.Single(d => d.Numero == "902");
        var recusa = await fiscal.Contabilizacao.ExcluirDocumentosAsync(empresa, [docB.Id], false, adm, ct);
        Assert.Contains("duplicata baixada", recusa.Erros[0]);
        var recusa2 = await fiscal.Documentos.ExcluirAsync(empresa, [docB.Id], adm, ct);
        Assert.Contains("duplicata baixada", recusa2.Erros[0]);
        var baixa = (await dup.BaixasAsync(empresa, [b.Id], adm, ct)).Valor.Single();
        Assert.True((await dup.EstornarBaixaAsync(empresa, baixa.Id, null, adm, ct)).Sucesso);
        Assert.True((await fiscal.Documentos.ExcluirAsync(empresa, [docB.Id], adm, ct)).Sucesso);
        Assert.DoesNotContain((await dup.ConsultarAsync(empresa, new FiltroDuplicatas(), adm, ct)).Valor, d => d.NumeroDocumento == "902");
    }

    [Fact]
    public async Task Manual_incluir_alterar_cancelar_excluir_e_relatorios_com_aging()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync(CnpjsDeTeste.Valido(705));
        var adm = await db.AdminAsync();
        var dup = db.App.Fiscal().Duplicatas;
        var forn = await db.EscalarAsync<long>("INSERT INTO core.participante (empresa_id, codigo, nome, cpf_cnpj, uf) VALUES (@e, '1', 'Fornecedor Gama', '45997418000153', 'SP') RETURNING id", ("e", empresa));
        var cli = await db.EscalarAsync<long>("INSERT INTO core.participante (empresa_id, codigo, nome, cpf_cnpj, uf) VALUES (@e, '2', 'Cliente Delta', '11222333000181', 'SP') RETURNING id", ("e", empresa));
        var hoje = new DateOnly(2026, 6, 30);

        DadosDuplicata D(TipoDuplicata t, long p, string doc, DateOnly venc, decimal valor, long id = 0) => new(id, t, p, doc, "001", venc.AddDays(-40), venc, valor, null);

        // validações
        Assert.Contains("número do documento", (await dup.GravarAsync(empresa, D(TipoDuplicata.Pagar, forn, "", hoje, 10m), adm, ct)).Erros[0]);
        Assert.Contains("maior que zero", (await dup.GravarAsync(empresa, D(TipoDuplicata.Pagar, forn, "1", hoje, 0m), adm, ct)).Erros[0]);
        Assert.Contains("fornecedor", (await dup.GravarAsync(empresa, D(TipoDuplicata.Pagar, 0, "1", hoje, 10m), adm, ct)).Erros[0]);
        Assert.Contains("anterior à emissão", (await dup.GravarAsync(empresa, D(TipoDuplicata.Pagar, forn, "1", hoje, 10m) with { Vencimento = hoje.AddDays(-90) }, adm, ct)).Erros[0]);

        var ids = new List<long>();
        foreach (var (doc, dias, valor) in new[] { ("1", 5, 100m), ("2", -10, 200m), ("3", -45, 300m), ("4", -75, 400m), ("5", -120, 500m) })
            ids.Add((await dup.GravarAsync(empresa, D(TipoDuplicata.Pagar, forn, doc, hoje.AddDays(dias), valor), adm, ct)).Valor);
        var receber = (await dup.GravarAsync(empresa, D(TipoDuplicata.Receber, cli, "9", hoje.AddDays(-3), 50m), adm, ct)).Valor;

        // alteração, cancelamento e exclusão
        Assert.True((await dup.GravarAsync(empresa, D(TipoDuplicata.Pagar, forn, "1", hoje.AddDays(5), 120m, ids[0]) with { Observacao = "negociada" }, adm, ct)).Sucesso);
        var alterada = (await dup.ConsultarAsync(empresa, new FiltroDuplicatas(), adm, ct)).Valor.Single(d => d.Id == ids[0]);
        Assert.Equal((120m, "negociada"), (alterada.Valor, alterada.Observacao));
        var extra = (await dup.GravarAsync(empresa, D(TipoDuplicata.Pagar, forn, "6", hoje, 77m), adm, ct)).Valor;
        Assert.True((await dup.CancelarAsync(empresa, extra, adm, ct)).Sucesso);
        Assert.True((await dup.ExcluirAsync(empresa, extra, adm, ct)).Sucesso);
        Assert.Contains("não encontrada", (await dup.CancelarAsync(empresa, extra, adm, ct)).Erros[0]);

        // filtros
        Assert.Equal(5, (await dup.ConsultarAsync(empresa, new FiltroDuplicatas(TipoDuplicata.Pagar), adm, ct)).Valor.Count);
        Assert.Equal(4, (await dup.ConsultarAsync(empresa, new FiltroDuplicatas(TipoDuplicata.Pagar, SomenteVencidas: true, Hoje: hoje), adm, ct)).Valor.Count);
        Assert.Equal(receber, Assert.Single((await dup.ConsultarAsync(empresa, new FiltroDuplicatas(Participante: "Delta"), adm, ct)).Valor).Id);
        Assert.Equal(2, (await dup.ConsultarAsync(empresa, new FiltroDuplicatas(TipoDuplicata.Pagar, hoje.AddDays(-50), hoje.AddDays(-5)), adm, ct)).Valor.Count);

        // baixa parcial e aging sobre o saldo
        Assert.True((await dup.GravarConfiguracaoAsync(empresa, Contas(), adm, ct)).Sucesso);
        Assert.True((await dup.BaixarAsync(empresa, new NovaBaixa(ids[1], hoje, 50m, 0, 0, 0, "1.1.01.002"), adm, ct)).Sucesso);
        Assert.Contains("tem baixa", (await dup.GravarAsync(empresa, D(TipoDuplicata.Pagar, forn, "2", hoje.AddDays(-10), 999m, ids[1]), adm, ct)).Erros[0]);
        Assert.Contains("baixas", (await dup.CancelarAsync(empresa, ids[1], adm, ct)).Erros[0]);
        Assert.Contains("baixas", (await dup.ExcluirAsync(empresa, ids[1], adm, ct)).Erros[0]);

        var pagarLista = (await dup.RelatorioAsync(empresa, new FiltroDuplicatas(TipoDuplicata.Pagar, SomenteAbertas: true), adm, ct)).Valor;
        var aging = Aging.Posicao(pagarLista, hoje);
        var linha = Assert.Single(aging);
        Assert.Equal([120m, 150m, 300m, 400m, 500m], linha.Faixas);
        var tabela = TabelasFiscais.Duplicatas(TipoDuplicata.Pagar, pagarLista, hoje);
        Assert.Equal("Duplicatas a pagar", tabela.Titulo);
        Assert.Equal(pagarLista.Sum(d => d.Saldo), tabela.Linhas[^1].Valores[7]);
        Assert.Equal("Duplicatas a receber", TabelasFiscais.Duplicatas(TipoDuplicata.Receber, [], hoje).Titulo);

        // relatório de pagamentos: uma linha por baixa e o total do valor pago
        var baixas = (await dup.BaixasAsync(empresa, pagarLista.Select(d => d.Id).ToList(), adm, ct)).Valor;
        var porId = (await dup.ConsultarAsync(empresa, new FiltroDuplicatas(TipoDuplicata.Pagar), adm, ct)).Valor.ToDictionary(d => d.Id);
        var pagos = TabelasFiscais.Baixas(baixas.Where(b => porId.ContainsKey(b.DuplicataId)).Select(b => (porId[b.DuplicataId], b)).ToList(), TipoDuplicata.Pagar);
        Assert.Equal("Relatório de pagamentos", pagos.Titulo);
        Assert.Equal(baixas.Where(b => porId.ContainsKey(b.DuplicataId)).Sum(b => b.Liquido), pagos.Linhas[^1].Valores[7]);
        Assert.All(pagos.Linhas, l => Assert.Equal(pagos.Colunas.Count, l.Valores.Count));
    }

    [Fact]
    public async Task Contabilizacao_da_nota_usa_a_conta_de_fornecedores_dos_parametros()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var cnpj = CnpjsDeTeste.Valido(706);
        var empresa = await db.CriarEmpresaAsync(cnpj);
        var adm = await db.AdminAsync();
        var fiscal = db.App.Fiscal();
        Assert.True((await fiscal.Duplicatas.GravarConfiguracaoAsync(empresa, Contas(), adm, ct)).Sucesso);
        Assert.True((await fiscal.Contabilizacao.GravarRegraAsync(empresa, new RegraContabilizacao(0, 100, null, "1102", null, null, "1.1.04.001", "2.1.01.001", "NF {DOCUMENTO}"), adm, ct)).Sucesso);
        await fiscal.Documentos.ImportarAsync(empresa,
            [Arq("c.xml", XmlsDeTeste.Nfe(XmlsDeTeste.CnpjFornecedor, cnpj, 1, 950, Mar, [new ItemNfe("1102", 700m)], cobranca: [("001", Mar.AddDays(30), 700m)]))], adm, ct: ct);
        // a regra aponta para estoque x fornecedores; muda-se a regra para outra conta de crédito e a nota segue a conta dos Parâmetros
        var regra = (await fiscal.Contabilizacao.ListarRegrasAsync(empresa, adm, ct)).Valor.Single();
        Assert.True((await fiscal.Contabilizacao.GravarRegraAsync(empresa, regra with { ContaCredito = "1.1.01.001" }, adm, ct)).Sucesso);
        var r = (await fiscal.Contabilizacao.ContabilizarAsync(empresa, Mar, Mar, null, null, adm, ct)).Valor;
        Assert.Equal(1, r.Contabilizados);
        Assert.Equal(-700m, await SaldoAsync(empresa, "2.1.01.001"));
        Assert.Equal(0m, await SaldoAsync(empresa, "1.1.01.001"));
        // com a participante controlado na partida (código do participante da nota)
        Assert.Equal(1L, await db.EscalarAsync<long>("""
            SELECT count(*) FROM contabil.partida p JOIN contabil.plano_contas c ON c.id = p.conta_id
             WHERE c.empresa_id = @e AND c.codigo = '2.1.01.001' AND p.participante_id IS NOT NULL
            """, ("e", empresa)));
    }

    [Fact]
    public async Task Conciliacao_sugere_e_baixa_pelo_extrato_com_estorno()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var cnpj = CnpjsDeTeste.Valido(707);
        var empresa = await db.CriarEmpresaAsync(cnpj);
        var adm = await db.AdminAsync();
        var fiscal = db.App.Fiscal();
        var dup = fiscal.Duplicatas;
        Assert.True((await dup.GravarConfiguracaoAsync(empresa, Contas(), adm, ct)).Sucesso);
        var venc1 = new DateOnly(2026, 4, 10);
        var venc2 = new DateOnly(2026, 5, 10);
        await fiscal.Documentos.ImportarAsync(empresa,
            [Arq("c.xml", XmlsDeTeste.Nfe(XmlsDeTeste.CnpjFornecedor, cnpj, 1, 500, Mar, [new ItemNfe("1102", 1000m)], nomeEmit: "Fornecedor Alfa",
                cobranca: [("001", venc1, 400m), ("002", venc2, 600m)]))], adm, ct: ct);
        var parcelas = (await dup.ConsultarAsync(empresa, new FiltroDuplicatas(TipoDuplicata.Pagar), adm, ct)).Valor;

        // extrato OFX já importado no contábil: um lançamento por movimento (banco x contrapartida)
        var seq = 0;
        LancamentoProposto Extrato(DateOnly data, decimal valor, string contrapartida, bool saida = true)
        {
            var banco = "1.1.01.002";
            var (deb, cred) = saida ? (contrapartida, banco) : (banco, contrapartida);
            return new LancamentoProposto(empresa, data, OrigemLancamento.Extrato,
                [new PartidaProposta(deb, IndicadorDC.D, valor, "Movimento bancário"), new PartidaProposta(cred, IndicadorDC.C, valor, "Movimento bancário")]);
        }
        LancamentoProposto[] movs =
        [
            Extrato(venc1.AddDays(1), 400m, "2.1.01.001"),     // parcela 1: contrapartida já é a conta de fornecedores
            Extrato(venc2.AddDays(2), 600m, "1.1.01.001"),     // parcela 2: contrapartida transitória
            Extrato(venc2, 600m, "1.1.01.001", saida: false),  // entrada de mesmo valor: não casa com conta a pagar
            Extrato(new DateOnly(2026, 8, 30), 400m, "1.1.01.001"),   // fora da tolerância de datas
        ];
        var gravados = await db.App.Lancamentos.EfetivarLoteAsync(movs, movs.Select(_ => (string?)$"OFX:001/1234/99/FIT{++seq}").ToList(), adm, ct);
        Assert.True(gravados.Sucesso, string.Join(";", gravados.Erros));

        var sug = await dup.SugerirConciliacaoAsync(empresa, "1.1.01.002", new DateOnly(2026, 3, 1), new DateOnly(2026, 12, 31), 5, adm, ct);
        Assert.True(sug.Sucesso, string.Join(";", sug.Erros));
        Assert.Equal(2, sug.Valor.Count);
        var s1 = sug.Valor.Single(x => x.Duplicata.Id == parcelas[0].Id);
        var s2 = sug.Valor.Single(x => x.Duplicata.Id == parcelas[1].Id);
        Assert.Equal((400m, venc1.AddDays(1)), (s1.Movimento.Valor, s1.Movimento.Data));
        Assert.Equal((600m, venc2.AddDays(2)), (s2.Movimento.Valor, s2.Movimento.Data));
        Assert.Contains("data a 1 dia", s1.Motivo);
        Assert.Empty((await dup.SugerirConciliacaoAsync(empresa, "1.1.01.002", new DateOnly(2026, 3, 1), new DateOnly(2026, 12, 31), 0, adm, ct)).Valor);   // tolerância zero: nada
        Assert.Contains("Informe a conta", (await dup.SugerirConciliacaoAsync(empresa, "", Mar, Mar, 5, adm, ct)).Erros[0]);

        // valor diferente do saldo é recusado
        var diferente = await dup.ConciliarComExtratoAsync(empresa, parcelas[0].Id, s2.Movimento.PartidaBancoId, adm, ct);
        Assert.Contains("diferente do saldo", string.Join(";", diferente.Erros));

        // parcela 1: o lançamento do extrato já baixa o fornecedor → baixa sem lançamento próprio
        var b1 = await dup.ConciliarComExtratoAsync(empresa, parcelas[0].Id, s1.Movimento.PartidaBancoId, adm, ct);
        Assert.True(b1.Sucesso, string.Join(";", b1.Erros));
        var baixa1 = (await dup.BaixasAsync(empresa, [parcelas[0].Id], adm, ct)).Valor.Single();
        Assert.Equal((null, s1.Movimento.PartidaBancoId, venc1.AddDays(1)), (baixa1.LancamentoId, baixa1.PartidaExtratoId, baixa1.Data));
        Assert.Equal(400m, await SaldoAsync(empresa, "2.1.01.001"));

        // parcela 2: baixa lança contra a conta transitória e as duas partidas ficam conciliadas
        var b2 = await dup.ConciliarComExtratoAsync(empresa, parcelas[1].Id, s2.Movimento.PartidaBancoId, adm, ct);
        Assert.True(b2.Sucesso, string.Join(";", b2.Erros));
        var baixa2 = (await dup.BaixasAsync(empresa, [parcelas[1].Id], adm, ct)).Valor.Single();
        Assert.NotNull(baixa2.LancamentoId);
        Assert.NotNull(baixa2.ConciliacaoId);
        Assert.Equal(2L, await db.EscalarAsync<long>("SELECT count(*) FROM contabil.conciliacao_partida WHERE conciliacao_id = @c", ("c", baixa2.ConciliacaoId!.Value)));
        Assert.Equal(1000m, await SaldoAsync(empresa, "2.1.01.001"));   // as duas parcelas debitaram o fornecedor
        Assert.All((await dup.ConsultarAsync(empresa, new FiltroDuplicatas(TipoDuplicata.Pagar), adm, ct)).Valor, d => Assert.Equal(SituacaoDuplicata.Liquidada, d.Situacao));

        // os movimentos usados não são sugeridos de novo; o movimento já usado é recusado
        Assert.Empty((await dup.SugerirConciliacaoAsync(empresa, "1.1.01.002", new DateOnly(2026, 3, 1), new DateOnly(2026, 12, 31), 5, adm, ct)).Valor);
        Assert.Contains("não encontrado", (await dup.ConciliarComExtratoAsync(empresa, parcelas[1].Id, s2.Movimento.PartidaBancoId, adm, ct)).Erros[0]);

        // estorno da baixa conciliada: desfaz a conciliação, estorna o lançamento e libera o movimento para nova sugestão
        Assert.True((await dup.EstornarBaixaAsync(empresa, baixa2.Id, null, adm, ct)).Sucesso);
        Assert.Equal(0L, await db.EscalarAsync<long>("SELECT count(*) FROM contabil.conciliacao WHERE id = @c", ("c", baixa2.ConciliacaoId!.Value)));
        Assert.Equal(SituacaoDuplicata.Aberta, (await dup.ConsultarAsync(empresa, new FiltroDuplicatas(DocumentoId: parcelas[1].DocumentoId), adm, ct)).Valor.Single(d => d.Id == parcelas[1].Id).Situacao);
        Assert.Single((await dup.SugerirConciliacaoAsync(empresa, "1.1.01.002", new DateOnly(2026, 3, 1), new DateOnly(2026, 12, 31), 5, adm, ct)).Valor);
    }

    [Fact]
    public async Task Permissoes_manter_e_baixar_por_perfil()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync(CnpjsDeTeste.Valido(708));
        var adm = await db.AdminAsync();
        var sessaoAdm = new SessaoUsuario(adm, "a", "A", true, false);
        var forn = await db.EscalarAsync<long>("INSERT INTO core.participante (empresa_id, codigo, nome) VALUES (@e, '1', 'Forn') RETURNING id", ("e", empresa));
        var dup = db.App.Fiscal().Duplicatas;
        var id = (await dup.GravarAsync(empresa, new DadosDuplicata(0, TipoDuplicata.Pagar, forn, "1", "001", Mar, Mar.AddDays(30), 10m, null), adm, ct)).Valor;

        async Task<long> Usuario(string login, string perfil)
        {
            var perfilId = await db.PerfilIdAsync(perfil);
            var uid = (await db.App.Usuarios.CriarUsuarioAsync(sessaoAdm, login, login, "Inicial123", perfilId, ct)).Valor;
            await db.App.Usuarios.DefinirAcessoAsync(sessaoAdm, uid, empresa, perfilId, ct);
            return uid;
        }
        var consulta = await Usuario("dup_consulta", "Consulta");
        var assistente = await Usuario("dup_assistente", "Assistente");
        var contador = await Usuario("dup_contador", "Contador");

        Assert.Single((await dup.ConsultarAsync(empresa, new FiltroDuplicatas(), consulta, ct)).Valor);
        Assert.Contains("não permite", (await dup.GravarAsync(empresa, new DadosDuplicata(0, TipoDuplicata.Pagar, forn, "2", "001", Mar, Mar, 10m, null), consulta, ct)).Erros[0]);
        Assert.Contains("não permite", (await dup.BaixarAsync(empresa, new NovaBaixa(id, Mar, 10m, 0, 0, 0, "1.1.01.002"), consulta, ct)).Erros[0]);
        Assert.Contains("não permite", (await dup.GravarConfiguracaoAsync(empresa, Contas(), consulta, ct)).Erros[0]);

        // assistente mantém, mas não baixa nem concilia
        Assert.True((await dup.GravarAsync(empresa, new DadosDuplicata(0, TipoDuplicata.Pagar, forn, "3", "001", Mar, Mar, 10m, null), assistente, ct)).Sucesso);
        Assert.Contains("não permite", (await dup.BaixarAsync(empresa, new NovaBaixa(id, Mar, 10m, 0, 0, 0, "1.1.01.002"), assistente, ct)).Erros[0]);
        Assert.Contains("não permite", (await dup.BaixarLoteAsync(empresa, [id], Mar, "1.1.01.002", assistente, ct)).Erros[0]);
        Assert.Contains("não permite", (await dup.SugerirConciliacaoAsync(empresa, "1.1.01.002", Mar, Mar, 5, assistente, ct)).Erros[0]);
        Assert.Contains("não permite", (await dup.EstornarBaixaAsync(empresa, 1, null, assistente, ct)).Erros[0]);

        // contador faz tudo
        Assert.True((await dup.GravarConfiguracaoAsync(empresa, Contas(), contador, ct)).Sucesso);
        Assert.True((await dup.BaixarAsync(empresa, new NovaBaixa(id, Mar, 10m, 0, 0, 0, "1.1.01.002"), contador, ct)).Sucesso);
        Assert.Equal(2L, await db.EscalarAsync<long>("SELECT count(*) FROM core.perfil_permissao pp JOIN core.perfil p ON p.id = pp.perfil_id WHERE pp.permissao LIKE 'FISCAL_DUPLICATAS_%' AND p.nome = 'Contador'"));
        Assert.Equal(1L, await db.EscalarAsync<long>("SELECT count(*) FROM core.perfil_permissao pp JOIN core.perfil p ON p.id = pp.perfil_id WHERE pp.permissao LIKE 'FISCAL_DUPLICATAS_%' AND p.nome = 'Assistente'"));
    }

    [Fact]
    public async Task Telas_de_duplicatas_consultam_baixam_estornam_e_geram_relatorios()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var cnpj = CnpjsDeTeste.Valido(709);
        var empresa = await db.CriarEmpresaAsync(cnpj);
        var adm = await db.AdminAsync();
        var fiscal = db.App.Fiscal();
        Assert.True((await fiscal.Duplicatas.GravarConfiguracaoAsync(empresa, Contas(), adm, ct)).Sucesso);
        await fiscal.Documentos.ImportarAsync(empresa,
        [
            Arq("c1.xml", XmlsDeTeste.Nfe(XmlsDeTeste.CnpjFornecedor, cnpj, 1, 960, Mar, [new ItemNfe("1102", 100m)], nomeEmit: "Fornecedor Alfa", cobranca: [("001", Mar.AddDays(30), 100m)])),
            Arq("c2.xml", XmlsDeTeste.Nfe(XmlsDeTeste.CnpjFornecedor, cnpj, 1, 961, Mar, [new ItemNfe("1102", 50m)], nomeEmit: "Fornecedor Alfa", cobranca: [("001", Mar.AddDays(40), 50m)])),
        ], adm, ct: ct);
        var ctx = new ContextoDeTeste(db.App, adm, empresa);

        var vm = new Vorticis.Fiscal.Desktop.ViewModels.DuplicatasViewModel(ctx);
        await vm.CarregarAsync();
        Assert.Equal(2, vm.Lista.Count);
        Assert.Contains("a pagar 150,00", vm.Resumo);

        // baixa em lote das marcadas
        vm.BaixaConta = "1.1.01.002";
        vm.BaixaData = new DateTime(2026, 4, 1);
        vm.Lista[1].Marcada = true;
        await vm.BaixarMarcadasCommand.ExecuteAsync(null);
        Assert.Empty(vm.Erros);
        Assert.Contains("1 duplicata(s) baixada(s)", vm.Mensagem);
        Assert.Single(vm.Lista);   // a liquidada sai da lista "em aberto"

        // baixa parcial da selecionada, com juros
        vm.Selecionada = vm.Lista[0];
        Assert.Equal("100,00", vm.BaixaPrincipal);
        vm.BaixaPrincipal = "60,00";
        vm.BaixaJuros = "1,50";
        await vm.BaixarCommand.ExecuteAsync(null);
        Assert.Empty(vm.Erros);
        Assert.Equal("Parcial", vm.Lista.Single().Situacao);

        // estorno
        for (var i = 0; i < 100 && vm.Baixas.Count == 0; i++) await Task.Delay(50, ct);
        vm.BaixaSelecionada = vm.Baixas.Single();
        await vm.EstornarBaixaCommand.ExecuteAsync(null);
        Assert.Empty(vm.Erros);
        Assert.Equal("Aberta", vm.Lista.Single().Situacao);

        // relatórios: a pagar e posição por vencimento
        var pagar = new Vorticis.Fiscal.Desktop.ViewModels.RelDuplicatasPagarViewModel(ctx);
        await pagar.GerarCommand.ExecuteAsync(null);
        Assert.Empty(pagar.Erros);
        Assert.Equal("Duplicatas a pagar", pagar.Exibidas.Single().Titulo);
        Assert.True(pagar.TemDados);
        var posicao = new Vorticis.Fiscal.Desktop.ViewModels.RelPosicaoDuplicatasViewModel(ctx);
        await posicao.GerarCommand.ExecuteAsync(null);
        Assert.Empty(posicao.Erros);
        Assert.Contains("Posição por vencimento", posicao.Exibidas.Single().Titulo);

        // conciliação: sem extrato importado, nenhuma sugestão
        var conc = new Vorticis.Fiscal.Desktop.ViewModels.ConciliacaoDuplicatasViewModel(ctx) { ContaBanco = "1.1.01.002", Inicio = new DateTime(2026, 1, 1), Fim = new DateTime(2026, 12, 31) };
        await conc.SugerirCommand.ExecuteAsync(null);
        Assert.Empty(conc.Erros);
        Assert.Empty(conc.Sugestoes);
        Assert.Contains("Nenhuma sugestão", conc.Resumo);
    }
}
