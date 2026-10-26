using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Vorticis.Application.Relatorios;

namespace Vorticis.Folha.Desktop.Views;

/// <summary>
/// Grade que mostra uma <see cref="TabelaRelatorio"/> (colunas definidas pelo relatório): o que aparece na tela é exatamente o que
/// vai para PDF, Excel e CSV. Os valores são formatados em pt-BR; colunas numéricas ficam alinhadas à direita.
/// </summary>
public sealed class TabelaRelatorioGrid : DataGrid
{
    public static readonly StyledProperty<TabelaRelatorio?> TabelaProperty = AvaloniaProperty.Register<TabelaRelatorioGrid, TabelaRelatorio?>(nameof(Tabela));

    static TabelaRelatorioGrid() => TabelaProperty.Changed.AddClassHandler<TabelaRelatorioGrid>((g, _) => g.Reconstruir());

    public TabelaRelatorioGrid()
    {
        IsReadOnly = true;
        AutoGenerateColumns = false;
    }

    protected override Type StyleKeyOverride => typeof(DataGrid);

    public TabelaRelatorio? Tabela
    {
        get => GetValue(TabelaProperty);
        set => SetValue(TabelaProperty, value);
    }

    private void Reconstruir()
    {
        ItemsSource = null;
        Columns.Clear();
        if (Tabela is not { } t) return;
        for (var i = 0; i < t.Colunas.Count; i++)
        {
            var c = t.Colunas[i];
            var numerica = c.Tipo is TipoColuna.Valor or TipoColuna.Inteiro;
            var coluna = new DataGridTextColumn
            {
                Header = c.Titulo,
                Binding = new Binding($"[{i}]"),
                IsReadOnly = true,
                Width = c.Tipo == TipoColuna.Texto && c.LarguraCm == 0 ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(numerica ? 120 : Math.Max(80, c.LarguraCm * 38)),
            };
            if (numerica) coluna.CellStyleClasses.Add("numerica");
            Columns.Add(coluna);
        }
        ItemsSource = t.Linhas.Select(l => l.Valores.Select((v, i) => ExportacaoRelatorio.Texto(v, t.Colunas[i].Tipo)).ToArray()).ToList();
    }
}
