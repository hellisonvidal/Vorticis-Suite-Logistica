using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Vorticis.Desktop.Comum.Views;

/// <summary>Botão "Layout" da Suite com os cinco cartões de layout; <see cref="Escuro"/> para fundos escuros, <see cref="SoIcone"/> sem o texto.</summary>
public partial class SuiteEscolhaLayoutView : UserControl
{
    public static readonly StyledProperty<bool> EscuroProperty = AvaloniaProperty.Register<SuiteEscolhaLayoutView, bool>(nameof(Escuro));
    public static readonly StyledProperty<bool> SoIconeProperty = AvaloniaProperty.Register<SuiteEscolhaLayoutView, bool>(nameof(SoIcone));

    public bool Escuro { get => GetValue(EscuroProperty); set => SetValue(EscuroProperty, value); }
    public bool SoIcone { get => GetValue(SoIconeProperty); set => SetValue(SoIconeProperty, value); }

    public SuiteEscolhaLayoutView() => InitializeComponent();

    /// <summary>Escolhido um layout, o painel fecha.</summary>
    private void Escolhido(object? sender, RoutedEventArgs e) => BotaoLayout.Flyout?.Hide();

    /// <summary>O painel de layouts está aberto (a navegação não recolhe enquanto isso).</summary>
    public bool Aberto => BotaoLayout.Flyout?.IsOpen == true;
}
