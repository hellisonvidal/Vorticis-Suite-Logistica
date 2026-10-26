using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Desktop.Comum.ViewModels;
using C = Vorticis.Desktop;
using E = Vorticis.Erp.Desktop;
using F = Vorticis.Fiscal.Desktop;
using Fr = Vorticis.Frota.Desktop;

namespace Vorticis.Logistica.Desktop;

/// <summary>
/// Vorticis Suite Logística: o programa da transportadora. Reúne num menu só as telas do Frota (operação, manutenção e TMS), do Gerencial
/// (financeiro, compras e estoque), as de faturamento no menu Emissão e, no menu Gerencial, as do Contábil. Não mostra o Fiscal (o usuário usa o
/// Financeiro), o calendário de obrigações nem o painel do empresário; todos os relatórios ficam no menu Relatórios. As telas são as dos módulos (mesmas regras,
/// permissões e banco); a empresa ativa é a transportadora, cadastrada já com todos esses módulos.
/// </summary>
public sealed class ModuloLogistica() : ModuloCombinado(Frota, Contabil, Fiscal, Gerencial), Vorticis.Desktop.Comum.Modulos.IModuloDesktop
{
    private static readonly Fr.ModuloFrota Frota = new();
    private static readonly C.ModuloContabil Contabil = new();
    private static readonly F.ModuloFiscal Fiscal = new();
    private static readonly E.ModuloErp Gerencial = new();

    /// <summary>Telas (DataTemplates) dos módulos reunidos, do App.axaml de cada um.</summary>
    public static IEnumerable<Avalonia.Application> Apps() => [new Fr.App(), new C.App(), new F.App(), new E.App()];

    public override string Nome => "Vorticis Suite Logística";
    public override Vorticis.Application.Cadastros.ModulosEmpresa ModuloEmpresa => Vorticis.Application.Cadastros.ModulosEmpresa.Frota;
    public override string NomeCurto => "Logística";
    public override string Subtitulo => "Frota, TMS, manutenção, financeiro e contábil da transportadora";
    public override string NomeExecutavel => "VorticisLogistica";
    public override string CorDestaque => "#0F766E";   // verde-petróleo (ícone e selo do programa)
    public override string CorBarra => "#134E4A";
    public bool UsaCalendarioObrigacoes => false;
    public string RotuloEmpresas => "Transportadoras";
    public string RotuloEmpresaAtiva => "Transportadora";
    /// <summary>Os contadores ficam só na aba Contadores de Cadastros › Transportadoras.</summary>
    public bool ContadoresNoMenuCadastros => false;

    /// <summary>Atalhos das partes, sem os de telas que este programa não mostra (ex.: Consulta de documentos fiscais).</summary>
    public override IReadOnlyList<AtalhoTela> Atalhos =>
        [.. base.Atalhos.Where(a => a.Chave != Chave(Fiscal, F.ViewModels.Telas.Documentos))];

    private static ItemMenu? Topo(Vorticis.Desktop.Comum.Modulos.IModuloDesktop parte, string titulo) =>
        parte.Menus.FirstOrDefault(m => m.Titulo.Replace("_", "") == titulo);

    public override IReadOnlyList<ItemMenu> ItensCadastros =>
    [
        .. DaParte(Frota, Frota.ItensCadastros),
        ItemMenu.Separador,
        ItemMenu.Submenu("_Gerencial", [.. DaParte(Gerencial, Gerencial.ItensCadastros)]),
        ItemMenu.Submenu("_Contábil", [.. DaParte(Contabil, Contabil.ItensCadastros)]),
    ];

