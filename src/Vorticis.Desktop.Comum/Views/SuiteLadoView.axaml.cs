using Avalonia.Controls;
using Avalonia.Threading;
using Vorticis.Desktop.Comum.ViewModels;

namespace Vorticis.Desktop.Comum.Views;

/// <summary>Navegação à esquerda da Suite: abre quando o mouse chega à faixa e recolhe pouco depois que ele sai.</summary>
public partial class SuiteLadoView : UserControl
{
    private readonly DispatcherTimer _recolher = new() { Interval = TimeSpan.FromMilliseconds(450) };

    public SuiteLadoView()
    {
        InitializeComponent();
        _recolher.Tick += (_, _) =>
        {
            _recolher.Stop();
            if (DataContext is SuiteViewModel s && !Navegacao.IsPointerOver && !MenuAberto()) s.LateralAberta = false;
        };
        Navegacao.PointerEntered += (_, _) =>
        {
            _recolher.Stop();
            if (DataContext is SuiteViewModel s) s.LateralAberta = true;
        };
        Navegacao.PointerExited += (_, _) => _recolher.Start();
    }

    /// <summary>Com o menu de layout aberto, a navegação não recolhe.</summary>
    private bool MenuAberto() => this.FindControl<Button>("BotaoLayoutLateral")?.Flyout?.IsOpen == true;
}
