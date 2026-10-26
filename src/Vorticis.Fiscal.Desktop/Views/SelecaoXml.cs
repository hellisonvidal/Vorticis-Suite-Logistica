using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System.Text;
using Vorticis.Fiscal.Desktop.ViewModels;

namespace Vorticis.Fiscal.Desktop.Views;

/// <summary>Seletores de arquivos usados pelas telas que importam XML e pelo resumo exportado.</summary>
internal static class SelecaoXml
{
    private static readonly FilePickerFileType Xml = new("XML fiscal (.xml, .zip)") { Patterns = ["*.xml", "*.XML", "*.zip", "*.ZIP"] };

    /// <summary>Um ou vários .xml/.zip; lista vazia se o usuário cancelar.</summary>
    public static async Task<List<string>> ArquivosAsync(TopLevel top)
    {
        var arquivos = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "XML de notas fiscais (um ou vários arquivos, ou .zip)", AllowMultiple = true, FileTypeFilter = [Xml, FilePickerFileTypes.All],
        });
        return arquivos.Select(a => a.TryGetLocalPath()).OfType<string>().ToList();
    }

    public static async Task<string?> PastaAsync(TopLevel top)
    {
        var pastas = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Pasta com os XML (as subpastas também são lidas)", AllowMultiple = false });
        return pastas.Count > 0 ? pastas[0].TryGetLocalPath() : null;
    }

    /// <summary>Salva o resumo da última importação em CSV (abre no Excel); devolve o caminho gravado ou null.</summary>
    public static async Task<string?> ExportarResumoAsync(TopLevel top, ImportacaoXmlViewModel vm)
    {
        if (vm.ResumoCsv() is not { } csv) return null;
        var destino = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar resumo da importação", SuggestedFileName = $"resumo_importacao_xml_{DateTime.Now:yyyyMMdd_HHmm}.csv",
            DefaultExtension = "csv", FileTypeChoices = [new FilePickerFileType("CSV (separado por ponto e vírgula)") { Patterns = ["*.csv"] }],
        });
        if (destino?.TryGetLocalPath() is not { } caminho) return null;
        await File.WriteAllTextAsync(caminho, csv, new UTF8Encoding(true));
        return caminho;
    }
}
