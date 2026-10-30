using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Contabil.Desktop.ViewModels;

namespace Vorticis.Contabil.Desktop;

public sealed class ModuloContabil : IModuloDesktop
{
    public string Nome => "Contábil";
    public string NomeCurto => "Contábil";
    public string NomeExecutavel => "contabil";
    public string Subtitulo => "Plano de Contas, Lançamentos e Relatórios";
    public bool GerenciaSistema => false;
    public IBrush PincelDestaque => new SolidColorBrush(Color.Parse("#1E40AF"));
    public IBrush PincelBarra => new SolidColorBrush(Color.Parse("#1E3A8A"));

    public IReadOnlyList<ItemMenu> Menus =>
    [
        new ItemMenu("Plano de Contas", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Plano de Contas", ItemMenuTipo.Tela, "Contabil.PlanoContas"),
            new ItemMenu("Importar Referencial", ItemMenuTipo.Tela, "Contabil.ImportarReferencial"),
        ]),
        new ItemMenu("Lançamentos", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Lançamentos Contábeis", ItemMenuTipo.Tela, "Contabil.Lancamentos"),
            new ItemMenu("Partida Simples/Dobrada", ItemMenuTipo.Tela, "Contabil.TipoPartida"),
        ]),
        new ItemMenu("Relatórios", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Balanço Patrimonial", ItemMenuTipo.Tela, "Contabil.BalancoPatrimonial"),
            new ItemMenu("Balanço Comparativo", ItemMenuTipo.Tela, "Contabil.BalancoComparativo"),
            new ItemMenu("DRE", ItemMenuTipo.Tela, "Contabil.DRE"),
            new ItemMenu("DRE Comparativo", ItemMenuTipo.Tela, "Contabil.DreComparativo"),
            new ItemMenu("Balancete", ItemMenuTipo.Tela, "Contabil.Balancete"),
            new ItemMenu("Balancete Comparativo", ItemMenuTipo.Tela, "Contabil.BalanceteComparativo"),
            new ItemMenu("Razão", ItemMenuTipo.Tela, "Contabil.Razao"),
            new ItemMenu("Razão Comparativo", ItemMenuTipo.Tela, "Contabil.RazaoComparativo"),
            new ItemMenu("Livro Diário", ItemMenuTipo.Tela, "Contabil.LivroDiario"),
            new ItemMenu("Livro Diário Comparativo", ItemMenuTipo.Tela, "Contabil.LivroDiarioComparativo"),
            new ItemMenu("Livro Caixa", ItemMenuTipo.Tela, "Contabil.LivroCaixa"),
            new ItemMenu("Livro Caixa Comparativo", ItemMenuTipo.Tela, "Contabil.LivroCaixaComparativo"),
            new ItemMenu("Fluxo de Caixa", ItemMenuTipo.Tela, "Contabil.FluxoCaixa"),
            new ItemMenu("Fluxo de Caixa Comparativo", ItemMenuTipo.Tela, "Contabil.FluxoCaixaComparativo"),
        ]),
    ];

    public IReadOnlyList<AtalhoPainel> Atalhos =>
    [
        new AtalhoPainel("Contabil.PlanoContas", "Plano de Contas", "Cadastro do plano de contas"),
        new AtalhoPainel("Contabil.Lancamentos", "Lançamentos", "Lançamentos contábeis"),
        new AtalhoPainel("Contabil.BalancoPatrimonial", "Balanço", "Balanço patrimonial"),
        new AtalhoPainel("Contabil.DRE", "DRE", "Demonstração de resultados"),
    ];

    public void RegistrarTelas(CatalogoTelas catalogo)
    {
        catalogo.Registrar("Contabil.PlanoContas", () => new PlanoContasView());
        catalogo.Registrar("Contabil.ImportarReferencial", () => new ImportarReferencialView());
        catalogo.Registrar("Contabil.Lancamentos", () => new LancamentosView());
        catalogo.Registrar("Contabil.TipoPartida", () => new TipoPartidaView());
        catalogo.Registrar("Contabil.BalancoPatrimonial", () => new BalancoPatrimonialView());
        catalogo.Registrar("Contabil.BalancoComparativo", () => new BalancoComparativoView());
        catalogo.Registrar("Contabil.DRE", () => new DREView());
        catalogo.Registrar("Contabil.DreComparativo", () => new DreComparativoView());
        catalogo.Registrar("Contabil.Balancete", () => new BalanceteView());
        catalogo.Registrar("Contabil.BalanceteComparativo", () => new BalanceteComparativoView());
        catalogo.Registrar("Contabil.Razao", () => new RazaoView());
        catalogo.Registrar("Contabil.RazaoComparativo", () => new RazaoComparativoView());
        catalogo.Registrar("Contabil.LivroDiario", () => new LivroDiarioView());
        catalogo.Registrar("Contabil.LivroDiarioComparativo", () => new LivroDiarioComparativoView());
        catalogo.Registrar("Contabil.LivroCaixa", () => new LivroCaixaView());
        catalogo.Registrar("Contabil.LivroCaixaComparativo", () => new LivroCaixaComparativoView());
        catalogo.Registrar("Contabil.FluxoCaixa", () => new FluxoCaixaView());
        catalogo.Registrar("Contabil.FluxoCaixaComparativo", () => new FluxoCaixaComparativoView());
    }
}