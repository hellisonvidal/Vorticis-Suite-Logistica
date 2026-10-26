using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Desktop.Comum.Modulos;

namespace Vorticis.Desktop.Comum.ViewModels;

/// <summary>Temas de navegação da Suite (o usuário escolhe em Exibir › Layout da Suite).</summary>
public enum LayoutSuite
{
    /// <summary>A: uma aba por módulo acima do menu.</summary>
    Abas,
    /// <summary>B (padrão): barra lateral com os ícones dos módulos.</summary>
    Lateral,
    /// <summary>C: árvore com todos os módulos e funções, com busca.</summary>
    Arvore,
    /// <summary>D: painel inicial com um cartão por módulo e o seletor de módulo no topo.</summary>
    Painel,
}

/// <summary>Um módulo dentro da Suite: o ViewModel completo do módulo e os dados para os botões de troca.</summary>
public sealed partial class ModuloSuite(MainViewModelBase vm) : ObservableObject
{
    public MainViewModelBase Vm { get; } = vm;
    public string Nome => Vm.Modulo.NomeCurto;
    public string NomeCompleto => Vm.Modulo.Nome;
    public string Subtitulo => Vm.Modulo.Subtitulo;
    public IBrush Cor => Vm.PincelDestaque;
    public IBrush CorBarra => Vm.PincelBarra;

    /// <summary>Sigla do ícone (C, F, FP, E, G, CD, FR).</summary>
    public string Sigla => Vm.Modulo.NomeExecutavel switch
    {
        "VorticisContabil" => "C",
        "VorticisFiscal" => "F",
        "VorticisFolha" => "FP",
        "VorticisEscritorio" => "E",
        "VorticisGerencial" => "G",
        "VorticisCondominio" => "CD",
        "VorticisFrota" => "FR",
        _ => Nome[..1],
    };

    [ObservableProperty] private bool _ativo;

    /// <summary>Atalhos do módulo (cartão do painel D): as funções com tecla de atalho, menos o manual.</summary>
    public IReadOnlyList<NoSuite> Atalhos => _atalhos ??= [.. Vm.Atalhos.Where(a => a.Chave is { } c && c != TelasComuns.ManualContextual)
        .Take(3).Select(a => new NoSuite(a.Descricao, this, a.Chave, [], $"{Nome} › {a.Descricao}"))];
    private IReadOnlyList<NoSuite>? _atalhos;

    /// <summary>Funções abertas no módulo (selo no ícone).</summary>
    public int Abertas => Vm.JanelasAbertas.Count;
    public bool TemAbertas => Abertas > 0;

    internal void AvisarAbertas()
    {
        OnPropertyChanged(nameof(Abertas));
        OnPropertyChanged(nameof(TemAbertas));
    }
}

/// <summary>Nó da árvore de navegação (layout C): módulo, submenu ou função.</summary>
public sealed class NoSuite(string titulo, ModuloSuite modulo, string? chave, IReadOnlyList<NoSuite> filhos, string caminho)
{
    public string Titulo { get; } = titulo;
    public ModuloSuite Modulo { get; } = modulo;
    public string? Chave { get; } = chave;
    public IReadOnlyList<NoSuite> Filhos { get; } = filhos;

    /// <summary>Módulo › menu › função (resultado da busca).</summary>
    public string Caminho { get; } = caminho;
    public bool EhModulo => Chave is null && Caminho == Modulo.Nome;
    public override string ToString() => Caminho;
}

/// <summary>
/// Suite: todos os módulos instalados na mesma janela. Cada módulo continua com o seu ViewModel (menus, telas, empresa
/// ativa); o login é feito uma vez, no primeiro módulo, e a sessão vale para todos. Sair em qualquer módulo sai de todos.
/// </summary>
public sealed partial class SuiteViewModel : ObservableObject
{
    private bool _propagando;

