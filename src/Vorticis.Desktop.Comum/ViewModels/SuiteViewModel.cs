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
    /// <summary>E: faixa de comando escura no topo, com os módulos em pílulas e a busca de funções.</summary>
    Faixa,
}

/// <summary>Opção do seletor de layouts (cartão com a prévia, o nome e a descrição).</summary>
public sealed partial class OpcaoLayout(LayoutSuite layout, string letra, string nome, string descricao) : ObservableObject
{
    public LayoutSuite Layout { get; } = layout;
    public string Letra { get; } = letra;
    public string Nome { get; } = nome;
    public string Descricao { get; } = descricao;
    public bool EhAbas => Layout == LayoutSuite.Abas;
    public bool EhLateral => Layout == LayoutSuite.Lateral;
    public bool EhArvore => Layout == LayoutSuite.Arvore;
    public bool EhPainel => Layout == LayoutSuite.Painel;
    public bool EhFaixa => Layout == LayoutSuite.Faixa;
    [ObservableProperty] private bool _selecionado;
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

    /// <summary>Desenho do ícone (do módulo ou, nos módulos conhecidos, pelo executável); sem ele, a sigla.</summary>
    public Geometry? Icone => _icone ??= (Vm.Modulo.Icone ?? IconesModulos.DoExecutavel(Vm.Modulo.NomeExecutavel)) is { } d ? Geometry.Parse(d) : null;
    private Geometry? _icone;
    public bool TemIcone => Icone is not null;
    public bool SemIcone => Icone is null;

