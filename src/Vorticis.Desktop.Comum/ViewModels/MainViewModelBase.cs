using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Cadastros;
using Vorticis.Application.Seguranca;
using Vorticis.Application.Sistema;
using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra;

namespace Vorticis.Desktop.Comum.ViewModels;

/// <summary>Período (competência) da lista ao lado da empresa.</summary>
public sealed record OpcaoPeriodo(DateOnly Periodo, bool Fechado)
{
    public string Texto => Fechado ? $"{Periodo:MM/yyyy} (fechada)" : $"{Periodo:MM/yyyy}";
}

/// <summary>Contexto compartilhado pelos módulos: aplicação conectada, usuário logado e empresa ativa.</summary>
public interface IContexto
{
    VorticisApp? App { get; }
    SessaoUsuario? Sessao { get; }
    long? EmpresaId { get; }
    bool Pode(Permissao permissao);
    Task RecarregarEmpresasAsync(long? selecionar = null);
    void AoConcluirConfiguracaoInicial();
    /// <summary>Pergunta Sim/Não ao usuário (diálogo modal). Usado antes de excluir ou gravar dados obtidos de fora.</summary>
    Task<bool> ConfirmarAsync(string titulo, string mensagem, string textoSim = "Sim", string textoNao = "Não");
    /// <summary>Reconecta ao banco e volta à tela de login, mostrando a mensagem (após restaurar ou limpar o banco).</summary>
    Task ReiniciarParaLoginAsync(string mensagem);
    /// <summary>
    /// "Cadastrar esta empresa em quais módulos?": o módulo atual vem marcado e obrigatório. Devolve null se o usuário cancelar.
    /// </summary>
    Task<ModulosEmpresa?> EscolherModulosAsync(ModulosEmpresa inicial);
    /// <summary>Módulo que este executável hospeda (Contábil, Fiscal ou Folha).</summary>
    ModulosEmpresa ModuloAtual { get; }
    /// <summary>Alerta com campo de senha; devolve a senha digitada ou null se o usuário cancelar.</summary>
    Task<string?> SolicitarSenhaAsync(string titulo, string mensagem, string textoSim = "Confirmar", string textoNao = "Cancelar");
    /// <summary>Razão social e CNPJ da empresa ativa (cabeçalho dos relatórios).</summary>
    string DescricaoEmpresa { get; }
    /// <summary>Abre outra função (ver <see cref="TelasComuns"/> e as chaves de cada módulo), como pelo menu.</summary>
    void AbrirTela(string tela);
    /// <summary>
    /// O primeiro administrador foi cadastrado pela sessão de implantação: o acesso vorticis/vorticis deixa de valer e o sistema
    /// volta à tela de login para o novo administrador entrar.
    /// </summary>
    Task ConcluirImplantacaoAsync(string loginAdministrador);
    /// <summary>Período de trabalho escolhido ao lado da empresa (dia 1 do mês); módulos sem período usam o mês atual.</summary>
    DateOnly PeriodoTrabalho => new(DateTime.Today.Year, DateTime.Today.Month, 1);
    /// <summary>Relê a situação (aberto/fechado) do período de trabalho, após fechar ou reabrir.</summary>
    Task AtualizarSituacaoPeriodoAsync() => Task.CompletedTask;
    /// <summary>Relê os recursos ligados nos parâmetros (mostra ou esconde os menus que dependem deles), após gravar os parâmetros.</summary>
    Task AtualizarRecursosAsync() => Task.CompletedTask;
}

/// <summary>Telas comuns a todos os módulos. A chave é o CommandParameter do item de menu.</summary>
public static class TelasComuns
{
    public const string Empresas = "empresas";
    public const string Contabilidade = "contabilidade";
    public const string Socios = "socios";
    public const string Clientes = "clientes";
    public const string Fornecedores = "fornecedores";
    /// <summary>Cadastros financeiros do casco: cada módulo que os usa (Contábil, Fiscal, ERP) põe o item no seu menu Cadastros.</summary>
    public const string ContasBancarias = "contasbancarias";
    public const string FormasPagamento = "formaspagamento";
    public const string Usuarios = "usuarios";
    public const string Perfis = "perfis";
    public const string AlterarSenha = "senha";
    public const string Conexao = "conexao";
    public const string Backup = "backup";
    public const string Obrigacoes = "obrigacoes";
    /// <summary>Cadastros › Parâmetros: cada módulo registra a sua tela com esta chave.</summary>
    public const string Parametros = "parametros";
    public const string ConfiguracoesGerais = "configuracoesgerais";
    /// <summary>Integrações › Exportar › Contabilização em lote (pendências dos módulos quando a empresa contabiliza em lote).</summary>
    public const string ContabilizacaoLote = "contabilizacaolote";
    public const string Certificados = "certificados";
    public const string Licenca = "licenca";
    public const string Sobre = "sobre";
    /// <summary>Ajuda › Manual do usuário (abre no índice ou no último tópico).</summary>
    public const string Manual = "manual";
    /// <summary>F1: abre o manual no tópico da tela em uso.</summary>
    public const string ManualContextual = "manual.f1";
    public const string Inicio = "inicio";
    /// <summary>Item de menu que encerra a sessão (trocar de usuário); não abre tela.</summary>
    public const string Sair = "sair";
}

/// <summary>
/// Janela principal de qualquer módulo da suíte: conexão com o banco, login, empresa ativa, permissões, navegação
/// por funções em janelas flutuantes e confirmações. As telas próprias do módulo vêm de <see cref="IModuloDesktop"/>.
/// </summary>
public abstract partial class MainViewModelBase : ObservableObject, IContexto, IAsyncDisposable
{
    private string _connectionString = "";
    private string? _telaAtual;
    private string? _telaDaAjuda;
    private PermissoesEfetivas _permissoes = PermissoesEfetivas.Nenhuma;
    private IConsultaCnpj? _consultaCnpj;
    private IConsultaInscricaoEstadual? _consultaIe;
    private bool _configuracaoInicialPendente;
    private bool _implantacao;
    private readonly CatalogoTelas _catalogo = new();
    private readonly Dictionary<string, object> _instancias = [];