    /// <param name="nome">Nome do produto na janela e no menu (Vorticis Suite Empresarial, Vorticis Suite Logística...).</param>
    /// <param name="principal">Executável do módulo que conecta e faz o login; sem ele, o que administra a suíte (Escritório) ou o primeiro.</param>
    public SuiteViewModel(IReadOnlyList<MainViewModelBase> modulos, LayoutSuite? layout = null, string nome = "Vorticis Suite Empresarial", string? principal = null)
    {
        Nome = nome;
        if (modulos.Count == 0) throw new ArgumentException("A Suite precisa de pelo menos um módulo.", nameof(modulos));
        // Em ordem alfabética em todos os layouts (abas, barra lateral, árvore, painel e Ctrl+1, Ctrl+2...)
        var ordem = StringComparer.Create(Domain.Comum.Formato.PtBr, ignoreCase: true);
        Modulos = [.. modulos.Select(m => new ModuloSuite(m)).OrderBy(m => m.Nome, ordem)];
        // Quem conecta e faz o login: o módulo que administra a suíte (Escritório), senão o primeiro da lista
        _principal = Modulos.FirstOrDefault(m => principal is not null && m.Vm.Modulo.NomeExecutavel == principal)
                     ?? Modulos.FirstOrDefault(m => m.Vm.Modulo.GerenciaSistema) ?? Modulos[0];
        foreach (var m in Modulos)
        {
            m.Vm.PerguntarAtivacaoAoEntrar = false;   // na Suite cada módulo mostra as empresas dele, sem perguntar
            m.Vm.SessaoPronta += async (_, _) => await PropagarSessaoAsync(m.Vm);
            m.Vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModelBase.Logado) && !m.Vm.Logado) SairDeTodos();
                if (e.PropertyName == nameof(MainViewModelBase.Logado)) { OnPropertyChanged(nameof(Logado)); OnPropertyChanged(nameof(MostraPainel)); OnPropertyChanged(nameof(Saudacao)); }
            };
            m.Vm.JanelasAbertas.CollectionChanged += (_, _) => m.AvisarAbertas();
        }
        _ativo = _principal;
        _ativo.Ativo = true;
        _layout = layout ?? LerLayout();
        _lateralFixa = LerFixa();
        Arvore = [.. Modulos.Select(MontarNo)];
    }

    public ObservableCollection<ModuloSuite> Modulos { get; }

    /// <summary>Nome do produto (título da janela e marca do menu).</summary>
    public string Nome { get; }

    /// <summary>Módulo que faz a conexão e o login (o Escritório, quando instalado; senão, o primeiro da lista).</summary>
    private readonly ModuloSuite _principal;

    public MainViewModelBase Principal => _principal.Vm;

    [ObservableProperty] private ModuloSuite _ativo;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EhAbas), nameof(EhLateral), nameof(EhArvore), nameof(EhPainel), nameof(MostraPainel), nameof(NomeLayout),
                              nameof(MostraBarraLateral), nameof(MostraFaixaLateral), nameof(MostraArvore), nameof(MostraFaixaArvore))]
    private LayoutSuite _layout;

    /// <summary>Layout D: o painel inicial com os cartões dos módulos está à mostra (em vez das telas do módulo).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(MostraPainel))] private bool _noPainel = true;

    // ------------------------------------------------------------------ navegação à esquerda que se esconde (B e C)

    /// <summary>
    /// A barra lateral (B) e a árvore (C) ficam recolhidas numa faixa fina à esquerda, para não tirar espaço das telas;
    /// abrem quando o mouse se aproxima e recolhem quando ele sai. "Fixar" deixa sempre aberta (gravado neste computador).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LateralExpandida), nameof(MostraBarraLateral), nameof(MostraFaixaLateral), nameof(MostraArvore), nameof(MostraFaixaArvore))]
    private bool _lateralAberta;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LateralExpandida), nameof(MostraBarraLateral), nameof(MostraFaixaLateral), nameof(MostraArvore), nameof(MostraFaixaArvore), nameof(TextoFixar))]
    private bool _lateralFixa;

    public bool LateralExpandida => LateralFixa || LateralAberta;
    public bool MostraBarraLateral => EhLateral && LateralExpandida;
    public bool MostraFaixaLateral => EhLateral && !LateralExpandida;
    public bool MostraArvore => EhArvore && LateralExpandida;
    public bool MostraFaixaArvore => EhArvore && !LateralExpandida;
    public string TextoFixar => LateralFixa ? "Soltar" : "Fixar";

    partial void OnLateralFixaChanged(bool value) => GravarTexto(ArquivoFixa, value ? "fixa" : "recolhe");

    [RelayCommand]
    private void AlternarFixa() => LateralFixa = !LateralFixa;

    public static string ArquivoFixa => Path.ChangeExtension(ArquivoLayout, null) + "-fixa.txt";

    private static bool LerFixa()
    {
        try { return File.Exists(ArquivoFixa) && File.ReadAllText(ArquivoFixa).Trim() == "fixa"; }
        catch (Exception) { return false; }
    }

    public bool EhAbas => Layout == LayoutSuite.Abas;
    public bool EhLateral => Layout == LayoutSuite.Lateral;
    public bool EhArvore => Layout == LayoutSuite.Arvore;
    public bool EhPainel => Layout == LayoutSuite.Painel;
    public bool MostraPainel => EhPainel && NoPainel && Logado;
    public bool Logado => Principal.Logado;

    public string NomeLayout => Nomes[Layout];

    public static IReadOnlyDictionary<LayoutSuite, string> Nomes { get; } = new Dictionary<LayoutSuite, string>
    {
        [LayoutSuite.Abas] = "A – Abas no topo",
        [LayoutSuite.Lateral] = "B – Barra lateral (padrão)",
        [LayoutSuite.Arvore] = "C – Árvore com busca",
        [LayoutSuite.Painel] = "D – Painel inicial",
    };

    /// <summary>Troca de módulo pedida pela janela (ela esconde as janelas do módulo anterior e mostra as do novo).</summary>
    public event EventHandler<ModuloSuite>? AoTrocar;

    partial void OnAtivoChanged(ModuloSuite? oldValue, ModuloSuite newValue)
    {
        if (oldValue is not null) oldValue.Ativo = false;
        newValue.Ativo = true;
        OnPropertyChanged(nameof(ModuloNoSeletor));
    }

    /// <summary>Seletor "Módulo ▾" do layout D (escolher troca de módulo).</summary>
    public ModuloSuite? ModuloNoSeletor
    {
        get => Ativo;
        set => Trocar(value);
    }

    /// <summary>Saudação do painel D (bom dia/boa tarde/boa noite e o nome do usuário).</summary>
    public string Saudacao
    {
        get
        {
            var h = DateTime.Now.Hour;
            var nome = Principal.Sessao?.Nome.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            return (h < 12 ? "Bom dia" : h < 18 ? "Boa tarde" : "Boa noite") + (nome.Length > 0 ? $", {nome}" : "");
        }
    }

    public string Hoje => DateTime.Today.ToString("dddd, d 'de' MMMM 'de' yyyy", Domain.Comum.Formato.PtBr) is var t && t.Length > 0 ? char.ToUpper(t[0]) + t[1..] : "";

    partial void OnLayoutChanged(LayoutSuite value)
    {
        GravarLayout(value);
        NoPainel = value == LayoutSuite.Painel;
    }

    [RelayCommand]
    private void EscolherLayout(LayoutSuite layout) => Layout = layout;

    /// <summary>Vai para o módulo; a empresa escolhida continua a mesma quando ela também está liberada no módulo.</summary>
    [RelayCommand]
    public void Trocar(ModuloSuite? modulo)
    {
        if (modulo is null) return;
        NoPainel = false;
        if (ReferenceEquals(modulo, Ativo)) return;
        if (!modulo.Vm.Logado && Principal.Logado) { Status($"O {modulo.NomeCompleto} ainda está entrando com o seu usuário. Tente de novo em instantes."); return; }
        if (!Principal.Logado && !ReferenceEquals(modulo.Vm, Principal)) { Status("Entre com o seu usuário primeiro."); return; }
        var empresa = Ativo.Vm.EmpresaSelecionada?.Id;
        Ativo = modulo;
        if (empresa is { } id && modulo.Vm.EmpresasPermitidas.FirstOrDefault(e => e.Id == id) is { } mesma) modulo.Vm.EmpresaSelecionada = mesma;
        AoTrocar?.Invoke(this, modulo);
    }

    [RelayCommand]
    private void IrParaPainel() => NoPainel = true;

    /// <summary>Abre a função de um módulo (árvore e busca do layout C, atalhos do painel D).</summary>
    [RelayCommand]
    private void AbrirFuncao(NoSuite? no)
    {
        if (no is null) return;
        Trocar(no.Modulo);
        if (no.Chave is { } chave && ReferenceEquals(Ativo, no.Modulo))
        {
            if (chave == TelasComuns.Sair) no.Modulo.Vm.SairCommand.Execute(null);
            else if (no.Modulo.Vm.AbrirCommand.CanExecute(chave)) no.Modulo.Vm.AbrirCommand.Execute(chave);
        }
    }

    private void Status(string texto) => Ativo.Vm.Status = texto;

    // ------------------------------------------------------------------ árvore e busca (layout C)

    public IReadOnlyList<NoSuite> Arvore { get; }
    public ObservableCollection<NoSuite> Resultados { get; } = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Buscando))] private string _busca = "";
    public bool Buscando => Busca.Trim().Length > 0;

    private static NoSuite MontarNo(ModuloSuite m) =>
        new(m.NomeCompleto, m, null, [.. m.Vm.Menus.Select(i => MontarNo(m, i, m.Nome)).OfType<NoSuite>()], m.Nome);

    private static NoSuite? MontarNo(ModuloSuite m, ItemMenu item, string caminho)
    {
        if (item.EhSeparador) return null;
        var titulo = item.Titulo.Replace("_", "");
        var meu = $"{caminho} › {titulo}";
        return new NoSuite(titulo, m, item.Filhos is null ? item.Chave : null,
                           item.Filhos is null ? [] : [.. item.Filhos.Select(f => MontarNo(m, f, meu)).OfType<NoSuite>()], meu);
    }

    private static IEnumerable<NoSuite> Folhas(NoSuite no) => no.Filhos.Count == 0 ? (no.Chave is null ? [] : [no]) : no.Filhos.SelectMany(Folhas);

    partial void OnBuscaChanged(string value)
    {
        Resultados.Clear();
        var termo = Normalizar(value);
        if (termo.Length == 0) return;
        foreach (var no in Arvore.SelectMany(Folhas).Where(n => Normalizar(n.Caminho).Contains(termo)).Take(40)) Resultados.Add(no);
    }

    /// <summary>Busca sem acento e sem diferença de maiúsculas ("ferias" acha "Férias").</summary>
    public static string Normalizar(string texto)
    {
        var d = texto.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string([.. d.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)]);
    }

    // ------------------------------------------------------------------ sessão única

    /// <summary>Login concluído num módulo: os demais entram com a mesma conexão e o mesmo usuário.</summary>
    private async Task PropagarSessaoAsync(MainViewModelBase origem)
    {
        if (_propagando) return;
        _propagando = true;
        try
        {
            foreach (var m in Modulos.Where(m => !ReferenceEquals(m.Vm, origem)))
            {
                try { await m.Vm.EntrarComSessaoDeAsync(origem); }
                catch (Exception ex) { m.Vm.Status = $"Não foi possível entrar no {m.NomeCompleto}: {ex.Message}"; }
            }
        }
        finally { _propagando = false; }
        OnPropertyChanged(nameof(Logado));
        OnPropertyChanged(nameof(MostraPainel));
    }

    /// <summary>Sair (trocar de usuário) em qualquer módulo encerra a sessão em todos e volta ao login do principal.</summary>
    private void SairDeTodos()
    {
        if (_propagando) return;
        _propagando = true;
        try
        {
            foreach (var m in Modulos) m.Vm.SairDaSessao();
        }
        finally { _propagando = false; }
        if (!ReferenceEquals(Ativo.Vm, Principal)) { Ativo = _principal; AoTrocar?.Invoke(this, Ativo); }
        NoPainel = EhPainel;
        OnPropertyChanged(nameof(MostraPainel));
    }

    // ------------------------------------------------------------------ preferência do layout (por computador)

    /// <summary>Arquivo da preferência; substituível nos testes.</summary>
    public static string ArquivoLayout { get; set; } = Path.Combine(Configuracao.PastaLocal, "suite-layout.txt");

    public static LayoutSuite LerLayout()
    {
        try
        {
            return File.Exists(ArquivoLayout) && Enum.TryParse<LayoutSuite>(File.ReadAllText(ArquivoLayout).Trim(), out var l) ? l : LayoutSuite.Lateral;
        }
        catch (Exception) { return LayoutSuite.Lateral; }
    }

    private static void GravarLayout(LayoutSuite layout) => GravarTexto(ArquivoLayout, layout.ToString());

    private static void GravarTexto(string arquivo, string texto)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
            File.WriteAllText(arquivo, texto);
        }
        catch (Exception) { /* sem gravar a preferência, o layout vale só nesta execução */ }
    }
}
