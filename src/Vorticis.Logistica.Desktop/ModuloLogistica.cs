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
/// Módulo do Vorticis Suite Logística. A Suite tem sete módulos na mesma janela (Frotas, Transporte, Faturamento, Compras, Financeiro, Gestão
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

    /// <summary>Os sete módulos, na ordem do trabalho da transportadora (é a ordem da navegação e de Ctrl+1, Ctrl+2...).</summary>
    public static IReadOnlyList<ModuloLogistica> Todos() =>
        [new ModuloFrotaLogistica(), new ModuloTransporte(), new ModuloFaturamento(), new ModuloCompras(), new ModuloFinanceiro(), new ModuloGestao(), new ModuloContabilidade()];

    public const string NomeSuite = "Vorticis Suite Logística";

    public override string Nome => $"{NomeSuite} – {NomeCurto}";
    public override string NomeExecutavel => "VorticisLogistica." + NomeCurto;
    public override Vorticis.Application.Cadastros.ModulosEmpresa ModuloEmpresa => Vorticis.Application.Cadastros.ModulosEmpresa.Frota;

    /// <summary>A transportadora cadastrada em qualquer módulo já fica ativa em todos.</summary>
    public override Vorticis.Application.Cadastros.ModulosEmpresa ModulosAoCadastrar =>
        Vorticis.Application.Cadastros.ModulosEmpresa.Frota | Vorticis.Application.Cadastros.ModulosEmpresa.Contabil
        | Vorticis.Application.Cadastros.ModulosEmpresa.Fiscal | Vorticis.Application.Cadastros.ModulosEmpresa.Erp;

    /// <summary>A tela de acesso mostra só o nome da Suite (o login vale para todos os módulos).</summary>
    public string NomeAcesso => NomeSuite;
    public string SubtituloAcesso => "Frotas, transporte, faturamento, compras, financeiro, gestão e contabilidade";

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

    /// <summary>Áreas dos gráficos deste módulo (Relatórios › Gráficos e Relatórios › BI); vazio = sem gráficos; null = todas (Gestão).</summary>
    protected virtual IReadOnlyList<string>? AreasGraficos => [];

    public override IReadOnlyList<DefinicaoGrafico> Graficos => AreasGraficos is { } areas ? [.. base.Graficos.Where(g => areas.Contains(g.Area))] : base.Graficos;

    /// <summary>Gráficos de BI (indicadores), que ficam no submenu BI de Relatórios; os demais ficam em Gráficos.</summary>
    protected static bool EhBi(DefinicaoGrafico g) => g.Area.StartsWith("BI", StringComparison.Ordinal);

    /// <summary>Atalhos só das telas que estão nos menus deste módulo.</summary>
    public override IReadOnlyList<AtalhoTela> Atalhos =>
        [.. base.Atalhos.Where(a => a.Chave is { } c && Folhas([.. ItensCadastros, .. Menus]).Any(i => i.Chave == c))];

    protected static IEnumerable<ItemMenu> Folhas(IEnumerable<ItemMenu> itens) => itens.SelectMany(i => i.Filhos is { } f ? Folhas(f) : [i]);

    /// <summary>Itens de menu de uma parte, já com as chaves do programa.</summary>
    protected IReadOnlyList<ItemMenu> De(IModuloDesktop parte, params ItemMenu[] itens) => DaParte(parte, itens);

    /// <summary>Cadastros › Transportadoras só no módulo Frotas (nos demais, a transportadora é só escolhida no alto da tela).</summary>
    public virtual bool EmpresasNoMenuCadastros => false;

    /// <summary>Todas as funções de faturamento (módulo Faturamento); o módulo precisa das partes Frota, Gerencial e Fiscal.</summary>
    protected IReadOnlyList<ItemMenu> FuncoesFaturamento() =>
    [
        .. De(Frota,
            ItemMenu.Tela("Conhecimentos de transporte (CT-e)", T.Ctes),
            ItemMenu.Tela("Manifestos de carga (MDF-e)", T.Manifestos),
            ItemMenu.Separador,
            ItemMenu.Tela("Faturas de frete", T.Faturas)), ItemMenu.Separador,
        .. De(Gerencial, ItemMenu.Tela("Pedidos de venda", E.ViewModels.ErpTelas.PedidosVenda), ItemMenu.Tela("Notas fiscais de saída", Ft.NotaSaida)),
        .. De(Fiscal, ItemMenu.Tela("NFS-e – Notas fiscais de serviço", Ft.Nfse)),
        ItemMenu.Separador,
        ItemMenu.EmBreve("Transmissão de CT-e e MDF-e à SEFAZ"),
        ItemMenu.EmBreve("Emissão de NF-e na SEFAZ"),
        ItemMenu.EmBreve("Transmissão da NFS-e ao Emissor Nacional"),
    ];

    /// <summary>Relatórios do faturamento: conhecimentos, faturas de frete, vendas e recebimentos.</summary>
    protected IReadOnlyList<ItemMenu> RelatoriosFaturamento() =>
        [.. De(Frota, ItemMenu.Tela(TabelasTms.TituloConhecimentos, T.RelConhecimentos), ItemMenu.Tela(TabelasTms.TituloFaturas, T.RelFaturas)),
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
        itens.AddRange(SubmenusGraficos());
        return ItemMenu.Submenu("_Relatórios", [.. itens]);
    }

    /// <summary>Submenus Gráficos e BI (os indicadores) com os gráficos do módulo, precedidos de separador.</summary>
    protected IReadOnlyList<ItemMenu> SubmenusGraficos()
    {
        var itens = new List<ItemMenu>();
        if (Graficos.Where(g => !EhBi(g)).ToList() is { Count: > 0 } graficos)
            itens.Add(ItemMenu.Submenu("Grá_ficos", [.. graficos.Select(g => ItemMenu.Tela(g.Titulo, g.Tela))]));
        if (Graficos.Where(EhBi).ToList() is { Count: > 0 } bi)
            itens.Add(ItemMenu.Submenu("_BI – indicadores", [.. bi.Select(g => ItemMenu.Tela(g.Titulo, g.Tela))]));
        return itens.Count > 0 ? [ItemMenu.Separador, .. itens] : [];
    }

    /// <summary>
    /// Menu Contabilidade de cada módulo: a ligação com o módulo Contabilidade (consulta do plano de contas e dos lançamentos gerados pelo módulo,
    /// as regras de contabilização do módulo e a contabilização em lote, quando a empresa usa esse tipo de contabilização).
    /// </summary>
    protected IReadOnlyList<ItemMenu> MenuContabilidade(params ItemMenu[] doModulo) =>
    [
        ItemMenu.Submenu("Conta_bilidade",
        [
            .. De(Contabil, ItemMenu.Tela("Plano de contas", C.ViewModels.Telas.PlanoContas),
                  ItemMenu.Tela("Consulta de lançamentos", C.ViewModels.Telas.ConsultaLancamentos),
                  ItemMenu.Tela("Saldos de contas", C.ViewModels.Telas.ConsultaSaldos)),
            .. doModulo.Length > 0 ? [ItemMenu.Separador, .. doModulo] : Array.Empty<ItemMenu>(),
            ItemMenu.Separador,
            ItemMenu.Tela("Contabilização em lote", TelasComuns.ContabilizacaoLote).SoCom(MainViewModelBase.RecursoContabilizacaoLote),
        ]),
    ];

    /// <summary>Regras de contabilização e contabilização das notas fiscais (módulos com a parte Fiscal).</summary>
    protected IReadOnlyList<ItemMenu> ContabilizacaoNotas() =>
        De(Fiscal, ItemMenu.Tela("Regras de contabilização", Ft.RegrasContabilizacao),
           ItemMenu.Tela("Contabilizar notas fiscais", Ft.ContabilizarNotas).SoCom(F.ModuloFiscal.RecursoContabilizacaoLote));

    /// <summary>Relatórios de manutenção da frota.</summary>
    protected IReadOnlyList<ItemMenu> RelatoriosManutencao() => De(Frota,
        ItemMenu.Tela("Acompanhamento das ordens de serviço", T.RelAcompanhamento),
        ItemMenu.Tela("Aprovações de serviços", T.RelAprovacoes),
        ItemMenu.Tela("Manutenções preventivas", T.RelPreventivas),
        ItemMenu.Tela("Histórico de manutenção por veículo", T.RelHistoricoManutencao));

    /// <summary>Relatórios das compras: compras, notas de compra da manutenção, contas a pagar e pagamentos.</summary>
    protected IReadOnlyList<ItemMenu> RelatoriosCompras() =>
        [.. De(Gerencial, ItemMenu.Tela("Relatório de compras", Ft.ResumoCompras)),
         .. De(Frota, ItemMenu.Tela("Notas fiscais de compra (manutenção)", T.RelNotas))];

    protected IReadOnlyList<ItemMenu> RelatoriosContasPagar() => De(Gerencial,
        ItemMenu.Tela("Relatório de contas a pagar", Ft.RelDuplicatasPagar),
        ItemMenu.Tela("Relatório de pagamentos", Ft.RelPagamentos));
}

