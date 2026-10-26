using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Domain.Seguranca;
using Vorticis.Frota.Desktop.ViewModels;

namespace Vorticis.Frota.Desktop;

/// <summary>
/// Módulo Frota: a empresa ativa é a transportadora. Veículos, motoristas, abastecimentos, viagens, vencimentos e manutenção (ordens de serviço
/// com autorização e notas de compra).
/// TMS: tabelas de frete, cotações, conhecimentos (CT-e), manifestos (MDF-e), entregas e faturas de frete (a transmissão à SEFAZ fica para depois).
/// Permissões FROTA_* conferidas nos serviços.
/// </summary>
public sealed class ModuloFrota : IModuloDesktop
{
    public string Nome => "Vorticis Frota";
    public Vorticis.Application.Cadastros.ModulosEmpresa ModuloEmpresa => Vorticis.Application.Cadastros.ModulosEmpresa.Frota;
    public string NomeCurto => "Frota";
    public string Subtitulo => "Coordenação de frota e manutenção para transportadoras";
    public string NomeExecutavel => "VorticisFrota";
    public string CorDestaque => "#475569";   // grafite-aço (ícone e selo do módulo)
    public string CorBarra => "#1E293B";      // asfalto
    public bool UsaCalendarioObrigacoes => false;
    /// <summary>Cria os serviços da Frota na abertura, para registrar o processador da Contabilização em lote.</summary>
    public void PrepararServicos(Vorticis.Infra.VorticisApp app) => _ = app.Frota();
    public string RotuloEmpresas => "Transportadoras";
    public string RotuloEmpresaAtiva => "Transportadora";

    public IReadOnlyList<ItemMenu> ItensCadastros { get; } =
    [
        ItemMenu.Tela("Veículos", FrotaTelas.Veiculos),
        ItemMenu.Tela("Motoristas", FrotaTelas.Motoristas),
        ItemMenu.Tela("Frotas", FrotaTelas.Frotas),
        ItemMenu.Tela("Rotas", FrotaTelas.Rotas),
        ItemMenu.Tela("Tabelas de frete", FrotaTelas.TabelasFrete),
        ItemMenu.Tela("Documentos e vencimentos", FrotaTelas.Documentos),
    ];

    public IReadOnlyList<ItemMenu> Menus { get; } =
    [
        ItemMenu.Submenu("_Operação",
            ItemMenu.Tela("Abastecimentos", FrotaTelas.Abastecimentos),
            ItemMenu.Tela("Viagens", FrotaTelas.Viagens),
            ItemMenu.Tela("Diárias", FrotaTelas.Diarias),
            ItemMenu.Separador,
            ItemMenu.Submenu("_Coletas", ItemMenu.Tela("Ordens de coleta", FrotaTelas.Coletas)),
            ItemMenu.Tela("Entregas e ocorrências", FrotaTelas.Entregas),
            ItemMenu.Submenu("_Pedágio",
                ItemMenu.Tela("Passagens e notas", FrotaTelas.Pedagio),
                ItemMenu.Tela("Layouts de arquivo", FrotaTelas.LayoutsPedagio))),
        ItemMenu.Submenu("_Emissão",
            ItemMenu.Tela("Cotações de frete", FrotaTelas.Cotacoes),
            ItemMenu.Tela("Conhecimentos de transporte (CT-e)", FrotaTelas.Ctes),
            ItemMenu.Tela("Manifestos de carga (MDF-e)", FrotaTelas.Manifestos),
            ItemMenu.Separador,
            ItemMenu.Tela("Faturas de frete", FrotaTelas.Faturas)),
        ItemMenu.Submenu("_Manutenção",
            ItemMenu.Tela("Planos de manutenção", FrotaTelas.Planos),
            ItemMenu.Tela("Peças e serviços", FrotaTelas.Itens),
            ItemMenu.Tela("Ordens de serviço", FrotaTelas.Ordens),
            ItemMenu.Tela("Autorização de serviços", FrotaTelas.Autorizacao)),
        ItemMenu.Submenu("_Relatórios",
            ItemMenu.Tela("Painel da frota", FrotaTelas.Painel),
            ItemMenu.Tela("Custos por veículo", FrotaTelas.RelCustos),
            ItemMenu.Tela("Consumo de combustível", FrotaTelas.RelConsumo),
            ItemMenu.Tela("Resultado das viagens", FrotaTelas.RelViagens),
            ItemMenu.Tela("Vencimentos e manutenções", FrotaTelas.RelVencimentos),
            ItemMenu.Tela("Custos por frota", FrotaTelas.RelCustosFrota),
            ItemMenu.Tela("Diárias por colaborador", FrotaTelas.RelDiariasColaborador),
            ItemMenu.Tela("Diárias por frota", FrotaTelas.RelDiariasFrota),
            ItemMenu.Tela("Diárias pagas e acertadas", FrotaTelas.RelDiariasAcerto),
            ItemMenu.Tela("Consumo por veículo", FrotaTelas.RelConsumoVeiculo),
            ItemMenu.Tela("Consumo por motorista", FrotaTelas.RelConsumoMotorista),
            ItemMenu.Tela("Viagens por motorista", FrotaTelas.RelViagensMotorista),
            ItemMenu.Tela("Viagens por cliente", FrotaTelas.RelViagensCliente),
            ItemMenu.Tela("Viagens por rota", FrotaTelas.RelViagensRota),
            ItemMenu.Tela("Coletas por cliente", FrotaTelas.RelColetasCliente),
            ItemMenu.Tela("Coletas por rota", FrotaTelas.RelColetasRota),
            ItemMenu.Tela("Coletas por motorista", FrotaTelas.RelColetasMotorista),
            ItemMenu.Tela("Resumo gerencial da frota", FrotaTelas.RelResumo),
            ItemMenu.Separador,
            ItemMenu.Tela("Acompanhamento das ordens de serviço", FrotaTelas.RelAcompanhamento),
            ItemMenu.Tela("Aprovações de serviços", FrotaTelas.RelAprovacoes),
            ItemMenu.Tela("Notas fiscais de compra", FrotaTelas.RelNotas),
            ItemMenu.Tela("Manutenções preventivas", FrotaTelas.RelPreventivas),
            ItemMenu.Tela("Histórico de manutenção por veículo", FrotaTelas.RelHistoricoManutencao),
            ItemMenu.Separador,
            ItemMenu.Tela(TabelasTms.TituloFretesCliente, FrotaTelas.RelFretesCliente),
            ItemMenu.Tela(TabelasTms.TituloFretesTrecho, FrotaTelas.RelFretesTrecho),
            ItemMenu.Tela(TabelasTms.TituloConhecimentos, FrotaTelas.RelConhecimentos),
            ItemMenu.Tela(TabelasTms.TituloPrazoEntrega, FrotaTelas.RelPrazoEntrega),
            ItemMenu.Tela(TabelasTms.TituloFaturas, FrotaTelas.RelFaturas)),
    ];

