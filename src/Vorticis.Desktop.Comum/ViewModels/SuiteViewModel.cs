using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Specialized;
using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Domain.Comum.Formato;

namespace Vorticis.Desktop.Comum.ViewModels;

public enum LayoutSuite { Abas, Lateral, Arvore, Painel }

public sealed partial class SuiteViewModel : ObservableObject
{
    public SuiteViewModel(IReadOnlyList<MainViewModelBase> modulos,
        LayoutSuite? layout = null, string nome = "Vorticis Suite Logística",
        string? principal = null)
    {
        if (modulos.Count == 0) throw new ArgumentException(
            "A Suite precisa de pelo menos um módulo.", nameof(modulos));

        Nome = nome;

        var ordem = StringComparer.Create(PtBr.Instance, ignoreCase: true);
        Modulos = [.. modulos.Select(m => new ModuloSuite(m))
            .OrderBy(m => m.Nome, ordem)];

        _principal = Modulos.FirstOrDefault(m =>
            principal is not null && m.Vm.Modulo.NomeExecutavel == principal)
            ?? Modulos.FirstOrDefault(m => m.Vm.Modulo.GerenciaSistema)
            ?? Modulos[0];

        foreach (var m in Modulos)
        {
            m.Vm.PerguntarAtivacaoAoEntrar = false;
            m.Vm.SessaoPronta += async (_, _) => await PropagarSessaoAsync(m.Vm);
            m.Vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModelBase.Logado)) VerificarLogado(); };
            m.Vm.JanelasAbertas.CollectionChanged += (_, _) => m.AvisarAbertas();
        }

        _ativo = _principal;
        _ativo.Ativo = true;
        _layout = layout ?? LerLayout();
        _lateralFixa = LerFixa();
        Arvore = [.. Modulos.Select(MontarNo)];
    }

    public string Nome { get; }

    [ObservableProperty] private LayoutSuite _layout;
    [ObservableProperty] private bool _lateralAberta = true;
    [ObservableProperty] private bool _lateralFixa;
    [ObservableProperty] private string _busca = "";
    [ObservableProperty] private ObservableCollection<ModuloSuite> _modulos = [];
    [ObservableProperty] private ModuloSuite? _ativo;
    [ObservableProperty] private ObservableCollection<NoSuite> _arvore = [];
    [ObservableProperty] private ObservableCollection<NoSuite> _resultados = [];

    public bool EhAbas => Layout == LayoutSuite.Abas;
    public bool EhLateral => Layout == LayoutSuite.Lateral;
    public bool EhArvore => Layout == LayoutSuite.Arvore;
    public bool EhPainel => Layout == LayoutSuite.Painel;
    public bool MostraPainel => EhPainel && !Logado;
    public bool Logado => Modulos.All(m => m.Vm.Logado);

    public string Saudacao => Logado
        ? $"Bem-vindo, {Modulos.First().Vm.Sessao?.Usuario?.Nome ?? "Usuário"}!"
        : "Bem-vindo ao Vorticis Suite Logística";

    public string Hoje => DateTime.Now.ToString("dddd, dd 'de' MMMM 'de' yyyy", new CultureInfo("pt-BR"));

    private readonly MainViewModelBase _principal;

    [RelayCommand]
    private void EscolherLayout(LayoutSuite l)
    {
        Layout = l;
        SalvarLayout(l);
    }

    [RelayCommand]
    private void AlternarFixa() => LateralFixa = !LateralFixa;

    [RelayCommand]
    private async Task TrocarAsync(ModuloSuite? modulo)
    {
        if (modulo == null || modulo == Ativo) return;
        Ativo.Ativo = false;
        Ativo = modulo;
        Ativo.Ativo = true;
        await Ativo.Vm.GarantirSessaoAsync();
    }

    [RelayCommand]
    private void IrParaPainel() => Layout = LayoutSuite.Painel;

    [RelayCommand]
    private void AbrirFuncao(NoSuite no)
    {
        if (no.Chave != null) Ativo?.Vm.Abrir(no.Chave);
    }

    partial void OnBuscaChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            Resultados.Clear();
        else
            Resultados = new(Arvore.SelectMany(BuscarNo).Where(n =>
                Normalizar(n.Titulo).Contains(Normalizar(value))));
    }

    private static string Normalizar(string s) => s
        .Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
        .Select(c => char.ToLowerInvariant(c))
        .ToArray();

    private IEnumerable<NoSuite> BuscarNo(NoSuite no)
    {
        yield return no;
        foreach (var filho in no.Filhos)
            foreach (var f in BuscarNo(filho)) yield return f;
    }

    private NoSuite MontarNo(ModuloSuite m) => new(m.NomeCompleto, m, null, [], m.NomeCompleto)
    {
        Filhos = [.. m.Vm.Modulo.Menus.SelectMany(mn => MontarSubmenu(mn, m))]
    };

    private IEnumerable<NoSuite> MontarSubmenu(ItemMenu menu, ModuloSuite modulo)
    {
        if (menu is { Tipo: ItemMenuTipo.Submenu, Filhos: { Count: > 0 } filhos })
        {
            yield return new(menu.Titulo, modulo, null, [], $"{modulo.Nome} › {menu.Titulo}")
            {
                Filhos = [.. filhos.SelectMany(f => MontarItem(f, modulo))]
            };
        }
    }

    private IEnumerable<NoSuite> MontarItem(ItemMenu item, ModuloSuite modulo)
    {
        if (item.Tipo == ItemMenuTipo.Tela && item.Chave != null)
            yield return new(item.Titulo, modulo, item.Chave, [], $"{modulo.Nome} › {item.Titulo}");
        else if (item.Tipo == ItemMenuTipo.Submenu && item.Filhos?.Count > 0)
        {
            yield return new(item.Titulo, modulo, null, [], $"{modulo.Nome} › {item.Titulo}")
            {
                Filhos = [.. item.Filhos.SelectMany(f => MontarItem(f, modulo))]
            };
        }
    }

    private async Task PropagarSessaoAsync(MainViewModelBase destino)
    {
        if (!destino.Logado && _principal.Sessao != null)
            await destino.EntrarAsync(_principal.Sessao);
    }

    private void VerificarLogado() => OnPropertyChanged(nameof(Logado));

    private static LayoutSuite LerLayout()
    {
        var path = Path.Combine(ConfigHelper.PastaConfig, "suite-layout.txt");
        return File.Exists(path) && Enum.TryParse(File.ReadAllText(path), out LayoutSuite l) ? l : LayoutSuite.Lateral;
    }

    private static bool LerFixa()
    {
        var path = Path.Combine(ConfigHelper.PastaConfig, "suite-layout-fixa.txt");
        return File.Exists(path) && bool.TryParse(File.ReadAllText(path), out var f) && f;
    }

    private void SalvarLayout(LayoutSuite l)
    {
        var path = Path.Combine(ConfigHelper.PastaConfig, "suite-layout.txt");
        File.WriteAllText(path, l.ToString());
    }

    partial void OnLateralFixaChanged(bool value)
    {
        var path = Path.Combine(ConfigHelper.PastaConfig, "suite-layout-fixa.txt");
        File.WriteAllText(path, value.ToString());
    }
}

