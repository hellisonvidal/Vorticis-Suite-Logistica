using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Vorticis.Desktop.Comum.ViewModels;

namespace Vorticis.Desktop.Comum.Views;

public partial class CadastroEmpresaView : UserControl
{
    private static readonly FilePickerFileType Imagem = new("Imagem (PNG ou JPG)") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.PNG", "*.JPG", "*.JPEG"] };

    public CadastroEmpresaView() => InitializeComponent();

    private async void EnviarLogo_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CadastroEmpresaViewModel vm && await EscolhaArquivo.AbrirAsync(this, "Logo da empresa", Imagem) is { } caminho)
            await vm.EnviarLogoAsync(caminho);
    }
}
