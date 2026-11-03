using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Desktop.Comum.ViewModels;

namespace Vorticis.Desktop.Comum.Views;

/// <summary>
/// Cadastros em janela pop-up: a tela do cadastro mostra só a lista dos registros já gravados e o formulário abre numa janela por cima.
/// <list type="bullet">
/// <item><c>c:FormularioPopup.Titulo="Veículo"</c> no painel do formulário: ele sai da tela (a coluna ou a linha dele fecha) e passa a abrir no pop-up.</item>
/// <item><c>c:FormularioPopup.Lista="True"</c> na lista (DataGrid/ListBox): ganha a barra <b>Novo</b> / <b>Abrir</b>; duplo clique ou Enter abrem o registro.</item>
/// </list>
/// Nos cadastros com a barra padrão (Incluir, Alterar, Salvar, Cancelar: <see cref="CadastroViewModel"/>), o pop-up abre sozinho ao incluir ou alterar e fecha
/// ao salvar ou cancelar; fechar pelo X ou Esc cancela a edição. Nos demais, o pop-up fecha ao gravar sem erros (botão de destaque) ou pelo X, Esc e Fechar.
/// </summary>
public static class FormularioPopup
{
    public static readonly AttachedProperty<string?> TituloProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Titulo", typeof(FormularioPopup));

    public static readonly AttachedProperty<bool> ListaProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Lista", typeof(FormularioPopup));

    public static string? GetTitulo(Control c) => c.GetValue(TituloProperty);
    public static void SetTitulo(Control c, string? v) => c.SetValue(TituloProperty, v);
    public static bool GetLista(Control c) => c.GetValue(ListaProperty);
    public static void SetLista(Control c, bool v) => c.SetValue(ListaProperty, v);

    /// <summary>Nomes dos comandos de "registro novo" procurados no ViewModel (na ordem).</summary>
    private static readonly string[] ComandosNovo = ["NovoCommand", "NovaCommand", "IncluirCommand", "NovoRegistroCommand"];

    private static readonly ConditionalWeakTable<UserControl, Estado> Estados = new();

    static FormularioPopup()
    {
        TituloProperty.Changed.AddClassHandler<Control>((c, e) =>
        {
            if (e.NewValue is string) c.AttachedToVisualTree += (_, _) => Registrar(c);
        });
        ListaProperty.Changed.AddClassHandler<Control>((c, e) =>
        {
            if (e.NewValue is true) c.AttachedToVisualTree += (_, _) => RegistrarLista(c);
        });
    }

    /// <summary>Estado de uma tela de cadastro: o formulário, onde ele estava e o pop-up aberto.</summary>
    private sealed class Estado
    {
        public Control? Formulario;
        public string Titulo = "";
        public Panel? Pai;
        public int Indice;
        public Window? Janela;
        public INotifyPropertyChanged? Vm;
        public PropertyChangedEventHandler? AoMudar;
        public bool Fechando;
    }

    private static UserControl? Tela(Control c) => c.FindAncestorOfType<UserControl>(includeSelf: false);

    /// <summary>Pop-up aberto da tela (testes).</summary>
    public static Window? JanelaDe(UserControl tela) => Estados.TryGetValue(tela, out var e) ? e.Janela : null;

    private static void Registrar(Control formulario)
    {
        if (Tela(formulario) is not { } tela) return;
        var estado = Estados.GetValue(tela, _ => new Estado());
        if (ReferenceEquals(estado.Formulario, formulario) || estado.Janela is not null) return;
        estado.Formulario = formulario;
        estado.Titulo = GetTitulo(formulario) ?? "Cadastro";
        Recolher(formulario);
        tela.DataContextChanged += (_, _) => Ligar(tela, estado);
        Ligar(tela, estado);
    }

    /// <summary>Esconde o formulário na tela e fecha a coluna (ou a linha) dele, com o espaçador ao lado.</summary>
    private static void Recolher(Control formulario)
    {
        formulario.IsVisible = false;
        if (formulario.Parent is not Grid grade) return;
        var col = Grid.GetColumn(formulario);
        if (grade.ColumnDefinitions.Count > 1 && col < grade.ColumnDefinitions.Count && Grid.GetColumnSpan(formulario) == 1
            && !grade.Children.Any(o => !ReferenceEquals(o, formulario) && Grid.GetColumn(o) == col && Grid.GetColumnSpan(o) == 1))
        {
            grade.ColumnDefinitions[col].Width = new GridLength(0);
            grade.ColumnDefinitions[col].MinWidth = 0;
            foreach (var vizinha in new[] { col - 1, col + 1 })
                if (vizinha >= 0 && vizinha < grade.ColumnDefinitions.Count && grade.ColumnDefinitions[vizinha] is { Width.IsAbsolute: true } d && d.Width.Value <= 16
                    && !grade.Children.Any(o => Grid.GetColumn(o) == vizinha && Grid.GetColumnSpan(o) == 1))
                    d.Width = new GridLength(0);
            // a lista ocupa o espaço que sobrou (ex.: a lista de clientes, que tinha largura fixa ao lado do formulário)
            if (!grade.ColumnDefinitions.Any(d => d.Width.IsStar))
                foreach (var d in grade.ColumnDefinitions.Where(d => d.Width.IsAbsolute && d.Width.Value > 16)) d.Width = new GridLength(1, GridUnitType.Star);
        }
        var lin = Grid.GetRow(formulario);
        if (grade.RowDefinitions.Count > 1 && lin < grade.RowDefinitions.Count && Grid.GetRowSpan(formulario) == 1
            && !grade.Children.Any(o => !ReferenceEquals(o, formulario) && Grid.GetRow(o) == lin && Grid.GetRowSpan(o) == 1))
        {
            grade.RowDefinitions[lin].Height = new GridLength(0);
            foreach (var vizinha in new[] { lin - 1, lin + 1 })
                if (vizinha >= 0 && vizinha < grade.RowDefinitions.Count && grade.RowDefinitions[vizinha] is { Height.IsAbsolute: true } d && d.Height.Value <= 16
                    && !grade.Children.Any(o => Grid.GetRow(o) == vizinha && Grid.GetRowSpan(o) == 1))
                    d.Height = new GridLength(0);
        }
    }

