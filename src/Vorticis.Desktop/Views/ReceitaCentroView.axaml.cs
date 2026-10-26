using Avalonia.Controls;

namespace Vorticis.Desktop.Views;

public partial class ReceitaCentroView : UserControl
{
    public ReceitaCentroView()
    {
        InitializeComponent();
        GradeGerencial.Ligar(this, Grade);
    }
}