/// <summary>Frotas: transportadoras, veículos, motoristas, abastecimentos, viagens, diárias, pedágio, manutenção e multas.</summary>
public sealed class ModuloFrotaLogistica() : ModuloLogistica(Frota, Contabil)
{
    protected override IReadOnlyList<(string Titulo, IModuloDesktop Parte)> AbasParametros => [("Frota e manutenção", Frota)];
    public override string NomeCurto => "Frotas";
    public override string Subtitulo => "Veículos, motoristas, abastecimentos, viagens, diárias, pedágio, manutenção e multas";
    public override bool EmpresasNoMenuCadastros => true;
    public override string CorDestaque => "#0284C7";
    public override string CorBarra => "#0C4A6E";
    public override string Icone => IconesModulos.Frota;
    protected override IReadOnlyList<string>? AreasGraficos => ["Frota"];

    public override IReadOnlyList<ItemMenu> ItensCadastros => De(Frota,
        ItemMenu.Tela("Veículos", T.Veiculos), ItemMenu.Tela("Motoristas", T.Motoristas), ItemMenu.Tela("Frotas", T.Frotas),
        ItemMenu.Tela("Documentos e vencimentos", T.Documentos));

    public override IReadOnlyList<ItemMenu> Menus =>
    [
        .. De(Frota, ItemMenu.Submenu("_Operação",
            ItemMenu.Tela("Abastecimentos", T.Abastecimentos),
            ItemMenu.Tela("Viagens", T.Viagens),
            ItemMenu.Tela("Diárias", T.Diarias),
            ItemMenu.Separador,
            ItemMenu.Submenu("_Pedágio",
                ItemMenu.Tela("Passagens e notas", T.Pedagio),
                ItemMenu.Tela("Layouts de arquivo", T.LayoutsPedagio))),
            // o antigo módulo Manutenção: as ordens de serviço e a autorização, com os cadastros de planos e de peças e serviços
            ItemMenu.Submenu("_Manutenção",
                ItemMenu.Tela("Ordens de serviço", T.Ordens),
                ItemMenu.Tela("Autorização de serviços", T.Autorizacao),
                ItemMenu.Separador,
                ItemMenu.Tela("Planos de manutenção", T.Planos),
                ItemMenu.Tela("Peças e serviços", T.Itens)),
            ItemMenu.Submenu("M_onitores", ItemMenu.Tela("Monitor de multas de trânsito", T.Multas))),
        .. MenuContabilidade(),
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
            ItemMenu.Tela("Vencimentos e manutenções", T.RelVencimentos)),
            RelatoriosManutencao()),
    ];
}

