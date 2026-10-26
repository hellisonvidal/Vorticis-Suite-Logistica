using Avalonia.Controls;
using Vorticis.Desktop.Comum.Ajuda;
using Vorticis.Desktop.Comum.ViewModels;

namespace Vorticis.Desktop.Comum.Views;

public partial class ManualView : UserControl
{
    private ManualViewModel? _vm;

    public ManualView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= AoMudar;
            _vm = DataContext as ManualViewModel;
            if (_vm is null) return;
            _vm.PropertyChanged += AoMudar;
            Montar();
        };
    }

    private void AoMudar(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ManualViewModel.Topico)) Montar();
    }

    private void Montar()
    {
        if (_vm?.Topico is not { } t) { Conteudo.Content = null; return; }
        Conteudo.Content = MarkdownRenderizador.Construir(t.Blocos, _vm.NavegarCommand.Execute);
        if (Conteudo.Parent is ScrollViewer sv) sv.Offset = default;
    }
}