public sealed partial class ModuloSuite : ObservableObject
{
    public ModuloSuite(MainViewModelBase vm) { Vm = vm; }

    public MainViewModelBase Vm { get; }
    public string Nome => Vm.Modulo.NomeCurto;
    public string NomeCompleto => Vm.Modulo.Nome;
    public string Subtitulo => Vm.Modulo.Subtitulo;
    public IBrush Cor => Vm.PincelDestaque;
    public IBrush CorBarra => Vm.PincelBarra;

    public string Sigla => "";

    [ObservableProperty] private bool _ativo;
    private IReadOnlyList<NoSuite>? _atalhos;
    public IReadOnlyList<NoSuite> Atalhos => _atalhos ??= [.. Vm.Atalhos
        .Where(a => a.Chave is { } c && c != TelasComuns.ManualContextual)
        .Take(3)
        .Select(a => new NoSuite(a.Descricao, this, a.Chave, [], $"{Nome} › {a.Descricao}"))];

    public int Abertas => Vm.JanelasAbertas.Count;
    public bool TemAbertas => Abertas > 0;

    internal void AvisarAbertas()
    {
        OnPropertyChanged(nameof(Abertas));
        OnPropertyChanged(nameof(TemAbertas));
    }
}

public sealed class NoSuite(string titulo, ModuloSuite modulo, string? chave, List<NoSuite> filhos, string caminhoCompleto) : ObservableObject
{
    public string Titulo { get; } = titulo;
    public ModuloSuite Modulo { get; } = modulo;
    public string? Chave { get; } = chave;
    public List<NoSuite> Filhos { get; } = filhos;
    public string CaminhoCompleto { get; } = caminhoCompleto;
    public bool EhFuncao => Chave != null;
    public bool EhModulo => Chave == null && Filhos.Count == 0;
}

public static class ConfigHelper
{
    public static string PastaConfig => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vorticis", "config");
}