/// <summary>Transporte (TMS): tabelas de frete, rotas, cotações, coletas, entregas, roteirizador e CIOT.</summary>
public sealed class ModuloTransporte() : ModuloLogistica(Frota, Contabil)
{
    protected override IReadOnlyList<(string Titulo, IModuloDesktop Parte)> AbasParametros => [("Frota e transporte", Frota)];
    public override string NomeCurto => "Transporte";
    public override string Subtitulo => "TMS: tabelas de frete, rotas, cotações, coletas, entregas, roteirizador e CIOT";
    public override string CorDestaque => "#0D9488";
    public override string CorBarra => "#134E4A";
    public override string Icone => IconesModulos.Transporte;
    protected override IReadOnlyList<string>? AreasGraficos => ["Transporte"];

    public override IReadOnlyList<ItemMenu> ItensCadastros => De(Frota, ItemMenu.Tela("Tabelas de frete", T.TabelasFrete), ItemMenu.Tela("Rotas", T.Rotas));

    public override IReadOnlyList<ItemMenu> Menus =>
    [
        .. De(Frota,
            ItemMenu.Submenu("_Operação",
                ItemMenu.Tela("Cotações de frete", T.Cotacoes),
                ItemMenu.Tela("Ordens de coleta", T.Coletas),
                ItemMenu.Tela("Entregas e ocorrências", T.Entregas),
                ItemMenu.Separador,
                ItemMenu.Tela("Roteirizador", T.Roteirizador),
                ItemMenu.Tela("CIOT – operações de transporte", T.Ciot))),
        .. MenuContabilidade(),
        Relatorios(De(Frota,
            ItemMenu.Tela(TabelasTms.TituloFretesCliente, T.RelFretesCliente),
            ItemMenu.Tela(TabelasTms.TituloFretesTrecho, T.RelFretesTrecho),
            ItemMenu.Tela(TabelasTms.TituloPrazoEntrega, T.RelPrazoEntrega)),
            De(Frota,
            ItemMenu.Tela("Coletas por cliente", T.RelColetasCliente),
            ItemMenu.Tela("Coletas por rota", T.RelColetasRota),
            ItemMenu.Tela("Coletas por motorista", T.RelColetasMotorista))),
    ];
}

