using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Frota;
using C = Vorticis.Desktop;
using E = Vorticis.Erp.Desktop;
using F = Vorticis.Fiscal.Desktop;
using Fr = Vorticis.Frota.Desktop;
using Ft = Vorticis.Fiscal.Desktop.ViewModels.Telas;
using T = Vorticis.Frota.Desktop.ViewModels.FrotaTelas;

namespace Vorticis.Logistica.Desktop;

/// <summary>
/// Módulo do Vorticis Suite Logística. A Suite tem seis módulos na mesma janela (Frotas, Transporte, Manutenção, Financeiro, Gerencial
/// e Contabilidade), cada um com os seus menus, a sua cor e o seu ícone; o login é um só. As telas são as dos módulos de origem (Frota, Gerencial,
/// Fiscal e Contábil: mesmas regras, permissões e banco); a empresa ativa é a transportadora, cadastrada já com todos esses módulos.
/// </summary>
public abstract class ModuloLogistica(params IModuloDesktop[] partes) : ModuloCombinado(partes), IModuloDesktop
{
    protected static readonly Fr.ModuloFrota Frota = new();
    protected static readonly C.ModuloContabil Contabil = new();
    protected static readonly F.ModuloFiscal Fiscal = new();
    protected static readonly E.ModuloErp Gerencial = new();

    /// <summary>Telas (DataTemplates) dos módulos de origem, do App.axaml de cada um.</summary>
    public static IEnumerable<Avalonia.Application> Apps() => [new Fr.App(), new C.App(), new F.App(), new E.App()];

    /// <summary>Os seis módulos, na ordem do trabalho da transportadora (é a ordem da navegação e de Ctrl+1, Ctrl+2...).</summary>
    public static IReadOnlyList<ModuloLogistica> Todos() =>
        [new ModuloFrotaLogistica(), new ModuloTransporte(), new ModuloManutencao(), new ModuloFinanceiro(), new ModuloGerencial(), new ModuloContabilidade()];

    public const string NomeSuite = "Vorticis Suite Logística";

    public override string Nome => $"{NomeSuite} – {NomeCurto}";
    public override string NomeExecutavel => "VorticisLogistica." + NomeCurto;
    public override Vorticis.Application.Cadastros.ModulosEmpresa ModuloEmpresa => Vorticis.Application.Cadastros.ModulosEmpresa.Frota;

    /// <summary>A transportadora cadastrada em qualquer módulo já fica ativa em todos.</summary>
    public override Vorticis.Application.Cadastros.ModulosEmpresa ModulosAoCadastrar =>
        Vorticis.Application.Cadastros.ModulosEmpresa.Frota | Vorticis.Application.Cadastros.ModulosEmpresa.Contabil
        | Vorticis.Application.Cadastros.ModulosEmpresa.Fiscal | Vorticis.Application.Cadastros.ModulosEmpresa.Erp;

    public bool UsaCalendarioObrigacoes => false;
    public string RotuloEmpresas => "Transportadoras";
    public string RotuloEmpresaAtiva => "Transportadora";
    /// <summary>Os contadores ficam só na aba Contadores de Cadastros › Transportadoras.</summary>
    public bool ContadoresNoMenuCadastros => false;

    /// <summary>Desenho do ícone do módulo (caminho vetorial 24×24, ver <see cref="IconesModulos"/>).</summary>
    public abstract string Icone { get; }
    string? IModuloDesktop.Icone => Icone;

    /// <summary>Abas de Cadastros › Parâmetros: os parâmetros das áreas do módulo (a aba Menus vem por último).</summary>
    protected abstract IReadOnlyList<(string Titulo, IModuloDesktop Parte)> AbasParametros { get; }