    public IReadOnlyList<AtalhoTela> Atalhos { get; } =
    [
        new("Ctrl+B", FrotaTelas.Abastecimentos, "Abastecimentos"),
        new("Ctrl+P", FrotaTelas.Painel, "Painel da frota"),
        new("Ctrl+O", FrotaTelas.Ordens, "Ordens de serviço"),
    ];

    private static bool Pode(MainViewModelBase m, params Permissao[] p) => m.TemAcessoNaEmpresa && p.Any(m.Pode);
    private static readonly Permissao[] Todas = [Permissao.FrotaCadastros, Permissao.FrotaOperacao, Permissao.FrotaRelatorios, Permissao.FrotaManutencao, Permissao.FrotaAprovar, Permissao.FrotaTms];
    private static readonly Permissao[] PermissoesTms = [Permissao.FrotaTms, Permissao.FrotaOperacao, Permissao.FrotaRelatorios];
    private static readonly Permissao[] Manutencao = [Permissao.FrotaManutencao, Permissao.FrotaAprovar, Permissao.FrotaRelatorios];

    public void RegistrarTelas(CatalogoTelas c)
    {
        c.Instancia("PainelFrota", m => new PainelFrotaViewModel(m), aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FrotaTelas.Painel, "Relatórios › Painel da frota", m => Pode(m, Todas), v => v.CarregarAsync());
        c.Instancia("Veiculos", m => new VeiculosViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FrotaTelas.Veiculos, "Cadastros › Veículos", m => Pode(m, Todas), v => v.CarregarAsync());
        c.Instancia("Motoristas", m => new MotoristasViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FrotaTelas.Motoristas, "Cadastros › Motoristas", m => Pode(m, Todas), v => v.CarregarAsync());
        c.Instancia("PlanosManutencao", m => new PlanosViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FrotaTelas.Planos, "Manutenção › Planos de manutenção", m => Pode(m, Manutencao), v => v.CarregarAsync());
        c.Instancia("DocumentosFrota", m => new DocumentosViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FrotaTelas.Documentos, "Cadastros › Documentos e vencimentos", m => Pode(m, Todas), v => v.CarregarAsync());
        c.Instancia("Abastecimentos", m => new AbastecimentosViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FrotaTelas.Abastecimentos, "Operação › Abastecimentos", m => Pode(m, Permissao.FrotaOperacao, Permissao.FrotaRelatorios), v => v.CarregarAsync());
        c.Instancia("ItensManutencao", m => new ItensManutencaoViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FrotaTelas.Itens, "Manutenção › Peças e serviços", m => Pode(m, Manutencao), v => v.CarregarAsync());
        c.Instancia("OrdensServico", m => new OrdensViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FrotaTelas.Ordens, "Manutenção › Ordens de serviço", m => Pode(m, Manutencao), v => v.CarregarAsync());
        c.Instancia("AutorizacaoServicos", m => new AutorizacaoViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FrotaTelas.Autorizacao, "Manutenção › Autorização de serviços", m => Pode(m, Manutencao), v => v.CarregarAsync());
        c.Instancia("NotasCompra", m => new NotasCompraViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FrotaTelas.Notas, "Manutenção › Notas de compra", m => Pode(m, Manutencao), v => v.CarregarAsync());
        c.Instancia("Viagens", m => new ViagensViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FrotaTelas.Viagens, "Operação › Viagens", m => Pode(m, Permissao.FrotaOperacao, Permissao.FrotaRelatorios), v => v.CarregarAsync());
        c.Instancia("Frotas", m => new FrotasViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FrotaTelas.Frotas, "Cadastros › Frotas", m => Pode(m, Todas), v => v.CarregarAsync());
        c.Instancia("Rotas", m => new RotasViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FrotaTelas.Rotas, "Cadastros › Rotas", m => Pode(m, Todas), v => v.CarregarAsync());
        c.Instancia("Diarias", m => new DiariasViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FrotaTelas.Diarias, "Operação › Diárias", m => Pode(m, Permissao.FrotaOperacao, Permissao.FrotaRelatorios), v => v.CarregarAsync());
        c.Instancia("Coletas", m => new ColetasViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FrotaTelas.Coletas, "Operação › Coletas › Ordens de coleta", m => Pode(m, Permissao.FrotaOperacao, Permissao.FrotaRelatorios), v => v.CarregarAsync());
        Relatorio(c, "RelCustosFrotaGrupo", FrotaTelas.RelCustosFrota, Vorticis.Frota.TabelasFrota.TituloCustosFrota);
        Relatorio(c, "RelDiariasColab", FrotaTelas.RelDiariasColaborador, Vorticis.Frota.TabelasFrota.TituloDiariasColaborador);
        Relatorio(c, "RelDiariasFrota", FrotaTelas.RelDiariasFrota, Vorticis.Frota.TabelasFrota.TituloDiariasFrota);
        Relatorio(c, "RelCustosFrota", FrotaTelas.RelCustos, Vorticis.Frota.TabelasFrota.TituloCustos);
        Relatorio(c, "RelConsumoFrota", FrotaTelas.RelConsumo, Vorticis.Frota.TabelasFrota.TituloConsumo);
        Relatorio(c, "RelViagensFrota", FrotaTelas.RelViagens, Vorticis.Frota.TabelasFrota.TituloViagens);
        Relatorio(c, "RelVencimentosFrota", FrotaTelas.RelVencimentos, Vorticis.Frota.TabelasFrota.TituloVencimentos);
        Relatorio(c, "RelOrdensFrota", FrotaTelas.RelAcompanhamento, Vorticis.Frota.TabelasFrota.TituloAcompanhamento);
        Relatorio(c, "RelAprovacoesFrota", FrotaTelas.RelAprovacoes, Vorticis.Frota.TabelasFrota.TituloAprovacoes);
        Relatorio(c, "RelNotasFrota", FrotaTelas.RelNotas, Vorticis.Frota.TabelasFrota.TituloNotas);
        Relatorio(c, "RelConsumoVeiculo", FrotaTelas.RelConsumoVeiculo, Vorticis.Frota.TabelasFrota.TituloConsumoVeiculo);
        Relatorio(c, "RelConsumoMotorista", FrotaTelas.RelConsumoMotorista, Vorticis.Frota.TabelasFrota.TituloConsumoMotorista);
        Relatorio(c, "RelPreventivas", FrotaTelas.RelPreventivas, Vorticis.Frota.TabelasFrota.TituloPreventivas);
        Relatorio(c, "RelHistoricoManutencao", FrotaTelas.RelHistoricoManutencao, Vorticis.Frota.TabelasFrota.TituloHistoricoManutencao);
        Relatorio(c, "RelViagensMotorista", FrotaTelas.RelViagensMotorista, Vorticis.Frota.TabelasFrota.TituloViagensMotorista);
        Relatorio(c, "RelViagensCliente", FrotaTelas.RelViagensCliente, Vorticis.Frota.TabelasFrota.TituloViagensCliente);
        Relatorio(c, "RelViagensRota", FrotaTelas.RelViagensRota, Vorticis.Frota.TabelasFrota.TituloViagensRota);
        Relatorio(c, "RelColetasCliente", FrotaTelas.RelColetasCliente, Vorticis.Frota.TabelasFrota.TituloColetasCliente);
        Relatorio(c, "RelColetasRota", FrotaTelas.RelColetasRota, Vorticis.Frota.TabelasFrota.TituloColetasRota);
        Relatorio(c, "RelColetasMotorista", FrotaTelas.RelColetasMotorista, Vorticis.Frota.TabelasFrota.TituloColetasMotorista);
        Relatorio(c, "RelDiariasAcerto", FrotaTelas.RelDiariasAcerto, Vorticis.Frota.TabelasFrota.TituloDiariasAcerto);
        Relatorio(c, "RelResumoFrota", FrotaTelas.RelResumo, Vorticis.Frota.TabelasFrota.TituloResumo);
        c.Instancia("Pedagio", m => new PedagioViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FrotaTelas.Pedagio, "Operações › Pedágio › Passagens e notas", m => Pode(m, Permissao.FrotaOperacao, Permissao.FrotaRelatorios), v => v.CarregarAsync());
        c.Instancia("LayoutsPedagio", m => new LayoutsPedagioViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FrotaTelas.LayoutsPedagio, "Operações › Pedágio › Layouts de arquivo", m => Pode(m, Permissao.FrotaOperacao, Permissao.FrotaRelatorios), v => v.CarregarAsync());
        Tms(c, "TabelasFrete", FrotaTelas.TabelasFrete, "Cadastros › Tabelas de frete", m => new TabelasFreteViewModel(m), v => v.CarregarAsync());
        Tms(c, "Cotacoes", FrotaTelas.Cotacoes, "Emissão › Cotações de frete", m => new CotacoesViewModel(m), v => v.CarregarAsync());
        Tms(c, "Ctes", FrotaTelas.Ctes, "Emissão › Conhecimentos de transporte (CT-e)", m => new CtesViewModel(m), v => v.CarregarAsync());
        Tms(c, "Manifestos", FrotaTelas.Manifestos, "Emissão › Manifestos de carga (MDF-e)", m => new ManifestosViewModel(m), v => v.CarregarAsync());
        Tms(c, "Entregas", FrotaTelas.Entregas, "Operações › Entregas e ocorrências", m => new EntregasViewModel(m), v => v.CarregarAsync());
        Tms(c, "FaturasFrete", FrotaTelas.Faturas, "Emissão › Faturas de frete", m => new FaturasFreteViewModel(m), v => v.CarregarAsync());
        Relatorio(c, "RelFretesCliente", FrotaTelas.RelFretesCliente, TabelasTms.TituloFretesCliente);
        Relatorio(c, "RelFretesTrecho", FrotaTelas.RelFretesTrecho, TabelasTms.TituloFretesTrecho);
        Relatorio(c, "RelConhecimentos", FrotaTelas.RelConhecimentos, TabelasTms.TituloConhecimentos);
        Relatorio(c, "RelPrazoEntrega", FrotaTelas.RelPrazoEntrega, TabelasTms.TituloPrazoEntrega);
        Relatorio(c, "RelFaturasFrete", FrotaTelas.RelFaturas, TabelasTms.TituloFaturas);
        c.Instancia("ParametrosModulo", m => new ParametrosFrotaViewModel(m), aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(TelasComuns.Parametros, "Cadastros › Parâmetros", m => m.TemAcessoNaEmpresa, v => v.CarregarAsync());
    }

    private static void Tms<T>(CatalogoTelas c, string instancia, string chave, string titulo, Func<MainViewModelBase, T> criar, Func<T, Task> carregar) where T : FrotaBase =>
        c.Instancia(instancia, criar, atualizarPermissoes: v => v.AtualizarPermissoes(), aoTrocarEmpresa: (v, ativa) => ativa ? carregar(v) : Task.CompletedTask)
            .Tela(chave, titulo, m => Pode(m, PermissoesTms), carregar);

    private static void Relatorio(CatalogoTelas c, string instancia, string chave, string titulo) =>
        c.Instancia(instancia, m => new RelatorioFrotaViewModel(m, titulo), atualizarPermissoes: v => v.AtualizarPermissoesExportacao(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(chave, $"Relatórios › {titulo}", m => Pode(m, Permissao.FrotaRelatorios), v => v.CarregarAsync());
}

public sealed class MainViewModel() : MainViewModelBase(new ModuloFrota());