/// <summary>Faturamento: CT-e, MDF-e, faturas de frete, pedidos de venda, notas fiscais de saída e NFS-e.</summary>
public sealed class ModuloFaturamento() : ModuloLogistica(Frota, Gerencial, Fiscal, Contabil)
{
    protected override IReadOnlyList<(string Titulo, IModuloDesktop Parte)> AbasParametros => [("Faturamento", Gerencial), ("CT-e e faturas de frete", Frota)];
    public override string NomeCurto => "Faturamento";
    public override string Subtitulo => "CT-e, MDF-e, faturas de frete, vendas, notas fiscais de saída e NFS-e";
    public override string CorDestaque => "#EA580C";
    public override string CorBarra => "#7C2D12";
    public override string Icone => IconesModulos.Faturamento;

    public override IReadOnlyList<ItemMenu> ItensCadastros => De(Gerencial,
        ItemMenu.Tela("Produtos e serviços", Ft.Produtos), ItemMenu.Tela("Regras fiscais", Ft.RegrasFiscais), ItemMenu.Tela("Formas de pagamento", TelasComuns.FormasPagamento));

    public override IReadOnlyList<ItemMenu> Menus =>
    [
        ItemMenu.Submenu("_Faturamento", [.. FuncoesFaturamento()]),
        .. MenuContabilidade([.. ContabilizacaoNotas()]),
        Relatorios(RelatoriosFaturamento()),
    ];
}

/// <summary>Compras: pedidos de compra, notas de entrada, importação de XML, estoque e contas a pagar.</summary>
public sealed class ModuloCompras() : ModuloLogistica(Gerencial, Fiscal, Frota, Contabil)
{
    protected override IReadOnlyList<(string Titulo, IModuloDesktop Parte)> AbasParametros => [("Compras e estoque", Gerencial)];
    public override string NomeCurto => "Compras";
    public override string Subtitulo => "Pedidos de compra, notas de entrada, XML, estoque e contas a pagar";
    public override string CorDestaque => "#CA8A04";
    public override string CorBarra => "#713F12";
    public override string Icone => IconesModulos.Compras;

    public override IReadOnlyList<ItemMenu> ItensCadastros => De(Gerencial,
        ItemMenu.Tela("Produtos e serviços", Ft.Produtos), ItemMenu.Tela("Regras fiscais", Ft.RegrasFiscais), ItemMenu.Tela("Formas de pagamento", TelasComuns.FormasPagamento));

