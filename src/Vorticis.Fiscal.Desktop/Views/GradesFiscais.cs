using Avalonia;
using Avalonia.Controls;

namespace Vorticis.Fiscal.Desktop.Views;

/// <summary>
/// Ajuste das grades do Fiscal: o nome de cada coluna aparece por inteiro (largura mínima calculada pelo cabeçalho; o cabeçalho também
/// mostra dica com o nome completo) e as colunas numéricas ficam alinhadas à direita. Ligado com <c>g:GradesFiscais.Ajustar="True"</c>.
/// </summary>
public static class GradesFiscais
{
    public static readonly AttachedProperty<bool> AjustarProperty = AvaloniaProperty.RegisterAttached<DataGrid, bool>("Ajustar", typeof(GradesFiscais));

    public static bool GetAjustar(DataGrid g) => g.GetValue(AjustarProperty);
    public static void SetAjustar(DataGrid g, bool v) => g.SetValue(AjustarProperty, v);

    /// <summary>Cabeçalhos de colunas de valores/quantidades (alinhadas à direita), quando a grade é montada em XAML.</summary>
    private static readonly HashSet<string> Numericos = new(StringComparer.CurrentCultureIgnoreCase)
    {
        "Valor", "Total", "Qtde", "Unitário", "ICMS", "ICMS-ST", "IPI", "PIS", "COFINS", "ISS", "Receita", "Receita bruta", "Débito", "Crédito",
        "Base", "Alíquota", "Alíq.", "Saldo", "Desconto", "Frete", "Vl. contábil", "Valor contábil", "Prior.",
    };

    static GradesFiscais()
    {
        AjustarProperty.Changed.AddClassHandler<DataGrid>((g, e) =>
        {
            if (e.NewValue is true) g.Loaded += (_, _) => Ajustar(g);
        });
    }

    /// <summary>Largura mínima (px) para o cabeçalho caber por inteiro: ~0,62 em por caractere em negrito + margens e ícone de ordenação.</summary>
    public static double LarguraMinima(string? cabecalho, double tamanhoFonte = 12.5)
        => string.IsNullOrEmpty(cabecalho) ? 0 : Math.Ceiling(cabecalho.Length * Math.Max(tamanhoFonte, 12.5) * 0.62 + 34);   // 12,5 = fonte padrão das grades (a grade ainda pode não ter o estilo aplicado)

    /// <summary>Texto do cabeçalho da coluna (string ou TextBlock criado pelo ajuste).</summary>
    public static string? TextoCabecalho(DataGridColumn c) => c.Header switch { string t => t, TextBlock b => b.Text, _ => null };

    public static bool EhNumerico(string? cabecalho) => cabecalho is not null && Numericos.Contains(cabecalho.Trim());

    /// <summary>Aplica largura mínima e alinhamento a uma coluna.</summary>
    public static void AjustarColuna(DataGridColumn c, bool numerico, double tamanhoFonte = 12.5)
    {
        var minimo = LarguraMinima(TextoCabecalho(c), tamanhoFonte);
        if (minimo > c.MinWidth) c.MinWidth = minimo;
        if (c.Width.IsAbsolute && c.Width.Value < minimo) c.Width = new DataGridLength(minimo);
        if (numerico)
        {
            if (!c.CellStyleClasses.Contains("num")) c.CellStyleClasses.Add("num");
            if (c.Header is string h)   // cabeçalho também à direita, com dica do nome completo
                c.Header = new TextBlock { Text = h, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, TextAlignment = Avalonia.Media.TextAlignment.Right };
        }
    }

    public static void Ajustar(DataGrid g)
    {
        foreach (var c in g.Columns) AjustarColuna(c, EhNumerico(TextoCabecalho(c)), g.FontSize);
        g.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
    }
}
