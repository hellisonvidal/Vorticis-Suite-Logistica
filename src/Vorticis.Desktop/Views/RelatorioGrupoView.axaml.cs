using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Data;
using Vorticis.Desktop.ViewModels;

namespace Vorticis.Desktop.Views;

public partial class RelatorioGrupoView : UserControl
{
    private RelatorioGrupoViewModel? _vm;

    public RelatorioGrupoView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.Colunas.CollectionChanged -= Montar;
            _vm = DataContext as RelatorioGrupoViewModel;
            if (_vm is not null) _vm.Colunas.CollectionChanged += Montar;
            Montar(null, null);
        };
    }

    /// <summary>As colunas mudam com o relatório e a forma (uma por empresa no lado a lado).</summary>
    private void Montar(object? sender, NotifyCollectionChangedEventArgs? e)
    {
        Grade.Columns.Clear();
        if (_vm is null) return;
        for (var i = 0; i < _vm.Colunas.Count; i++)
            Grade.Columns.Add(new DataGridTextColumn
            {
                Header = _vm.Colunas[i], Binding = new Binding($"{nameof(LinhaRelatorioGrupo.Valores)}[{i}]"),
                Width = i == 1 ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto,
            });
    }
}
