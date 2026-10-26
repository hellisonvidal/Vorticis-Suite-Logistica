using Vorticis.Erp;
using Vorticis.Fiscal;
using Vorticis.Fiscal.Documentos;
using Vorticis.Fiscal.Duplicatas;
using Vorticis.Fiscal.Produtos;
using Vorticis.Infra.Tests;

namespace Vorticis.Modulos.Tests;

public class RegrasErpTests
{
    private static readonly DateOnly Hoje = new(2026, 5, 20);

    private static Duplicata Dup(long id, long? doc, DateOnly venc, decimal saldo, string cliente, SituacaoDuplicata sit = SituacaoDuplicata.Aberta, TipoDuplicata tipo = TipoDuplicata.Receber) =>
        new(id, doc, null, cliente[0], null, cliente, tipo, "1", "NF" + id, venc.AddDays(-30), venc, saldo, 0, 0, 0, saldo, sit, null, "MANUAL");

    [Fact]
    public void Monitor_de_cobrancas_separa_por_faixa_de_vencimento()
    {
        Duplicata[] dups =
        [
            Dup(1, null, Hoje.AddDays(-100), 100m, "Ana"), Dup(2, null, Hoje.AddDays(-10), 50m, "Bia"), Dup(3, null, Hoje, 30m, "Ana"),
            Dup(4, null, Hoje.AddDays(5), 20m, "Caio"), Dup(5, null, Hoje.AddDays(40), 10m, "Caio"),
            Dup(6, null, Hoje.AddDays(-5), 999m, "Paga", SituacaoDuplicata.Liquidada), Dup(7, null, Hoje.AddDays(-5), 999m, "Fornecedor", tipo: TipoDuplicata.Pagar),
        ];
        var (lista, r) = Monitores.Cobrancas(dups, Hoje);
        Assert.Equal(5, lista.Count);
        Assert.Equal((210m, 150m, 30m, 20m, 10m), (r.Aberto, r.Vencido, r.VenceHoje, r.Proximos7Dias, r.Depois));
        Assert.Equal(["Vencida há mais de 90 dias", "Vencida até 30 dias", "Vence hoje", "Vence em até 7 dias", "A vencer"], lista.Select(c => c.Faixa));
        Assert.Equal(("Ana", 100m), r.MaioresAtrasos[0]);
        Assert.Equal(3, r.Clientes);
    }

    [Fact]
    public void Monitor_de_notas_aponta_o_que_pede_atencao()
    {
        var zero = new TotaisDocumento(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0, 0, 0, 0, 0);
        var baseDoc = new DocumentoFiscal(1, ModeloDocumento.NFe55, new string('1', 44), TipoOperacao.Saida, "1", "1", Hoje, Hoje, null, SituacaoDocumento.Autorizada,
            new ParteDocumento("11222333000181", "Eu", "SP"), new ParteDocumento("12345678909", "Cliente", "SP"), 5, "7", zero with { Total = 100m }, null, true);
        DocumentoFiscal[] docs =
        [
            baseDoc,                                                                         // em ordem (contabilizada, com duplicata em dia)
            baseDoc with { Id = 2, Numero = "2", Contabilizado = false },                    // não contabilizada
            baseDoc with { Id = 3, Numero = "3", Situacao = SituacaoDocumento.Cancelada },   // cancelada
            baseDoc with { Id = 4, Numero = "4" },                                           // cobrança vencida
            baseDoc with { Id = 5, Numero = "5", Operacao = TipoOperacao.Entrada },          // sem duplicatas
        ];
        Duplicata[] dups = [Dup(1, 1, Hoje.AddDays(10), 100m, "Cliente"), Dup(2, 2, Hoje.AddDays(10), 100m, "Cliente"), Dup(4, 4, Hoje.AddDays(-3), 100m, "Cliente")];
        var (notas, r) = Monitores.Notas(docs, dups, Hoje);
        var alertas = notas.ToDictionary(n => n.Documento.Id, n => n.Alerta);
        Assert.Equal("", alertas[1]);
        Assert.Equal("Não contabilizada", alertas[2]);
        Assert.Equal("Nota cancelada", alertas[3]);
        Assert.Equal("Cobrança vencida", alertas[4]);
        Assert.Equal("Sem duplicatas", alertas[5]);
        Assert.Equal((5, 1, 4, 1, 1, 1, 1), (r.Total, r.Entradas, r.Saidas, r.Canceladas, r.NaoContabilizadas, r.SemCobranca, r.ComCobrancaVencida));
        Assert.Equal((300m, 100m), (r.ValorSaidas, r.ValorEntradas));
    }

