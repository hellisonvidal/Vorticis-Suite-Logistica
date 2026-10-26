using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Domain.Seguranca;
using Vorticis.Folha.Desktop.ViewModels;

namespace Vorticis.Folha.Desktop;

/// <summary>
/// Módulo Folha de Pagamento: cadastros (funcionários, cargos, departamentos, rubricas, parâmetros), folha mensal com eventos
/// variáveis, férias, 13º salário, rescisões, provisões, contabilização, eSocial e relatórios. As permissões (FOLHA_*) são
/// verificadas nos serviços; o menu apenas esconde o que o perfil não permite.
/// </summary>
public sealed class ModuloFolha : IModuloDesktop
{
    public void PrepararServicos(Vorticis.Infra.VorticisApp app) => _ = app.Folha();

    public string Nome => "Vorticis Folha de Pagamento";
    public Vorticis.Application.Cadastros.ModulosEmpresa ModuloEmpresa => Vorticis.Application.Cadastros.ModulosEmpresa.Folha;
    public string NomeCurto => "Folha";
    public string Subtitulo => "Folha de pagamento e eSocial";
    public string NomeExecutavel => "VorticisFolha";
    public string CorDestaque => "#7E22CE";   // roxo (ícone e selo do módulo)
    public string CorBarra => "#3B0764";      // roxo-escuro

    // Clientes e fornecedores não têm uso na folha; a competência de trabalho fica ao lado do seletor de empresas.
    public bool UsaClientesFornecedores => false;
    public string? RotuloPeriodo => "Competência";

    public IReadOnlyList<ItemMenu> ItensCadastros { get; } =
    [
        ItemMenu.Submenu("Funcionários",
            ItemMenu.Tela("Cadastro de funcionários", FolhaTelas.Funcionarios),
            ItemMenu.Tela("Cargos", FolhaTelas.Cargos),
            ItemMenu.Tela("Departamentos", FolhaTelas.Departamentos),
            ItemMenu.Tela("Rubricas (eventos)", FolhaTelas.Rubricas),
            ItemMenu.Separador,
            ItemMenu.Tela("Parâmetros e tabelas legais", FolhaTelas.Parametros)),
    ];

    public IReadOnlyList<ItemMenu> Menus { get; } =
    [
        ItemMenu.Submenu("C_álculos",
            ItemMenu.Submenu("_Folha",
                ItemMenu.Tela("Eventos variáveis", FolhaTelas.Eventos),
                ItemMenu.Tela("Cálculo da folha mensal", FolhaTelas.FolhaMensal),
                ItemMenu.Tela("Férias", FolhaTelas.Ferias),
                ItemMenu.Tela("13º salário", FolhaTelas.Decimo),
                ItemMenu.Tela("Rescisões", FolhaTelas.Rescisoes),
                ItemMenu.Separador,
                ItemMenu.Tela("Provisões de férias e 13º", FolhaTelas.Provisoes),
                ItemMenu.Tela("Contabilização", FolhaTelas.Contabilizacao)),
            ItemMenu.Submenu("_Pró-labore",
                ItemMenu.Tela("Sócios e cálculo do pró-labore", FolhaTelas.ProLabore),
                ItemMenu.Tela("Sócios (cadastro da empresa)", Vorticis.Desktop.Comum.ViewModels.TelasComuns.Socios))),
        ItemMenu.Submenu("_Obrigações",
            ItemMenu.Submenu("eSocial",
                ItemMenu.Tela("Assistente de configuração", FolhaTelas.AssistenteEsocial),
                ItemMenu.Separador,
                ItemMenu.Tela("Geração dos eventos", FolhaTelas.ESocial),
                ItemMenu.Tela("Transmissão", FolhaTelas.Transmissao)),
            ItemMenu.EmBreve("FGTS (GFD)"),
            ItemMenu.EmBreve("DCTFWeb e INSS"),
            ItemMenu.EmBreve("RAIS e DIRF")),
        ItemMenu.Submenu("_Relatórios",
            ItemMenu.Tela("Holerites e recibos", FolhaTelas.Holerites),
            ItemMenu.Tela("Resumo da folha", FolhaTelas.Resumo),
            ItemMenu.Tela("Encargos patronais", FolhaTelas.Encargos),
            ItemMenu.Tela("Provisões", FolhaTelas.Provisoes)),
    ];

    public IReadOnlyList<AtalhoTela> Atalhos { get; } =
    [
        new("Ctrl+F", FolhaTelas.FolhaMensal, "Cálculo da folha mensal"),
        new("Ctrl+E", FolhaTelas.Eventos, "Eventos variáveis"),
    ];

    private static bool Pode(Vorticis.Desktop.Comum.ViewModels.MainViewModelBase m, params Permissao[] permissoes)
        => m.TemAcessoNaEmpresa && permissoes.Any(m.Pode);

    private static bool Ver(Vorticis.Desktop.Comum.ViewModels.MainViewModelBase m)
        => Pode(m, Permissao.FolhaConsultar, Permissao.FolhaCadastrosManter, Permissao.FolhaParametrosManter, Permissao.FolhaEventosLancar, Permissao.FolhaCalcular,
                Permissao.FolhaFechar, Permissao.FolhaReabrir, Permissao.FolhaContabilizar, Permissao.FolhaESocial, Permissao.FolhaRelatorios);

