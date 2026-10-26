using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Data;
using Vorticis.Desktop.ViewModels;

namespace Vorticis.Desktop.Views;

/// <summary>As colunas dos relatórios gerenciais dependem do relatório gerado (meses, comparação...): montadas a cada geração.</summary>
internal static class GradeGerencial
{
    public static void Ligar(UserControl view, DataGrid grade)
    {
        GerencialBaseViewModel? vm = null;
        void AoMudar(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GerencialBaseViewModel.Colunas)) Montar(grade, vm);
        }
        view.DataContextChanged += (_, _) =>
        {
            if (vm is not null) vm.PropertyChanged -= AoMudar;
            vm = view.DataContext as GerencialBaseViewModel;
            if (vm is not null) vm.PropertyChanged += AoMudar;
            Montar(grade, vm);
        };
    }

    private static void Montar(DataGrid grade, GerencialBaseViewModel? vm)
    {
        grade.Columns.Clear();
        if (vm is null) return;
        for (var i = 0; i < vm.Colunas.Count; i++)
            grade.Columns.Add(new DataGridTextColumn
            {
                Header = new TextBlock { Text = vm.Colunas[i], TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                Binding = new Binding($"{nameof(LinhaGradeView.Valores)}[{i}]"),
                Width = i == 1 ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(i == 0 ? 120 : 125), MinWidth = i == 1 ? 220 : 80,
            });
    }
}
