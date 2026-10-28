using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Desktop.Comum.Views;

namespace Vorticis.Frota.Desktop.Views;

/// <summary>Tela de lista que edita o documento numa janela pop-up: o ViewModel abre e fecha a edição por esta propriedade.</summary>
public interface IEdicaoEmPopup : INotifyPropertyChanged
{
    bool EdicaoAberta { get; set; }
    string TituloEdicao { get; }
}

/// <summary>
/// Liga uma tela de lista (CT-e, MDF-e) à janela pop-up de edição: quando o ViewModel abre a edição, mostra a janela modal com o formulário;
/// ao fechar (botão, X, Esc), avisa o ViewModel e devolve o foco à lista.
/// </summary>
public sealed class PopupEdicao
{
    private readonly UserControl _lista;
    private readonly Func<Control> _criarConteudo;
    private readonly double _largura, _altura;
    private IEdicaoEmPopup? _vm;
    private Window? _janela;

    public Window? Janela => _janela;

    public PopupEdicao(UserControl lista, Func<Control> criarConteudo, double largura = 1100, double altura = 760)
    {
        (_lista, _criarConteudo, _largura, _altura) = (lista, criarConteudo, largura, altura);
        lista.DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= AoMudar;
            _vm = lista.DataContext as IEdicaoEmPopup;
            if (_vm is not null) { _vm.PropertyChanged += AoMudar; if (_vm.EdicaoAberta) Abrir(); }
        };
        lista.AttachedToVisualTree += (_, _) => { if (_vm?.EdicaoAberta == true) Abrir(); };
    }

    private void AoMudar(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IEdicaoEmPopup.EdicaoAberta) || _vm is null) return;
        if (_vm.EdicaoAberta) Abrir(); else _janela?.Close();
    }

    private void Abrir()
    {
        if (_janela is not null || _vm is null || TopLevel.GetTopLevel(_lista) is not Window dono || !dono.IsVisible) return;
        var conteudo = _criarConteudo();
        conteudo.DataContext = _vm;
        var janela = new Window
        {
            Title = _vm.TituloEdicao,
            Width = _largura, Height = _altura, MinWidth = 760, MinHeight = 520,
            CanResize = true,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = conteudo,
            Icon = dono.Icon,
        };
        var principal = dono.Owner as MainWindow;
        janela.Closed += (_, _) =>
        {
            _janela = null;
            if (principal is not null && ReferenceEquals(principal.ModalAberta, janela)) principal.ModalAberta = null;
            if (_vm is not null) _vm.EdicaoAberta = false;
            Dispatcher.UIThread.Post(() => _lista.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
        };
        janela.KeyDown += (_, e) =>
        {
            if (principal?.DataContext is MainViewModelBase m) MainWindow.TratarDelete(e, janela, m, _vm);
            MainWindow.TratarEsc(e, () => { janela.Close(); return Task.CompletedTask; });
        };
        _janela = janela;
        if (principal is not null) principal.ModalAberta = janela;
        _ = janela.ShowDialog(dono);
    }
}
