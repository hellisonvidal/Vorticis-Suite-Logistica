using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Vorticis.Desktop.Comum.Views;

/// <summary>
/// Ícone desenhado de um caminho vetorial em grade 24×24 (<see cref="Modulos.IconesModulos"/>): ocupa o tamanho do controle mantendo a
/// proporção e o respiro da grade, na cor de <see cref="Preenchimento"/>. <see cref="Dados"/> aceita o texto do caminho; <see cref="Geometria"/>, a geometria pronta.
/// </summary>
public sealed class IconeVetorial : Control
{
    public static readonly StyledProperty<Geometry?> GeometriaProperty = AvaloniaProperty.Register<IconeVetorial, Geometry?>(nameof(Geometria));
    public static readonly StyledProperty<string?> DadosProperty = AvaloniaProperty.Register<IconeVetorial, string?>(nameof(Dados));
    public static readonly StyledProperty<IBrush?> PreenchimentoProperty = AvaloniaProperty.Register<IconeVetorial, IBrush?>(nameof(Preenchimento), Brushes.White);

    static IconeVetorial()
    {
        AffectsRender<IconeVetorial>(GeometriaProperty, DadosProperty, PreenchimentoProperty);
        DadosProperty.Changed.AddClassHandler<IconeVetorial>((i, e) => i._doTexto = e.NewValue is string s && s.Length > 0 ? Geometry.Parse(s) : null);
    }

    private Geometry? _doTexto;

    public Geometry? Geometria { get => GetValue(GeometriaProperty); set => SetValue(GeometriaProperty, value); }
    public string? Dados { get => GetValue(DadosProperty); set => SetValue(DadosProperty, value); }
    public IBrush? Preenchimento { get => GetValue(PreenchimentoProperty); set => SetValue(PreenchimentoProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 24 : 0, double.IsInfinity(availableSize.Height) ? 24 : 0);

    public override void Render(DrawingContext context)
    {
        if ((Geometria ?? _doTexto) is not { } g || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var escala = Math.Min(Bounds.Width, Bounds.Height) / 24.0;
        var dx = (Bounds.Width - 24 * escala) / 2;
        var dy = (Bounds.Height - 24 * escala) / 2;
        using (context.PushTransform(Matrix.CreateScale(escala, escala) * Matrix.CreateTranslation(dx, dy)))
            context.DrawGeometry(Preenchimento, null, g);
    }
}