    public void RegistrarTelas(CatalogoTelas c)
    {
        c.Instancia(nameof(MainViewModel.Funcionarios), m => new FuncionariosViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FolhaTelas.Funcionarios, "Cadastros › Funcionários › Cadastro de funcionários", Ver, v => v.CarregarAsync());
        c.Instancia(nameof(MainViewModel.Cargos), m => new CargosViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FolhaTelas.Cargos, "Cadastros › Funcionários › Cargos", Ver, v => v.CarregarAsync());
        c.Instancia(nameof(MainViewModel.Departamentos), m => new DepartamentosViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FolhaTelas.Departamentos, "Cadastros › Funcionários › Departamentos", Ver, v => v.CarregarAsync());
        c.Instancia(nameof(MainViewModel.Rubricas), m => new RubricasViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FolhaTelas.Rubricas, "Cadastros › Funcionários › Rubricas (eventos)", Ver, v => v.CarregarAsync());
        c.Instancia("ParametrosModulo", m => new ParametrosModuloFolhaViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(Vorticis.Desktop.Comum.ViewModels.TelasComuns.Parametros, "Cadastros › Parâmetros", m => m.TemAcessoNaEmpresa, v => v.CarregarAsync());
        c.Instancia(nameof(MainViewModel.Parametros), m => new ParametrosFolhaViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FolhaTelas.Parametros, "Cadastros › Funcionários › Parâmetros e tabelas legais", Ver, v => v.CarregarAsync());

        c.Instancia(nameof(MainViewModel.Eventos), m => new EventosVariaveisViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FolhaTelas.Eventos, "Cálculos › Folha › Eventos variáveis", Ver, v => v.CarregarAsync());
        c.Instancia(nameof(MainViewModel.FolhaMensal), m => new FolhaMensalViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoLimpar: v => { v.Calculos.Clear(); v.Itens.Clear(); },
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FolhaTelas.FolhaMensal, "Cálculos › Folha › Cálculo da folha mensal", Ver, v => v.CarregarAsync())
            .Tela(FolhaTelas.Holerites, "Relatórios › Holerites e recibos", m => Pode(m, Permissao.FolhaRelatorios, Permissao.FolhaConsultar, Permissao.FolhaCalcular), v => v.CarregarAsync());
        c.Instancia(nameof(MainViewModel.Ferias), m => new FeriasViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FolhaTelas.Ferias, "Cálculos › Folha › Férias", Ver, v => v.CarregarAsync());
        c.Instancia(nameof(MainViewModel.Decimo), m => new DecimoTerceiroViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoLimpar: v => { v.Calculos.Clear(); v.Itens.Clear(); },
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FolhaTelas.Decimo, "Cálculos › Folha › 13º salário", Ver, v => v.CarregarAsync());
        c.Instancia(nameof(MainViewModel.Rescisoes), m => new RescisoesViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FolhaTelas.Rescisoes, "Cálculos › Folha › Rescisões", Ver, v => v.CarregarAsync());
        c.Instancia(nameof(MainViewModel.ProLabore), m => new ProLaboreViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FolhaTelas.ProLabore, "Cálculos › Pró-labore › Sócios e cálculo do pró-labore", Ver, v => v.CarregarAsync());
        c.Instancia(nameof(MainViewModel.Contabilizacao), m => new ContabilizacaoFolhaViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FolhaTelas.Contabilizacao, "Cálculos › Folha › Contabilização", m => Pode(m, Permissao.FolhaContabilizar, Permissao.FolhaParametrosManter), v => v.CarregarAsync());

        c.Instancia(nameof(MainViewModel.ESocial), m => new ESocialViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoLimpar: v => { v.Eventos.Clear(); v.Xml = ""; },
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FolhaTelas.ESocial, "Obrigações › eSocial", m => Pode(m, Permissao.FolhaESocial), v => v.CarregarAsync());
        c.Instancia("AssistenteEsocial", m => new AssistenteEsocialViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FolhaTelas.AssistenteEsocial, "Obrigações › eSocial › Assistente de configuração", m => Pode(m, Permissao.FolhaESocial), v => v.CarregarAsync());
        c.Instancia(nameof(MainViewModel.Transmissao), m => new TransmissaoEsocialViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoLimpar: v => v.Limpar(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(FolhaTelas.Transmissao, "Obrigações › eSocial › Transmissão", m => Pode(m, Permissao.FolhaESocial), v => v.CarregarAsync());

        c.Instancia(nameof(MainViewModel.Resumo), m => new ResumoFolhaViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoesExportacao(), aoLimpar: v => v.Limpar())
            .Tela(FolhaTelas.Resumo, "Relatórios › Resumo da folha", m => Pode(m, Permissao.FolhaRelatorios));
        c.Instancia(nameof(MainViewModel.Encargos), m => new EncargosFolhaViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoesExportacao(), aoLimpar: v => v.Limpar())
            .Tela(FolhaTelas.Encargos, "Relatórios › Encargos patronais", m => Pode(m, Permissao.FolhaRelatorios));
        c.Instancia(nameof(MainViewModel.Provisoes), m => new ProvisoesFolhaViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(), aoLimpar: v => v.Limpar())
            .Tela(FolhaTelas.Provisoes, "Relatórios › Provisões de férias e 13º", m => Pode(m, Permissao.FolhaRelatorios, Permissao.FolhaCalcular));
    }
}
