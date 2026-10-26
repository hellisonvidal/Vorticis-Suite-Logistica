using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vorticis.Desktop.Comum.ViewModels;

/// <summary>Item da árvore da aba Menus. Desmarcado = oculto. Itens essenciais ficam sempre marcados.</summary>
public sealed partial class NoMenuViewModel : ObservableObject
{
    private readonly Action? _aoMudar;
    private bool _ajustando;

    public NoMenuViewModel(NoMenu no, bool marcado, bool ocultoParaTodos, Action? aoMudar)
    {
        Caminho = no.Caminho;
        Titulo = no.Titulo;
        Essencial = no.Essencial;
        OcultoParaTodos = ocultoParaTodos;
        _aoMudar = aoMudar;
        _ajustando = true;
        Marcado = marcado || no.Essencial;
        _ajustando = false;
    }

    public string Caminho { get; }
    public string Titulo { get; }
    public bool Essencial { get; }
    public bool OcultoParaTodos { get; }
    public IReadOnlyList<NoMenuViewModel> Filhos { get; set; } = [];
    public NoMenuViewModel? Pai { get; set; }

    public bool Editavel => !Essencial;

    public string Rotulo => (Essencial ? $"{Titulo} (essencial)" : Titulo) + (OcultoParaTodos ? " (oculto pelo administrador)" : "");

    [ObservableProperty] private bool _marcado;

    partial void OnMarcadoChanged(bool value)
    {
        if (_ajustando) return;
        _ajustando = true;
        // marcar/desmarcar um menu leva junto os itens dele (menos os essenciais); marcar um item mostra também os menus que o contêm
        if (!value) foreach (var f in Filhos) f.DefinirTodos(false);
        else if (Filhos.Count > 0) foreach (var f in Filhos) f.DefinirTodos(true);
        if (value) for (var p = Pai; p is not null; p = p.Pai) p.ForcarMarcado();
        _ajustando = false;
        _aoMudar?.Invoke();
    }

    internal void ForcarMarcado()
    {
        if (!Marcado) { _ajustando = true; Marcado = true; _ajustando = false; }
    }

    internal void DefinirTodos(bool valor)
    {
        if (Essencial && !valor) { foreach (var f in Filhos) f.DefinirTodos(valor); return; }
        _ajustando = true;
        Marcado = valor || Essencial;
        _ajustando = false;
        foreach (var f in Filhos) f.DefinirTodos(valor);
    }

    internal IEnumerable<NoMenuViewModel> Todos() => Filhos.SelectMany(f => f.Todos()).Prepend(this);
}

/// <summary>
/// Cadastros › Parâmetros › Menus: árvore dos menus do programa com caixas de marcação. O que for desmarcado some do menu e a tela fica
/// bloqueada (inclusive por atalho). Cada usuário escolhe para si; o administrador pode aplicar a escolha para todos.
/// </summary>
public sealed partial class PreferenciasMenuViewModel(MainViewModelBase main) : ModuloViewModel(main), IAlteracoesPendentes
{
    private bool _carregando;

    public ObservableCollection<NoMenuViewModel> Raiz { get; } = [];

    public bool PodeParaTodos => main.Sessao?.Administrador == true;

    [ObservableProperty] private bool _paraTodos;
    [ObservableProperty] private bool _alterado;

    public bool TemAlteracoesPendentes => Alterado;

    private IEnumerable<NoMenuViewModel> Nos => Raiz.SelectMany(n => n.Todos());

    public void Carregar()
    {
        _carregando = true;
        OnPropertyChanged(nameof(PodeParaTodos));
        Raiz.Clear();
        var prefs = main.PreferenciasDeMenu;
        void Ligar(NoMenuViewModel n) { foreach (var f in n.Filhos) { f.Pai = n; Ligar(f); } }
        var ocultos = prefs.Ocultos;
        foreach (var no in main.ArvoreDeMenus()) Raiz.Add(Montar(no, ocultos, prefs.DeTodos));
        foreach (var n in Raiz) Ligar(n);
        ParaTodos = false;
        Alterado = false;
        Erros.Clear();
        Mensagem = null;
        _carregando = false;
    }

    private NoMenuViewModel Montar(NoMenu no, IReadOnlySet<string> ocultos, IReadOnlySet<string> deTodos)
    {
        var vm = new NoMenuViewModel(no, !ocultos.Contains(no.Caminho), deTodos.Contains(no.Caminho) && !no.Essencial, AoMudar)
        {
            Filhos = [.. no.Filhos.Select(f => Montar(f, ocultos, deTodos))],
        };
        return vm;
    }

    private void AoMudar()
    {
        if (!_carregando) Alterado = true;
    }

    partial void OnParaTodosChanged(bool value) => AoMudar();

    [RelayCommand]
    private void MostrarTodos()
    {
        foreach (var n in Raiz) n.DefinirTodos(true);
        AoMudar();
    }

    [RelayCommand]
    private Task DescartarAsync() { Carregar(); return Task.CompletedTask; }

    [RelayCommand]
    private async Task SalvarAsync()
    {
        Erros.Clear();
        Mensagem = null;
        try
        {
            var ocultos = Nos.Where(n => !n.Marcado && !n.Essencial).Select(n => n.Caminho).ToList();
            // escolha pessoal não repete o que o administrador já escondeu para todos
            if (!ParaTodos) ocultos = [.. ocultos.Where(c => !main.PreferenciasDeMenu.DeTodos.Contains(c))];
            var r = await main.SalvarPreferenciasMenuAsync(ocultos, ParaTodos);
            if (!r.Sucesso) { foreach (var e in r.Erros) Erros.Add(e); return; }
            Carregar();
            Mensagem = "Preferências de menu gravadas.";
        }
        catch (Exception ex) { Erros.Add(ex.Message); }
    }
}
