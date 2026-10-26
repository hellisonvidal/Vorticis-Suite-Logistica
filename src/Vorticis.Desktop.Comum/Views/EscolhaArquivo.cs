using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Vorticis.Desktop.Comum.Views;

/// <summary>Diálogo de abrir arquivo usado pelas telas de importação.</summary>
public static class EscolhaArquivo
{
    public static readonly FilePickerFileType Planilha = new("Planilha (Excel ou CSV)") { Patterns = ["*.xlsx", "*.xlsm", "*.csv", "*.txt"] };
    public static readonly FilePickerFileType Ofx = new("Extrato OFX") { Patterns = ["*.ofx", "*.OFX"] };
    public static readonly FilePickerFileType Extrato = new("Extrato bancário (OFX, PDF, TXT ou CSV)") { Patterns = ["*.ofx", "*.OFX", "*.pdf", "*.PDF", "*.txt", "*.csv"] };

    public static async Task<string?> AbrirAsync(Control origem, string titulo, FilePickerFileType tipo)
    {
        if (TopLevel.GetTopLevel(origem) is not { } top) return null;
        var arquivos = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = titulo, AllowMultiple = false, FileTypeFilter = [tipo, FilePickerFileTypes.All],
        });
        return arquivos.Count > 0 ? arquivos[0].TryGetLocalPath() : null;
    }
}