    /// <summary>
    /// Cadastros › Parâmetros é uma janela com uma aba para cada área do módulo (cada uma é a tela de parâmetros da área, com os mesmos campos e a
    /// mesma gravação) e a aba Menus, onde o usuário esconde os itens de menu deste módulo que não usa.
    /// </summary>
    protected override void RegistrarTelasPrimeiro(CatalogoTelas catalogo) =>
        catalogo.Instancia("ParametrosLogistica", m => new ParametrosAbasViewModel(m, [.. AbasParametros.Select(a => (a.Titulo, Chave(a.Parte, TelasComuns.Parametros)))]),
                atualizarPermissoes: v => v.AtualizarPermissoes(), aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(TelasComuns.Parametros, "Cadastros › Parâmetros", m => m.TemAcessoNaEmpresa, v => v.CarregarAsync());

    /// <summary>Área dos gráficos deste módulo (Relatórios › Gráficos); null = sem gráficos.</summary>
    protected virtual string? AreaGraficos => null;

    public override IReadOnlyList<DefinicaoGrafico> Graficos => [.. base.Graficos.Where(g => g.Area == AreaGraficos)];

    /// <summary>Atalhos só das telas que estão nos menus deste módulo.</summary>
    public override IReadOnlyList<AtalhoTela> Atalhos =>
        [.. base.Atalhos.Where(a => a.Chave is { } c && Folhas([.. ItensCadastros, .. Menus]).Any(i => i.Chave == c))];

    protected static IEnumerable<ItemMenu> Folhas(IEnumerable<ItemMenu> itens) => itens.SelectMany(i => i.Filhos is { } f ? Folhas(f) : [i]);

    /// <summary>Itens de menu de uma parte, já com as chaves do programa.</summary>
    protected IReadOnlyList<ItemMenu> De(IModuloDesktop parte, params ItemMenu[] itens) => DaParte(parte, itens);

    /// <summary>Cadastros › Transportadoras só no módulo Frotas (nos demais, a transportadora é só escolhida no alto da tela).</summary>
    public virtual bool EmpresasNoMenuCadastros => false;

    /// <summary>Todas as funções de faturamento (menu Faturamento do Transporte); o módulo precisa das partes Frota e Gerencial.</summary>
    protected IReadOnlyList<ItemMenu> FuncoesFaturamento() =>
    [
        .. De(Frota, ItemMenu.Tela("Faturas de frete", T.Faturas)), ItemMenu.Separador,
        .. De(Gerencial, ItemMenu.Tela("Pedidos de venda", E.ViewModels.ErpTelas.PedidosVenda), ItemMenu.Tela("Notas fiscais de saída", Ft.NotaSaida)),
        ItemMenu.Separador,
        ItemMenu.EmBreve("Emissão de NF-e na SEFAZ"),
        ItemMenu.EmBreve("NFS-e – Nota fiscal de serviço (Padrão Nacional)"),
    ];

    /// <summary>Relatórios do faturamento: faturas de frete, vendas e recebimentos.</summary>
    protected IReadOnlyList<ItemMenu> RelatoriosFaturamento() =>
        [.. De(Frota, ItemMenu.Tela(TabelasTms.TituloFaturas, T.RelFaturas)),
         .. De(Gerencial, ItemMenu.Tela("Relatório de vendas", Ft.ResumoVendas), ItemMenu.Tela("Relatório de recebimentos", Ft.RelRecebimentos))];

    /// <summary>Menu Relatórios do módulo, com o submenu Gráficos quando há gráficos da área.</summary>
    protected ItemMenu Relatorios(params IEnumerable<ItemMenu>[] blocos)
    {
        var itens = new List<ItemMenu>();
        foreach (var b in blocos.Where(b => b.Any()))
        {
            if (itens.Count > 0) itens.Add(ItemMenu.Separador);
            itens.AddRange(b);
        }
        if (Graficos.Count > 0) itens.AddRange([ItemMenu.Separador, ItemMenu.Submenu("Grá_ficos", [.. Graficos.Select(g => ItemMenu.Tela(g.Titulo, g.Tela))])]);
        return ItemMenu.Submenu("_Relatórios", [.. itens]);
    }
}

/// <summary>Frotas: transportadoras, veículos, motoristas, abastecimentos, viagens, diárias e pedágio.</summary>
public sealed class ModuloFrotaLogistica() : ModuloLogistica(Frota)
{
    protected override IReadOnlyList<(string Titulo, IModuloDesktop Parte)> AbasParametros => [("Frota", Frota)];
    public override string NomeCurto => "Frotas";
    public override string Subtitulo => "Transportadoras, veículos, motoristas, abastecimentos, viagens, diárias e pedágio";
    public override bool EmpresasNoMenuCadastros => true;
    public override string CorDestaque => "#0284C7";
    public override string CorBarra => "#0C4A6E";
    public override string Icone => IconesModulos.Frota;
    protected override string? AreaGraficos => "Frota";

    public override IReadOnlyList<ItemMenu> ItensCadastros => De(Frota,
        ItemMenu.Tela("Veículos", T.Veiculos), ItemMenu.Tela("Motoristas", T.Motoristas), ItemMenu.Tela("Frotas", T.Frotas),
        ItemMenu.Tela("Rotas", T.Rotas), ItemMenu.Tela("Documentos e vencimentos", T.Documentos));