    public override IReadOnlyList<ItemMenu> Menus =>
    [
        ItemMenu.Submenu("C_ompras",
        [
            .. De(Gerencial,
                ItemMenu.Tela("Pedidos de compra", E.ViewModels.ErpTelas.PedidosCompra),
                ItemMenu.Tela("Notas fiscais de entrada", Ft.NotaEntrada),
                ItemMenu.Tela("Importação de XML", Ft.ImportacaoXml)),
            .. De(Frota, ItemMenu.Tela("Notas de compra da manutenção", T.Notas)),
            ItemMenu.Separador,
            .. De(Gerencial, ItemMenu.Submenu("_Estoque", ItemMenu.Tela("Posição e movimentos", E.ViewModels.ErpTelas.Estoque))),
        ]),
        // o financeiro das compras: as contas a pagar aos fornecedores (as baixas e a conciliação seguem no Financeiro)
        .. De(Gerencial, ItemMenu.Submenu("F_inanceiro", ItemMenu.Tela("Contas a pagar", Ft.ContasPagar))),
        .. MenuContabilidade([.. ContabilizacaoNotas()]),
        Relatorios(RelatoriosCompras(), RelatoriosContasPagar()),
    ];
}

/// <summary>Financeiro: contas a receber, consórcios e financiamentos, conciliação bancária, imobilizado, cobranças e BI.</summary>
public sealed class ModuloFinanceiro() : ModuloLogistica(Gerencial, Fiscal, Frota, Contabil)
{
    protected override IReadOnlyList<(string Titulo, IModuloDesktop Parte)> AbasParametros => [("Financeiro", Gerencial)];
    public override string NomeCurto => "Financeiro";
    public override string Subtitulo => "Contas a receber, consórcios, financiamentos, conciliação, imobilizado, cobranças e BI";
    public override string CorDestaque => "#16A34A";
    public override string CorBarra => "#14532D";
    public override string Icone => IconesModulos.Financeiro;
    protected override IReadOnlyList<string>? AreasGraficos => ["Financeiro", F.GraficosFiscais.AreaBi];

    public override IReadOnlyList<ItemMenu> ItensCadastros => De(Gerencial,
        ItemMenu.Tela("Contas bancárias", TelasComuns.ContasBancarias), ItemMenu.Tela("Formas de pagamento", TelasComuns.FormasPagamento));

