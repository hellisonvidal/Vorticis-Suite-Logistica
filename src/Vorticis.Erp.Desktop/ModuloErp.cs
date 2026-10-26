using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Domain.Seguranca;
using Vorticis.Erp.Desktop.ViewModels;
using F = Vorticis.Fiscal.Desktop.ViewModels;

namespace Vorticis.Erp.Desktop;

/// <summary>
/// Módulo Gerencial (antigo ERP): rotinas da empresa. Produtos, notas fiscais, duplicatas e conciliação são as telas do Fiscal (mesmos dados); o Gerencial acrescenta
/// pedidos de venda e compra, estoque e o painel do empresário. Permissões ERP_* (e FISCAL_* nas telas do Fiscal) nos serviços.
/// </summary>
public sealed class ModuloErp : IModuloDesktop
{
    public void PrepararServicos(Vorticis.Infra.VorticisApp app) => _ = Vorticis.Fiscal.VorticisAppFiscalExtensions.Fiscal(app);

    public string Nome => "Vorticis Gerencial";
    public Vorticis.Application.Cadastros.ModulosEmpresa ModuloEmpresa => Vorticis.Application.Cadastros.ModulosEmpresa.Erp;
    public string NomeCurto => "Gerencial";
    public string Subtitulo => "Gestão empresarial: vendas, compras, estoque e financeiro";
    public string NomeExecutavel => "VorticisGerencial";
    public string CorDestaque => "#0891B2";   // ciano (ícone e selo do módulo)
    public string CorBarra => "#164E63";      // petróleo
    public bool UsaCalendarioObrigacoes => false;

    public IReadOnlyList<DefinicaoGrafico> Graficos => Vorticis.Fiscal.Desktop.GraficosFiscais.Todos;

    public IReadOnlyList<DefinicaoImportacao> Importacoes => Vorticis.Fiscal.Desktop.ImportacoesFiscais.Todas;

    public IReadOnlyList<ItemMenu> ItensCadastros { get; } =
    [
        ItemMenu.Tela("Produtos", F.Telas.Produtos),
        ItemMenu.Tela("Regras fiscais", F.Telas.RegrasFiscais),
        ItemMenu.Tela("Formas de pagamento", TelasComuns.FormasPagamento),
        ItemMenu.Tela("Contas bancárias", TelasComuns.ContasBancarias),
    ];

    public IReadOnlyList<ItemMenu> Menus { get; } =
    [
        ItemMenu.Submenu("_Faturamento",
            ItemMenu.Tela("Pedidos de venda", ErpTelas.PedidosVenda),
            ItemMenu.Tela("Notas fiscais de saída", F.Telas.NotaSaida),
            ItemMenu.EmBreve("Emissão de NF-e na SEFAZ")),
        ItemMenu.Submenu("C_ompras",
            ItemMenu.Tela("Pedidos de compra", ErpTelas.PedidosCompra),
            ItemMenu.Tela("Notas fiscais de entrada", F.Telas.NotaEntrada),
            ItemMenu.Tela("Importação de XML", F.Telas.ImportacaoXml)),
        ItemMenu.Submenu("_Estoque",
            ItemMenu.Tela("Posição e movimentos", ErpTelas.Estoque)),
        ItemMenu.Submenu("F_inanceiro",
            ItemMenu.Tela("Contas a pagar", F.Telas.ContasPagar),
            ItemMenu.Tela("Contas a receber", F.Telas.ContasReceber),
            ItemMenu.Separador,
            ItemMenu.Tela("Conciliação bancária", F.Telas.ConciliacaoDuplicatas)),
        ItemMenu.Submenu("_Monitores",
            ItemMenu.Tela("Monitor de notas fiscais", ErpTelas.MonitorNotas),
            ItemMenu.Tela("Monitor de cobranças", ErpTelas.MonitorCobrancas)),
        ItemMenu.Submenu("_Relatórios",
            ItemMenu.Tela("Painel do empresário", ErpTelas.Painel),
            ItemMenu.Separador,
            ItemMenu.Tela("Relatório de vendas", F.Telas.ResumoVendas),
            ItemMenu.Tela("Relatório de compras", F.Telas.ResumoCompras),
            ItemMenu.Tela("Relatório de recebimentos", F.Telas.RelRecebimentos),
            ItemMenu.Separador,
            ItemMenu.Submenu("_Financeiro",
                ItemMenu.Tela("Relatório de contas a pagar", F.Telas.RelDuplicatasPagar),
                ItemMenu.Tela("Relatório de contas a receber", F.Telas.RelDuplicatasReceber),
                ItemMenu.Tela("Posição por vencimento", F.Telas.RelPosicaoDuplicatas),
                ItemMenu.Tela("Relatório de pagamentos", F.Telas.RelPagamentos))),
    ];

    public IReadOnlyList<AtalhoTela> Atalhos { get; } =
    [
        new("Ctrl+P", ErpTelas.Painel, "Painel do empresário"),
        new("Ctrl+V", ErpTelas.PedidosVenda, "Pedidos de venda"),
    ];

    private static bool Pode(MainViewModelBase m, params Permissao[] p) => m.TemAcessoNaEmpresa && p.Any(m.Pode);