    public override IReadOnlyList<ItemMenu> Menus =>
    [
        .. De(Frota, ItemMenu.Submenu("_Operação",
            ItemMenu.Tela("Abastecimentos", T.Abastecimentos),
            ItemMenu.Tela("Viagens", T.Viagens),
            ItemMenu.Tela("Diárias", T.Diarias),
            ItemMenu.Separador,
            ItemMenu.Submenu("_Pedágio",
                ItemMenu.Tela("Passagens e notas", T.Pedagio),
                ItemMenu.Tela("Layouts de arquivo", T.LayoutsPedagio)))),
        Relatorios(De(Frota,
            ItemMenu.Tela("Painel da frota", T.Painel),
            ItemMenu.Tela("Resumo gerencial da frota", T.RelResumo),
            ItemMenu.Tela("Custos por veículo", T.RelCustos),
            ItemMenu.Tela("Custos por frota", T.RelCustosFrota),
            ItemMenu.Tela("Consumo de combustível", T.RelConsumo),
            ItemMenu.Tela("Consumo por veículo", T.RelConsumoVeiculo),
            ItemMenu.Tela("Consumo por motorista", T.RelConsumoMotorista)),
            De(Frota,
            ItemMenu.Tela("Resultado das viagens", T.RelViagens),
            ItemMenu.Tela("Viagens por motorista", T.RelViagensMotorista),
            ItemMenu.Tela("Viagens por cliente", T.RelViagensCliente),
            ItemMenu.Tela("Viagens por rota", T.RelViagensRota)),
            De(Frota,
            ItemMenu.Tela("Diárias por colaborador", T.RelDiariasColaborador),
            ItemMenu.Tela("Diárias por frota", T.RelDiariasFrota),
            ItemMenu.Tela("Diárias pagas e acertadas", T.RelDiariasAcerto),
            ItemMenu.Tela("Vencimentos e manutenções", T.RelVencimentos))),
    ];
}

/// <summary>Transporte (TMS): tabelas de frete, cotações, coletas, conhecimentos, manifestos, entregas e todo o faturamento.</summary>
public sealed class ModuloTransporte() : ModuloLogistica(Frota, Gerencial, Fiscal)
{
    protected override IReadOnlyList<(string Titulo, IModuloDesktop Parte)> AbasParametros => [("Frota e transporte", Frota), ("Faturamento", Gerencial)];
    public override string NomeCurto => "Transporte";
    public override string Subtitulo => "TMS: cotações, coletas, CT-e, MDF-e, entregas e faturamento";
    public override string CorDestaque => "#0D9488";
    public override string CorBarra => "#134E4A";
    public override string Icone => IconesModulos.Transporte;
    protected override string? AreaGraficos => "Transporte";

    public override IReadOnlyList<ItemMenu> ItensCadastros =>
        [.. De(Frota, ItemMenu.Tela("Tabelas de frete", T.TabelasFrete)), .. De(Gerencial, ItemMenu.Tela("Formas de pagamento", TelasComuns.FormasPagamento))];

