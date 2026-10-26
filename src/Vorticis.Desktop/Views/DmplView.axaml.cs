using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Data;
using Vorticis.Desktop.ViewModels;

namespace Vorticis.Desktop.Views;

/// <summary>As colunas da DMPL dependem das contas do PL da empresa: montadas a cada geração.</summary>
public partial class DmplView : UserControl
{
    private DmplViewModel? _vm;

    public DmplView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= AoMudar;
            _vm = DataContext as DmplViewModel;
            if (_vm is not null) _vm.PropertyChanged += AoMudar;
            MontarColunas();
        };
    }

    private void AoMudar(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DmplViewModel.ColunasDmpl)) MontarColunas();
    }

    private void MontarColunas()
    {
        GradeDmpl.Columns.Clear();
        if (_vm is null) return;
        GradeDmpl.Columns.Add(new DataGridTextColumn
        {
            Header = "Descrição", Binding = new Binding(nameof(LinhaMutacaoView.Descricao)), Width = new DataGridLength(320),
        });
        for (var i = 0; i < _vm.ColunasDmpl.Count; i++)
            GradeDmpl.Columns.Add(new DataGridTextColumn
            {
                Header = new TextBlock { Text = _vm.ColunasDmpl[i], TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                Binding = new Binding($"{nameof(LinhaMutacaoView.Valores)}[{i}]"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 120,
            });
    }
}