    private static void Ligar(UserControl tela, Estado estado)
    {
        if (estado.Vm is { } antigo && estado.AoMudar is { } h) antigo.PropertyChanged -= h;
        estado.Vm = tela.DataContext as INotifyPropertyChanged;
        if (estado.Vm is { } vm)
        {
            estado.AoMudar = (_, e) =>
            {
                if (e.PropertyName != nameof(CadastroViewModel.EmEdicao) || vm is not CadastroViewModel c) return;
                if (c.EmEdicao) Abrir(tela);
                else Fechar(estado);
            };
            vm.PropertyChanged += estado.AoMudar;
        }
    }

    private static void RegistrarLista(Control lista)
    {
        if (lista.Tag as string == "popup-ligada") return;
        lista.Tag = "popup-ligada";
        lista.DoubleTapped += (_, e) =>
        {
            if (e.Source is Visual v && v.FindAncestorOfType<DataGridColumnHeader>(includeSelf: true) is not null) return;
            AbrirSelecionado(lista);
        };
        lista.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None) { AbrirSelecionado(lista); e.Handled = true; }
        };
        // a barra Novo/Abrir sobre a lista (os cadastros com a barra padrão já têm Incluir e Alterar)
        if (Tela(lista) is not { } tela || tela.DataContext is CadastroViewModel || lista.Parent is not Panel pai) return;
        var novo = new Button { Content = "Novo", MinWidth = 86, HorizontalContentAlignment = HorizontalAlignment.Center };
        novo.Classes.Add("accent");
        novo.Click += (_, _) => AbrirNovo(lista);
        var abrir = new Button { Content = "Abrir", MinWidth = 86, HorizontalContentAlignment = HorizontalAlignment.Center };
        abrir.Click += (_, _) => AbrirSelecionado(lista);
        var dica = new TextBlock
        {
            Text = "Duplo clique ou Enter abre o registro.", FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#5B6478")),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var barra = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 6), Children = { novo, abrir, dica } };
        var indice = pai.Children.IndexOf(lista);
        pai.Children.RemoveAt(indice);
        var envoltorio = new DockPanel();
        foreach (var p in new AvaloniaProperty[] { Grid.RowProperty, Grid.ColumnProperty, Grid.RowSpanProperty, Grid.ColumnSpanProperty, DockPanel.DockProperty, Layoutable.MarginProperty })
            if (lista.IsSet(p)) { envoltorio.SetValue(p, lista.GetValue(p)); lista.ClearValue(p); }
        DockPanel.SetDock(barra, Dock.Top);
        envoltorio.Children.Add(barra);
        envoltorio.Children.Add(lista);
        pai.Children.Insert(indice, envoltorio);
        lista.Tag = "popup-ligada";
    }

    private static object? Selecionado(Control lista) => lista switch
    {
        DataGrid g => g.SelectedItem,
        Avalonia.Controls.Primitives.SelectingItemsControl s => s.SelectedItem,
        _ => null,
    };

    private static void AbrirSelecionado(Control lista)
    {
        if (Tela(lista) is not { } tela || Selecionado(lista) is null) return;
        if (tela.DataContext is CadastroViewModel c)
        {
            if (c.AlterarCommand.CanExecute(null)) c.AlterarCommand.Execute(null);   // abre o pop-up pelo modo de edição
            return;
        }
        Abrir(tela);
    }

    private static void AbrirNovo(Control lista)
    {
        if (Tela(lista) is not { } tela) return;
        var vm = tela.DataContext;
        var comando = vm is null ? null : ComandosNovo.Select(n => vm.GetType().GetProperty(n)?.GetValue(vm) as ICommand).FirstOrDefault(c => c is not null);
        if (comando?.CanExecute(null) == true) comando.Execute(null);
        if (vm is not CadastroViewModel) Dispatcher.UIThread.Post(() => Abrir(tela), DispatcherPriority.Background);
    }

    /// <summary>Abre o formulário da tela no pop-up (o mesmo formulário, com o mesmo ViewModel).</summary>
    public static void Abrir(UserControl tela)
    {
        if (!Estados.TryGetValue(tela, out var estado) || estado.Formulario is not { } formulario || estado.Janela is not null) return;
        if (TopLevel.GetTopLevel(tela) is not Window dono || !dono.IsVisible) return;
        if (formulario.Parent is not Panel pai) return;
        estado.Pai = pai;
        estado.Indice = pai.Children.IndexOf(formulario);
        pai.Children.RemoveAt(estado.Indice);
        formulario.IsVisible = true;
        formulario.DataContext = tela.DataContext;

        var fechar = new Button { Content = "Fechar", MinWidth = 86, HorizontalContentAlignment = HorizontalAlignment.Center };
        var rodape = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 8) };
        if (tela.DataContext is CadastroViewModel cad)
        {
            var salvar = new Button { Content = "Salvar", MinWidth = 86, HorizontalContentAlignment = HorizontalAlignment.Center, Command = cad.SalvarCommand };
            salvar.Classes.Add("accent");
            rodape.Children.Add(salvar);
            fechar.Content = "Cancelar";
        }
        rodape.Children.Add(fechar);
        var corpo = formulario is ScrollViewer ? formulario : new ScrollViewer { Content = formulario, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var conteudo = new DockPanel { Background = Brushes.White };
        DockPanel.SetDock(rodape, Dock.Bottom);
        conteudo.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.Parse("#D0D5DD")), BorderThickness = new Thickness(0, 1, 0, 0), Child = rodape, [DockPanel.DockProperty] = Dock.Bottom });
        var moldura = new Border { Padding = new Thickness(14, 12), Child = corpo };
        conteudo.Children.Add(moldura);

        var janela = new Window
        {
            Title = estado.Titulo,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 480, MinHeight = 240, MaxWidth = Math.Max(600, dono.Bounds.Width * 0.95), MaxHeight = Math.Max(400, dono.Bounds.Height * 0.92),
            CanResize = true,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = conteudo,
            Icon = dono.Icon,
        };
        fechar.Click += (_, _) => janela.Close();
        // gravar sem erros fecha o pop-up (cadastros sem a barra padrão)
        if (tela.DataContext is ModuloViewModel mvm and not CadastroViewModel)
            foreach (var b in formulario.GetVisualDescendants().OfType<Button>().Concat(formulario.GetLogicalDescendantsSeguro().OfType<Button>()).Distinct()
                         .Where(b => b.Classes.Contains("accent")))
                b.Click += (_, _) => Dispatcher.UIThread.Post(async () =>
                {
                    if (b.Command is IAsyncRelayCommand a && a.ExecutionTask is { } t) { try { await t; } catch (Exception) { /* o erro aparece no formulário */ } }
                    if (mvm.Erros.Count == 0 && estado.Janela is not null && !Pendente(mvm)) janela.Close();
                }, DispatcherPriority.Background);
        var principal = dono as MainWindow ?? dono.Owner as MainWindow;
        janela.Closing += (_, _) =>
        {
            // fechar pelo X ou Esc durante a edição desiste da edição
            if (tela.DataContext is CadastroViewModel c && c.EmEdicao && !estado.Fechando && c.CancelarCommand.CanExecute(null)) { estado.Fechando = true; c.CancelarCommand.Execute(null); }
        };
        janela.Closed += (_, _) =>
        {
            conteudo.Children.Clear();
            moldura.Child = null;
            if (corpo is ScrollViewer sv && !ReferenceEquals(sv, formulario)) sv.Content = null;
            formulario.ClearValue(StyledElement.DataContextProperty);
            if (estado.Pai is { } p && !p.Children.Contains(formulario)) p.Children.Insert(Math.Min(estado.Indice, p.Children.Count), formulario);
            formulario.IsVisible = false;
            estado.Janela = null;
            estado.Fechando = false;
            if (principal is not null && ReferenceEquals(principal.ModalAberta, janela)) principal.ModalAberta = null;
            Dispatcher.UIThread.Post(() => tela.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
        };
        janela.KeyDown += (_, e) => MainWindow.TratarEsc(e, () => { janela.Close(); return Task.CompletedTask; });
        estado.Janela = janela;
        if (principal is not null) principal.ModalAberta = janela;
        _ = janela.ShowDialog(dono);
    }

    /// <summary>O ViewModel ainda tem alteração não gravada (cadastros que avisam pelo <see cref="IAlteracoesPendentes"/>).</summary>
    private static bool Pendente(object vm) => vm is IAlteracoesPendentes p && p.TemAlteracoesPendentes;

    private static void Fechar(Estado estado)
    {
        if (estado.Janela is { } j && !estado.Fechando) { estado.Fechando = true; j.Close(); }
    }

    private static IEnumerable<Avalonia.LogicalTree.ILogical> GetLogicalDescendantsSeguro(this Control c) =>
        Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(c);
}
