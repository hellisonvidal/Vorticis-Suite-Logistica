using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Erp.Desktop.ViewModels;

namespace Vorticis.Erp.Desktop;

public sealed class ModuloCompras : IModuloDesktop
{
    public string Nome => "Compras";
    public string NomeCurto => "Compras";
    public string NomeExecutavel => "compras";
    public string Subtitulo => "Aquisição, Financeiro, Estoque e BI";
    public bool GerenciaSistema => false;
    public IBrush PincelDestaque => new SolidColorBrush(Color.Parse("#DC2626"));
    public IBrush PincelBarra => new SolidColorBrush(Color.Parse("#B91C1C"));

    public IReadOnlyList<ItemMenu> Menus =>
    [
        new ItemMenu("Aquisição", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Pedidos de Compra", ItemMenuTipo.Tela, ErpTelas.PedidosCompra),
            new ItemMenu("Cotações de Compra", ItemMenuTipo.Tela, ErpTelas.CotacoesCompra),
            new ItemMenu("Aprovação de Pedidos", ItemMenuTipo.Tela, ErpTelas.AprovacaoPedidos),
            new ItemMenu("Conferência de Entrada", ItemMenuTipo.Tela, ErpTelas.ConferenciaEntrada),
            new ItemMenu("Condições Comerciais", ItemMenuTipo.Tela, ErpTelas.CondicoesComerciais),
            new ItemMenu("Histórico Compras Fornecedor", ItemMenuTipo.Tela, ErpTelas.HistoricoComprasFornecedor),
        ]),
        new ItemMenu("Financeiro", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Programação de Pagamentos", ItemMenuTipo.Tela, ErpTelas.ProgramacaoPagamentos),
            new ItemMenu("Fluxo de Caixa Previsto", ItemMenuTipo.Tela, ErpTelas.FluxoCaixaPrevisto),
            new ItemMenu("Retenções de Impostos", ItemMenuTipo.Tela, ErpTelas.RetencoesImpostos),
            new ItemMenu("Conciliação de Recebimentos", ItemMenuTipo.Tela, ErpTelas.ConciliacaoRecebimentos),
            new ItemMenu("Cobrança", ItemMenuTipo.Tela, ErpTelas.Cobranca),
            new ItemMenu("Fluxo de Caixa Realizado", ItemMenuTipo.Tela, ErpTelas.FluxoCaixaRealizado),
        ]),
        new ItemMenu("Estoque", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Estoque", ItemMenuTipo.Tela, ErpTelas.Estoque),
            new ItemMenu("Inventário", ItemMenuTipo.Tela, ErpTelas.Inventario),
            new ItemMenu("Necessidade de Compras", ItemMenuTipo.Tela, ErpTelas.NecessidadeCompras),
            new ItemMenu("Curva ABC", ItemMenuTipo.Tela, ErpTelas.CurvaAbc),
            new ItemMenu("Giro de Estoque", ItemMenuTipo.Tela, ErpTelas.GiroEstoque),
        ]),
        new ItemMenu("BI Compras", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Análise de Fornecedores", ItemMenuTipo.Tela, ErpTelas.AnaliseFornecedores),
            new ItemMenu("Compras por Produto", ItemMenuTipo.Tela, ErpTelas.ComprasPorProduto),
            new ItemMenu("Compras por Período", ItemMenuTipo.Tela, ErpTelas.ComprasPorPeriodo),
            new ItemMenu("Saveiro Compras", ItemMenuTipo.Tela, ErpTelas.SaveiroCompras),
            new ItemMenu("Dashboard Compras", ItemMenuTipo.Tela, ErpTelas.DashboardCompras),
        ]),
        new ItemMenu("Monitor Notas Fiscais (A1/A3)", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Monitor de Notas", ItemMenuTipo.Tela, ErpTelas.MonitorNotasFiscais),
            new ItemMenu("Certificados A1", ItemMenuTipo.Tela, "Compras.CertificadosA1"),
            new ItemMenu("Certificados A3", ItemMenuTipo.Tela, "Compras.CertificadosA3"),
            new ItemMenu("Captura Automática", ItemMenuTipo.Tela, "Compras.CapturaAutomatica"),
        ]),
        new ItemMenu("Relatórios", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Resumo de Compras", ItemMenuTipo.Tela, ErpTelas.RelPagamentos),
            new ItemMenu("Posição Duplicatas Pagar", ItemMenuTipo.Tela, ErpTelas.RelPosicaoDuplicatas),
            new ItemMenu("Fluxo de Caixa", ItemMenuTipo.Tela, ErpTelas.RelFluxoCaixa),
            new ItemMenu("Posição Estoque", ItemMenuTipo.Tela, ErpTelas.RelPosicaoEstoque),
        ]),
    ];

    public IReadOnlyList<AtalhoPainel> Atalhos =>
    [
        new AtalhoPainel(ErpTelas.PedidosCompra, "Pedidos Compra", "Pedidos de compra"),
        new AtalhoPainel(ErpTelas.MonitorNotasFiscais, "Monitor Notas", "Monitor de notas fiscais A1/A3"),
        new AtalhoPainel(ErpTelas.Estoque, "Estoque", "Controle de estoque"),
        new AtalhoPainel(ErpTelas.DashboardCompras, "Dashboard", "Dashboard de compras"),
    ];

    public void RegistrarTelas(CatalogoTelas catalogo)
    {
        catalogo.Registrar(ErpTelas.PedidosCompra, () => new PedidosCompraView());
        catalogo.Registrar(ErpTelas.CotacoesCompra, () => new CotacoesCompraView());
        catalogo.Registrar(ErpTelas.AprovacaoPedidos, () => new AprovacaoPedidosView());
        catalogo.Registrar(ErpTelas.ConferenciaEntrada, () => new ConferenciaEntradaView());
        catalogo.Registrar(ErpTelas.CondicoesComerciais, () => new CondicoesComerciaisView());
        catalogo.Registrar(ErpTelas.HistoricoComprasFornecedor, () => new HistoricoComprasFornecedorView());
        catalogo.Registrar(ErpTelas.ProgramacaoPagamentos, () => new ProgramacaoPagamentosView());
        catalogo.Registrar(ErpTelas.FluxoCaixaPrevisto, () => new FluxoCaixaPrevistoView());
        catalogo.Registrar(ErpTelas.RetencoesImpostos, () => new RetencoesImpostosView());
        catalogo.Registrar(ErpTelas.ConciliacaoRecebimentos, () => new ConciliacaoRecebimentosView());
        catalogo.Registrar(ErpTelas.Cobranca, () => new CobrancaView());
        catalogo.Registrar(ErpTelas.FluxoCaixaRealizado, () => new FluxoCaixaRealizadoView());
        catalogo.Registrar(ErpTelas.Estoque, () => new EstoqueView());
        catalogo.Registrar(ErpTelas.Inventario, () => new InventarioView());
        catalogo.Registrar(ErpTelas.NecessidadeCompras, () => new NecessidadeComprasView());
        catalogo.Registrar(ErpTelas.CurvaAbc, () => new CurvaAbcView());
        catalogo.Registrar(ErpTelas.GiroEstoque, () => new GiroEstoqueView());
        catalogo.Registrar(ErpTelas.AnaliseFornecedores, () => new AnaliseFornecedoresView());
        catalogo.Registrar(ErpTelas.ComprasPorProduto, () => new ComprasPorProdutoView());
        catalogo.Registrar(ErpTelas.ComprasPorPeriodo, () => new ComprasPorPeriodoView());
        catalogo.Registrar(ErpTelas.SaveiroCompras, () => new SaveiroComprasView());
        catalogo.Registrar(ErpTelas.DashboardCompras, () => new DashboardComprasView());
        catalogo.Registrar(ErpTelas.MonitorNotasFiscais, () => new MonitorNotasFiscaisView());
        catalogo.Registrar("Compras.CertificadosA1", () => new CertificadosA1View());
        catalogo.Registrar("Compras.CertificadosA3", () => new CertificadosA3View());
        catalogo.Registrar("Compras.CapturaAutomatica", () => new CapturaAutomaticaView());
    }
}