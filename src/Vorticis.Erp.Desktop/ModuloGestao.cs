using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Gestao.Desktop.ViewModels;

namespace Vorticis.Erp.Desktop;

public sealed class ModuloGestao : IModuloDesktop
{
    public string Nome => "Gestão";
    public string NomeCurto => "Gestão";
    public string NomeExecutavel => "gestao";
    public string Subtitulo => "Business Intelligence e Análises Gerenciais";
    public bool GerenciaSistema => false;
    public IBrush PincelDestaque => new SolidColorBrush(Color.Parse("#EA580C"));
    public IBrush PincelBarra => new SolidColorBrush(Color.Parse("#C2410C"));

    public IReadOnlyList<ItemMenu> Menus =>
    [
        new ItemMenu("BI - Vendas", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Dashboard Vendas", ItemMenuTipo.Tela, ErpTelas.DashboardVendas),
            new ItemMenu("Vendas por Vendedor", ItemMenuTipo.Tela, ErpTelas.VendasPorVendedor),
            new ItemMenu("Vendas por Produto", ItemMenuTipo.Tela, ErpTelas.VendasPorProduto),
            new ItemMenu("Vendas por Cliente", ItemMenuTipo.Tela, ErpTelas.VendasPorCliente),
            new ItemMenu("Vendas por Região", ItemMenuTipo.Tela, ErpTelas.VendasPorRegiao),
            new ItemMenu("Ticket Médio", ItemMenuTipo.Tela, ErpTelas.TicketMedio),
            new ItemMenu("Conversão Orçamentos", ItemMenuTipo.Tela, ErpTelas.ConversaoOrcamentos),
        ]),
        new ItemMenu("BI - Financeiro", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Dashboard Financeiro", ItemMenuTipo.Tela, ErpTelas.DashboardFinanceiro),
            new ItemMenu("Recebimentos vs Previsto", ItemMenuTipo.Tela, ErpTelas.RecebimentosVsPrevisto),
            new ItemMenu("Inadimplência", ItemMenuTipo.Tela, ErpTelas.Inadimplencia),
            new ItemMenu("Fluxo de Caixa Consolidado", ItemMenuTipo.Tela, ErpTelas.FluxoCaixaConsolidado),
            new ItemMenu("DRE Gerencial", ItemMenuTipo.Tela, ErpTelas.DREGerencial),
        ]),
        new ItemMenu("BI - Estoque", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Giro de Estoque Consolidado", ItemMenuTipo.Tela, ErpTelas.GiroEstoqueConsolidado),
            new ItemMenu("Curva ABC Consolidada", ItemMenuTipo.Tela, ErpTelas.CurvaAbcConsolidada),
            new ItemMenu("Ruptura de Estoque", ItemMenuTipo.Tela, ErpTelas.RupturaEstoque),
        ]),
        new ItemMenu("BI - Fiscal", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Apuração de Impostos", ItemMenuTipo.Tela, "Gestao.ApuracaoImpostos"),
            new ItemMenu("Obrigações Acessórias", ItemMenuTipo.Tela, "Gestao.ObrigacoesAcessorias"),
        ]),
        new ItemMenu("BI - Contábil", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Balanço Patrimonial", ItemMenuTipo.Tela, "Gestao.BalancoPatrimonial"),
            new ItemMenu("DRE", ItemMenuTipo.Tela, "Gestao.DRE"),
            new ItemMenu("Fluxo de Caixa", ItemMenuTipo.Tela, "Gestao.FluxoCaixa"),
        ]),
        new ItemMenu("Relatórios Gerenciais", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Resumo Vendas", ItemMenuTipo.Tela, ErpTelas.ResumoVendas),
            new ItemMenu("Recebimentos", ItemMenuTipo.Tela, ErpTelas.RelRecebimentos),
            new ItemMenu("Duplicatas a Receber", ItemMenuTipo.Tela, ErpTelas.RelDuplicatasReceber),
            new ItemMenu("Posição Duplicatas", ItemMenuTipo.Tela, ErpTelas.RelPosicaoDuplicatas),
            new ItemMenu("Pagamentos", ItemMenuTipo.Tela, ErpTelas.RelPagamentos),
        ]),
    ];

    public IReadOnlyList<AtalhoPainel> Atalhos =>
    [
        new AtalhoPainel(ErpTelas.DashboardVendas, "Dashboard Vendas", "Visão geral de vendas"),
        new AtalhoPainel(ErpTelas.DashboardFinanceiro, "Dashboard Financeiro", "Visão financeira"),
        new AtalhoPainel(ErpTelas.DREGerencial, "DRE Gerencial", "Demonstração de resultados"),
        new AtalhoPainel(ErpTelas.GiroEstoqueConsolidado, "Giro Estoque", "Análise de giro"),
    ];

    public void RegistrarTelas(CatalogoTelas catalogo)
    {
        catalogo.Registrar(ErpTelas.DashboardVendas, () => new DashboardVendasView());
        catalogo.Registrar(ErpTelas.VendasPorVendedor, () => new VendasPorVendedorView());
        catalogo.Registrar(ErpTelas.VendasPorProduto, () => new VendasPorProdutoView());
        catalogo.Registrar(ErpTelas.VendasPorCliente, () => new VendasPorClienteView());
        catalogo.Registrar(ErpTelas.VendasPorRegiao, () => new VendasPorRegiaoView());
        catalogo.Registrar(ErpTelas.TicketMedio, () => new TicketMedioView());
        catalogo.Registrar(ErpTelas.ConversaoOrcamentos, () => new ConversaoOrcamentosView());
        catalogo.Registrar(ErpTelas.DashboardFinanceiro, () => new DashboardFinanceiroView());
        catalogo.Registrar(ErpTelas.RecebimentosVsPrevisto, () => new RecebimentosVsPrevistoView());
        catalogo.Registrar(ErpTelas.Inadimplencia, () => new InadimplenciaView());
        catalogo.Registrar(ErpTelas.FluxoCaixaConsolidado, () => new FluxoCaixaConsolidadoView());
        catalogo.Registrar(ErpTelas.DREGerencial, () => new DREGerencialView());
        catalogo.Registrar(ErpTelas.GiroEstoqueConsolidado, () => new GiroEstoqueConsolidadoView());
        catalogo.Registrar(ErpTelas.CurvaAbcConsolidada, () => new CurvaAbcConsolidadaView());
        catalogo.Registrar(ErpTelas.RupturaEstoque, () => new RupturaEstoqueView());
        catalogo.Registrar("Gestao.ApuracaoImpostos", () => new ApuracaoImpostosView());
        catalogo.Registrar("Gestao.ObrigacoesAcessorias", () => new ObrigacoesAcessoriasView());
        catalogo.Registrar("Gestao.BalancoPatrimonial", () => new BalancoPatrimonialView());
        catalogo.Registrar("Gestao.DRE", () => new DREView());
        catalogo.Registrar("Gestao.FluxoCaixa", () => new FluxoCaixaView());
    }
}