    [Fact]
    public void Custo_medio_placa_e_validacao_do_pedido()
    {
        Assert.Equal(12.5m, RegrasErp.CustoMedio([(10m, 10m), (10m, 15m), (-5m, 0m)]));
        Assert.Equal("ABC1D23", RegrasErp.PlacaValida("abc-1d23"));
        Assert.Null(RegrasErp.PlacaValida("AB12345"));
        var p = new Pedido(0, TipoPedido.Venda, 0, new DateOnly(2026, 3, 1), 0, SituacaoPedido.Aberto, 0, 30, null, null, [new ItemPedido(1, 1, 10, 20)]);
        var erros = RegrasErp.Validar(p);
        Assert.Contains("Escolha o cliente.", erros);
        Assert.Contains("O desconto não pode passar do valor do item.", erros);
        Assert.Contains("Parcelas: de 1 a 120.", erros);
    }
}

public class ErpIntegracaoTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    [Fact]
    public async Task Compra_recebida_e_venda_faturada_viram_notas_duplicatas_e_estoque()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("45723174000110");
        var cli = await db.EscalarAsync<long>("INSERT INTO core.participante (empresa_id, codigo, nome, cpf_cnpj, cliente) VALUES (@e, 'C1', 'Cliente A', '11222333000181', TRUE) RETURNING id", ("e", empresa));
        var forn = await db.EscalarAsync<long>("INSERT INTO core.participante (empresa_id, codigo, nome, cpf_cnpj, fornecedor) VALUES (@e, 'F1', 'Fornecedor B', '45997418000153', TRUE) RETURNING id", ("e", empresa));
        var prod = await ServicosFiscais.De(db.App).Produtos.GravarAsync(empresa, new Produto(0, 0, "Caneta", "UN", "96081000", null, null, "0", "00", "1102", "5102", "000", "000", 18, 0, 0, 0), adm, ct);
        Assert.True(prod.Sucesso, string.Join("; ", prod.Erros));
        var erp = ServicosErp.De(db.App);
        var hoje = new DateOnly(2026, 3, 10);

        var compra = await erp.GravarPedidoAsync(empresa, new Pedido(0, TipoPedido.Compra, 0, hoje, forn, SituacaoPedido.Aberto, 1, 30, null, null,
                                                                     [new ItemPedido(prod.Valor, 100, 2m)]), adm, ct);
        Assert.True(compra.Sucesso, string.Join("; ", compra.Erros));
        var pc = Assert.Single(await erp.ListarPedidosAsync(empresa, TipoPedido.Compra, hoje, hoje, adm, ct));
        var nfEntrada = await erp.ConcluirPedidoAsync(empresa, pc, "777", "1", null, hoje, adm, ct);
        Assert.True(nfEntrada.Sucesso, string.Join("; ", nfEntrada.Erros));

        var venda = await erp.GravarPedidoAsync(empresa, new Pedido(0, TipoPedido.Venda, 0, hoje, cli, SituacaoPedido.Aberto, 2, 30, hoje.AddDays(30), null,
                                                                    [new ItemPedido(prod.Valor, 40, 5m, 10m)]), adm, ct);
        var pv = Assert.Single(await erp.ListarPedidosAsync(empresa, TipoPedido.Venda, hoje, hoje, adm, ct));
        Assert.Equal(190m, pv.Total);
        var nfSaida = await erp.ConcluirPedidoAsync(empresa, pv, (await erp.ProximoNumeroNotaAsync(empresa, ct)).ToString(), "1", null, hoje, adm, ct);
        Assert.True(nfSaida.Sucesso, string.Join("; ", nfSaida.Erros));

        var pos = Assert.Single(await erp.ProdutosAsync(empresa, adm, ct));
        Assert.Equal((60m, 2m), (pos.Saldo, pos.CustoMedio));
        var dups = await ServicosFiscais.De(db.App).Duplicatas.ConsultarAsync(empresa, new FiltroDuplicatas(DocumentoId: nfSaida.Valor), adm, ct);
        Assert.Equal(2, dups.Valor.Count);
        Assert.Equal(190m, dups.Valor.Sum(d => d.Valor));
        Assert.Equal(SituacaoPedido.Concluido, Assert.Single(await erp.ListarPedidosAsync(empresa, TipoPedido.Venda, hoje, hoje, adm, ct)).Situacao);
        Assert.False((await erp.CancelarPedidoAsync(empresa, pv with { Situacao = SituacaoPedido.Concluido }, adm, ct)).Sucesso);

        var painel = await erp.PainelAsync(empresa, hoje, adm, ct);
        Assert.True(painel.Sucesso, string.Join("; ", painel.Erros));
        Assert.Equal((190m, 200m, 190m), (painel.Valor.Vendas, painel.Valor.Compras, painel.Valor.ReceberAberto));

        Assert.False((await erp.GravarVeiculoAsync(empresa, new Veiculo(0, "XX-1", null, "CAMINHAO", "SP", 0, 0), adm, ct)).Sucesso);
        Assert.True((await erp.GravarVeiculoAsync(empresa, new Veiculo(0, "abc1d23", null, "CAMINHAO", "SP", 5000, 10000), adm, ct)).Sucesso);
    }
}
