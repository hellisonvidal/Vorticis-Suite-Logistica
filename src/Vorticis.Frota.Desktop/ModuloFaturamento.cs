using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Faturamento.Desktop.ViewModels;

namespace Vorticis.Frota.Desktop;

public sealed class ModuloFaturamento : IModuloDesktop
{
    public string Nome => "Faturamento";
    public string NomeCurto => "Faturamento";
    public string NomeExecutavel => "faturamento";
    public string Subtitulo => "Faturamento TMS + Rotas e Planejamento";
    public bool GerenciaSistema => false;
    public IBrush PincelDestaque => new SolidColorBrush(Color.Parse("#7C3AED"));
    public IBrush PincelBarra => new SolidColorBrush(Color.Parse("#6D28D9"));

    public IReadOnlyList<ItemMenu> Menus =>
    [
        new ItemMenu("Faturamento TMS", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Cotações", ItemMenuTipo.Tela, FrotaTelas.Cotacoes),
            new ItemMenu("CT-es", ItemMenuTipo.Tela, FrotaTelas.Ctes),
            new ItemMenu("Manifestos", ItemMenuTipo.Tela, FrotaTelas.Manifestos),
            new ItemMenu("Faturas de Frete", ItemMenuTipo.Tela, FrotaTelas.FaturasFrete),
        ]),
        new ItemMenu("Rotas e Planejamento", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Rotas", ItemMenuTipo.Tela, FrotaTelas.Rotas),
            new ItemMenu("Pontos de Parada", ItemMenuTipo.Tela, FrotaTelas.PontosParada),
            new ItemMenu("Planejamento de Rotas", ItemMenuTipo.Tela, "Faturamento.PlanejamentoRotas"),
            new ItemMenu("Otimização de Cargas", ItemMenuTipo.Tela, "Faturamento.OtimizacaoCargas"),
        ]),
        new ItemMenu("Relatórios", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Tabelas de Frete", ItemMenuTipo.Tela, FrotaTelas.RelTabelasFrete),
            new ItemMenu("CT-es", ItemMenuTipo.Tela, FrotaTelas.RelCtes),
            new ItemMenu("Manifestos", ItemMenuTipo.Tela, FrotaTelas.RelManifestos),
            new ItemMenu("Prazos de Entrega", ItemMenuTipo.Tela, FrotaTelas.RelPrazosEntrega),
            new ItemMenu("Faturas de Frete", ItemMenuTipo.Tela, FrotaTelas.RelFaturasFrete),
            new ItemMenu("Coletas", ItemMenuTipo.Tela, FrotaTelas.RelColetas),
            new ItemMenu("Diárias", ItemMenuTipo.Tela, FrotaTelas.RelDiarias),
            new ItemMenu("Resumo Operacional", ItemMenuTipo.Tela, FrotaTelas.RelResumoOperacional),
        ]),
    ];

    public IReadOnlyList<AtalhoPainel> Atalhos =>
    [
        new AtalhoPainel(FrotaTelas.Cotacoes, "Cotações", "Cotações de frete"),
        new AtalhoPainel(FrotaTelas.Ctes, "CT-es", "Emissão de CT-es"),
        new AtalhoPainel(FrotaTelas.Rotas, "Rotas", "Planejamento de rotas"),
        new AtalhoPainel(FrotaTelas.FaturasFrete, "Faturas", "Faturas de frete"),
    ];

    public void RegistrarTelas(CatalogoTelas catalogo)
    {
        catalogo.Registrar(FrotaTelas.Cotacoes, () => new CotacoesView());
        catalogo.Registrar(FrotaTelas.Ctes, () => new CtesView());
        catalogo.Registrar(FrotaTelas.Manifestos, () => new ManifestosView());
        catalogo.Registrar(FrotaTelas.FaturasFrete, () => new FaturasFreteView());
        catalogo.Registrar(FrotaTelas.Rotas, () => new RotasView());
        catalogo.Registrar(FrotaTelas.PontosParada, () => new PontosParadaView());
        catalogo.Registrar("Faturamento.PlanejamentoRotas", () => new PlanejamentoRotasView());
        catalogo.Registrar("Faturamento.OtimizacaoCargas", () => new OtimizacaoCargasView());
    }
}