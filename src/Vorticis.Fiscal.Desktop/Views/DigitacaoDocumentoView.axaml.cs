using System.ComponentModel;
using Vorticis.Desktop.Comum;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Desktop.Comum.Views;
using Vorticis.Fiscal.Desktop.ViewModels;

namespace Vorticis.Fiscal.Desktop.Views;

/// <summary>
/// Notas fiscais de entrada / saída: consulta das notas lançadas e importadas em tela cheia. "Novo lançamento" e "Abrir" (duplo clique ou Enter)
/// abrem a nota numa janela pop-up modal com <see cref="DigitacaoNotaView"/>; ao gravar ou excluir, o pop-up fecha e a lista se atualiza.
/// </summary>
public partial class DigitacaoDocumentoView : UserControl
{
    private DigitacaoDocumentoViewModel? _vm;
    private Window? _popup;

    public DigitacaoDocumentoView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= AoMudarVm;
            _vm = DataContext as DigitacaoDocumentoViewModel;
            if (_vm is not null) { _vm.PropertyChanged += AoMudarVm; if (_vm.DigitacaoAberta) AbrirPopup(); }
        };
    }

    private void AoMudarVm(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DigitacaoDocumentoViewModel.DigitacaoAberta) || _vm is null) return;
        if (_vm.DigitacaoAberta) AbrirPopup(); else _popup?.Close();
    }

    private void AbrirPopup()
    {
        if (_popup is not null || _vm is null || TopLevel.GetTopLevel(this) is not Window dono) return;
        var popup = new Window
        {
            Title = _vm.Titulo,
            Width = 1180, Height = 700, MinWidth = 900, MinHeight = 560,
            CanResize = true,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DigitacaoNotaView { DataContext = _vm },
            Icon = dono.Icon,
        };
        var principal = dono.Owner as MainWindow;
        popup.Closed += (_, _) =>
        {
            _popup = null;
            if (principal is not null && ReferenceEquals(principal.ModalAberta, popup)) principal.ModalAberta = null;
            if (_vm is not null) _vm.DigitacaoAberta = false;   // fechou pelo X/Alt+F4
            Dispatcher.UIThread.Post(() => ListaNotas.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
        };
        popup.KeyDown += (_, e) =>
        {
            if (principal?.DataContext is MainViewModelBase m) MainWindow.TratarDelete(e, popup, m, _vm);   // Delete: excluir a nota (com confirmação)
            MainWindow.TratarEsc(e, () => { popup.Close(); return Task.CompletedTask; });
        };
        _popup = popup;
        if (principal is not null) principal.ModalAberta = popup;
        _ = popup.ShowDialog(dono);
    }

    private void AoDuploClique(object? sender, TappedEventArgs e)
    {
        if (_vm is not null && (e.Source as Control)?.DataContext is DocumentoLinha && _vm.AbrirCommand.CanExecute(null)) _vm.AbrirCommand.Execute(null);
    }

    private void AoTeclarLista(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _vm is null || !_vm.AbrirCommand.CanExecute(null)) return;
        e.Handled = true;
        _vm.AbrirCommand.Execute(null);
    }

    private async void ImportarArquivos(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DigitacaoDocumentoViewModel vm || TopLevel.GetTopLevel(this) is not { } top) return;
        try
        {
            var caminhos = await SelecaoXml.ArquivosAsync(top);
            if (caminhos.Count > 0) await vm.Importador.ImportarArquivosAsync(caminhos);
        }
        catch (Exception ex) { vm.Importador.Erros.Add(ErrosInesperados.Registrar(ex)); }   // nunca fecha o módulo
    }

    private async void ImportarPasta(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DigitacaoDocumentoViewModel vm || TopLevel.GetTopLevel(this) is not { } top) return;
        try { if (await SelecaoXml.PastaAsync(top) is { } pasta) await vm.Importador.ImportarPastaAsync(pasta, true); }
        catch (Exception ex) { vm.Importador.Erros.Add(ErrosInesperados.Registrar(ex)); }
    }
}
