using Avalonia.Controls;

namespace Vorticis.Desktop.Views;

public partial class BalancoView : UserControl
{
    public BalancoView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ViewModels.BalancoViewModel vm)
                vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Rotulos)) ColunasComparativas.Aplicar(this, vm.Rotulos); };
        };
    }
}
