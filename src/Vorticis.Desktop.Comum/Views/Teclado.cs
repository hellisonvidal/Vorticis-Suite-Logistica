using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Vorticis.Desktop.Comum.Views;

/// <summary>
/// Navegação por Enter nas telas de digitação: <c>c:Teclado.EnterAvanca="True"</c> num contêiner (janela, tela ou painel)
/// faz Enter levar ao próximo campo e Shift+Enter ao anterior, na ordem de tabulação. Só age em caixas de texto de uma
/// linha cujo Enter ninguém tratou antes (listas de sugestões abertas, botões e textos de várias linhas ficam como estão);
/// no último campo, o foco segue para o botão seguinte (ex.: Salvar).
/// </summary>
public static class Teclado
{
    public static readonly AttachedProperty<bool> EnterAvancaProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("EnterAvanca", typeof(Teclado));

    public static bool GetEnterAvanca(Control c) => c.GetValue(EnterAvancaProperty);
    public static void SetEnterAvanca(Control c, bool v) => c.SetValue(EnterAvancaProperty, v);

    static Teclado()
    {
        EnterAvancaProperty.Changed.AddClassHandler<Control>((c, e) =>
        {
            c.RemoveHandler(InputElement.KeyDownEvent, AoTeclar);
            if (e.NewValue is true) c.AddHandler(InputElement.KeyDownEvent, AoTeclar);   // bolha: quem trata o Enter antes prevalece
        });
    }

    private static void AoTeclar(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Enter || e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift)) return;
        if (e.Source is not TextBox { AcceptsReturn: false } tb || tb.GetVisualAncestors().OfType<AutoCompleteBox>().Any(a => a.IsDropDownOpen)) return;
        var direcao = e.KeyModifiers == KeyModifiers.Shift ? NavigationDirection.Previous : NavigationDirection.Next;
        if (TopLevel.GetTopLevel(tb) is IInputRoot raiz) { raiz.KeyboardNavigationHandler?.Move(tb, direcao); e.Handled = true; }
    }
}
