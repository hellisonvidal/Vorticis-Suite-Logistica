using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Vorticis.Desktop.ViewModels;

namespace Vorticis.Desktop.Views;

public partial class ImportacaoPlanilhaView : UserControl
{
    private ImportacaoPlanilhaViewModel? _vm;

    public ImportacaoPlanilhaView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= AoMudarVm;
            _vm = DataContext as ImportacaoPlanilhaViewModel;
            if (_vm is not null) _vm.PropertyChanged += AoMudarVm;
            MontarColunas();
        };
    }

    private async void ProcurarPlanilha(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ImportacaoPlanilhaViewModel vm && await EscolhaArquivo.AbrirAsync(this, "Planilha com os lançamentos", EscolhaArquivo.Planilha) is { } c)
            vm.Arquivo = c;
    }

    /// <summary>Colunas da planilha mudam com o arquivo; o papel de cada uma (cabeçalho), com o layout.</summary>
    private void AoMudarVm(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is { } n && (n == nameof(ImportacaoPlanilhaViewModel.ColunasPlanilha) || n.StartsWith("Coluna")
                                        || n == nameof(ImportacaoPlanilhaViewModel.PorLancamento)))
            MontarColunas();
    }

    private void MontarColunas()
    {
        GradePlanilha.Columns.Clear();
        if (_vm is null) return;
        GradePlanilha.Columns.Add(new DataGridTextColumn
        {
            Header = "Linha", Binding = new Binding(nameof(LinhaPlanilha.Numero)), Width = new DataGridLength(70),
        });
        for (var i = 0; i < _vm.ColunasPlanilha.Count; i++)
        {
            var nome = _vm.ColunasPlanilha[i];
            var papel = _vm.PapelDaColuna(nome);
            GradePlanilha.Columns.Add(new DataGridTextColumn
            {
                Header = papel.Length > 0 ? $"{nome}  [{papel}]" : nome,
                Binding = new Binding($"{nameof(LinhaPlanilha.Celulas)}[{i}]"),
                MinWidth = 70,
            });
        }
    }
}