    public override IReadOnlyList<ItemMenu> Menus =>
    [
        ItemMenu.Submenu("_Contas",
        [
            .. De(Gerencial, ItemMenu.Tela("Contas a receber", Ft.ContasReceber)),
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
        .. MenuContabilidade(),
        Relatorios(De(Gerencial,
            ItemMenu.Tela("Relatório de contas a receber", Ft.RelDuplicatasReceber),
            ItemMenu.Tela("Posição por vencimento", Ft.RelPosicaoDuplicatas),
            ItemMenu.Tela("Relatório de contas a pagar", Ft.RelDuplicatasPagar),
            ItemMenu.Tela("Relatório de pagamentos", Ft.RelPagamentos))),
    ];
}

/// <summary>
/// Gestão: painel do empresário, monitor de notas fiscais e os relatórios de todos os módulos, por categoria (Frotas, Manutenção, Transporte,
/// Faturamento, Compras, Financeiro e Contabilidade), com os gráficos e o BI.
/// </summary>
public sealed class ModuloGestao() : ModuloLogistica(Gerencial, Fiscal, Frota, Contabil)
{
    protected override IReadOnlyList<(string Titulo, IModuloDesktop Parte)> AbasParametros => [("Gestão", Gerencial)];
    public override string NomeCurto => "Gestão";
    public override string Subtitulo => "Painel do empresário, monitores e relatórios de todos os módulos, com BI";
    public override string CorDestaque => "#DB2777";
    public override string CorBarra => "#831843";
    public override string Icone => IconesModulos.Gerencial;
    protected override IReadOnlyList<string>? AreasGraficos => null;

    public override IReadOnlyList<ItemMenu> ItensCadastros => [];

    private static ItemMenu Categoria(string titulo, params IEnumerable<ItemMenu>[] blocos)
    {
        var itens = new List<ItemMenu>();
        foreach (var b in blocos.Where(b => b.Any()))
        {
            if (itens.Count > 0) itens.Add(ItemMenu.Separador);
            itens.AddRange(b);
        }
        return ItemMenu.Submenu(titulo, [.. itens]);
    }

    public override IReadOnlyList<ItemMenu> Menus =>
    [
        .. De(Gerencial, ItemMenu.Submenu("_Painel", ItemMenu.Tela("Painel do empresário", E.ViewModels.ErpTelas.Painel))),
        .. De(Gerencial, ItemMenu.Submenu("_Monitores", ItemMenu.Tela("Monitor de notas fiscais", E.ViewModels.ErpTelas.MonitorNotas),
                                                        ItemMenu.Tela("Monitor de cobranças", E.ViewModels.ErpTelas.MonitorCobrancas))),
        ItemMenu.Submenu("_Relatórios",
        [
            Categoria("_Frotas", De(Frota,
                ItemMenu.Tela("Painel da frota", T.Painel), ItemMenu.Tela("Resumo gerencial da frota", T.RelResumo),
                ItemMenu.Tela("Custos por veículo", T.RelCustos), ItemMenu.Tela("Custos por frota", T.RelCustosFrota),
                ItemMenu.Tela("Consumo de combustível", T.RelConsumo), ItemMenu.Tela("Resultado das viagens", T.RelViagens),
                ItemMenu.Tela("Diárias por colaborador", T.RelDiariasColaborador), ItemMenu.Tela("Vencimentos e manutenções", T.RelVencimentos))),
            Categoria("_Manutenção", RelatoriosManutencao()),
            Categoria("_Transporte", De(Frota,
                ItemMenu.Tela(TabelasTms.TituloFretesCliente, T.RelFretesCliente), ItemMenu.Tela(TabelasTms.TituloFretesTrecho, T.RelFretesTrecho),
                ItemMenu.Tela(TabelasTms.TituloPrazoEntrega, T.RelPrazoEntrega), ItemMenu.Tela("Coletas por cliente", T.RelColetasCliente))),
            Categoria("F_aturamento", RelatoriosFaturamento()),
            Categoria("C_ompras", RelatoriosCompras()),
            Categoria("F_inanceiro", De(Gerencial,
                ItemMenu.Tela("Relatório de contas a receber", Ft.RelDuplicatasReceber), ItemMenu.Tela("Relatório de contas a pagar", Ft.RelDuplicatasPagar),
                ItemMenu.Tela("Posição por vencimento", Ft.RelPosicaoDuplicatas), ItemMenu.Tela("Relatório de pagamentos", Ft.RelPagamentos))),
            Categoria("_Contabilidade", De(Contabil,
                ItemMenu.Tela("Balancete de verificação", C.ViewModels.Telas.Balancete), ItemMenu.Tela("Balanço Patrimonial", C.ViewModels.Telas.Balanco),
                ItemMenu.Tela("Demonstração do Resultado (DRE)", C.ViewModels.Telas.Dre))),
            ItemMenu.Separador,
            // os gráficos de todos os módulos, por área, e os indicadores de BI
            .. Graficos.Where(g => !EhBi(g)).GroupBy(g => g.Area).Select(a =>
                ItemMenu.Submenu($"Gráficos – {a.Key}", [.. a.Select(g => ItemMenu.Tela(g.Titulo, g.Tela))])),
            .. Graficos.Where(EhBi).ToList() is { Count: > 0 } bi ? [ItemMenu.Submenu("_BI – indicadores", [.. bi.Select(g => ItemMenu.Tela(g.Titulo, g.Tela))])] : Array.Empty<ItemMenu>(),
        ]),
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
    protected override IReadOnlyList<string>? AreasGraficos => ["Contábil"];

    public override IReadOnlyList<ItemMenu> ItensCadastros => DaParte(Contabil, Contabil.ItensCadastros);

    /// <summary>Os menus do Contábil, com os gráficos no fim de Relatórios.</summary>
    public override IReadOnlyList<ItemMenu> Menus =>
        [.. DaParte(Contabil, Contabil.Menus).Select(m => m.Titulo.Replace("_", "") == "Relatórios" && Graficos.Count > 0
            ? m with { Filhos = [.. m.Filhos!, .. SubmenusGraficos()] }
            : m)];
}

/// <summary>Uma janela do Vorticis Suite Logística por módulo (a Suite reúne as sete).</summary>
public sealed class MainViewModel(ModuloLogistica modulo) : MainViewModelBase(modulo);