    public override IReadOnlyList<ItemMenu> Menus =>
    [
        .. De(Frota,
            ItemMenu.Submenu("_Operação",
                ItemMenu.Tela("Cotações de frete", T.Cotacoes),
                ItemMenu.Tela("Ordens de coleta", T.Coletas),
                ItemMenu.Tela("Entregas e ocorrências", T.Entregas),
                ItemMenu.Separador,
                ItemMenu.Tela("CIOT – operações de transporte", T.Ciot))),
        // todo o faturamento da transportadora: os documentos do transporte (CT-e e MDF-e), as faturas de frete, os pedidos de venda e as notas de saída
        ItemMenu.Submenu("_Faturamento",
        [
            .. De(Frota,
                ItemMenu.Tela("Conhecimentos de transporte (CT-e)", T.Ctes),
                ItemMenu.Tela("Manifestos de carga (MDF-e)", T.Manifestos),
                ItemMenu.EmBreve("Transmissão de CT-e e MDF-e à SEFAZ")),
            ItemMenu.Separador,
            .. FuncoesFaturamento(),
        ]),
        Relatorios(De(Frota,
            ItemMenu.Tela(TabelasTms.TituloFretesCliente, T.RelFretesCliente),
            ItemMenu.Tela(TabelasTms.TituloFretesTrecho, T.RelFretesTrecho),
            ItemMenu.Tela(TabelasTms.TituloConhecimentos, T.RelConhecimentos),
            ItemMenu.Tela(TabelasTms.TituloPrazoEntrega, T.RelPrazoEntrega)),
            De(Frota,
            ItemMenu.Tela("Coletas por cliente", T.RelColetasCliente),
            ItemMenu.Tela("Coletas por rota", T.RelColetasRota),
            ItemMenu.Tela("Coletas por motorista", T.RelColetasMotorista)),
            RelatoriosFaturamento()),
    ];
}

/// <summary>Manutenção: planos, peças e serviços, ordens de serviço e autorizações.</summary>
public sealed class ModuloManutencao() : ModuloLogistica(Frota)
{
    protected override IReadOnlyList<(string Titulo, IModuloDesktop Parte)> AbasParametros => [("Frota e manutenção", Frota)];
    public override string NomeCurto => "Manutenção";
    public override string Subtitulo => "Planos preventivos, peças e serviços, ordens de serviço e autorizações";
    public override string CorDestaque => "#EA580C";
    public override string CorBarra => "#7C2D12";
    public override string Icone => IconesModulos.Manutencao;

    public override IReadOnlyList<ItemMenu> ItensCadastros => De(Frota,
        ItemMenu.Tela("Planos de manutenção", T.Planos), ItemMenu.Tela("Peças e serviços", T.Itens));

    public override IReadOnlyList<ItemMenu> Menus =>
    [
        .. De(Frota, ItemMenu.Submenu("_Serviços",
            ItemMenu.Tela("Ordens de serviço", T.Ordens),
            ItemMenu.Tela("Autorização de serviços", T.Autorizacao))),
        Relatorios(De(Frota,
            ItemMenu.Tela("Acompanhamento das ordens de serviço", T.RelAcompanhamento),
            ItemMenu.Tela("Aprovações de serviços", T.RelAprovacoes),
            ItemMenu.Tela("Notas fiscais de compra", T.RelNotas),
            ItemMenu.Tela("Manutenções preventivas", T.RelPreventivas),
            ItemMenu.Tela("Histórico de manutenção por veículo", T.RelHistoricoManutencao))),
    ];
}

/// <summary>Financeiro: contas a pagar e a receber, consórcios e financiamentos, conciliação bancária, bens financiados (imobilizado) e cobranças.</summary>
public sealed class ModuloFinanceiro() : ModuloLogistica(Gerencial, Fiscal, Frota, Contabil)
{
    protected override IReadOnlyList<(string Titulo, IModuloDesktop Parte)> AbasParametros => [("Financeiro", Gerencial)];
    public override string NomeCurto => "Financeiro";
    public override string Subtitulo => "Contas a pagar e a receber, consórcios, financiamentos, imobilizado e cobranças";
    public override string CorDestaque => "#16A34A";
    public override string CorBarra => "#14532D";
    public override string Icone => IconesModulos.Financeiro;
    protected override string? AreaGraficos => "Financeiro";

    public override IReadOnlyList<ItemMenu> ItensCadastros => De(Gerencial,
        ItemMenu.Tela("Contas bancárias", TelasComuns.ContasBancarias), ItemMenu.Tela("Formas de pagamento", TelasComuns.FormasPagamento));

