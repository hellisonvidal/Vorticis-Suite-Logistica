using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Vorticis.Desktop.Views;

public partial class DreView : UserControl
{
    public DreView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ViewModels.DreViewModel vm)
                vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Rotulos)) ColunasComparativas.Aplicar(this, vm.Rotulos); };
        };
    }
}

/// <summary>Colunas dos anos anteriores nas demonstrações comparativas (títulos e visibilidade conforme a comparação gerada).</summary>
public static class ColunasComparativas
{
    public static void Aplicar(Control tela, IReadOnlyList<string> rotulos)
    {
        foreach (var grade in tela.GetVisualDescendants().OfType<DataGrid>().Where(g => g.Classes.Contains("comparativa")))
            foreach (var c in grade.Columns)
                switch (c.Tag as string)
                {
                    case "atual": c.Header = rotulos.Count > 0 ? rotulos[0] : "R$"; break;
                    case "var": c.IsVisible = rotulos.Count > 1; break;
                    case string t when t.StartsWith("ant") && int.Parse(t[3..]) is var i:
                        c.IsVisible = i + 1 < rotulos.Count;
                        c.Header = i + 1 < rotulos.Count ? rotulos[i + 1] : "";
                        break;
                }
    }
}
