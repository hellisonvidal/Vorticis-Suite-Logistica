using Avalonia.Controls;

namespace Vorticis.Desktop.Views;

public partial class RelatorioDepreciacaoView : UserControl
{
    public RelatorioDepreciacaoView()
    {
        InitializeComponent();
        GradeGerencial.Ligar(this, Grade);
    }
}
