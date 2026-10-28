using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Desktop.Comum.ViewModels;

namespace Vorticis.Desktop.Comum.Views;

public partial class MainWindow : Window
{
    private MainViewModelBase? _vm;

    public MainWindow()
    {
        InitializeComponent();
        // Sempre inicia maximizada (tela cheia, mantendo a barra de título com o nome do programa).
        WindowState = WindowState.Maximized;
        Opened += (_, _) => WindowState = WindowState.Maximized;
        // Fechar o sistema sempre pede confirmação.
        Closing += AoFechar;
        // Esc na janela principal fecha a função do topo (as janelas das funções tratam o próprio Esc).
        KeyDown += (_, e) =>
        {
            if (_vm?.Logado != true || _vm.FuncaoAtiva is not { } topo) return;
            TratarDelete(e, this, _vm, topo.ViewModel);
            TratarEsc(e, () => _vm.FecharFuncaoComConfirmacaoAsync(topo));
        };
        // As janelas das funções ocupam a área de trabalho (abaixo do título, menus e barra de janelas) e a acompanham.
        PositionChanged += (_, _) => ReposicionarJanelas();
        // Qualquer mudança de layout que mova a área de trabalho (faixa de funções, navegação da Suite) reposiciona as funções.
        AreaPrincipal.LayoutUpdated += (_, _) => ReposicionarSeMudou();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
            {
                _vm.PropertyChanged -= AoMudarVm;
                _vm.MenusAlterados -= AoMudarMenus;
                if (Suite is null) _vm.JanelasAbertas.CollectionChanged -= AoMudarJanelas;
                else EsconderJanelasDe(_vm);
                if (_dialogo is { } aberto) { _dialogo = null; _fechandoPorCodigo.Add(aberto); aberto.Close(); }
            }
            _vm = DataContext as MainViewModelBase;
            if (_vm is not null)
            {
                Title = Suite is null ? _vm.Modulo.Nome : $"{Suite.Nome} — {_vm.Modulo.NomeCurto}";
                AplicarIdentidadeVisual(_vm);
                MontarMenu(_vm);
                MontarAtalhos(_vm);
                _vm.PropertyChanged += AoMudarVm;
                _vm.MenusAlterados += AoMudarMenus;
                if (Suite is null) _vm.JanelasAbertas.CollectionChanged += AoMudarJanelas;
                Sincronizar(_vm);
                MostrarJanelasDe(_vm);
                if (_vm.Confirmacao is { } pendente) MostrarConfirmacao(pendente);
            }
        };
    }

    // ------------------------------------------------------------------ Suite: vários módulos na mesma janela

    /// <summary>Suite em uso (null nos executáveis de um módulo só).</summary>
    public SuiteViewModel? Suite { get; private set; }

    /// <summary>
    /// Hospeda todos os módulos da Suite: a navegação (abas, barra lateral, árvore ou painel, conforme o layout) troca o
    /// módulo ativo, que vira o DataContext da janela; as janelas das funções de cada módulo ficam escondidas enquanto
    /// outro módulo está à frente.
    /// </summary>
    public void UsarSuite(SuiteViewModel suite)
    {
        Suite = suite;
        SuiteLado.Content = new SuiteLadoView { DataContext = suite };
        SuiteTopo.Content = new SuiteTopoView { DataContext = suite };
        SuitePainel.Content = new SuitePainelView { DataContext = suite };
        (SuiteLado.IsVisible, SuiteTopo.IsVisible) = (true, true);
        foreach (var m in suite.Modulos) m.Vm.JanelasAbertas.CollectionChanged += AoMudarJanelas;
        suite.AoTrocar += (_, m) => DataContext = m.Vm;
        suite.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SuiteViewModel.MostraPainel)) AtualizarPainel();
            if (e.PropertyName is nameof(SuiteViewModel.Layout) or nameof(SuiteViewModel.LateralFixa) or nameof(SuiteViewModel.TopoFixo) && _vm is { } vm)
            {
                MontarMenu(vm);
                MontarAtalhos(vm);
            }
        };
        DataContext = suite.Ativo.Vm;
        AtualizarPainel();
    }

    /// <summary>Painel do layout D: à mostra, esconde as janelas das funções; ao sair dele, elas voltam.</summary>
    private void AtualizarPainel()
    {
        var painel = Suite?.MostraPainel == true;
        SuitePainel.IsVisible = painel;
        if (_vm is null) return;
        if (painel) EsconderJanelasDe(_vm);
        else MostrarJanelasDe(_vm);
    }

    private void EsconderJanelasDe(MainViewModelBase vm)
    {
        foreach (var (f, w) in _janelas.Where(kv => ReferenceEquals(_donos[kv.Key], vm)).ToList())
            if (w.IsVisible) w.Hide();
    }

    private void MostrarJanelasDe(MainViewModelBase vm)
    {
        if (Suite?.MostraPainel == true) return;
        foreach (var f in vm.JanelasAbertas)
            if (_janelas.TryGetValue(f, out var w) && !w.IsVisible) Mostrar(w);
        TrazerParaFrente(vm.FuncaoAtiva);
    }

    private void Mostrar(Window w)
    {
        Posicionar(w);
        // Sempre com a janela principal como dona: Show() sem dona soltaria a função, e a janela principal passaria a
        // cobri-la ao ser clicada (a barra de funções abertas ficava por cima das janelas).
        w.Show(this);
    }

    /// <summary>Ícone e cor do módulo (Contábil verde, Fiscal laranja, Folha roxo) na janela e nas janelas das funções.</summary>
    private void AplicarIdentidadeVisual(MainViewModelBase vm)
    {
        Resources["CorDestaqueModulo"] = vm.PincelDestaque;
        try
        {
            // módulos da mesma Suite (VorticisLogistica.Frota...) usam o ícone do programa (VorticisLogistica)
            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri($"avares://Vorticis.Desktop.Comum/Assets/{vm.Modulo.NomeExecutavel.Split('.')[0]}.ico"));
            Icon = new WindowIcon(stream);
        }
        catch (Exception)
        {
            // sem o ícone do módulo, fica o ícone padrão da janela
        }
    }

    // ------------------------------------------------------------------ janelas flutuantes das funções

    private readonly Dictionary<FuncaoAberta, Window> _janelas = [];
    private readonly Dictionary<FuncaoAberta, MainViewModelBase> _donos = [];
    private readonly HashSet<Window> _fechandoPorCodigo = [];
    private Window? _dialogo;

    /// <summary>Janela flutuante da função ativa (null se nenhuma função estiver aberta).</summary>
    private bool _fechamentoConfirmado;

    /// <summary>Pergunta antes de fechar o programa; só fecha de fato se o usuário confirmar.</summary>
    private async void AoFechar(object? sender, WindowClosingEventArgs e)
    {
        if (_fechamentoConfirmado || _vm is null) return;
        e.Cancel = true;
        if (_vm.Confirmacao is { } pendente)                               // já há uma pergunta aberta: traz para a frente (ou mostra de novo)
        {
            if (_dialogo is { IsVisible: true } d) d.Activate();
            else MostrarConfirmacao(pendente);
            return;
        }
        if (await _vm.ConfirmarAsync($"Fechar o {(Suite is null ? _vm.Modulo.Nome : Suite.Nome)}", "Deseja realmente fechar o sistema?", "Fechar", "Continuar trabalhando"))
        {
            _fechamentoConfirmado = true;
            Close();
        }
    }

    /// <summary>Diálogo de confirmação à mostra (null se não houver).</summary>
    public Window? DialogoConfirmacao => _dialogo;

    /// <summary>Janela modal em primeiro plano (pop-up de uma função); os diálogos de confirmação abrem sobre ela.</summary>
    public Window? ModalAberta { get; set; }

    public Window? JanelaAtiva => _vm?.FuncaoAtiva is { } f && _janelas.TryGetValue(f, out var w) ? w : null;

    public Window? JanelaDa(object viewModel) => _janelas.FirstOrDefault(kv => ReferenceEquals(kv.Key.ViewModel, viewModel)).Value;

    private void AoMudarJanelas(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var vm = Suite?.Modulos.Select(m => m.Vm).FirstOrDefault(m => ReferenceEquals(m.JanelasAbertas, sender)) ?? _vm;
        if (vm is not null) Sincronizar(vm);
    }

    /// <summary>Abre janelas das funções novas do módulo e fecha as que saíram da lista (na Suite, só as do módulo à frente aparecem).</summary>
    private void Sincronizar(MainViewModelBase vm)
    {
        foreach (var f in vm.JanelasAbertas.Where(f => !_janelas.ContainsKey(f)).ToList()) AbrirJanela(f, vm);
        foreach (var (f, w) in _janelas.Where(kv => ReferenceEquals(_donos[kv.Key], vm) && !vm.JanelasAbertas.Contains(kv.Key)).ToList())
        {
            _janelas.Remove(f);
            _donos.Remove(f);
            _fechandoPorCodigo.Add(w);
            w.Close();
        }
    }

    private void AbrirJanela(FuncaoAberta f, MainViewModelBase dono)
    {
        var titulo = new TextBlock
        {
            Text = f.Titulo, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0),
        };
        var fechar = new Button
        {
            Content = "✕  Fechar", FontSize = 12, Padding = new Thickness(10, 2), Margin = new Thickness(4, 2),
            Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0),
            [ToolTip.TipProperty] = "Fechar esta função",
        };
        // cadastro que aceita planilha: o botão de importar fica na própria janela do cadastro
        var importar = new Button
        {
            Content = "⇪  Importar de planilha (Excel)", FontSize = 12, Padding = new Thickness(10, 2), Margin = new Thickness(4, 2),
            Background = new SolidColorBrush(Colors.White, 0.18), Foreground = Brushes.White, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(4),
            [ToolTip.TipProperty] = "Trazer os registros deste cadastro de uma planilha do Excel (ou CSV), escolhendo a coluna de cada campo",
        };
        void AtualizarImportar() => importar.IsVisible = dono.ImportacaoDaTela(f.Tela) is not null;
        AtualizarImportar();
        importar.Click += (_, _) =>
        {
            if (dono.ImportacaoDaTela(f.Tela) is { } def && dono.AbrirCommand.CanExecute(def.Tela)) dono.AbrirCommand.Execute(def.Tela);
        };
        var cabecalho = new Border
        {
            Background = dono.PincelDestaque ?? new SolidColorBrush(Color.Parse("#3A5F8F")), Height = 30,
            Child = new DockPanel { Children = { fechar, importar, titulo } },
        };
        DockPanel.SetDock(cabecalho, Dock.Top);
        DockPanel.SetDock(fechar, Dock.Right);
        DockPanel.SetDock(importar, Dock.Right);
        var w = new Window
        {
            Title = $"{f.Titulo} — {dono.Modulo.Nome}",
            Content = new DockPanel
            {
                Children = { cabecalho, new ContentControl { Content = f.ViewModel, Margin = new Thickness(4) } },
            },
            SystemDecorations = SystemDecorations.BorderOnly,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Background = Background,
            Icon = Icon,
        };
        Teclado.SetEnterAvanca(w, true);                       // Enter avança de campo em todas as telas de digitação
        Posicionar(w);
        fechar.Click += (_, _) => dono.FecharFuncao(f);
        f.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FuncaoAberta.Tela)) AtualizarImportar();
            if (e.PropertyName != nameof(FuncaoAberta.Titulo)) return;
            w.Title = $"{f.Titulo} — {dono.Modulo.Nome}";
            titulo.Text = f.Titulo;
        };
        w.Activated += (_, _) => dono.AtivarFuncao(f);
        w.Closed += (_, _) =>
        {
            if (!_fechandoPorCodigo.Remove(w)) dono.FecharFuncao(f);   // Alt+F4
        };
        w.KeyDown += (_, e) =>
        {
            TratarDelete(e, w, dono, f.ViewModel);                               // Delete exclui o registro selecionado (com confirmação)
            TratarEsc(e, () => dono.FecharFuncaoComConfirmacaoAsync(f));        // Esc fecha a função
        };
        w.KeyBindings.AddRange(KeyBindings);   // mesmos atalhos (Ctrl+L, Ctrl+B, F1)
        _janelas[f] = w;
        _donos[f] = dono;
        if (ReferenceEquals(dono, _vm) && Suite?.MostraPainel != true) w.Show(this);
    }

    /// <summary>
    /// Delete (sem outra tecla, fora de campos de texto) exclui o registro selecionado: usa o botão Excluir da tela em foco (o ViewModel mais
    /// próximo do controle focado que tem ExcluirCommand; senão, o da janela), sempre com confirmação. Sem botão Excluir, não faz nada.
    /// </summary>
    public static void TratarDelete(KeyEventArgs e, TopLevel janela, MainViewModelBase dono, object? viewModelPadrao)
    {
        if (e.Handled || e.Key != Key.Delete || e.KeyModifiers != KeyModifiers.None) return;
        var foco = janela.FocusManager?.GetFocusedElement() as Visual;
        // dentro de um campo, Delete apaga texto
        if (foco is TextBox or ComboBox or NumericUpDown or CalendarDatePicker or DatePicker || foco?.FindAncestorOfType<TextBox>() is not null) return;
        object? alvo = null;
        for (var v = foco; v is not null && alvo is null; v = v.GetVisualParent())
            if (v is StyledElement s && MainViewModelBase.ComandoExcluir(s.DataContext) is not null) alvo = s.DataContext;
        alvo ??= viewModelPadrao;
        if (MainViewModelBase.ComandoExcluir(alvo) is not { } c || !c.CanExecute(null)) return;
        e.Handled = true;
        _ = dono.ExcluirPelaTeclaAsync(alvo);
    }

    /// <summary>Esc (sem outra tecla e não tratado por um campo, lista ou caixa aberta) executa o fechamento.</summary>
    public static void TratarEsc(KeyEventArgs e, Func<Task?> fechar)
    {
        if (e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None) return;
        e.Handled = true;
        _ = fechar();
    }

    /// <summary>Encaixa a janela da função na área de trabalho da janela principal (tela cheia sem cobrir título e menus).</summary>
    private void Posicionar(Window w)
    {
        var area = AreaPrincipal.Bounds;
        if (area.Width < 10 || area.Height < 10) return;
        var origem = AreaPrincipal.TranslatePoint(new Point(0, 0), this) ?? default;
        w.Position = this.PointToScreen(origem);
        (w.Width, w.Height) = (area.Width, area.Height);
    }

    private Rect _ultimaArea;

    private void ReposicionarSeMudou()
    {
        var origem = AreaPrincipal.TranslatePoint(new Point(0, 0), this) ?? default;
        var area = new Rect(origem, AreaPrincipal.Bounds.Size);
        if (area == _ultimaArea) return;
        _ultimaArea = area;
        ReposicionarJanelas();
    }

    private void ReposicionarJanelas()
    {
        foreach (var w in _janelas.Values) Posicionar(w);
    }

    private void TrazerParaFrente(FuncaoAberta? f)
    {
        if (f is not null && _janelas.TryGetValue(f, out var w) && !w.IsActive) w.Activate();
    }

    // ------------------------------------------------------------------ confirmação (Sim/Não)

    /// <summary>Caixas de seleção do diálogo (módulos da empresa, por exemplo); painel vazio quando o pedido não tem opções.</summary>
    private static StackPanel OpcoesDe(PedidoConfirmacao pedido)
    {
        var painel = new StackPanel { Spacing = 6, IsVisible = pedido.Opcoes.Count > 0 };
        foreach (var o in pedido.Opcoes)
        {
            var caixa = new CheckBox { Content = o.Texto, IsChecked = o.Marcada, IsEnabled = !o.Obrigatoria, Name = "Opcao" + o.Texto.Split(' ')[0] };
            caixa.IsCheckedChanged += (_, _) => o.Marcada = caixa.IsChecked == true || o.Obrigatoria;
            painel.Children.Add(caixa);
        }
        return painel;
    }

    private void MostrarConfirmacao(PedidoConfirmacao? pedido)
    {
        if (pedido is null)
        {
            if (_dialogo is { } aberto) { _dialogo = null; _fechandoPorCodigo.Add(aberto); aberto.Close(); }
            return;
        }
        var botaoSim = new Button { Content = pedido.TextoSim, Classes = { "accent" }, IsDefault = true, MinWidth = 90,
                                    HorizontalContentAlignment = HorizontalAlignment.Center };
        var botaoNao = new Button { Content = pedido.TextoNao, IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        var dialogo = new Window
        {
            Title = pedido.Titulo,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(24, 20),
                Spacing = 18,
                MaxWidth = 520,
                Children =
                {
                    new TextBlock { Text = pedido.Titulo, FontSize = 16, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = pedido.Mensagem, TextWrapping = TextWrapping.Wrap },
                    OpcoesDe(pedido),
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                                     Children = { botaoSim, botaoNao } },
                },
            },
        };
        if (pedido.PedeSenha)
        {
            var campo = new TextBox { PasswordChar = '●', Watermark = "Senha de administrador", Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
            campo.TextChanged += (_, _) => pedido.Senha = campo.Text ?? "";
            ((StackPanel)dialogo.Content!).Children.Insert(2, campo);
            dialogo.Opened += (_, _) => campo.Focus();
        }
        botaoSim.Click += (_, _) => _vm?.ResponderConfirmacaoCommand.Execute(true);
        botaoNao.Click += (_, _) => _vm?.ResponderConfirmacaoCommand.Execute(false);
        dialogo.Closed += (_, _) =>
        {
            if (!_fechandoPorCodigo.Remove(dialogo) && ReferenceEquals(_vm?.Confirmacao, pedido)) _vm.ResponderConfirmacaoCommand.Execute(false);
        };
        if (_dialogo is { } anterior && !ReferenceEquals(anterior, dialogo)) { _fechandoPorCodigo.Add(anterior); anterior.Close(); }
        _dialogo = dialogo;
        _ = dialogo.ShowDialog(DonoDoDialogo());
    }

    /// <summary>
    /// Dona do diálogo: a janela visível mais à frente (pop-up, função ativa ou a principal). Uma dona escondida (as funções ficam
    /// escondidas no painel da Suite e no módulo que não está à frente) deixaria o diálogo invisível e o X da janela sem resposta.
    /// </summary>
    private Window DonoDoDialogo() =>
        ModalAberta is { IsVisible: true } modal ? modal : JanelaAtiva is { IsVisible: true } funcao ? funcao : this;

    /// <summary>Ao entrar ou sair do sistema, volta a maximizar (caso o usuário tenha restaurado o tamanho).</summary>
    private void AoMudarVm(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModelBase.Logado)) WindowState = WindowState.Maximized;
        if (e.PropertyName == nameof(MainViewModelBase.FuncaoAtiva)) TrazerParaFrente(_vm?.FuncaoAtiva);
        if (e.PropertyName == nameof(MainViewModelBase.Confirmacao)) MostrarConfirmacao(_vm?.Confirmacao);
        if (e.PropertyName == nameof(MainViewModelBase.RecursosAtivos) && _vm is { } vm) AtualizarItensComRecurso(vm);
    }

    // ------------------------------------------------------------------ menu e atalhos fornecidos pelo módulo

    /// <summary>Itens que dependem de um recurso dos parâmetros: aparecem ou somem quando a empresa ou os parâmetros mudam.</summary>
    private readonly List<(MenuItem Item, string Recurso)> _itensComRecurso = [];

    /// <summary>O usuário escondeu ou mostrou itens do menu (aba Menus dos Parâmetros): refaz a barra de menus.</summary>
    private void AoMudarMenus(object? sender, EventArgs e)
    {
        if (_vm is { } vm && ReferenceEquals(sender, vm)) { MontarMenu(vm); MontarAtalhos(vm); }
    }

    private void MontarMenu(MainViewModelBase vm)
    {
        MenuPrincipal.Items.Clear();
        _itensComRecurso.Clear();
        foreach (var item in vm.MenusVisiveis) MenuPrincipal.Items.Add(CriarItem(vm, item));
        if (Suite is { } suite)
        {
            // o layout da Suite fica em Configurações (não há menu Exibir: os módulos são trocados pela navegação ou por Ctrl+1, Ctrl+2...)
            if (MenuPrincipal.Items.OfType<MenuItem>().FirstOrDefault(m => (m.Header as string) == "Con_figurações") is { } config)
            {
                config.Items.Insert(0, MenuLayout(suite));
                config.Items.Insert(1, new Separator());
            }
        }
    }

    private static MenuItem MenuLayout(SuiteViewModel suite)
    {
        var layout = new MenuItem { Header = "Layout da Suite" };
        foreach (var (valor, nome) in SuiteViewModel.Nomes)
            layout.Items.Add(new MenuItem
            {
                Header = (valor == suite.Layout ? "●  " : "     ") + nome, Command = suite.EscolherLayoutCommand, CommandParameter = valor,
            });
        layout.Items.Add(new Separator());
        layout.Items.Add(new MenuItem
        {
            Header = (suite.LateralFixa ? "●  " : "     ") + "Fixar a navegação à esquerda (B e C)", Command = suite.AlternarFixaCommand,
        });
        layout.Items.Add(new MenuItem
        {
            Header = (suite.TopoFixo ? "●  " : "     ") + "Fixar a navegação no topo (A, D e E)", Command = suite.AlternarTopoFixoCommand,
        });
        return layout;
    }

    private void AtualizarItensComRecurso(MainViewModelBase vm)
    {
        foreach (var (item, recurso) in _itensComRecurso) item.IsVisible = vm.RecursoAtivo(recurso);
    }

    private Control CriarItem(MainViewModelBase vm, ItemMenu item)
    {
        if (item.EhSeparador) return new Separator();
        var menu = new MenuItem { Header = item.Titulo, IsEnabled = item.Habilitado };
        if (item.Recurso is { } recurso)
        {
            _itensComRecurso.Add((menu, recurso));
            menu.IsVisible = vm.RecursoAtivo(recurso);
        }
        if (item.Atalho is { } atalho) menu.InputGesture = KeyGesture.Parse(atalho);   // só indicativo no Avalonia
        if (item.Filhos is { } filhos)
            foreach (var filho in filhos) menu.Items.Add(CriarItem(vm, filho));
        else if (item.Chave == TelasComuns.Sair)
            menu.Command = vm.SairCommand;
        else if (item.Chave is { } chave)
        {
            menu.Command = vm.AbrirCommand;
            menu.CommandParameter = chave;
        }
        return menu;
    }

    /// <summary>Atalhos globais (os InputGesture do menu são apenas indicativos no Avalonia).</summary>
    private void MontarAtalhos(MainViewModelBase vm)
    {
        KeyBindings.Clear();
        if (Suite is { } suite)
        {
            for (var i = 0; i < Math.Min(9, suite.Modulos.Count); i++)
                KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse($"Ctrl+{i + 1}"), Command = suite.TrocarCommand, CommandParameter = suite.Modulos[i] });
            KeyBindings.Add(new KeyBinding
            {
                Gesture = KeyGesture.Parse("Ctrl+K"),
                Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
                {
                    // E: busca da faixa de comando; nos demais, a da árvore (C)
                    if (suite.EhFaixa) { this.FindDescendantOfType<SuiteTopoView>()?.FocarBusca(); return; }
                    if (!suite.EhArvore) suite.Layout = LayoutSuite.Arvore;
                    suite.LateralAberta = true;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => this.FindDescendantOfType<SuiteLadoView>()?.FindControl<TextBox>("CampoBusca")?.Focus());
                }),
            });
        }
        foreach (var a in vm.Atalhos.Where(a => a.Chave is not null))
        {
            var chave = a.Chave!;
            KeyBindings.Add(new KeyBinding
            {
                Gesture = KeyGesture.Parse(a.Gesto),
                Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
                {
                    if (DataContext is MainViewModelBase atual) atual.AbrirPorAtalho(chave);
                }),
            });
        }
    }
}
