using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;

namespace Vorticis.Desktop.Comum.Views;

/// <summary>
/// Período de apuração (competência): mês e ano em duas listas, sem dia. O ano aparece sempre com 4 dígitos.
/// <c>Competencia</c> é o primeiro dia do mês escolhido (ligação de duas vias).
/// </summary>
public sealed class SeletorCompetencia : UserControl
{
    public static readonly StyledProperty<DateTime?> CompetenciaProperty =
        AvaloniaProperty.Register<SeletorCompetencia, DateTime?>(nameof(Competencia), defaultBindingMode: BindingMode.TwoWay);

    public static IReadOnlyList<string> Meses { get; } =
        ["Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho", "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro"];

    /// <summary>Anos oferecidos: de 1990 até dois anos à frente.</summary>
    public static IReadOnlyList<int> Anos { get; } = [.. Enumerable.Range(1990, DateTime.Today.Year + 3 - 1990).Reverse()];

    private readonly ComboBox _mes = new() { ItemsSource = Meses, Width = 125, Name = "MesCompetencia" };
    private readonly ComboBox _ano = new() { ItemsSource = Anos, Width = 90, Name = "AnoCompetencia" };
    private bool _atualizando;

    public SeletorCompetencia()
    {
        Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _mes, _ano } };
        ToolTip.SetTip(this, "Competência (período de apuração): mês e ano");
        _mes.SelectionChanged += (_, _) => Escolher();
        _ano.SelectionChanged += (_, _) => Escolher();
        Mostrar(Competencia);
    }

    public DateTime? Competencia
    {
        get => GetValue(CompetenciaProperty);
        set => SetValue(CompetenciaProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CompetenciaProperty) Mostrar(Competencia);
    }

    private void Mostrar(DateTime? d)
    {
        _atualizando = true;
        try
        {
            var data = d ?? DateTime.Today;
            _mes.SelectedIndex = data.Month - 1;
            _ano.SelectedItem = Anos.Contains(data.Year) ? data.Year : null;
        }
        finally { _atualizando = false; }
    }

    private void Escolher()
    {
        if (_atualizando || _mes.SelectedIndex < 0 || _ano.SelectedItem is not int ano) return;
        var nova = new DateTime(ano, _mes.SelectedIndex + 1, 1);
        if (Competencia != nova) Competencia = nova;
    }
}
