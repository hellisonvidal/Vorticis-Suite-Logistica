using System.Globalization;
using MiniExcelLibs;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;

namespace Vorticis.Infra.Importacao;

/// <summary>Lê a primeira aba de uma planilha Excel (.xlsx) ou um arquivo de texto (.csv/.txt) como <see cref="TabelaImportada"/>.</summary>
public static class LeitorPlanilha
{
    public static IReadOnlyList<string> Extensoes { get; } = ["xlsx", "xlsm", "csv", "txt"];

    public static Resultado<TabelaImportada> Ler(string caminho)
    {
        try
        {
            return Ler(File.ReadAllBytes(caminho), Path.GetExtension(caminho));
        }
        catch (IOException ex)
        {
            return Resultado<TabelaImportada>.Falha($"Não foi possível abrir o arquivo: {ex.Message}");
        }
    }

    public static Resultado<TabelaImportada> Ler(byte[] conteudo, string extensao)
    {
        var ext = extensao.TrimStart('.').ToLowerInvariant();
        try
        {
            var tabela = ext switch
            {
                "xlsx" or "xlsm" => Excel(conteudo),
                "csv" or "txt" => TabelaImportada.DeTexto(LeitorPlanoReferencial.Decodificar(conteudo)),
                "xls" => null,
                _ => null,
            };
            if (tabela is null)
                return Resultado<TabelaImportada>.Falha(ext == "xls"
                    ? "Planilhas no formato antigo (.xls) não são lidas: abra no Excel e salve como .xlsx ou .csv."
                    : "Formato não suportado: use .xlsx, .csv ou .txt.");
            return tabela.Colunas.Count == 0
                ? Resultado<TabelaImportada>.Falha("A planilha está vazia.")
                : Resultado<TabelaImportada>.Ok(tabela);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Resultado<TabelaImportada>.Falha($"Não foi possível ler a planilha: {ex.Message}");
        }
    }

    private static TabelaImportada Excel(byte[] xlsx)
    {
        using var ms = new MemoryStream(xlsx);
        var linhas = new List<IReadOnlyList<string>>();
        foreach (IDictionary<string, object?> linha in ms.Query(useHeaderRow: false, excelType: ExcelType.XLSX))
            linhas.Add(linha.Values.Select(Texto).ToList());
        return TabelaImportada.DeLinhas(linhas);
    }

    /// <summary>Datas em dd/MM/yyyy; números no formato invariante (1500.5), que os leitores de valor aceitam.</summary>
    private static string Texto(object? v) => v switch
    {
        null => "",
        DateTime d => d.TimeOfDay == TimeSpan.Zero ? d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : d.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
        double n => n.ToString("0.##########", CultureInfo.InvariantCulture),
        decimal n => n.ToString("0.##########", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };
}