    public override IReadOnlyList<ItemMenu> Menus =>
    [
        ItemMenu.Submenu("_Contas",
        [
            .. De(Gerencial, ItemMenu.Tela("Contas a pagar", Ft.ContasPagar), ItemMenu.Tela("Contas a receber", Ft.ContasReceber)),
            ItemMenu.Separador,
            .. De(Frota, ItemMenu.Tela("Consórcios", T.Consorcios), ItemMenu.Tela("Financiamentos", T.Financiamentos)),
            ItemMenu.Separador,
            .. De(Gerencial, ItemMenu.Tela("Conciliação bancária", Ft.ConciliacaoDuplicatas)),
        ]),
        ItemMenu.Submenu("_Imobilizado",
        [
            .. De(Frota, ItemMenu.Tela("Bens financiados e consorciados", T.BensContratos)),
            ItemMenu.Separador,
            .. De(Contabil, ItemMenu.Tela("Cadastro de bens", C.ViewModels.Telas.Bens),
                  ItemMenu.Tela("Cálculo e lançamento da depreciação", C.ViewModels.Telas.Depreciacao)),
        ]),
        .. De(Gerencial, ItemMenu.Submenu("_Monitores", ItemMenu.Tela("Monitor de cobranças", E.ViewModels.ErpTelas.MonitorCobrancas))),
        Relatorios(De(Gerencial,
            ItemMenu.Tela("Relatório de contas a pagar", Ft.RelDuplicatasPagar),
            ItemMenu.Tela("Relatório de contas a receber", Ft.RelDuplicatasReceber),
            ItemMenu.Tela("Posição por vencimento", Ft.RelPosicaoDuplicatas),
            ItemMenu.Tela("Relatório de pagamentos", Ft.RelPagamentos))),
    ];
}

/// <summary>Gerencial: compras, estoque, monitores de notas fiscais e de multas de trânsito e o painel do empresário.</summary>
public sealed class ModuloGerencial() : ModuloLogistica(Gerencial, Fiscal, Frota)
{
    protected override IReadOnlyList<(string Titulo, IModuloDesktop Parte)> AbasParametros => [("Gerencial", Gerencial)];
    public override string NomeCurto => "Gerencial";
    public override string Subtitulo => "Compras, estoque, monitores de notas e de multas e painel do empresário";
    public override string CorDestaque => "#DB2777";
    public override string CorBarra => "#831843";
    public override string Icone => IconesModulos.Gerencial;

    public override IReadOnlyList<ItemMenu> ItensCadastros => De(Gerencial,
        ItemMenu.Tela("Produtos", Ft.Produtos), ItemMenu.Tela("Regras fiscais", Ft.RegrasFiscais));

    public override IReadOnlyList<ItemMenu> Menus =>
    [
        .. De(Gerencial,
            ItemMenu.Submenu("C_ompras",
                ItemMenu.Tela("Pedidos de compra", E.ViewModels.ErpTelas.PedidosCompra),
                ItemMenu.Tela("Notas fiscais de entrada", Ft.NotaEntrada),
                ItemMenu.Tela("Importação de XML", Ft.ImportacaoXml)),
            ItemMenu.Submenu("_Estoque", ItemMenu.Tela("Posição e movimentos", E.ViewModels.ErpTelas.Estoque))),
        ItemMenu.Submenu("_Monitores",
        [
            .. De(Gerencial, ItemMenu.Tela("Monitor de notas fiscais", E.ViewModels.ErpTelas.MonitorNotas)),
            .. De(Frota, ItemMenu.Tela("Monitor de multas de trânsito", T.Multas)),
        ]),
        Relatorios(De(Gerencial, ItemMenu.Tela("Painel do empresário", E.ViewModels.ErpTelas.Painel)),
            De(Gerencial, ItemMenu.Tela("Relatório de compras", Ft.ResumoCompras))),
    ];
}

/// <summary>Contabilidade: lançamentos, fechamentos, imobilizado, integrações e relatórios contábeis.</summary>
public sealed class ModuloContabilidade() : ModuloLogistica(Contabil)
{
    protected override IReadOnlyList<(string Titulo, IModuloDesktop Parte)> AbasParametros => [("Contábil", Contabil)];
    public override string NomeCurto => "Contabilidade";
    public override string Subtitulo => "Lançamentos, fechamentos, imobilizado, SPED e demonstrações";
    public override string CorDestaque => "#4F46E5";
    public override string CorBarra => "#312E81";
    public override string Icone => IconesModulos.Contabilidade;
    protected override string? AreaGraficos => "Contábil";

    public override IReadOnlyList<ItemMenu> ItensCadastros => DaParte(Contabil, Contabil.ItensCadastros);

    /// <summary>Os menus do Contábil, com os gráficos no fim de Relatórios.</summary>
    public override IReadOnlyList<ItemMenu> Menus =>
        [.. DaParte(Contabil, Contabil.Menus).Select(m => m.Titulo.Replace("_", "") == "Relatórios" && Graficos.Count > 0
            ? m with { Filhos = [.. m.Filhos!, ItemMenu.Separador, ItemMenu.Submenu("Grá_ficos", [.. Graficos.Select(g => ItemMenu.Tela(g.Titulo, g.Tela))])] }
            : m)];
}

/// <summary>Uma janela do Vorticis Suite Logística por módulo (a Suite reúne as seis).</summary>
public sealed class MainViewModel(ModuloLogistica modulo) : MainViewModelBase(modulo);
