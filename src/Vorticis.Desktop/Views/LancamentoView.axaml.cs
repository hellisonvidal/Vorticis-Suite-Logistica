using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Desktop.Comum.Views;
using Vorticis.Desktop.ViewModels;

namespace Vorticis.Desktop.Views;

/// <summary>
/// Tela de lançamentos: grade dos lançamentos efetuados em tela cheia. A digitação (novo lançamento, ou duplo clique/Enter
/// num existente) abre numa janela pop-up modal com <see cref="LancamentoDigitacaoView"/>; ao salvar, o pop-up fecha, a
/// grade se atualiza e o foco volta para ela.
/// </summary>
public partial class LancamentoView : UserControl
{
    private LancamentoViewModel? _vm;
    private Window? _popup;

    public LancamentoView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= AoMudarVm;
            _vm = DataContext as LancamentoViewModel;
            if (_vm is not null) { _vm.PropertyChanged += AoMudarVm; if (_vm.DigitacaoAberta) AbrirPopup(); }
        };
    }

    private void AoMudarVm(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LancamentoViewModel.DigitacaoAberta) || _vm is null) return;
        if (_vm.DigitacaoAberta) AbrirPopup(); else FecharPopup();
    }

    private void AbrirPopup()
    {
        if (_popup is not null || _vm is null || TopLevel.GetTopLevel(this) is not Window dono) return;
        var popup = new Window
        {
            Title = _vm.EmEdicao ? "Lançamento contábil — alteração" : "Lançamento contábil",
            Width = 1100, Height = 660, MinWidth = 900, MinHeight = 560,
            CanResize = true,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new LancamentoDigitacaoView { DataContext = _vm },
            Icon = dono.Icon,
        };
        var principal = dono.Owner as MainWindow;
        popup.Closed += (_, _) =>
        {
            _popup = null;
            if (principal is not null && ReferenceEquals(principal.ModalAberta, popup)) principal.ModalAberta = null;
            if (_vm?.DigitacaoAberta == true) _vm.CancelarDigitacao();   // fechou pelo X/Alt+F4: descarta
            Dispatcher.UIThread.Post(() => ListaEfetuados.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
        };
        // Esc fecha o pop-up; com digitação não salva, pergunta antes (o fechamento descarta a digitação).
        popup.KeyDown += (_, e) => MainWindow.TratarEsc(e, async () =>
        {
            if (_vm is { TemDados: true } && principal?.DataContext is MainViewModelBase m
                && !await m.ConfirmarAsync("Fechar o lançamento", "O lançamento digitado ainda não foi salvo. Deseja descartá-lo e fechar?",
                                           "Descartar e fechar", "Continuar editando"))
                return;
            popup.Close();
        });
        _popup = popup;
        if (principal is not null) principal.ModalAberta = popup;
        _ = popup.ShowDialog(dono);
    }

    private void FecharPopup() => _popup?.Close();

    private async void AoDuploCliqueEfetuado(object? sender, TappedEventArgs e)
    {
        if (_vm is not null && (e.Source as Control)?.DataContext is LancamentoEfetuadoLinha efetuado) await _vm.EditarAsync(efetuado);
    }

    private async void AoTeclarLista(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _vm?.EfetuadoSelecionado is not { } efetuado) return;
        e.Handled = true;
        await _vm.EditarAsync(efetuado);
    }
}