    /// <summary>
    /// Cadastros › Parâmetros é uma janela só, com uma aba para cada área (cada uma é a tela de parâmetros do módulo, com os mesmos campos e a mesma
    /// gravação) e a aba Menus. Os parâmetros do Fiscal são os mesmos do Gerencial (contas a pagar e a receber), por isso não têm aba própria.
    /// </summary>
    protected override void RegistrarTelasPrimeiro(CatalogoTelas catalogo) =>
        catalogo.Instancia("ParametrosLogistica", m => new ParametrosAbasViewModel(m,
            [
                ("Frota", Chave(Frota, TelasComuns.Parametros)),
                ("Gerencial", Chave(Gerencial, TelasComuns.Parametros)),
                ("Contábil", Chave(Contabil, TelasComuns.Parametros)),
            ]), atualizarPermissoes: v => v.AtualizarPermissoes(), aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(TelasComuns.Parametros, "Cadastros › Parâmetros", m => m.TemAcessoNaEmpresa, v => v.CarregarAsync());

    private static string Sem(ItemMenu m) => m.Titulo.Replace("_", "");

    public override IReadOnlyList<ItemMenu> Menus
    {
        get
        {
            var gerencial = DaParte(Gerencial, Gerencial.Menus);
            ItemMenu? G(string titulo) => gerencial.FirstOrDefault(m => Sem(m) == titulo);
            var financeiro = G("Financeiro")!;
            // Relatórios de cada área, reunidos no menu Relatórios (saem dos menus de origem)
            // Relatórios do Frota em blocos separados por separador: operação, manutenção e TMS
            var blocos = new List<List<ItemMenu>> { new() };
            foreach (var i in DaParte(Frota, Topo(Frota, "Relatórios")!.Filhos!))
                if (i.EhSeparador) blocos.Add([]); else blocos[^1].Add(i);
            var (relOperacao, relManutencao, relTms) = (blocos[0], blocos[1], blocos[2]);
            // sem o Painel do empresário (e o separador que o seguia)
            var relGerencial = G("Relatórios")!.Filhos!.Where(i => i.Chave != E.ViewModels.ErpTelas.Painel).SkipWhile(i => i.EhSeparador).ToList();
            var relContabil = DaParte(Contabil, Topo(Contabil, "Relatórios")!.Filhos!);
            var operacao = DaParte(Frota, Frota.Menus.Where(m => Sem(m) == "Operação")).Single();
            var transporte = DaParte(Frota, Frota.Menus.Where(m => Sem(m) == "Emissão")).Single();
            // Operações: a operação da frota e do transporte, mais compras, estoque e monitores (vindos do Gerencial)
            ItemMenu Sub(ItemMenu m) => m with { Titulo = m.Titulo.Replace("_", "") };
            return
            [
                ItemMenu.Submenu("_Operações",
                    [.. operacao.Filhos!, ItemMenu.Separador, .. new[] { G("Compras"), G("Estoque"), G("Monitores") }.Where(m => m is not null).Select(m => Sub(m!))]),
                .. DaParte(Frota, Frota.Menus.Where(m => Sem(m) == "Manutenção")),
                // Emissão: as funções do Faturamento (pedidos de venda e notas de saída) e os documentos do TMS (cotações, CT-e, MDF-e e faturas de frete)
                ItemMenu.Submenu("_Emissão",
                    [.. G("Faturamento")!.Filhos!, ItemMenu.Separador,
                     .. transporte.Filhos!, ItemMenu.Separador,
                     ItemMenu.EmBreve("Transmissão de CT-e e MDF-e à SEFAZ"),
                     ItemMenu.EmBreve("NFS-e – Nota fiscal de serviço (Padrão Nacional)")]),
                ItemMenu.Submenu("_Financeiro", [.. financeiro.Filhos!]),
                ItemMenu.Submenu("_Gerencial",
                    Submenu("_Contábil", Contabil, m => Sem(m) != "Relatórios")),
                ItemMenu.Submenu("_Relatórios",
                    ItemMenu.Submenu("_Frota e operação", [.. relOperacao]),
                    ItemMenu.Submenu("_Manutenção", [.. relManutencao]),
                    ItemMenu.Submenu("_Transporte (TMS)", [.. relTms]),
                    ItemMenu.Submenu("_Gerencial", [.. relGerencial]),
                    ItemMenu.Submenu("_Contábil", [.. relContabil])),
            ];
        }
    }
}

/// <summary>Janela principal do Vorticis Suite Logística.</summary>
public sealed class MainViewModel() : MainViewModelBase(new ModuloLogistica());
