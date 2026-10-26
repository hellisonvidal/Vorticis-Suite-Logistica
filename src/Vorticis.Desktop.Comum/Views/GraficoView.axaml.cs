using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Data;
using Vorticis.Desktop.Comum.ViewModels;

namespace Vorticis.Desktop.Comum.Views;

public partial class GraficoView : UserControl
{
    private GraficoViewModel? _vm;

    public GraficoView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.Colunas.CollectionChanged -= MontarColunas;
            _vm = DataContext as GraficoViewModel;
            if (_vm is not null) _vm.Colunas.CollectionChanged += MontarColunas;
            MontarColunas(null, null);
        };
    }

    /// <summary>Uma coluna para a categoria e uma para cada série do gráfico.</summary>
    private void MontarColunas(object? sender, NotifyCollectionChangedEventArgs? e)
    {
        Grade.Columns.Clear();
        if (_vm is null) return;
        Grade.Columns.Add(new DataGridTextColumn { Header = "", Binding = new Binding(nameof(LinhaGrafico.Categoria)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        for (var i = 0; i < _vm.Colunas.Count; i++)
            Grade.Columns.Add(new DataGridTextColumn { Header = _vm.Colunas[i], Binding = new Binding($"{nameof(LinhaGrafico.Valores)}[{i}]"), Width = DataGridLength.Auto });
    }
}
