using Avalonia.Controls;

namespace Vorticis.Desktop.Views;

public partial class CentroCustoRelatorioView : UserControl
{
    public CentroCustoRelatorioView()
    {
        InitializeComponent();
        GradeGerencial.Ligar(this, Grade);
    }
}
