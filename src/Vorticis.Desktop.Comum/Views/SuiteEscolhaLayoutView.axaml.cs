using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Vorticis.Desktop.Comum.ViewModels;

namespace Vorticis.Desktop.Comum.Views;

/// <summary>Botão "Layout" da Suite com os cinco cartões de layout; <see cref="Escuro"/> para fundos escuros, <see cref="SoIcone"/> sem o texto.</summary>
public partial class SuiteEscolhaLayoutView : UserControl
{
    public static readonly StyledProperty<bool> EscuroProperty = AvaloniaProperty.Register<SuiteEscolhaLayoutView, bool>(nameof(Escuro));
    public static readonly StyledProperty<bool> SoIconeProperty = AvaloniaProperty.Register<SuiteEscolhaLayoutView, bool>(nameof(SoIcone));

    public bool Escuro { get => GetValue(EscuroProperty); set => SetValue(EscuroProperty, value); }
    public bool SoIcone { get => GetValue(SoIconeProperty); set => SetValue(SoIconeProperty, value); }

    public SuiteEscolhaLayoutView()
    {
        InitializeComponent();
        if (BotaoLayout.Flyout is Flyout f) f.Opening += (_, _) => f.Placement = Posicao();
    }

    /// <summary>
    /// Onde o painel abre: à direita na barra lateral (botão só com ícone); para cima quando o botão está na metade de baixo da janela
    /// (árvore à esquerda); senão, para baixo, alinhado pela direita (botões no alto).
    /// </summary>
    private PlacementMode Posicao()
    {
        if (SoIcone) return PlacementMode.RightEdgeAlignedBottom;
        var top = TopLevel.GetTopLevel(this);
        var y = top is null ? null : BotaoLayout.TranslatePoint(default, top)?.Y;
        return y is { } v && top is not null && v > top.Bounds.Height / 2 ? PlacementMode.TopEdgeAlignedLeft : PlacementMode.BottomEdgeAlignedRight;
    }

    /// <summary>Cartão clicado: troca o layout da Suite e fecha o painel.</summary>
    private void Escolhido(object? sender, RoutedEventArgs e)
    {
        // lê o cartão antes de fechar: fechado, o conteúdo do pop-up perde o DataContext
        var opcao = (sender as Control)?.DataContext as OpcaoLayout;
        BotaoLayout.Flyout?.Hide();
        if (opcao is not null && DataContext is SuiteViewModel suite) suite.EscolherLayoutCommand.Execute(opcao.Layout);
    }

    /// <summary>O painel de layouts está aberto (a navegação não recolhe enquanto isso).</summary>
    public bool Aberto => BotaoLayout.Flyout?.IsOpen == true;
}