    public void RegistrarTelas(CatalogoTelas c)
    {
        c.Instancia("Painel", m => new PainelViewModel(m), aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(ErpTelas.Painel, "Relatórios › Painel do empresário", m => Pode(m, Permissao.ErpRelatorios), v => v.CarregarAsync());
        c.Instancia("PedidosVenda", m => new PedidosVendaViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(ErpTelas.PedidosVenda, "Faturamento › Pedidos de venda", m => Pode(m, Permissao.ErpVendas, Permissao.ErpRelatorios), v => v.CarregarAsync());
        c.Instancia("PedidosCompra", m => new PedidosCompraViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(ErpTelas.PedidosCompra, "Compras › Pedidos de compra", m => Pode(m, Permissao.ErpCompras, Permissao.ErpRelatorios), v => v.CarregarAsync());
        c.Instancia("Estoque", m => new EstoqueViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(ErpTelas.Estoque, "Estoque › Posição e movimentos", m => Pode(m, Permissao.ErpEstoque, Permissao.ErpRelatorios), v => v.CarregarAsync());

        c.Instancia("MonitorNotas", m => new MonitorNotasViewModel(m), aoTrocarEmpresa: (v, ativa) => ativa ? v.AtualizarAsync() : Task.CompletedTask)
            .Tela(ErpTelas.MonitorNotas, "Monitores › Monitor de notas fiscais", m => m.Pode(Permissao.FiscalDocumentosConsultar), v => v.AtualizarAsync());
        c.Instancia("MonitorCobrancas", m => new MonitorCobrancasViewModel(m), aoTrocarEmpresa: (v, ativa) => ativa ? v.AtualizarAsync() : Task.CompletedTask)
            .Tela(ErpTelas.MonitorCobrancas, "Monitores › Monitor de cobranças", m => m.Pode(Permissao.FiscalDocumentosConsultar), v => v.AtualizarAsync());

        // Telas do Fiscal usadas pelo ERP (mesmas chaves, mesmo manual)
        c.Instancia("ImportacaoXml", m => new F.ImportacaoXmlViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(F.Telas.ImportacaoXml, "Compras › Importação de XML", m => m.Pode(Permissao.FiscalDocumentosImportar));
        c.Instancia("NotaEntrada", m => new F.NotaEntradaViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(F.Telas.NotaEntrada, "Compras › Notas fiscais de entrada", m => m.Pode(Permissao.FiscalDocumentosConsultar), v => v.CarregarAsync());
        c.Instancia("NotaSaida", m => new F.NotaSaidaViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(F.Telas.NotaSaida, "Vendas › Notas fiscais de saída", m => m.Pode(Permissao.FiscalDocumentosConsultar), v => v.CarregarAsync());
        c.Instancia("ContasPagar", m => new F.ContasPagarViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(F.Telas.ContasPagar, "Financeiro › Contas a pagar", m => m.Pode(Permissao.FiscalDocumentosConsultar), v => v.CarregarAsync());
        c.Instancia("ContasReceber", m => new F.ContasReceberViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(F.Telas.ContasReceber, "Financeiro › Contas a receber", m => m.Pode(Permissao.FiscalDocumentosConsultar), v => v.CarregarAsync());
        c.Instancia("ConciliacaoDuplicatas", m => new F.ConciliacaoDuplicatasViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(F.Telas.ConciliacaoDuplicatas, "Financeiro › Conciliação bancária", m => m.Pode(Permissao.FiscalDuplicatasBaixar));
        c.Instancia("RegrasFiscais", m => new F.RegrasFiscaisViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(F.Telas.RegrasFiscais, "Cadastros › Regras fiscais", m => m.Pode(Permissao.FiscalDocumentosConsultar), v => v.CarregarAsync());
        c.Instancia("Produtos", m => new F.ProdutosViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(F.Telas.Produtos, "Cadastros › Produtos", m => m.Pode(Permissao.FiscalDocumentosConsultar), v => v.CarregarAsync());
        c.Instancia("RelDuplicatasPagar", m => new F.RelDuplicatasPagarViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(F.Telas.RelDuplicatasPagar, "Relatórios › Financeiro › Relatório de contas a pagar", m => m.Pode(Permissao.FiscalRelatorios));
        c.Instancia("RelDuplicatasReceber", m => new F.RelDuplicatasReceberViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(F.Telas.RelDuplicatasReceber, "Relatórios › Financeiro › Relatório de contas a receber", m => m.Pode(Permissao.FiscalRelatorios));
        c.Instancia("RelPosicaoDuplicatas", m => new F.RelPosicaoDuplicatasViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(F.Telas.RelPosicaoDuplicatas, "Relatórios › Financeiro › Posição por vencimento", m => m.Pode(Permissao.FiscalRelatorios));
        c.Instancia("RelPagamentos", m => new F.RelPagamentosViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(F.Telas.RelPagamentos, "Relatórios › Financeiro › Relatório de pagamentos", m => m.Pode(Permissao.FiscalRelatorios));
        c.Instancia("RelRecebimentos", m => new F.RelRecebimentosViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(F.Telas.RelRecebimentos, "Relatórios › Relatório de recebimentos", m => m.Pode(Permissao.FiscalRelatorios));
        c.Instancia("ResumoVendas", m => new F.ResumoVendasViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(F.Telas.ResumoVendas, "Relatórios › Relatório de vendas", m => m.Pode(Permissao.FiscalRelatorios));
        c.Instancia("ResumoCompras", m => new F.ResumoComprasViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(F.Telas.ResumoCompras, "Relatórios › Relatório de compras", m => m.Pode(Permissao.FiscalRelatorios));
        c.Instancia("ParametrosModulo", m => new F.ParametrosFiscalViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(TelasComuns.Parametros, "Cadastros › Parâmetros", m => m.TemAcessoNaEmpresa, v => v.CarregarAsync());
    }
}

public sealed class MainViewModel() : MainViewModelBase(new ModuloErp());
