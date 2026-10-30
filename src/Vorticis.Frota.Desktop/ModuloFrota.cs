using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Frota.Desktop.ViewModels;

namespace Vorticis.Frota.Desktop;

public sealed class ModuloFrota : IModuloDesktop
{
    public string Nome => "Frota";
    public string NomeCurto => "Frota";
    public string NomeExecutavel => "frota";
    public string Subtitulo => "Gestão de Frota e Logística";
    public bool GerenciaSistema => false;
    public IBrush PincelDestaque => new SolidColorBrush(Color.Parse("#059669"));
    public IBrush PincelBarra => new SolidColorBrush(Color.Parse("#047857"));

    public IReadOnlyList<ItemMenu> Menus =>
    [
        new ItemMenu("Cadastros", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Veículos", ItemMenuTipo.Tela, FrotaTelas.Veiculos),
            new ItemMenu("Motoristas", ItemMenuTipo.Tela, FrotaTelas.Motoristas),
            new ItemMenu("Frotas", ItemMenuTipo.Tela, FrotaTelas.Frotas),
            new ItemMenu("Tabelas de Frete", ItemMenuTipo.Tela, FrotaTelas.TabelasFrete),
            new ItemMenu("Documentos da Frota", ItemMenuTipo.Tela, FrotaTelas.DocumentosFrota),
            new ItemMenu("Rotas", ItemMenuTipo.Tela, FrotaTelas.Rotas),
            new ItemMenu("Pontos de Parada", ItemMenuTipo.Tela, FrotaTelas.PontosParada),
        ]),
        new ItemMenu("Operação", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Painel", ItemMenuTipo.Tela, FrotaTelas.Painel),
            new ItemMenu("Abastecimentos", ItemMenuTipo.Tela, FrotaTelas.Abastecimentos),
            new ItemMenu("Viagens", ItemMenuTipo.Tela, FrotaTelas.Viagens),
            new ItemMenu("Diárias", ItemMenuTipo.Tela, FrotaTelas.Diarias),
            new ItemMenu("Coletas", ItemMenuTipo.Tela, FrotaTelas.Coletas),
            new ItemMenu("Entregas", ItemMenuTipo.Tela, FrotaTelas.Entregas),
            new ItemMenu("Pedágio", ItemMenuTipo.Tela, FrotaTelas.Pedagio),
            new ItemMenu("Layouts Pedágio", ItemMenuTipo.Tela, FrotaTelas.LayoutsPedagio),
        ]),
        new ItemMenu("Emissão (Faturamento TMS)", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Cotações", ItemMenuTipo.Tela, FrotaTelas.Cotacoes),
            new ItemMenu("CT-es", ItemMenuTipo.Tela, FrotaTelas.Ctes),
            new ItemMenu("Manifestos", ItemMenuTipo.Tela, FrotaTelas.Manifestos),
            new ItemMenu("Faturas de Frete", ItemMenuTipo.Tela, FrotaTelas.FaturasFrete),
        ]),
        new ItemMenu("Manutenção", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Planos de Manutenção", ItemMenuTipo.Tela, FrotaTelas.PlanosManutencao),
            new ItemMenu("Itens de Manutenção", ItemMenuTipo.Tela, FrotaTelas.ItensManutencao),
            new ItemMenu("Ordens de Serviço", ItemMenuTipo.Tela, FrotaTelas.OrdensServico),
            new ItemMenu("Autorização de Serviços", ItemMenuTipo.Tela, FrotaTelas.AutorizacaoServicos),
            new ItemMenu("Notas de Compra", ItemMenuTipo.Tela, FrotaTelas.NotasCompra),
        ]),
        new ItemMenu("Multas e Infrações", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Monitor de Multas", ItemMenuTipo.Tela, FrotaTelas.MonitorMultas),
            new ItemMenu("Infrações", ItemMenuTipo.Tela, FrotaTelas.Infracoes),
            new ItemMenu("Indicação de Condutor", ItemMenuTipo.Tela, FrotaTelas.IndicacaoCondutor),
            new ItemMenu("Recursos de Multas", ItemMenuTipo.Tela, FrotaTelas.RecursosMultas),
            new ItemMenu("Parcelamento de Multas", ItemMenuTipo.Tela, FrotaTelas.ParcelamentoMultas),
        ]),
        new ItemMenu("Relatórios", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Custos por Veículo", ItemMenuTipo.Tela, FrotaTelas.RelCustosVeiculo),
            new ItemMenu("Consumo de Combustível", ItemMenuTipo.Tela, FrotaTelas.RelConsumoCombustivel),
            new ItemMenu("Viagens", ItemMenuTipo.Tela, FrotaTelas.RelViagens),
            new ItemMenu("Vencimentos", ItemMenuTipo.Tela, FrotaTelas.RelVencimentos),
            new ItemMenu("Ordens de Serviço", ItemMenuTipo.Tela, FrotaTelas.RelOrdensServico),
            new ItemMenu("Aprovações", ItemMenuTipo.Tela, FrotaTelas.RelAprovacoes),
            new ItemMenu("Notas de Compra", ItemMenuTipo.Tela, FrotaTelas.RelNotasCompra),
            new ItemMenu("Manutenção Preventiva", ItemMenuTipo.Tela, FrotaTelas.RelManutencaoPreventiva),
            new ItemMenu("Histórico de Veículos", ItemMenuTipo.Tela, FrotaTelas.RelHistoricoVeiculos),
            new ItemMenu("Multas por Veículo", ItemMenuTipo.Tela, FrotaTelas.RelMultasVeiculo),
            new ItemMenu("Multas por Condutor", ItemMenuTipo.Tela, FrotaTelas.RelMultasCondutor),
            new ItemMenu("Custos com Multas", ItemMenuTipo.Tela, FrotaTelas.RelCustosMultas),
            new ItemMenu("Pontuação CNH", ItemMenuTipo.Tela, FrotaTelas.RelPontuacaoCnh),
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
        new AtalhoPainel(FrotaTelas.Veiculos, "Veículos", "Cadastro de veículos da frota"),
        new AtalhoPainel(FrotaTelas.Motoristas, "Motoristas", "Cadastro de motoristas"),
        new AtalhoPainel(FrotaTelas.Viagens, "Viagens", "Controle de viagens"),
        new AtalhoPainel(FrotaTelas.Abastecimentos, "Abastecimentos", "Registro de abastecimentos"),
        new AtalhoPainel(FrotaTelas.OrdensServico, "Ordens de Serviço", "Manutenção da frota"),
        new AtalhoPainel(FrotaTelas.MonitorMultas, "Monitor de Multas", "Acompanhamento de multas"),
    ];

    public void RegistrarTelas(CatalogoTelas catalogo)
    {
        catalogo.Registrar(FrotaTelas.Veiculos, () => new VeiculosView());
        catalogo.Registrar(FrotaTelas.Motoristas, () => new MotoristasView());
        catalogo.Registrar(FrotaTelas.Frotas, () => new FrotasView());
        catalogo.Registrar(FrotaTelas.TabelasFrete, () => new TabelasFreteView());
        catalogo.Registrar(FrotaTelas.DocumentosFrota, () => new DocumentosFrotaView());
        catalogo.Registrar(FrotaTelas.Rotas, () => new RotasView());
        catalogo.Registrar(FrotaTelas.PontosParada, () => new PontosParadaView());
        catalogo.Registrar(FrotaTelas.Painel, () => new PainelFrotaView());
        catalogo.Registrar(FrotaTelas.Abastecimentos, () => new AbastecimentosView());
        catalogo.Registrar(FrotaTelas.Viagens, () => new ViagensView());
        catalogo.Registrar(FrotaTelas.Diarias, () => new DiariasView());
        catalogo.Registrar(FrotaTelas.Coletas, () => new ColetasView());
        catalogo.Registrar(FrotaTelas.Entregas, () => new EntregasView());
        catalogo.Registrar(FrotaTelas.Pedagio, () => new PedagioView());
        catalogo.Registrar(FrotaTelas.LayoutsPedagio, () => new LayoutsPedagioView());
        catalogo.Registrar(FrotaTelas.Cotacoes, () => new CotacoesView());
        catalogo.Registrar(FrotaTelas.Ctes, () => new CtesView());
        catalogo.Registrar(FrotaTelas.Manifestos, () => new ManifestosView());
        catalogo.Registrar(FrotaTelas.FaturasFrete, () => new FaturasFreteView());
        catalogo.Registrar(FrotaTelas.PlanosManutencao, () => new PlanosManutencaoView());
        catalogo.Registrar(FrotaTelas.ItensManutencao, () => new ItensManutencaoView());
        catalogo.Registrar(FrotaTelas.OrdensServico, () => new OrdensServicoView());
        catalogo.Registrar(FrotaTelas.AutorizacaoServicos, () => new AutorizacaoServicosView());
        catalogo.Registrar(FrotaTelas.NotasCompra, () => new NotasCompraView());
        catalogo.Registrar(FrotaTelas.MonitorMultas, () => new MonitorMultasView());
        catalogo.Registrar(FrotaTelas.Infracoes, () => new InfracoesView());
        catalogo.Registrar(FrotaTelas.IndicacaoCondutor, () => new IndicacaoCondutorView());
        catalogo.Registrar(FrotaTelas.RecursosMultas, () => new RecursosMultasView());
        catalogo.Registrar(FrotaTelas.ParcelamentoMultas, () => new ParcelamentoMultasView());
        // Relatórios
        foreach (var rel in FrotaTelas.RelatoriosFrota.Todos)
            catalogo.Registrar(rel, () => new RelatorioGenericoView(rel));
    }
}