    protected MainViewModelBase(IModuloDesktop modulo)
    {
        Modulo = modulo;
        PincelDestaque = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse(modulo.CorDestaque));   // imutável: sem vínculo com a thread de interface
        PincelBarra = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse(modulo.CorBarra));
        Login = new LoginViewModel(this);
        RegistrarTelasComuns(_catalogo);
        modulo.RegistrarTelas(_catalogo);
        JanelasAbertas.CollectionChanged += (_, _) => OnPropertyChanged(nameof(TemJanelasAbertas));
        Menus = MontarMenus();
        Atalhos = [.. modulo.Atalhos, new AtalhoTela("F1", TelasComuns.ManualContextual, "Manual do usuário (tópico desta tela)")];
        RecriarModulos();
    }

    /// <summary>O módulo (Contábil, Fiscal, Folha) que este executável hospeda.</summary>
    public IModuloDesktop Modulo { get; }

    /// <summary>Cor de destaque do módulo (<see cref="IModuloDesktop.CorDestaque"/>): faixa, nome do módulo, cabeçalho das janelas.</summary>
    public IBrush PincelDestaque { get; }

    /// <summary>Cor da barra de menus do módulo (<see cref="IModuloDesktop.CorBarra"/>).</summary>
    public IBrush PincelBarra { get; }

    /// <summary>Menu completo: Cadastros (comuns + do módulo), menus do módulo, Configurações e Ajuda.</summary>
    public IReadOnlyList<ItemMenu> Menus { get; }

    /// <summary>Atalhos de teclado da janela (do módulo e F1 = manual).</summary>
    public IReadOnlyList<AtalhoTela> Atalhos { get; }

    // ------------------------------------------------------------------ instâncias das telas

    /// <summary>
    /// Instâncias novas de todas as telas do sistema (as fixas são criadas só na primeira vez). Chamado a cada logout,
    /// para que nada digitado ou consultado por um usuário (relatórios, formulários, listas) fique visível para o próximo.
    /// </summary>
    private void RecriarModulos()
    {
        foreach (var def in _catalogo.Instancias.Where(d => !d.Fixa || !_instancias.ContainsKey(d.Nome)))
            _instancias[def.Nome] = def.Criar(this);
    }

    /// <summary>ViewModel da instância (propriedades tipadas das classes derivadas usam o nome da própria propriedade).</summary>
    protected T Instancia<T>([CallerMemberName] string nome = "") where T : class => (T)_instancias[nome];

    public LoginViewModel Login { get; }
    public ContasBancariasViewModel ContasBancarias => Instancia<ContasBancariasViewModel>();
    public FormasPagamentoViewModel FormasPagamento => Instancia<FormasPagamentoViewModel>();
    public ConexaoViewModel Conexao => Instancia<ConexaoViewModel>();
    public ContabilizacaoLoteViewModel ContabilizacaoLote => Instancia<ContabilizacaoLoteViewModel>();
    public InicioViewModel Inicio => Instancia<InicioViewModel>();
    public CadastroEmpresaViewModel Empresas => Instancia<CadastroEmpresaViewModel>();
    public UsuariosViewModel Usuarios => Instancia<UsuariosViewModel>();
    public PerfisViewModel Perfis => Instancia<PerfisViewModel>();
    public ContabilidadeEmpresaViewModel Contabilidade => Instancia<ContabilidadeEmpresaViewModel>();
    public SociosViewModel Socios => Instancia<SociosViewModel>();
    public SindicoViewModel Sindico => Instancia<SindicoViewModel>();
    public AlterarSenhaViewModel AlterarSenha => Instancia<AlterarSenhaViewModel>();
    public SobreViewModel Sobre => Instancia<SobreViewModel>();
    public ManualViewModel Manual => Instancia<ManualViewModel>();
    public CentralUsuariosViewModel CentralUsuarios => Instancia<CentralUsuariosViewModel>();
    public LicencaViewModel Licenca => Instancia<LicencaViewModel>();
    public ClientesViewModel Clientes => Instancia<ClientesViewModel>();
    public FornecedoresViewModel Fornecedores => Instancia<FornecedoresViewModel>();
    public ConfiguracoesGeraisViewModel ConfiguracoesGerais => Instancia<ConfiguracoesGeraisViewModel>();
    public CertificadosViewModel Certificados => Instancia<CertificadosViewModel>();
    public BackupViewModel Backup => Instancia<BackupViewModel>();
    public ObrigacoesViewModel Obrigacoes => Instancia<ObrigacoesViewModel>();
    public ImportacaoCadastrosViewModel ImportacaoCadastros => Instancia<ImportacaoCadastrosViewModel>();

    /// <summary>Cadastros que aceitam importação de planilha: clientes e fornecedores (casco) e os do módulo.</summary>
    public IReadOnlyList<DefinicaoImportacao> Importacoes =>
        [.. (Modulo.UsaCadastrosComuns && Modulo.UsaClientesFornecedores ? ImportacoesComuns.Todas : []).Concat(Modulo.Importacoes).DistinctBy(d => d.Chave)];

    /// <summary>Telas comuns a todos os módulos (os menus delas ficam em <see cref="MontarMenus"/>).</summary>
    private void RegistrarTelasComuns(CatalogoTelas c)
    {
        static bool Admin(MainViewModelBase m) => m.Pode(Permissao.AdministrarSistema);
        static bool ComAcesso(MainViewModelBase m) => m.TemAcessoNaEmpresa;

        c.Instancia(nameof(Conexao), m => new ConexaoViewModel(m), fixa: true)
            .Tela(TelasComuns.Conexao, "Configurações › Conexão com o banco de dados", Admin,
                  v => { v.CarregarDe(BancoDoAplicativo.EhBancoLocal(_connectionString) ? Configuracao.Padrao : _connectionString, null); return Task.CompletedTask; });
        c.Instancia(nameof(Inicio), m => new InicioViewModel(m), fixa: true, aoTrocarEmpresa: (v, _) => v.AtualizarProximasAsync())
            .Tela(TelasComuns.Inicio, "", null, v => { v.Atualizar(); return Task.CompletedTask; });
        c.Instancia(nameof(Obrigacoes), m => new ObrigacoesViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(TelasComuns.Obrigacoes, "Obrigações › Calendário de obrigações", ComAcesso, v => v.CarregarAsync());

        c.Instancia(nameof(ContabilizacaoLote), m => new ContabilizacaoLoteViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(TelasComuns.ContabilizacaoLote, "Integrações › Exportar › Contabilização em lote", ComAcesso, v => v.CarregarAsync());

        c.Instancia(nameof(Empresas), m => new CadastroEmpresaViewModel(m), reiniciaJunto: [nameof(Socios), nameof(Contabilidade), nameof(Sindico)],
                    atualizarPermissoes: v => v.AtualizarBarraCadastro(), aoLimpar: v => v.AoTrocarEmpresa())
            .Tela(TelasComuns.Empresas, $"Cadastros › {Modulo.RotuloEmpresas}", Admin, v =>
            {
                if (v.EmConsulta) _ = Disparar(v.CarregarSelecionadaAsync);
                CarregarAbaEmpresa();
                return Task.CompletedTask;
            });
        c.Instancia(nameof(Contabilidade), m => new ContabilidadeEmpresaViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa || AbaDaEmpresaAtiva(CadastroEmpresaViewModel.AbaContadores) ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(TelasComuns.Contabilidade, "Cadastros › Contadores", ComAcesso, v => v.CarregarAsync());
        c.Instancia(nameof(Sindico), m => new SindicoViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => AbaDaEmpresaAtiva(CadastroEmpresaViewModel.AbaSindico) ? v.CarregarAsync() : Task.CompletedTask);
        c.Instancia(nameof(Socios), m => new SociosViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa || AbaDaEmpresaAtiva(CadastroEmpresaViewModel.AbaSocios) ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(TelasComuns.Socios, "Cadastros › Sócios", ComAcesso, v => v.CarregarAsync());
        foreach (var g in Modulo.Graficos)
            c.Instancia("Grafico." + g.Chave, m => new GraficoViewModel(m, g), aoTrocarEmpresa: (v, ativa) => ativa ? v.GerarCommand.ExecuteAsync(null) : Task.CompletedTask)
                .Tela(g.Tela, $"Relatórios › Gráficos › {g.Titulo}", ComAcesso, v => v.CarregarAsync());
        var importacao = c.Instancia(nameof(ImportacaoCadastros), m => new ImportacaoCadastrosViewModel(m));
        foreach (var def in Importacoes)
            importacao = importacao.Tela(def.Tela, $"Cadastros › Importar de planilha › {def.Titulo}", ComAcesso, v => v.AbrirAsync(def));
        c.Instancia(nameof(Clientes), m => new ClientesViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(TelasComuns.Clientes, "Cadastros › Clientes", ComAcesso, v => v.CarregarAsync());
        c.Instancia(nameof(Fornecedores), m => new FornecedoresViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(TelasComuns.Fornecedores, "Cadastros › Fornecedores", ComAcesso, v => v.CarregarAsync());
        c.Instancia(nameof(ContasBancarias), m => new ContasBancariasViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(TelasComuns.ContasBancarias, "Cadastros › Contas bancárias", ComAcesso, v => v.CarregarAsync());
        c.Instancia(nameof(FormasPagamento), m => new FormasPagamentoViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(TelasComuns.FormasPagamento, "Cadastros › Formas de pagamento", ComAcesso, v => v.CarregarAsync());

        c.Instancia(nameof(Usuarios), m => new UsuariosViewModel(m));
        c.Instancia(nameof(Perfis), m => new PerfisViewModel(m));
        c.Instancia(nameof(AlterarSenha), m => new AlterarSenhaViewModel(m));
        c.Instancia(nameof(CentralUsuarios), m => new CentralUsuariosViewModel(m),
                    reiniciaJunto: [nameof(Usuarios), nameof(Perfis), nameof(AlterarSenha)])
            .Tela(TelasComuns.Perfis, "Configurações › Usuários", Admin, v => AbrirCentralUsuarios(v, TelasComuns.Perfis))
            .Tela(TelasComuns.Usuarios, "Configurações › Usuários", null, v => AbrirCentralUsuarios(v, TelasComuns.Usuarios))
            .Tela(TelasComuns.AlterarSenha, "Configurações › Usuários", null, v => AbrirCentralUsuarios(v, TelasComuns.AlterarSenha));

        c.Instancia(nameof(Backup), m => new BackupViewModel(m))
            .Tela(TelasComuns.Backup, "Configurações › Backup e restauração", Admin, v => v.CarregarAsync());
        c.Instancia(nameof(ConfiguracoesGerais), m => new ConfiguracoesGeraisViewModel(m))
            .Tela(TelasComuns.ConfiguracoesGerais, "Configurações › Configurações gerais", Admin, v => v.CarregarAsync());
        c.Instancia(nameof(Certificados), m => new CertificadosViewModel(m))
            .Tela(TelasComuns.Certificados, "Configurações › Certificados digitais", m => m.Sessao?.Administrador == true, v => v.CarregarAsync());
        c.Instancia(nameof(Licenca), m => new LicencaViewModel(m))
            .Tela(TelasComuns.Licenca, "Ajuda › Licença e registro", null, v => v.CarregarAsync());
        c.Instancia(nameof(Manual), m => new ManualViewModel(m))
            .Tela(TelasComuns.Manual, "Ajuda › Manual do usuário", null, v => { v.Preparar(); return Task.CompletedTask; })
            .Tela(TelasComuns.ManualContextual, "Ajuda › Manual do usuário", null, v => { v.IrParaTela(_telaDaAjuda); return Task.CompletedTask; });
        c.Instancia(nameof(Sobre), m => new SobreViewModel(m))
            .Tela(TelasComuns.Sobre, $"Ajuda › Sobre o {Modulo.Nome}", null, v => { v.Atualizar(); return Task.CompletedTask; });
    }

    /// <summary>
    /// Instalação com o Vorticis Escritório (o executável na mesma pasta): usuários, acessos e banco de dados ficam só nele.
    /// Substituível nos testes.
    /// </summary>
    public static Func<bool> EscritorioInstalado { get; set; } = () =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "VorticisEscritorio.exe")) || File.Exists(Path.Combine(AppContext.BaseDirectory, "VorticisEscritorio"));

    /// <summary>Usuários, acessos e banco de dados são administrados no Escritório, não neste módulo.</summary>
    public bool SistemaCentralizadoNoEscritorio => !Modulo.GerenciaSistema && EscritorioInstalado();

    private IEnumerable<ItemMenu> MenuConfiguracoes()
    {
        var sistema = new[]
        {
            ItemMenu.Tela(Modulo.GerenciaSistema ? "Usuários e controle de acesso" : "Usuários", TelasComuns.Usuarios),
            ItemMenu.Tela("Conexão com o banco de dados", TelasComuns.Conexao),
            ItemMenu.Tela("Backup e restauração", TelasComuns.Backup),
            ItemMenu.Tela(Modulo.GerenciaSistema ? "Senha do administrador e limpeza do banco" : "Configurações gerais", TelasComuns.ConfiguracoesGerais),
        };
        if (Modulo.GerenciaSistema)
        {
            yield return sistema[0];
            yield return ItemMenu.Submenu("_Banco de dados", sistema[1], sistema[2], sistema[3]);
            yield return ItemMenu.Tela("Certificados digitais", TelasComuns.Certificados);
        }
        else if (SistemaCentralizadoNoEscritorio) yield return ItemMenu.Tela("Certificados digitais", TelasComuns.Certificados);
        else
        {
            yield return sistema[3];
            yield return ItemMenu.Tela("Certificados digitais", TelasComuns.Certificados);
            foreach (var i in sistema[..3]) yield return i;
        }
        yield return ItemMenu.Separador;
        yield return ItemMenu.Tela("Sair (trocar de usuário)", TelasComuns.Sair);
    }

    /// <summary>Menus do casco em volta dos do módulo.</summary>
    private IReadOnlyList<ItemMenu> MontarMenus()
    {
        var cadastros = new List<ItemMenu> { ItemMenu.Tela(Modulo.RotuloEmpresas, TelasComuns.Empresas) };
        if (Modulo.UsaCadastrosComuns && Modulo.ContadoresNoMenuCadastros) cadastros.Add(ItemMenu.Tela("Contadores", TelasComuns.Contabilidade));
        if (Modulo.UsaCadastrosComuns && Modulo.UsaClientesFornecedores)
            cadastros.AddRange([ItemMenu.Tela("Clientes", TelasComuns.Clientes), ItemMenu.Tela("Fornecedores", TelasComuns.Fornecedores)]);
        if (Modulo.ItensCadastros.Count > 0) cadastros.AddRange(Modulo.ItensCadastros.Prepend(ItemMenu.Separador));
        if (Importacoes.Count > 0)
            cadastros.AddRange([ItemMenu.Separador, ItemMenu.Submenu("Importar de _planilha (Excel)", [.. Importacoes.Select(d => ItemMenu.Tela(d.Titulo, d.Tela))])]);
        cadastros.AddRange([ItemMenu.Separador, ItemMenu.Tela("Parâmetros", TelasComuns.Parametros)]);
        // O calendário de obrigações fica em "Obrigações", se o módulo tem esse menu; senão, em "Consultas";
        // sem nenhum dos dois, o casco cria o menu "Obrigações".
        var calendario = ItemMenu.Tela("Calendário de obrigações", TelasComuns.Obrigacoes);
        var menusModulo = Modulo.Menus.ToList();
        var posObrig = menusModulo.FindIndex(m => m.Titulo.Replace("_", "") == "Obrigações");
        var posConsultas = menusModulo.FindIndex(m => m.Titulo.Replace("_", "") == "Consultas");
        if (!Modulo.UsaCalendarioObrigacoes) { }
        else if (posObrig >= 0)
        {
            var o = menusModulo[posObrig];
            menusModulo[posObrig] = o with { Filhos = [calendario, ItemMenu.Separador, .. o.Filhos ?? []] };
        }
        else if (posConsultas >= 0)
        {
            var c = menusModulo[posConsultas];
            menusModulo[posConsultas] = c with { Filhos = [.. c.Filhos ?? [], ItemMenu.Separador, calendario] };
        }
        else menusModulo.Add(ItemMenu.Submenu("_Obrigações", calendario));
        return
        [
            ItemMenu.Submenu("_Cadastros", [.. cadastros]),
            .. menusModulo,
            ItemMenu.Submenu("Con_figurações", [.. MenuConfiguracoes()]),
            ItemMenu.Submenu("_Ajuda",
                ItemMenu.Tela("Tela inicial", TelasComuns.Inicio),
                ItemMenu.Tela("Manual do usuário", TelasComuns.Manual, "F1"),
                ItemMenu.Separador,
                ItemMenu.Tela("Licença e registro", TelasComuns.Licenca),
                ItemMenu.Tela($"Sobre o {Modulo.Nome}", TelasComuns.Sobre)),
        ];
    }

    // ------------------------------------------------------------------ período de trabalho

    /// <summary>O módulo trabalha por período (Folha: competência): mostra o seletor e a situação ao lado da empresa.</summary>
    public bool UsaPeriodo => Modulo.RotuloPeriodo is not null;

    /// <summary>A faixa da competência (abaixo da barra de menus) aparece só nos módulos com período, depois do login.</summary>
    public bool MostraFaixaPeriodo => Logado && UsaPeriodo;
    public string RotuloPeriodo => (Modulo.RotuloPeriodo ?? "") + ":";

    [ObservableProperty] private DateTime? _periodoSelecionado = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private string _situacaoPeriodo = "";
    [ObservableProperty] private bool _periodoAberto = true;

    public DateOnly PeriodoTrabalho => PeriodoSelecionado is { } d ? new DateOnly(d.Year, d.Month, 1) : new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);

    /// <summary>Cor da situação: verde se aberto, vermelho se fechado.</summary>
    public IBrush PincelSituacaoPeriodo => new SolidColorBrush(Color.Parse(PeriodoAberto ? "#86EFAC" : "#FCA5A5"));

    partial void OnPeriodoAbertoChanged(bool value) => OnPropertyChanged(nameof(PincelSituacaoPeriodo));

    partial void OnPeriodoSelecionadoChanged(DateTime? value)
    {
        if (value is { } d && d.Day != 1) { PeriodoSelecionado = new DateTime(d.Year, d.Month, 1); return; }
        _ = Disparar(async () => { await AtualizarSituacaoPeriodoAsync(); await AoAlterarPeriodoAsync(PeriodoTrabalho); });
    }

    /// <summary>Situação do período no banco (aberto?, texto); o módulo que tem <see cref="IModuloDesktop.RotuloPeriodo"/> sobrescreve.</summary>
    protected virtual Task<(bool Aberto, string Situacao)?> ConsultarPeriodoAsync(DateOnly periodo)
        => Task.FromResult<(bool, string)?>(null);

    /// <summary>O usuário trocou o período: o módulo leva o novo período às telas abertas.</summary>
    protected virtual Task AoAlterarPeriodoAsync(DateOnly periodo) => Task.CompletedTask;

    /// <summary>Recursos ligados nos parâmetros da empresa ativa (ver <see cref="IModuloDesktop.RecursosAtivosAsync"/>).</summary>
    [ObservableProperty] private IReadOnlySet<string> _recursosAtivos = new HashSet<string>();

    public bool RecursoAtivo(string? recurso) => recurso is null || RecursosAtivos.Contains(recurso);

    public async Task AtualizarRecursosAsync()
    {
        try
        {
            if (App is null || Sessao is null || EmpresaSelecionada is null) { RecursosAtivos = new HashSet<string>(); return; }
            var recursos = new HashSet<string>(await Modulo.RecursosAtivosAsync(this));
            // Contabilização em lote (Contábil › Parâmetros › Tipo de contabilização): liga o item do menu Exportar
            if (await App.Pendencias.ModoAsync(EmpresaSelecionada.Id, Sessao.Id) is { Sucesso: true } m && m.Valor == Vorticis.Application.Contabil.ModoContabilizacao.Lote)
                recursos.Add(RecursoContabilizacaoLote);
            RecursosAtivos = recursos;
        }
        catch (Exception) { RecursosAtivos = new HashSet<string>(); }
    }

    public async Task AtualizarSituacaoPeriodoAsync()
    {
        if (!UsaPeriodo) return;
        var r = App is not null && Sessao is not null && EmpresaSelecionada is not null ? await ConsultarPeriodoAsync(PeriodoTrabalho) : null;
        (PeriodoAberto, SituacaoPeriodo) = r is { } v ? (v.Aberto, v.Aberto ? "ABERTA" : "FECHADA") : (true, "");
        await RecarregarPeriodosAsync();
    }

    /// <summary>Períodos da lista ao lado da empresa (do mais recente ao mais antigo); com <see cref="SomenteFechados"/>, só os fechados.</summary>
    public ObservableCollection<OpcaoPeriodo> ListaPeriodos { get; } = [];

    [ObservableProperty] private OpcaoPeriodo? _periodoEscolhido;
    [ObservableProperty] private bool _somenteFechados;
    private bool _montandoPeriodos;

    partial void OnPeriodoEscolhidoChanged(OpcaoPeriodo? value)
    {
        if (_montandoPeriodos || value is null || value.Periodo == PeriodoTrabalho) return;
        PeriodoSelecionado = value.Periodo.ToDateTime(TimeOnly.MinValue);
    }

    partial void OnSomenteFechadosChanged(bool value) => _ = Disparar(RecarregarPeriodosAsync);

    /// <summary>Períodos fechados da empresa ativa; o módulo que tem <see cref="IModuloDesktop.RotuloPeriodo"/> sobrescreve.</summary>
    protected virtual Task<IReadOnlyCollection<DateOnly>> ListarPeriodosFechadosAsync() => Task.FromResult<IReadOnlyCollection<DateOnly>>([]);

    /// <summary>Monta a lista: os meses de dois anos atrás até o próximo mês (e o período em uso), marcando os fechados.</summary>
    public async Task RecarregarPeriodosAsync()
    {
        if (!UsaPeriodo) return;
        var fechados = App is not null && Sessao is not null && EmpresaSelecionada is not null ? await ListarPeriodosFechadosAsync() : [];
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var meses = new SortedSet<DateOnly>(fechados) { PeriodoTrabalho };
        for (var m = new DateOnly(hoje.Year - 2, 1, 1); m <= new DateOnly(hoje.Year, hoje.Month, 1).AddMonths(1); m = m.AddMonths(1)) meses.Add(m);
        _montandoPeriodos = true;
        try
        {
            ListaPeriodos.Clear();
            foreach (var m in meses.Reverse().Where(m => !SomenteFechados || fechados.Contains(m)))
                ListaPeriodos.Add(new OpcaoPeriodo(m, fechados.Contains(m)));
            PeriodoEscolhido = ListaPeriodos.FirstOrDefault(p => p.Periodo == PeriodoTrabalho);
        }
        finally { _montandoPeriodos = false; }
    }

    // ------------------------------------------------------------------ estado

    public VorticisApp? App { get; private set; }
    public long? EmpresaId => EmpresaSelecionada?.Id;
    public string ConnectionString => _connectionString;

    [ObservableProperty] private SessaoUsuario? _sessao;
    [ObservableProperty] private EmpresaPermitida? _empresaSelecionada;
    [ObservableProperty] private string _status = "Conectando ao banco de dados...";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ConteudoPrincipal), nameof(MostraFaixaPeriodo))] private bool _logado;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ConteudoPrincipal))] private object? _conteudo;
    [ObservableProperty] private FuncaoAberta? _funcaoAtiva;
    [ObservableProperty] private PedidoConfirmacao? _confirmacao;

    /// <summary>Área da janela principal: login/conexão antes de entrar; depois, a tela inicial (as funções abrem em janelas flutuantes).</summary>
    public object? ConteudoPrincipal => Logado ? Inicio : Conteudo;

    /// <summary>Funções abertas, cada uma em sua janela flutuante.</summary>
    public ObservableCollection<FuncaoAberta> JanelasAbertas { get; } = [];

    public bool TemJanelasAbertas => JanelasAbertas.Count > 0;

    public string DescricaoEmpresa => EmpresaSelecionada is { } e
        ? $"{e.RazaoSocial} — CNPJ {Domain.Cadastros.Cnpj.Formatar(e.Cnpj)}"
        : "";
    [ObservableProperty] private string _tituloTela = "";

    public ObservableCollection<EmpresaPermitida> EmpresasPermitidas { get; } = [];

    public bool ConfiguracaoInicialPendente => _configuracaoInicialPendente;

    /// <summary>Sessão do usuário de implantação (vorticis): só o cadastro do primeiro administrador está liberado.</summary>
    public bool EmImplantacao => _implantacao;

    public string UsuarioRotulo => Sessao is null ? "" : $"{Sessao.Nome}{(Sessao.Administrador ? " (administrador)" : "")}";

    /// <summary>O usuário tem algum acesso (perfil) na empresa ativa.</summary>
    public bool TemAcessoNaEmpresa => EmpresaSelecionada is not null && _permissoes.TemAcesso;

    // ------------------------------------------------------------------ ciclo de vida

    /// <summary>Abertura do programa: prepara o banco (no modo local, cria e inicia o PostgreSQL embutido) e conecta.</summary>
    public async Task IniciarAsync()
    {
        string cs;
        try
        {
            Status = "Preparando o banco de dados...";
            cs = await BancoDoAplicativo.PrepararAsync(s => Status = s);
        }
        catch (Exception ex)
        {
            Status = $"Sem conexão com o banco de dados: {ex.Message}";
            Conexao.CarregarDe(Configuracao.Padrao, ex.Message);
            Conteudo = Conexao;
            return;
        }
        await InicializarAsync(cs);
    }

    public async Task InicializarAsync(string connectionString, IConsultaCnpj? consultaCnpj = null, IConsultaInscricaoEstadual? consultaIe = null)
    {
        _connectionString = connectionString;
        _consultaCnpj = consultaCnpj ?? _consultaCnpj;
        _consultaIe = consultaIe ?? _consultaIe;
        if (Logado) Sair();
        if (App is not null && !ConexaoCompartilhada) await App.DisposeAsync();
        (App, ConexaoCompartilhada) = (null, false);
        try
        {
            await BancoLocal.GarantirBancoAsync(connectionString);
            App = await VorticisApp.IniciarAsync(connectionString, _consultaCnpj, consultaIe: _consultaIe);
            Modulo.PrepararServicos(App);
            Status = $"Conectado a {Configuracao.Descrever(connectionString)}.";
            await Login.PrepararAsync();
            Conteudo = Login;
        }
        catch (Exception ex)
        {
            Status = $"Sem conexão com o banco de dados: {ex.Message}";
            Conexao.CarregarDe(BancoDoAplicativo.EhBancoLocal(connectionString) ? Configuracao.Padrao : connectionString, ex.Message);
            Conteudo = Conexao;
        }
    }

    /// <summary>Chamado pelo login (ou pela troca obrigatória de senha) quando a sessão está pronta para uso.</summary>
    public async Task EntrarAsync(SessaoUsuario sessao)
    {
        Sessao = sessao;
        Logado = true;
        _implantacao = sessao.Implantacao;
        _configuracaoInicialPendente = sessao.Administrador && App is not null && await App.Perfis.ConfiguracaoInicialPendenteAsync();
        await RecarregarEmpresasAsync();
        await CarregarPreferenciasMenuAsync();
        if (PerguntarAtivacaoAoEntrar) await OferecerAtivacaoAsync();
        if (_implantacao)
        {
            // Instalação nova: abre o cadastro de usuários, que só permite incluir o administrador, até concluir.
            AbrirCommand.NotifyCanExecuteChanged();
            Abrir(TelasComuns.Usuarios);
            Status = $"Implantação: cadastre o usuário administrador. Depois disso o acesso {ServicoUsuarios.LoginImplantacao}/{ServicoUsuarios.SenhaImplantacao} deixa de funcionar.";
            return;
        }
        if (_configuracaoInicialPendente)
        {
            // Primeiro uso: definir os perfis de acesso é obrigatório; o cadastro de empresas pode ficar para depois.
            Perfis.ConfiguracaoInicial = true;
            AbrirCommand.NotifyCanExecuteChanged();
            Abrir(TelasComuns.Perfis);
            Status = "Configuração inicial: defina os perfis de acesso dos usuários para liberar o sistema.";
        }
        else AbrirInicio();
        AvisarSessaoPronta();
    }

    /// <summary>Tela inicial; no Escritório sem nenhum escritório cadastrado (primeiro uso), o cadastro do escritório.</summary>
    private void AbrirInicio()
    {
        if (Modulo.GerenciaSistema && EmpresasPermitidas.Count == 0 && Sessao?.Administrador == true)
        {
            Abrir(TelasComuns.Empresas);
            Status = "Primeiro uso: cadastre o escritório contábil (Buscar na Receita) e, depois, os clientes em Cadastros › Clientes.";
        }
        else Abrir(TelasComuns.Inicio);
    }

    public void AoConcluirConfiguracaoInicial()
    {
        _configuracaoInicialPendente = false;
        CentralUsuarios.AtualizarAcesso();
        AbrirCommand.NotifyCanExecuteChanged();
        FecharTodasAsJanelas();
        AbrirInicio();
        AtualizarStatus();
        AvisarSessaoPronta();
    }

    public async Task ConcluirImplantacaoAsync(string loginAdministrador)
    {
        Sair();
        Login.Usuario = loginAdministrador;
        Login.Mensagem = $"Administrador {loginAdministrador} cadastrado. O acesso de implantação ({ServicoUsuarios.LoginImplantacao}) foi desativado: " +
                         "entre com o login e a senha do administrador.";
        await Login.AtualizarImplantacaoAsync();
    }

    [RelayCommand]
    private void Sair()
    {
        _implantacao = false;
        Sessao = null;
        FecharTodasAsJanelas();
        Logado = false;
        _configuracaoInicialPendente = false;
        EmpresasPermitidas.Clear();
        EmpresaSelecionada = null;
        _permissoes = PermissoesEfetivas.Nenhuma;
        _telaAtual = null;
        TituloTela = "";
        AplicarPreferenciasMenu(PreferenciasMenu.Vazia);
        RecriarModulos();
        Login.Limpar();
        Login.CarregarLoginSalvo();
        _ = Disparar(Login.AtualizarImplantacaoAsync);
        Conteudo = Login;
        Status = $"Sessão encerrada. Conectado a {Configuracao.Descrever(_connectionString)}.";
    }

    public async Task RecarregarEmpresasAsync(long? selecionar = null)
    {
        if (App is null || Sessao is null) return;
        var atual = selecionar ?? EmpresaSelecionada?.Id;
        var lista = await App.Usuarios.EmpresasDoModuloAsync(Sessao, ModuloAtual);
        EmpresasPermitidas.Clear();
        foreach (var e in lista) EmpresasPermitidas.Add(e);
        EmpresaSelecionada = EmpresasPermitidas.FirstOrDefault(e => e.Id == atual) ?? EmpresasPermitidas.FirstOrDefault();
        Inicio.Atualizar();
        await AtualizarPermissoesAsync();
    }

    /// <summary>Lê do banco o que o usuário pode fazer na empresa ativa (perfil atribuído pelo administrador).</summary>
    public async Task AtualizarPermissoesAsync()
    {
        _permissoes = App is not null && Sessao is not null && EmpresaSelecionada is not null
            ? await App.Autorizador.ObterAsync(Sessao.Id, EmpresaSelecionada.Id)
            : PermissoesEfetivas.Nenhuma;
        AbrirCommand.NotifyCanExecuteChanged();
        AtualizarPermissoesModulos();
        AtualizarStatus();
        // Se a tela aberta deixou de ser permitida (troca de empresa/perfil), vai para a primeira permitida.
        if (Logado)
            foreach (var f in JanelasAbertas.Where(f => !PodeAbrir(f.Tela)).ToList()) FecharFuncao(f);
        if (Logado && _telaAtual is not null && !PodeAbrir(_telaAtual)) AbrirPrimeiraPermitida();
    }

    private void AtualizarPermissoesModulos()
    {
        foreach (var def in _catalogo.Instancias.Where(d => d.AtualizarPermissoes is not null))
            def.AtualizarPermissoes!(_instancias[def.Nome]);
    }

    private void AbrirPrimeiraPermitida() => Abrir(PodeAbrir(TelasComuns.Inicio) ? TelasComuns.Inicio : TelasComuns.Perfis);

    // ------------------------------------------------------------------ permissões e navegação

    public bool Pode(Permissao permissao) =>
        Sessao is not null && (permissao == Permissao.AdministrarSistema
            ? Sessao.Administrador
            : EmpresaSelecionada is not null && _permissoes.Pode(permissao));

    public bool PodeAbrir(string? tela) => Logado && tela is not null && _catalogo.Telas.TryGetValue(tela, out var def) && !_chavesBloqueadas.Contains(tela)
        && (_implantacao
            ? tela is TelasComuns.Usuarios or TelasComuns.Sobre or TelasComuns.Manual or TelasComuns.ManualContextual
            : !_configuracaoInicialPendente
                ? def.Pode(this)
                : tela is TelasComuns.Perfis or TelasComuns.Sobre or TelasComuns.AlterarSenha or TelasComuns.Usuarios or TelasComuns.Manual or TelasComuns.ManualContextual);

    /// <summary>A chave pertence a uma tela registrada (comum ou do módulo)?</summary>
    public bool ConheceTela(string chave) => _catalogo.Telas.ContainsKey(chave);

    /// <summary>Chaves de todas as telas registradas (comuns e do módulo).</summary>
    internal IEnumerable<string> ChavesDeTelas => _catalogo.Telas.Keys;

    public void AbrirTela(string tela) => Abrir(tela);

    [RelayCommand(CanExecute = nameof(PodeAbrir))]
    private void Abrir(string? tela)
    {
        if (!PodeAbrir(tela))
        {
            if (Logado && TelaBloqueadaPorMenu(tela)) AvisarTelaOculta(tela!);
            return;
        }
        var def = _catalogo.Telas[tela!];
        var vm = _instancias[def.Instancia];
        // F1: guarda a tela em uso (a janela ativa) para o manual abrir o tópico dela; F1 dentro do próprio manual não muda o tópico
        if (tela == TelasComuns.ManualContextual)
            _telaDaAjuda = Conteudo is null || ReferenceEquals(Conteudo, Inicio) || ReferenceEquals(Conteudo, vm) ? null : _telaAtual;
        (Conteudo, TituloTela) = (vm, def.Titulo);
        _telaAtual = tela;
        RegistrarJanela(tela!);
        if (def.AoAbrir is { } aoAbrir) _ = Disparar(() => aoAbrir(vm));
    }

    /// <summary>Configurações › Usuários: seleciona e carrega a aba correspondente à chave aberta.</summary>
    private Task AbrirCentralUsuarios(CentralUsuariosViewModel central, string tela)
    {
        central.AtualizarAcesso();
        var aba = tela switch
        {
            TelasComuns.Perfis => CentralUsuariosViewModel.AbaPerfis,
            TelasComuns.AlterarSenha => CentralUsuariosViewModel.AbaSenha,
            _ => central.PodeUsuarios ? CentralUsuariosViewModel.AbaUsuarios
               : central.PodePerfis ? CentralUsuariosViewModel.AbaPerfis : CentralUsuariosViewModel.AbaSenha,
        };
        if (central.AbaSelecionada == aba) CarregarAbaUsuarios();
        else central.AbaSelecionada = aba;
        return Task.CompletedTask;
    }

    /// <summary>Configurações › Usuários: carrega a aba aberta.</summary>
    public void CarregarAbaUsuarios()
    {
        if (!ReferenceEquals(Conteudo, CentralUsuarios)) return;
        switch (CentralUsuarios.AbaSelecionada)
        {
            case CentralUsuariosViewModel.AbaUsuarios when CentralUsuarios.PodeUsuarios: _ = Disparar(Usuarios.CarregarAsync); break;
            case CentralUsuariosViewModel.AbaPerfis when CentralUsuarios.PodePerfis: _ = Disparar(Perfis.CarregarAsync); break;
            case CentralUsuariosViewModel.AbaRegistro when CentralUsuarios.PodeAdministrar: _ = Disparar(CentralUsuarios.CarregarRegistrosAsync); break;
        }
    }

    /// <summary>Cadastros › Empresas: carrega a aba de sócios ou de contadores da empresa selecionada.</summary>
    public void CarregarAbaEmpresa()
    {
        if (_telaAtual != TelasComuns.Empresas || EmpresaSelecionada is null) return;
        if (Empresas.AbaSelecionada == CadastroEmpresaViewModel.AbaSocios) _ = Disparar(Socios.CarregarAsync);
        if (Empresas.AbaSelecionada == CadastroEmpresaViewModel.AbaContadores) _ = Disparar(Contabilidade.CarregarAsync);
        if (Empresas.AbaSelecionada == CadastroEmpresaViewModel.AbaSindico) _ = Disparar(Sindico.CarregarAsync);
    }

    private bool AbaDaEmpresaAtiva(int aba) => _telaAtual == TelasComuns.Empresas && Empresas.AbaSelecionada == aba;

    /// <summary>A tela aberta no momento exibe a instância informada?</summary>
    private bool InstanciaAtiva(string nome) =>
        _telaAtual is not null && _catalogo.Telas.TryGetValue(_telaAtual, out var d) && d.Instancia == nome;

    // ------------------------------------------------------------------ janelas flutuantes e confirmação

    private void RegistrarJanela(string tela)
    {
        if (!Logado || Conteudo is null || ReferenceEquals(Conteudo, Inicio))
        {
            FuncaoAtiva = null;
            return;
        }
        var f = JanelasAbertas.FirstOrDefault(j => ReferenceEquals(j.ViewModel, Conteudo));
        if (f is null) JanelasAbertas.Add(f = new FuncaoAberta(Conteudo, tela, TituloTela));
        else (f.Tela, f.Titulo) = (tela, TituloTela);
        FuncaoAtiva = f;
    }

    partial void OnFuncaoAtivaChanged(FuncaoAberta? value)
    {
        foreach (var j in JanelasAbertas) j.Ativa = ReferenceEquals(j, value);
    }

    /// <summary>Traz a janela da função para a frente (clique na barra de janelas ou foco na janela).</summary>
    [RelayCommand]
    public void AtivarFuncao(FuncaoAberta? f)
    {
        if (f is null || !JanelasAbertas.Contains(f)) return;
        (Conteudo, TituloTela, _telaAtual) = (f.ViewModel, f.Titulo, f.Tela);
        FuncaoAtiva = f;
    }

    /// <summary>Fecha a função (botão ✕ da janela ou da barra). Sem outras abertas, volta à tela inicial.</summary>
    [RelayCommand]
    public void FecharFuncao(FuncaoAberta? f)
    {
        if (f is null || !JanelasAbertas.Remove(f)) return;
        if (ReferenceEquals(FuncaoAtiva, f) || ReferenceEquals(Conteudo, f.ViewModel))
        {
            if (JanelasAbertas.LastOrDefault() is { } proxima) AtivarFuncao(proxima);
            else
            {
                (Conteudo, TituloTela, _telaAtual, FuncaoAtiva) = (Inicio, "", TelasComuns.Inicio, null);
                Inicio.Atualizar();
            }
        }
        ReiniciarModulo(f.ViewModel);
    }

    /// <summary>
    /// Tecla Esc: fecha a função (a ativa, se não for informada). Com digitação não salva, pergunta antes.
    /// </summary>
    /// <returns>True se a função foi fechada.</returns>
    public async Task<bool> FecharFuncaoComConfirmacaoAsync(FuncaoAberta? f = null)
    {
        f ??= FuncaoAtiva;
        if (f is null || !JanelasAbertas.Contains(f)) return false;
        if (f.ViewModel is IAlteracoesPendentes { TemAlteracoesPendentes: true }
            && !await ConfirmarAsync("Fechar a tela", "Há alterações que ainda não foram salvas. Deseja fechar mesmo assim e perder o que foi digitado?",
                                     "Fechar sem salvar", "Continuar editando"))
            return false;
        FecharFuncao(f);
        return true;
    }

    /// <summary>
    /// Função fechada recomeça do zero na próxima abertura: o módulo é trocado por uma instância nova
    /// (nada do que foi digitado, filtrado ou consultado permanece). Telas compostas reiniciam as abas junto.
    /// </summary>
    private void ReiniciarModulo(object modulo)
    {
        var alvo = _catalogo.Instancias.FirstOrDefault(d => !d.Fixa && ReferenceEquals(_instancias[d.Nome], modulo));
        if (alvo is null) return;
        foreach (var nome in alvo.ReiniciaJunto.Prepend(alvo.Nome))
        {
            _instancias[nome] = _catalogo.Instancias.First(d => d.Nome == nome).Criar(this);
            OnPropertyChanged(nome);
        }
        AtualizarPermissoesModulos();
    }

    private void FecharTodasAsJanelas()
    {
        JanelasAbertas.Clear();
        FuncaoAtiva = null;
    }

    public ModulosEmpresa ModuloAtual => Modulo.ModuloEmpresa;
    public string RotuloEmpresaAtiva => Modulo.RotuloEmpresaAtiva + ":";

    public async Task<ModulosEmpresa?> EscolherModulosAsync(ModulosEmpresa inicial)
    {
        Confirmacao?.Responder(false);
        var atual = ModuloAtual;
        var opcoes = ModulosEmpresaExtensoes.Individuais
            .Select(m => (Modulo: m, Opcao: new OpcaoConfirmacao(m.Nome(), inicial.HasFlag(m), obrigatoria: m == atual))).ToList();
        var pedido = new PedidoConfirmacao("Cadastrar esta empresa em quais módulos?",
                                           "Os cadastros (empresa, participantes, sócios, contadores etc.) são únicos e compartilhados; escolha em quais módulos a empresa ficará ativa.",
                                           "Cadastrar", "Cancelar") { Opcoes = [.. opcoes.Select(o => o.Opcao)] };
        Confirmacao = pedido;
        if (!await pedido.Resposta) return null;
        return opcoes.Where(o => o.Opcao.Marcada).Aggregate(ModulosEmpresa.Nenhum, (acc, o) => acc | o.Modulo);
    }

    /// <summary>Oferece ativar, neste módulo, as empresas liberadas ao usuário que ainda não estão ativas nele.</summary>
    public async Task OferecerAtivacaoAsync()
    {
        if (App is null || Sessao is null || _implantacao) return;
        var fora = await App.Usuarios.EmpresasForaDoModuloAsync(Sessao, ModuloAtual);
        var ativou = false;
        foreach (var e in fora.Take(5))
            if (await ConfirmarAsync($"Ativar empresa no {Modulo.NomeCurto}",
                                     $"A empresa {e.RazaoSocial} (CNPJ {e.CnpjFormatado}) não está ativa no módulo {Modulo.NomeCurto}. Ativar esta empresa no módulo {Modulo.NomeCurto}?",
                                     "Ativar", "Agora não")
                && (await App.Empresas.AtivarNoModuloAsync(e.Id, ModuloAtual)).Sucesso)
                ativou = true;
        if (ativou) await RecarregarEmpresasAsync();
    }

    /// <summary>Erro não previsto (ver <see cref="ErrosInesperados"/>): aparece na barra de status e num aviso, sem fechar o módulo.</summary>
    public void MostrarErroInesperado(string mensagem)
    {
        Status = mensagem;
        if (Confirmacao is null) _ = ConfirmarAsync("Erro inesperado", mensagem, "OK", "Fechar");
    }

    public Task<bool> ConfirmarAsync(string titulo, string mensagem, string textoSim = "Sim", string textoNao = "Não")
    {
        Confirmacao?.Responder(false);
        var pedido = new PedidoConfirmacao(titulo, mensagem, textoSim, textoNao);
        Confirmacao = pedido;
        return pedido.Resposta;
    }

    public async Task ReiniciarParaLoginAsync(string mensagem)
    {
        await InicializarAsync(_connectionString, _consultaCnpj, _consultaIe);
        Login.Mensagem = mensagem;
    }

    public async Task<string?> SolicitarSenhaAsync(string titulo, string mensagem, string textoSim = "Confirmar", string textoNao = "Cancelar")
    {
        Confirmacao?.Responder(false);
        var pedido = new PedidoConfirmacao(titulo, mensagem, textoSim, textoNao, pedeSenha: true);
        Confirmacao = pedido;
        return await pedido.Resposta ? pedido.Senha : null;
    }

    [RelayCommand]
    private void ResponderConfirmacao(bool sim)
    {
        var pedido = Confirmacao;
        Confirmacao = null;
        pedido?.Responder(sim);
    }

    partial void OnLogadoChanged(bool value) => AbrirCommand.NotifyCanExecuteChanged();

    partial void OnSessaoChanged(SessaoUsuario? value) => OnPropertyChanged(nameof(UsuarioRotulo));

    partial void OnEmpresaSelecionadaChanged(EmpresaPermitida? value)
    {
        foreach (var def in _catalogo.Instancias.Where(d => d.AoLimpar is not null)) def.AoLimpar!(_instancias[def.Nome]);
        _ = Disparar(async () =>
        {
            await AtualizarPermissoesAsync();
            await AtualizarRecursosAsync();
            await AtualizarSituacaoPeriodoAsync();
            foreach (var def in _catalogo.Instancias.Where(d => d.AoTrocarEmpresa is not null))
                await def.AoTrocarEmpresa!(_instancias[def.Nome], InstanciaAtiva(def.Nome));
        });
    }

    private void AtualizarStatus()
    {
        if (Sessao is null) return;
        var empresa = EmpresaSelecionada is null
            ? (EmpresasPermitidas.Count == 0 ? "nenhuma empresa liberada" : "nenhuma empresa selecionada")
            : Sessao.Administrador ? "administrador" : $"perfil {_permissoes.NomePerfil ?? "sem acesso"}";
        Status = $"Usuário: {Sessao.Login} | {empresa} | {Configuracao.Descrever(_connectionString)}";
    }

    /// <summary>Tarefa disparada em segundo plano: erros vão para a barra de status em vez de sumirem.</summary>
    private async Task Disparar(Func<Task> acao)
    {
        try
        {
            await acao();
        }
        catch (Exception ex)
        {
            Status = $"Erro ao carregar dados: {ex.Message}";
        }
    }

    // ------------------------------------------------------------------ Suite (vários módulos na mesma janela)

    /// <summary>
    /// A conexão é de outro módulo da Suite (quem conectou e fez o login): este módulo só a usa e não a encerra.
    /// </summary>
    public bool ConexaoCompartilhada { get; private set; }

    /// <summary>No login, pergunta se ativa no módulo as empresas que estão fora dele (desligado na Suite).</summary>
    public bool PerguntarAtivacaoAoEntrar { get; set; } = true;

    /// <summary>A sessão ficou pronta para uso (login concluído, sem implantação nem configuração inicial pendente).</summary>
    public event EventHandler? SessaoPronta;

    private void AvisarSessaoPronta()
    {
        if (Logado && !_implantacao && !_configuracaoInicialPendente) SessaoPronta?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Suite: entra com a conexão e a sessão de outro módulo, sem nova tela de login.</summary>
    public async Task EntrarComSessaoDeAsync(MainViewModelBase origem)
    {
        if (origem.App is null || origem.Sessao is null) return;
        if (!ReferenceEquals(App, origem.App))
        {
            if (Logado) Sair();
            if (App is not null && !ConexaoCompartilhada) await App.DisposeAsync();
            (App, _connectionString, ConexaoCompartilhada) = (origem.App, origem.ConnectionString, true);
            Status = $"Conectado a {Configuracao.Descrever(_connectionString)}.";
        }
        if (Logado && Sessao?.Id == origem.Sessao.Id) return;
        if (Logado) Sair();
        await EntrarAsync(origem.Sessao);
    }

    /// <summary>Suite: encerra a sessão deste módulo quando o usuário sai em qualquer um deles.</summary>
    public void SairDaSessao()
    {
        if (Logado) Sair();
    }

    public async ValueTask DisposeAsync()
    {
        if (App is not null && !ConexaoCompartilhada) await App.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
