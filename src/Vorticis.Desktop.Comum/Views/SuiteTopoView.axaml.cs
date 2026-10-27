using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vorticis.Desktop.Comum.ViewModels;

namespace Vorticis.Desktop.Comum.Views;

/// <summary>Navegação no topo da Suite: abre quando o mouse chega à faixa do alto e recolhe pouco depois que ele sai (se não estiver fixa).</summary>
public partial class SuiteTopoView : UserControl
{
    private readonly DispatcherTimer _recolher = new() { Interval = TimeSpan.FromMilliseconds(550) };

    public SuiteTopoView()
    {
        InitializeComponent();
        _recolher.Tick += (_, _) =>
        {
            _recolher.Stop();
            if (DataContext is SuiteViewModel s && !NavegacaoTopo.IsPointerOver && !Ocupado()) s.TopoAberto = false;
        };
        NavegacaoTopo.PointerEntered += (_, _) =>
        {
            _recolher.Stop();
            if (DataContext is SuiteViewModel s) s.TopoAberto = true;
        };
        NavegacaoTopo.PointerExited += (_, _) => _recolher.Start();
    }

    /// <summary>Com o seletor de layout, a lista de módulos ou a busca em uso, a navegação não recolhe.</summary>
    private bool Ocupado() =>
        this.GetVisualDescendants().OfType<SuiteEscolhaLayoutView>().Any(v => v.Aberto)
        || SeletorModulo.IsDropDownOpen || BuscaFaixa.IsFocused || (DataContext as SuiteViewModel)?.Buscando == true;

    /// <summary>Ctrl+K no layout E: abre a faixa e põe o cursor na busca.</summary>
    public void FocarBusca()
    {
        if (DataContext is SuiteViewModel s) s.TopoAberto = true;
        Dispatcher.UIThread.Post(() => BuscaFaixa.Focus(), DispatcherPriority.Loaded);
    }

    /// <summary>Resultado da busca (no pop-up, fora da árvore do controle): abre a função e limpa a busca.</summary>
    private void ResultadoEscolhido(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SuiteViewModel s || (sender as Control)?.DataContext is not NoSuite no) return;
        s.Busca = "";                                     // fecha o pop-up dos resultados
        s.AbrirFuncaoCommand.Execute(no);
        _recolher.Start();
    }
}
