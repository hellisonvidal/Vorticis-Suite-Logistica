using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Data;
using Vorticis.Application.Relatorios;
using Vorticis.Frota.Desktop.ViewModels;

namespace Vorticis.Frota.Desktop.Views;

/// <summary>Relatórios da frota: as colunas da grade vêm da tabela gerada (as mesmas do PDF, Excel e CSV).</summary>
public partial class RelatorioFrotaView : UserControl
{
    private RelatorioFrotaViewModel? _vm;

    public RelatorioFrotaView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= Mudou;
            _vm = DataContext as RelatorioFrotaViewModel;
            if (_vm is not null) _vm.PropertyChanged += Mudou;
            Montar();
        };
    }

    private void Mudou(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RelatorioFrotaViewModel.Colunas)) Montar();
    }

    private void Montar()
    {
        Grade.Columns.Clear();
        var colunas = _vm?.Colunas ?? [];
        for (var i = 0; i < colunas.Count; i++)
            Grade.Columns.Add(new DataGridTextColumn
            {
                Header = colunas[i].Titulo, Binding = new Binding($"V{i}"),
                Width = colunas[i].Tipo == TipoColuna.Texto && colunas[i].LarguraCm == 0 ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto,
            });
    }
}