    /// <summary>Degradê da cor do módulo para a cor da barra (cartões, ícones e destaques).</summary>
    public IBrush Gradiente => _gradiente ??= new LinearGradientBrush
    {
        StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
        EndPoint = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative),
        GradientStops = [new GradientStop(CorDe(Vm.Modulo.CorDestaque, Colors.SteelBlue), 0), new GradientStop(CorDe(Vm.Modulo.CorBarra, Colors.DarkSlateBlue), 1)],
    };
    private IBrush? _gradiente;

    /// <summary>A cor do módulo bem clara (fundo de destaque do módulo ativo nas barras claras).</summary>
    public IBrush CorSuave => _suave ??= new SolidColorBrush(CorDe(Vm.Modulo.CorDestaque, Colors.SteelBlue), 0.14);
    private IBrush? _suave;

    private static Color CorDe(string? texto, Color padrao) => Color.TryParse(texto, out var c) ? c : padrao;

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

    /// <summary>
    /// Funções em destaque do módulo (cartão do painel D): as que têm tecla de atalho e, completando até quatro, as primeiras dos menus
    /// do módulo (sem as de Cadastros, Relatórios e do casco).
    /// </summary>
    public IReadOnlyList<NoSuite> Atalhos => _atalhos ??= MontarAtalhos();
    private IReadOnlyList<NoSuite>? _atalhos;

    private List<NoSuite> MontarAtalhos()
    {
        var lista = Vm.Atalhos.Where(a => a.Chave is { } c && c != TelasComuns.ManualContextual)
                      .Select(a => new NoSuite(a.Descricao, this, a.Chave, [], $"{Nome} › {a.Descricao}")).ToList();
        static IEnumerable<ItemMenu> Folhas(IEnumerable<ItemMenu> itens) => itens.SelectMany(i => i.Filhos is { } f ? Folhas(f) : [i]);
        foreach (var f in Folhas(Vm.Modulo.Menus.Where(m => m.Titulo.Replace("_", "") != "Relatórios"))
                     .Where(f => f.Habilitado && f.Chave is not null && lista.All(a => a.Chave != f.Chave)))
        {
            if (lista.Count >= 4) break;
            var titulo = f.Titulo.Replace("_", "");
            lista.Add(new NoSuite(titulo, this, f.Chave, [], $"{Nome} › {titulo}"));
        }
        return [.. lista.Take(4)];
    }
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
    /// <param name="ordemAlfabetica">Falso: os módulos ficam na ordem recebida (a ordem do trabalho, na Suite Logística).</param>
    public SuiteViewModel(IReadOnlyList<MainViewModelBase> modulos, LayoutSuite? layout = null, string nome = "Vorticis Suite Empresarial", string? principal = null,
                          bool ordemAlfabetica = true)
    {
        Nome = nome;
        if (modulos.Count == 0) throw new ArgumentException("A Suite precisa de pelo menos um módulo.", nameof(modulos));
        // A mesma ordem em todos os layouts (abas, barra lateral, árvore, painel e Ctrl+1, Ctrl+2...): alfabética ou a recebida
        var ordem = StringComparer.Create(Domain.Comum.Formato.PtBr, ignoreCase: true);
        var lista = modulos.Select(m => new ModuloSuite(m));
        Modulos = [.. ordemAlfabetica ? lista.OrderBy(m => m.Nome, ordem) : lista];
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
        _topoFixo = LerTexto(ArquivoTopoFixo) == "fixo";
        Arvore = [.. Modulos.Select(MontarNo)];
        OpcoesLayout = [.. Nomes.Select(n => new OpcaoLayout(n.Key, n.Value[..1], n.Value[4..].Replace(" (padrão)", ""), Descricoes[n.Key]) { Selecionado = n.Key == _layout })];
    }

    public ObservableCollection<ModuloSuite> Modulos { get; }

    /// <summary>Nome do produto (título da janela e marca do menu).</summary>
    public string Nome { get; }

    /// <summary>Módulo que faz a conexão e o login (o Escritório, quando instalado; senão, o primeiro da lista).</summary>
    private readonly ModuloSuite _principal;

    public MainViewModelBase Principal => _principal.Vm;

    [ObservableProperty] private ModuloSuite _ativo;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EhAbas), nameof(EhLateral), nameof(EhArvore), nameof(EhPainel), nameof(EhFaixa), nameof(EhTopo), nameof(EhEsquerda),
                              nameof(MostraPainel), nameof(NomeLayout),
                              nameof(MostraBarraLateral), nameof(MostraFaixaLateral), nameof(MostraArvore), nameof(MostraFaixaArvore),
                              nameof(MostraAbas), nameof(MostraBarraPainel), nameof(MostraFaixaComando), nameof(MostraTopoRecolhido), nameof(BuscandoNaFaixa))]
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

    partial void OnLateralFixaChanged(bool value)
    {
        OnPropertyChanged(nameof(NavegacaoFixa));
        GravarTexto(ArquivoFixa, value ? "fixa" : "recolhe");
    }

    [RelayCommand]
    private void AlternarFixa() => LateralFixa = !LateralFixa;

    public static string ArquivoFixa => Path.ChangeExtension(ArquivoLayout, null) + "-fixa.txt";

    private static bool LerFixa()
    {
        try { return File.Exists(ArquivoFixa) && File.ReadAllText(ArquivoFixa).Trim() == "fixa"; }
        catch (Exception) { return false; }
    }

    // ------------------------------------------------------------------ navegação no topo que se esconde (A, D e E)

    /// <summary>
    /// A navegação de cima (abas, barra do painel ou faixa de comando) também fica recolhida numa faixa fina, abrindo quando o mouse chega
    /// ao alto da janela; "Fixar" deixa sempre aberta (gravado neste computador).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TopoExpandido), nameof(MostraAbas), nameof(MostraBarraPainel), nameof(MostraFaixaComando), nameof(MostraTopoRecolhido))]
    private bool _topoAberto;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TopoExpandido), nameof(MostraAbas), nameof(MostraBarraPainel), nameof(MostraFaixaComando), nameof(MostraTopoRecolhido),
                              nameof(TextoFixarTopo))]
    private bool _topoFixo;

    public bool TopoExpandido => TopoFixo || TopoAberto;
    public bool MostraAbas => EhAbas && TopoExpandido;
    public bool MostraBarraPainel => EhPainel && TopoExpandido;
    public bool MostraFaixaComando => EhFaixa && TopoExpandido;
    public bool MostraTopoRecolhido => EhTopo && !TopoExpandido;
    public string TextoFixarTopo => TopoFixo ? "Soltar" : "Fixar";

    partial void OnTopoFixoChanged(bool value)
    {
        OnPropertyChanged(nameof(NavegacaoFixa));
        GravarTexto(ArquivoTopoFixo, value ? "fixo" : "recolhe");
    }

    [RelayCommand]
    private void AlternarTopoFixo() => TopoFixo = !TopoFixo;

    /// <summary>Fixar ou soltar a navegação do layout em uso (à esquerda ou no topo).</summary>
    [RelayCommand]
    private void AlternarNavegacaoFixa()
    {
        if (EhTopo) TopoFixo = !TopoFixo;
        else LateralFixa = !LateralFixa;
    }

    public bool NavegacaoFixa => EhTopo ? TopoFixo : LateralFixa;

    public static string ArquivoTopoFixo => Path.ChangeExtension(ArquivoLayout, null) + "-topo-fixo.txt";

    private static string LerTexto(string arquivo)
    {
        try { return File.Exists(arquivo) ? File.ReadAllText(arquivo).Trim() : ""; }
        catch (Exception) { return ""; }
    }

    /// <summary>Layouts com a navegação no topo (A, D e E) e à esquerda (B e C).</summary>
    public bool EhTopo => Layout is LayoutSuite.Abas or LayoutSuite.Painel or LayoutSuite.Faixa;
    public bool EhEsquerda => !EhTopo;
    public bool EhFaixa => Layout == LayoutSuite.Faixa;

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
        [LayoutSuite.Faixa] = "E – Faixa de comando",
    };

    public static IReadOnlyDictionary<LayoutSuite, string> Descricoes { get; } = new Dictionary<LayoutSuite, string>
    {
        [LayoutSuite.Abas] = "Uma aba colorida por módulo, no alto da janela, como num navegador.",
        [LayoutSuite.Lateral] = "Coluna escura à esquerda com o ícone de cada módulo.",
        [LayoutSuite.Arvore] = "Todos os módulos e funções numa árvore à esquerda, com busca.",
        [LayoutSuite.Painel] = "Tela inicial com um cartão por módulo e as funções principais.",
        [LayoutSuite.Faixa] = "Barra escura no topo com os módulos em pílulas e a busca no meio.",
    };

    /// <summary>Cartões do seletor de layouts.</summary>
    public IReadOnlyList<OpcaoLayout> OpcoesLayout { get; }

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
        foreach (var o in OpcoesLayout) o.Selecionado = o.Layout == value;
        OnPropertyChanged(nameof(NavegacaoFixa));
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
        if (!modulo.Vm.Logado && Principal.Logado) { Status($"O módulo {modulo.Nome} ainda está entrando com o seu usuário. Tente de novo em instantes."); return; }
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
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Buscando), nameof(BuscandoNaFaixa))] private string _busca = "";
    public bool Buscando => Busca.Trim().Length > 0;
    /// <summary>Resultados da busca da faixa de comando (layout E) à mostra.</summary>
    public bool BuscandoNaFaixa => EhFaixa && Buscando;

    private static NoSuite MontarNo(ModuloSuite m) =>
        new(m.Nome, m, null, [.. m.Vm.Menus.Select(i => MontarNo(m, i, m.Nome)).OfType<NoSuite>()], m.Nome);

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
