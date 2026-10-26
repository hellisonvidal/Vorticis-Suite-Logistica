using Avalonia.Controls;

namespace Vorticis.Desktop.Views;

public partial class RelacaoCustosView : UserControl
{
    public RelacaoCustosView()
    {
        InitializeComponent();
        GradeGerencial.Ligar(this, Grade);
    }
}
