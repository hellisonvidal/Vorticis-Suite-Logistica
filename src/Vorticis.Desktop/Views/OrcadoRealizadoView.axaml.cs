using Avalonia.Controls;

namespace Vorticis.Desktop.Views;

public partial class OrcadoRealizadoView : UserControl
{
    public OrcadoRealizadoView()
    {
        InitializeComponent();
        GradeGerencial.Ligar(this, Grade);
    }
}
