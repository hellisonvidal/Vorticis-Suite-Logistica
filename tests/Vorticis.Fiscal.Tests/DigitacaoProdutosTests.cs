using Vorticis.Application.Relatorios;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Contabilizacao;
using Vorticis.Fiscal.Documentos;
using Vorticis.Fiscal.Produtos;
using Vorticis.Fiscal.Relatorios;
using Vorticis.Infra.Relatorios;

namespace Vorticis.Fiscal.Tests;

public class DigitacaoCalculosTests
{
    private static ItemFiscal Item(decimal qtd, decimal unit, decimal desc = 0, decimal ipi = 0, decimal st = 0, decimal icms = 0) =>
        new(1, null, "X", null, null, "5102", "UN", qtd, unit, 0, desc, 0, 0, 0, 0, null, null, 0, 0, icms, 0, st, null, 0, 0, ipi, null, 0, 0, 0, null, 0, 0, 0, 0, 0, 0);

    [Fact]
    public void Item_calcula_valor_do_produto_e_valor_contabil()
    {
        var i = CalculadoraNota.Completar(Item(3, 10.333m, desc: 1m, ipi: 2m, st: 3m));
        Assert.Equal(31.00m, i.ValorProduto);   // 3 x 10,333 = 30,999 -> 31,00
        Assert.Equal(31.00m - 1m + 2m + 3m, i.ValorContabil);
    }

    [Fact]
    public void Totais_somam_os_itens_e_a_conferencia_aponta_diferencas()
    {
        var zero = new TotaisDocumento(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0, 0, 0, 0, 0);
        var calc = CalculadoraNota.Totalizar([Item(2, 50m, icms: 18m), Item(1, 100m, desc: 10m, ipi: 5m)], zero);
        Assert.Equal((200m, 10m, 195m, 18m, 5m), (calc.Produtos, calc.Desconto, calc.Total, calc.Icms, calc.Ipi));
        Assert.Empty(CalculadoraNota.Conferir(calc, calc));
        var erros = CalculadoraNota.Conferir(calc with { Total = 190m }, calc);
        Assert.Contains("Valor total da nota", Assert.Single(erros));
    }

    [Fact]
    public void Gtin_e_chave_validam_o_digito_verificador()
    {
        Assert.True(ValidadorGtin.Valido("7891000100103"));
        Assert.False(ValidadorGtin.Valido("7891000100104"));
        Assert.False(ValidadorGtin.Valido("SEM GTIN"));
        Assert.Null(ValidadorGtin.Normalizar("SEM GTIN"));
        Assert.Equal("7891000100103", ValidadorGtin.Normalizar("7891000100103"));
        Assert.True(CalculadoraNota.ChaveNfeValida(ChaveValida("352603616952270001935500100000000011000000" + "0")));
        Assert.False(CalculadoraNota.ChaveNfeValida(new string('1', 44)));
    }

    /// <summary>Completa os 43 primeiros dígitos com o DV módulo 11 (implementação independente da do código).</summary>
    internal static string ChaveValida(string quarentaETres)
    {
        var base43 = quarentaETres[..43];
        int soma = 0, peso = 2;
        foreach (var c in base43.Reverse()) { soma += (c - '0') * peso; peso = peso == 9 ? 2 : peso + 1; }
        var resto = soma % 11;
        return base43 + (resto < 2 ? 0 : 11 - resto);
    }

    [Fact]
    public void Validacao_do_produto_normaliza_e_recusa_dados_invalidos()
    {
        var ok = ServicoProdutos.Validar(new Produto(0, 0, " Parafuso ", "un", "7318.15.00", "", "7891000100103", "0", "00", "1102", "5102", "000", "102", 18, 0, 1.65m, 7.6m));
        Assert.True(ok.Sucesso, string.Join(";", ok.Erros));
        Assert.Equal(("Parafuso", "UN", "73181500"), (ok.Valor.Descricao, ok.Valor.Unidade, ok.Valor.Ncm));
        var ruim = ServicoProdutos.Validar(new Produto(0, 0, "", "UN", "123", "12", "123", "9", "XX", "5102", "1102", "1", null, 101, 0, 0, 0));
        Assert.True(ruim.Erros.Count >= 9, string.Join(";", ruim.Erros));
    }

