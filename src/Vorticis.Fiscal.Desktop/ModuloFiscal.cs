using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Fiscal.Desktop.ViewModels;

namespace Vorticis.Fiscal.Desktop;

public sealed class ModuloFiscal : IModuloDesktop
{
    public string Nome => "Fiscal";
    public string NomeCurto => "Fiscal";
    public string NomeExecutavel => "fiscal";
    public string Subtitulo => "Regras Fiscais, SPED, NF-e e Obrigações";
    public bool GerenciaSistema => false;
    public IBrush PincelDestaque => new SolidColorBrush(Color.Parse("#0F766E"));
    public IBrush PincelBarra => new SolidColorBrush(Color.Parse("#115E59"));

    public IReadOnlyList<ItemMenu> Menus =>
    [
        new ItemMenu("Regras Fiscais", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Regras de Entrada", ItemMenuTipo.Tela, "Fiscal.RegrasEntrada"),
            new ItemMenu("Regras de Saída", ItemMenuTipo.Tela, "Fiscal.RegrasSaida"),
            new ItemMenu("CST ICMS/IPI/PIS/COFINS", ItemMenuTipo.Tela, "Fiscal.CstCatalog"),
        ]),
        new ItemMenu("SPED Fiscal", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("EFD ICMS/IPI", ItemMenuTipo.Tela, "Fiscal.EfdIcmsIpi"),
            new ItemMenu("EFD Contribuições", ItemMenuTipo.Tela, "Fiscal.EfdContribuicoes"),
        ]),
        new ItemMenu("NF-e / CT-e", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Emissão NF-e", ItemMenuTipo.Tela, "Fiscal.EmissaoNfe"),
            new ItemMenu("Emissão CT-e", ItemMenuTipo.Tela, "Fiscal.EmissaoCte"),
            new ItemMenu("Monitor Notas A1/A3", ItemMenuTipo.Tela, "Fiscal.MonitorNotas"),
        ]),
        new ItemMenu("Obrigações Acessórias", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("DCTF", ItemMenuTipo.Tela, "Fiscal.Dctf"),
            new ItemMenu("GIAS", ItemMenuTipo.Tela, "Fiscal.Gias"),
            new ItemMenu("DeSTDA", ItemMenuTipo.Tela, "Fiscal.Destda"),
        ]),
        new ItemMenu("Relatórios", ItemMenuTipo.Submenu, filhos:
        [
            new ItemMenu("Apuração ICMS", ItemMenuTipo.Tela, "Fiscal.RelApuracaoIcms"),
            new ItemMenu("Apuração IPI", ItemMenuTipo.Tela, "Fiscal.RelApuracaoIpi"),
            new ItemMenu("Apuração PIS/COFINS", ItemMenuTipo.Tela, "Fiscal.RelApuracaoPisCofins"),
        ]),
    ];

    public IReadOnlyList<AtalhoPainel> Atalhos =>
    [
        new AtalhoPainel("Fiscal.RegrasEntrada", "Regras Entrada", "Regras fiscais de entrada"),
        new AtalhoPainel("Fiscal.RegrasSaida", "Regras Saída", "Regras fiscais de saída"),
        new AtalhoPainel("Fiscal.MonitorNotas", "Monitor Notas", "Monitor de notas A1/A3"),
        new AtalhoPainel("Fiscal.EfdIcmsIpi", "SPED Fiscal", "EFD ICMS/IPI"),
    ];

    public void RegistrarTelas(CatalogoTelas catalogo)
    {
        catalogo.Registrar("Fiscal.RegrasEntrada", () => new RegrasFiscaisView());
        catalogo.Registrar("Fiscal.RegrasSaida", () => new RegrasFiscaisView());
        catalogo.Registrar("Fiscal.CstCatalog", () => new CstCatalogView());
        catalogo.Registrar("Fiscal.EfdIcmsIpi", () => new EfdIcmsIpiView());
        catalogo.Registrar("Fiscal.EfdContribuicoes", () => new EfdContribuicoesView());
        catalogo.Registrar("Fiscal.EmissaoNfe", () => new EmissaoNfeView());
        catalogo.Registrar("Fiscal.EmissaoCte", () => new EmissaoCteView());
        catalogo.Registrar("Fiscal.MonitorNotas", () => new MonitorNotasView());
        catalogo.Registrar("Fiscal.Dctf", () => new DctfView());
        catalogo.Registrar("Fiscal.Gias", () => new GiasView());
        catalogo.Registrar("Fiscal.Destda", () => new DestdaView());
        catalogo.Registrar("Fiscal.RelApuracaoIcms", () => new RelApuracaoIcmsView());
        catalogo.Registrar("Fiscal.RelApuracaoIpi", () => new RelApuracaoIpiView());
        catalogo.Registrar("Fiscal.RelApuracaoPisCofins", () => new RelApuracaoPisCofinsView());
    }
}