    [Fact]
    public void Motor_leva_o_centro_de_custo_ao_debito_na_entrada_e_ao_credito_na_saida()
    {
        var regras = new[] { new RegraContabilizacao(1, 100, null, "1102", null, null, "1.1.04.001", "2.1.01.001", "h"), new RegraContabilizacao(2, 100, null, "5102", null, null, "1.1.02.001", "3.1.01.001", "h") };
        var zero = new TotaisDocumento(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0, 0, 0, 0, 0);
        DocumentoFiscal Doc(TipoOperacao op) => new(1, ModeloDocumento.NFe55, "", op, "9", "1", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1), null, SituacaoDocumento.Autorizada,
            new ParteDocumento("1", "A", "SP"), new ParteDocumento("2", "B", "SP"), null, null, zero, null, false, "DIGITADO", 5, "CC.DOC");
        ItemFiscal I(string cfop, decimal v, string? cc) => Item(1, v) with { Cfop = cfop, ValorContabil = v, CentroCustoCodigo = cc };

        var entrada = MotorContabilizacao.Montar(1, Doc(TipoOperacao.Entrada), [I("1102", 100m, "CC.A"), I("1102", 50m, null)], regras, usaCentroCusto: true).Lancamento!;
        Assert.Equal(4, entrada.Partidas.Count);   // um par por centro (CC.A e o do documento)
        Assert.Equal(["CC.A", null, "CC.DOC", null], entrada.Partidas.Select(p => p.CodigoCentroCusto));
        var saida = MotorContabilizacao.Montar(1, Doc(TipoOperacao.Saida), [I("5102", 80m, null)], regras, usaCentroCusto: true).Lancamento!;
        Assert.Equal([null, "CC.DOC"], saida.Partidas.Select(p => p.CodigoCentroCusto));
        var semConfig = MotorContabilizacao.Montar(1, Doc(TipoOperacao.Saida), [I("5102", 80m, "CC.A")], regras).Lancamento!;
        Assert.All(semConfig.Partidas, p => Assert.Null(p.CodigoCentroCusto));
    }
}

public class DigitacaoProdutosIntegracaoTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static readonly DateOnly Mar = new(2026, 3, 10);

    private static ItemFiscal Item(string cfop, decimal qtd, decimal unit, long? produto = null, string? descricao = "Parafuso", decimal icms = 0, decimal bc = 0, long? cc = null) =>
        new(0, null, descricao, null, null, cfop, "UN", qtd, unit, 0, 0, 0, 0, 0, 0, "00", "0", bc, bc > 0 ? 18m : 0, icms, 0, 0, null, 0, 0, 0, null, 0, 0, 0, null, 0, 0, 0, 0, 0, 0,
             ProdutoId: produto, CentroCustoId: cc);

    private static DocumentoDigitado Nota(TipoOperacao op, long participante, IReadOnlyList<ItemFiscal> itens, string numero = "1001", long id = 0, long? cc = null,
                                          Func<TotaisDocumento, TotaisDocumento>? ajusteTotais = null)
    {
        var t = CalculadoraNota.Totalizar(itens, new TotaisDocumento(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0, 0, 0, 0, 0));
        return new DocumentoDigitado(id, op, ModeloDocumento.NFe55, "1", numero, null, Mar, Mar, "Venda", participante, cc, ajusteTotais?.Invoke(t) ?? t, itens);
    }

    private async Task<long> ParticipanteAsync(long empresa, string codigo, string cnpj, string nome) =>
        await db.EscalarAsync<long>("INSERT INTO core.participante (empresa_id, codigo, nome, cpf_cnpj, uf, cliente, fornecedor) VALUES (@e, @c, @n, @d, 'SP', FALSE, FALSE) RETURNING id",
            ("e", empresa), ("c", codigo), ("n", nome), ("d", cnpj));

    [Fact]
    public async Task Produtos_cadastro_codigo_sequencial_gtin_unico_e_exclusao()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync("33000167000101");
        var adm = await db.AdminAsync();
        var prod = db.App.Fiscal().Produtos;

        var invalido = await prod.GravarAsync(empresa, new Produto(0, 0, "", "UN", null, null, null, "0", "00", null, null, null, null, 0, 0, 0, 0), adm, ct);
        Assert.False(invalido.Sucesso);
        var id1 = (await prod.GravarAsync(empresa, new Produto(0, 0, "Parafuso", "UN", "73181500", null, "7891000100103", "0", "00", "1102", "5102", "000", "102", 18, 0, 1.65m, 7.6m), adm, ct)).Valor;
        var id2 = (await prod.GravarAsync(empresa, new Produto(0, 0, "Porca", "UN", null, null, null, "1", "01", null, null, null, null, 0, 0, 0, 0), adm, ct)).Valor;
        var lista = (await prod.ListarAsync(empresa, false, null, adm, ct)).Valor;
        Assert.Equal([1, 2], lista.Select(p => p.Codigo));
        var repetido = await prod.GravarAsync(empresa, new Produto(0, 0, "Outro", "UN", null, null, "7891000100103", "0", "00", null, null, null, null, 0, 0, 0, 0), adm, ct);
        Assert.Contains("já está cadastrado", repetido.Erros[0]);

        // alteração mantém o código; inativar tira da lista de ativos
        Assert.True((await prod.GravarAsync(empresa, lista[1] with { Descricao = "Porca sextavada", Ativo = false }, adm, ct)).Sucesso);
        var ativos = (await prod.ListarAsync(empresa, true, null, adm, ct)).Valor;
        Assert.Equal(["Parafuso"], ativos.Select(p => p.Descricao));
        Assert.Single((await prod.ListarAsync(empresa, false, "sextavada", adm, ct)).Valor);

        // produto usado em nota não pode ser excluído; sem uso, pode
        var part = await ParticipanteAsync(empresa, "10", XmlsDeTeste.CnpjFornecedor, "Fornecedor Alfa");
        Assert.True((await db.App.Fiscal().Digitacao.GravarAsync(empresa, Nota(TipoOperacao.Entrada, part, [Item("1102", 1, 10m, id1)]), adm, ct)).Sucesso);
        Assert.Contains("inative-o", (await prod.ExcluirAsync(empresa, id1, adm, ct)).Erros[0]);
        Assert.True((await prod.ExcluirAsync(empresa, id2, adm, ct)).Sucesso);

        // permissões: consulta lê mas não grava
        var sessaoAdm = new SessaoUsuario(adm, "a", "A", true, false);
        var consulta = await db.PerfilIdAsync("Consulta");
        var uid = (await db.App.Usuarios.CriarUsuarioAsync(sessaoAdm, "fiscal_prod_leitor", "Leitor", "Inicial123", consulta, ct)).Valor;
        await db.App.Usuarios.DefinirAcessoAsync(sessaoAdm, uid, empresa, consulta, ct);
        Assert.True((await prod.ListarAsync(empresa, false, null, uid, ct)).Sucesso);
        Assert.Contains("não permite", (await prod.GravarAsync(empresa, lista[0], uid, ct)).Erros[0]);
        Assert.Contains("não permite", (await db.App.Fiscal().Digitacao.GravarAsync(empresa, Nota(TipoOperacao.Entrada, part, [Item("1102", 1, 5m)], "77"), uid, ct)).Erros[0]);
    }

    [Fact]
    public async Task Digitacao_grava_confere_totais_edita_e_bloqueia_apos_contabilizar()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync("12345678000195");
        var adm = await db.AdminAsync();
        var dig = db.App.Fiscal().Digitacao;
        var docs = db.App.Fiscal().Documentos;
        var cont = db.App.Fiscal().Contabilizacao;
        var forn = await ParticipanteAsync(empresa, "10", XmlsDeTeste.CnpjFornecedor, "Fornecedor Alfa");
        var cli = await ParticipanteAsync(empresa, "20", XmlsDeTeste.CnpjCliente, "Cliente Beta");
        var produto = (await db.App.Fiscal().Produtos.GravarAsync(empresa, new Produto(0, 0, "Parafuso", "UN", "73181500", null, null, "0", "00", null, null, null, null, 0, 0, 0, 0), adm, ct)).Valor;

        // validações
        Assert.Contains("CFOP 5102 não é de entrada", (await dig.GravarAsync(empresa, Nota(TipoOperacao.Entrada, forn, [Item("5102", 1, 10m)]), adm, ct)).Erros[0]);
        Assert.Contains("ao menos um item", (await dig.GravarAsync(empresa, Nota(TipoOperacao.Entrada, forn, []), adm, ct)).Erros[0]);
        Assert.Contains("quantidade", (await dig.GravarAsync(empresa, Nota(TipoOperacao.Entrada, forn, [Item("1102", 0, 10m)]), adm, ct)).Erros[0]);
        Assert.Contains("Chave de acesso inválida", (await dig.GravarAsync(empresa, Nota(TipoOperacao.Entrada, forn, [Item("1102", 1, 10m)]) with { Chave = "123" }, adm, ct)).Erros[0]);
        Assert.Contains("Centros de custo não estão habilitados", (await dig.GravarAsync(empresa, Nota(TipoOperacao.Entrada, forn, [Item("1102", 1, 10m, cc: 999)]), adm, ct)).Erros[0]);

        // totais do cabeçalho conferidos com os itens
        var divergente = await dig.GravarAsync(empresa, Nota(TipoOperacao.Entrada, forn, [Item("1102", 2, 50m, produto, icms: 18m, bc: 100m)], ajusteTotais: t => t with { Total = 99m }), adm, ct);
        Assert.Contains("Valor total da nota", divergente.Erros[0]);

        // entrada: item do cadastro de produtos, valores calculados e gravados nas tabelas comuns
        var chave = DigitacaoCalculosTests.ChaveValida(("3526" + XmlsDeTeste.CnpjFornecedor + "55001000000123100000000").PadRight(43, '0'));
        var entrada = Nota(TipoOperacao.Entrada, forn, [Item("1102", 2, 50m, produto, descricao: null, icms: 18m, bc: 100m), Item("1102", 1, 25.5m, descricao: "Arruela")]) with { Chave = chave };
        var gravada = await dig.GravarAsync(empresa, entrada, adm, ct);
        Assert.True(gravada.Sucesso, string.Join(";", gravada.Erros));
        var d = (await docs.ConsultarAsync(empresa, new FiltroDocumentos(), adm, ct)).Valor.Single();
        Assert.Equal((TipoOperacao.Entrada, 125.5m, 18m, "Fornecedor Alfa", true, chave), (d.Operacao, d.Totais.Total, d.Totais.Icms, d.NomeParticipante, d.Digitado, d.Chave));
        var itens = (await docs.ItensAsync(empresa, d.Id, adm, ct)).Valor;
        Assert.Equal(["Parafuso", "Arruela"], itens.Select(i => i.Descricao));
        Assert.Equal(("1", "73181500", produto), (itens[0].CodProduto, itens[0].Ncm, itens[0].ProdutoId));
        Assert.Equal("digitado", await db.EscalarAsync<string>("SELECT lower(origem) FROM fiscal.documento WHERE id = @d", ("d", d.Id)));
        Assert.True(await db.EscalarAsync<bool>("SELECT fornecedor FROM core.participante WHERE id = @p", ("p", forn)));

        // nota repetida (mesmo número/série/participante) e chave repetida
        Assert.Contains("Já existe a nota", (await dig.GravarAsync(empresa, Nota(TipoOperacao.Entrada, forn, [Item("1102", 1, 5m)]), adm, ct)).Erros[0]);
        // saída sem chave
        var saida = await dig.GravarAsync(empresa, Nota(TipoOperacao.Saida, cli, [Item("5102", 3, 100m, produto, icms: 54m, bc: 300m)], "2001"), adm, ct);
        Assert.True(saida.Sucesso, string.Join(";", saida.Erros));
        Assert.Equal(2, (await docs.ConsultarAsync(empresa, new FiltroDocumentos(), adm, ct)).Valor.Count);
        // XML importado não é editável como digitado
        await docs.ImportarAsync(empresa, [new ArquivoXml("x.xml", XmlsDeTeste.Bytes(XmlsDeTeste.Nfe("12345678000195", XmlsDeTeste.CnpjCliente, 1, 300, Mar, [new ItemNfe("5102", 10m)])))], adm, ct: ct);
        var xmlDoc = (await docs.ConsultarAsync(empresa, new FiltroDocumentos(Texto: "300"), adm, ct)).Valor.Single(x => x.Numero == "300");
        Assert.False(xmlDoc.Digitado);
        Assert.Contains("Somente notas digitadas", (await dig.GravarAsync(empresa, Nota(TipoOperacao.Saida, cli, [Item("5102", 1, 10m)], "300", xmlDoc.Id), adm, ct)).Erros[0]);

        // edição: troca itens e totais, preservando o id
        var editada = Nota(TipoOperacao.Entrada, forn, [Item("1102", 1, 80m)], id: d.Id) with { Chave = chave };
        Assert.Equal(d.Id, (await dig.GravarAsync(empresa, editada, adm, ct)).Valor);
        var depois = (await dig.ObterAsync(empresa, d.Id, adm, ct)).Valor;
        Assert.Equal((80m, 1, false), (depois.Totais.Total, depois.Itens.Count, depois.Contabilizado));

        // contabiliza; depois disso a edição e a exclusão são recusadas
        Assert.True((await cont.GravarRegraAsync(empresa, new RegraContabilizacao(0, 100, null, "1102", null, null, "1.1.04.001", "2.1.01.001", "Compra NF {DOCUMENTO}"), adm, ct)).Sucesso);
        Assert.True((await cont.GravarRegraAsync(empresa, new RegraContabilizacao(0, 100, null, "5102", null, null, "1.1.02.001", "3.1.01.001", "Venda NF {DOCUMENTO}"), adm, ct)).Sucesso);
        var r = (await cont.ContabilizarAsync(empresa, Mar, Mar, null, new HashSet<long> { d.Id }, adm, ct)).Valor;
        Assert.Equal((1, 80m), (r.Contabilizados, r.Valor));
        Assert.True((await dig.ObterAsync(empresa, d.Id, adm, ct)).Valor.Contabilizado);
        Assert.Contains("contabilizada", (await dig.GravarAsync(empresa, editada with { Natureza = "outra" }, adm, ct)).Erros[0]);
        Assert.False((await docs.ExcluirAsync(empresa, [d.Id], adm, ct)).Sucesso);

        // desfazendo a contabilização, volta a ser editável e excluível
        Assert.Equal(1, (await cont.DesfazerAsync(empresa, [d.Id], null, adm, ct)).Valor.Contabilizados);
        Assert.True((await dig.GravarAsync(empresa, editada with { Natureza = "outra" }, adm, ct)).Sucesso);
        Assert.Equal(1, (await docs.ExcluirAsync(empresa, [d.Id], adm, ct)).Valor);
        Assert.Equal(0L, await db.EscalarAsync<long>("SELECT count(*) FROM fiscal.documento_item WHERE documento_id = @d", ("d", d.Id)));
    }

    [Fact]
    public async Task Centro_de_custo_no_fiscal_vai_para_as_partidas_da_contabilizacao()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync("98765432000198");
        var adm = await db.AdminAsync();
        var dig = db.App.Fiscal().Digitacao;
        var forn = await ParticipanteAsync(empresa, "10", XmlsDeTeste.CnpjFornecedor, "Fornecedor Alfa");

        // a configuração exige que a empresa use centros de custo
        Assert.Contains("não usa centros de custo", (await dig.GravarConfiguracaoAsync(empresa, true, adm, ct)).Erros[0]);
        await db.EscalarAsync<long>("UPDATE core.empresa SET usa_centro_custo = TRUE WHERE id = @e RETURNING id", ("e", empresa));
        var c1 = (await db.App.PlanoContas.IncluirCentroAsync(empresa, "1", "Administrativo", null, adm, ct: ct)).Valor;
        var c2 = (await db.App.PlanoContas.IncluirCentroAsync(empresa, "2", "Produção", null, adm, ct: ct)).Valor;
        Assert.False((await dig.ObterConfiguracaoAsync(empresa, adm, ct)).Valor.CentroCustoAtivo);
        Assert.True((await dig.GravarConfiguracaoAsync(empresa, true, adm, ct)).Sucesso);
        Assert.True((await dig.ObterConfiguracaoAsync(empresa, adm, ct)).Valor.CentroCustoAtivo);

        Assert.Contains("inexistente", (await dig.GravarAsync(empresa, Nota(TipoOperacao.Entrada, forn, [Item("1102", 1, 10m)], cc: 987654), adm, ct)).Erros[0]);
        // documento no centro 1; um item no centro 2
        var nota = Nota(TipoOperacao.Entrada, forn, [Item("1102", 1, 100m), Item("1102", 1, 40m, cc: c2)], cc: c1);
        var gravada = await dig.GravarAsync(empresa, nota, adm, ct);
        Assert.True(gravada.Sucesso, string.Join(";", gravada.Erros));
        var lida = (await dig.ObterAsync(empresa, gravada.Valor, adm, ct)).Valor;
        Assert.Equal(c1, lida.CentroCustoId);
        Assert.Equal(new long?[] { null, c2 }, lida.Itens.Select(i => i.CentroCustoId).ToArray());

        var cont = db.App.Fiscal().Contabilizacao;
        Assert.True((await cont.GravarRegraAsync(empresa, new RegraContabilizacao(0, 100, null, "1102", null, null, "1.1.04.001", "2.1.01.001", "Compra NF {DOCUMENTO}"), adm, ct)).Sucesso);
        var res = (await cont.ContabilizarAsync(empresa, Mar, Mar, null, null, adm, ct)).Valor;
        Assert.Equal((1, 0), (res.Contabilizados, res.Falhas.Count));
        var porCentro = await db.EscalarAsync<string>("""
            SELECT string_agg(cc.codigo || ':' || p.ind_dc || ':' || p.valor, ',' ORDER BY cc.codigo)
              FROM contabil.partida p JOIN contabil.lancamento l ON l.id = p.lancamento_id JOIN contabil.centro_custo cc ON cc.id = p.centro_custo_id
             WHERE l.empresa_id = @e AND l.origem = 'NFE'
            """, ("e", empresa));
        Assert.Equal("1:D:100.00,2:D:40.00", porCentro);   // entrada: centro na partida de débito
        Assert.Equal(2L, await db.EscalarAsync<long>("SELECT count(*) FROM contabil.partida p JOIN contabil.lancamento l ON l.id = p.lancamento_id WHERE l.empresa_id = @e AND p.ind_dc = 'C' AND p.centro_custo_id IS NULL", ("e", empresa)));

        // desligado: o fiscal não usa mais o centro de custo
        Assert.True((await dig.GravarConfiguracaoAsync(empresa, false, adm, ct)).Sucesso);
        Assert.Contains("não estão habilitados", (await dig.GravarAsync(empresa, Nota(TipoOperacao.Entrada, forn, [Item("1102", 1, 10m)], "55", cc: c1), adm, ct)).Erros[0]);
    }

    [Fact]
    public async Task Importacao_de_xml_vincula_e_cadastra_produtos_e_relatorios_filtram_itens()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var cnpj = "55566677000183";
        var empresa = await db.CriarEmpresaAsync(cnpj);
        var adm = await db.AdminAsync();
        var docs = db.App.Fiscal().Documentos;
        var prod = db.App.Fiscal().Produtos;

        // produto já cadastrado com a mesma descrição de um item do XML: é reaproveitado (não duplica)
        var existente = (await prod.GravarAsync(empresa, new Produto(0, 0, "Produto de teste", "UN", null, null, null, "0", "00", null, null, null, null, 0, 0, 0, 0), adm, ct)).Valor;
        var r = await docs.ImportarAsync(empresa,
        [
            new ArquivoXml("a.xml", XmlsDeTeste.Bytes(XmlsDeTeste.Nfe(XmlsDeTeste.CnpjFornecedor, cnpj, 1, 10, Mar, [new ItemNfe("1102", 100m), new ItemNfe("1102", 50m, Produto: "Cabo de rede", Ncm: "85444200")]))),
            new ArquivoXml("b.xml", XmlsDeTeste.Bytes(XmlsDeTeste.Nfe(XmlsDeTeste.CnpjFornecedor, cnpj, 1, 11, Mar, [new ItemNfe("1102", 70m), new ItemNfe("1102", 20m, Produto: "Cabo de rede", Ncm: "85444200")]))),
            new ArquivoXml("c.xml", XmlsDeTeste.Bytes(XmlsDeTeste.Nfe(cnpj, XmlsDeTeste.CnpjCliente, 1, 12, Mar, [new ItemNfe("5102", 300m, Produto: "Cabo de rede", Ncm: "85444200")]))),
        ], adm, ct: ct);
        Assert.Equal(3, r.Valor.Importados);

        var produtos = (await prod.ListarAsync(empresa, false, null, adm, ct)).Valor;
        Assert.Equal(["Produto de teste", "Cabo de rede"], produtos.Select(p => p.Descricao));   // 2 produtos para 5 itens
        Assert.Equal("85444200", produtos[1].Ncm);
        Assert.Equal(existente, produtos[0].Id);
        // vínculo (emitente + cProd): P1 e P2 do fornecedor; e os do próprio emitente (empresa) na nota de saída
        Assert.Equal(3L, await db.EscalarAsync<long>("SELECT count(*) FROM fiscal.produto_vinculo WHERE empresa_id = @e", ("e", empresa)));
        Assert.Equal(5L, await db.EscalarAsync<long>("SELECT count(*) FROM fiscal.documento_item i JOIN fiscal.documento d ON d.id = i.documento_id WHERE d.empresa_id = @e AND i.produto_id IS NOT NULL", ("e", empresa)));
        // padrões do cadastro automático: CFOP de entrada do primeiro item recebido
        Assert.Equal(("1102", null), (produtos[1].CfopEntrada, produtos[1].CfopSaida));

        // relatório de itens: filtros
        var cabo = produtos[1].Id;
        var ini = new DateOnly(2026, 3, 1);
        var fim = new DateOnly(2026, 3, 31);
        async Task<IReadOnlyList<LinhaItemNota>> Itens(FiltroItensNotas f) => (await prod.RelatorioItensAsync(empresa, f, adm, ct)).Valor;
        Assert.Equal(5, (await Itens(new FiltroItensNotas(ini, fim))).Count);
        Assert.Equal(4, (await Itens(new FiltroItensNotas(ini, fim, TipoOperacao.Entrada))).Count);
        Assert.Single(await Itens(new FiltroItensNotas(ini, fim, TipoOperacao.Saida)));
        Assert.Equal(3, (await Itens(new FiltroItensNotas(ini, fim, ProdutoId: cabo))).Count);
        Assert.Equal(2, (await Itens(new FiltroItensNotas(ini, fim, Cfop: "11", Ncm: "8544"))).Count);
        Assert.Equal(4, (await Itens(new FiltroItensNotas(ini, fim, Cfop: "1"))).Count);
        Assert.Empty(await Itens(new FiltroItensNotas(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30))));
        var fornecedorId = await db.EscalarAsync<long>("SELECT id FROM core.participante WHERE empresa_id = @e AND cpf_cnpj = @d", ("e", empresa), ("d", XmlsDeTeste.CnpjFornecedor));
        Assert.Equal(4, (await Itens(new FiltroItensNotas(ini, fim, ParticipanteId: fornecedorId))).Count);
        Assert.False((await prod.RelatorioItensAsync(empresa, new FiltroItensNotas(fim, ini), adm, ct)).Sucesso);

        // tabelas exportáveis (PDF/Excel/CSV a partir da mesma tabela)
        var tItens = TabelasFiscais.ItensNotas(await Itens(new FiltroItensNotas(ini, fim)));
        var tProd = TabelasFiscais.Produtos(produtos);
        Assert.All(tItens.Linhas, l => Assert.Equal(tItens.Colunas.Count, l.Valores.Count));
        Assert.All(tProd.Linhas, l => Assert.Equal(tProd.Colunas.Count, l.Valores.Count));
        Assert.Equal(6, tItens.Linhas.Count);   // 5 itens + total
        var pdf = new GeradorPdf().Gerar(ExportacaoRelatorio.ParaPdf("Itens", ["Empresa"], [tItens, tProd], paisagem: true));
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf.Conteudo, 0, 4));
        Assert.Equal(6, ExportadorExcel.Ler(ExportadorExcel.Gerar([tItens])).Count);

        // relatórios exigem a função de relatórios
        var sessaoAdm = new SessaoUsuario(adm, "a", "A", true, false);
        var perfil = await db.PerfilIdAsync("Consulta");
        var uid = (await db.App.Usuarios.CriarUsuarioAsync(sessaoAdm, "fiscal_sem_rel", "Sem", "Inicial123", perfil, ct)).Valor;
        await db.App.Usuarios.DefinirAcessoAsync(sessaoAdm, uid, empresa, perfil, ct);
        Assert.True((await prod.RelatorioProdutosAsync(empresa, false, null, uid, ct)).Sucesso);   // o perfil Consulta tem FISCAL_RELATORIOS
        Assert.Contains("acesso", (await prod.RelatorioItensAsync(empresa, new FiltroItensNotas(ini, fim), 987654, ct)).Erros[0]);
    }
}
