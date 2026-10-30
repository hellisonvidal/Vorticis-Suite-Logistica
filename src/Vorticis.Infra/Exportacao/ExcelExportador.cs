using ClosedXML.Excel;
using System.ComponentModel;
using System.Reflection;

namespace Vorticis.Infra.Exportacao;

[AttributeUsage(AttributeTargets.Property)]
public sealed class MoedaAttribute : Attribute
{
    public int CasasDecimais { get; }
    public string Simbolo { get; }

    public MoedaAttribute(int casasDecimais = 2, string simbolo = "R$ ")
    {
        CasasDecimais = casasDecimais;
        Simbolo = simbolo;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class ColunaExcelAttribute : Attribute
{
    public string? Cabecalho { get; set; }
    public int? Largura { get; set; }
    public string? Formato { get; set; }
    public bool Ignorar { get; set; }
}

public static class ExcelExportador
{
    public static byte[] Exportar<T>(IEnumerable<T> dados, string nomePlanilha = "Dados", Action<IXLWorksheet>? configurar = null)
        where T : class
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(nomePlanilha);

        var propriedades = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<ColunaExcelAttribute>()?.Ignorar != true)
            .ToArray();

        var headerStyle = wb.Style;
        headerStyle.Font.Bold = true;
        headerStyle.Fill.BackgroundColor = XLColor.FromHtml("#2D6CDF");
        headerStyle.Font.FontColor = XLColor.White;
        headerStyle.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerStyle.Border.OutsideBorder = XLBorderStyleValues.Thin;

        for (int i = 0; i < propriedades.Length; i++)
        {
            var prop = propriedades[i];
            var attr = prop.GetCustomAttribute<ColunaExcelAttribute>();
            var cabecalho = attr?.Cabecalho ?? SepararPalavras(prop.Name);
            ws.Cell(1, i + 1).Value = cabecalho;
            ws.Cell(1, i + 1).Style = headerStyle;

            if (attr?.Largura != null)
                ws.Column(i + 1).Width = attr.Largura.Value;
        }

        int linha = 2;
        foreach (var item in dados)
        {
            for (int i = 0; i < propriedades.Length; i++)
            {
                var prop = propriedades[i];
                var valor = prop.GetValue(item);
                var cell = ws.Cell(linha, i + 1);

                var moedaAttr = prop.GetCustomAttribute<MoedaAttribute>();
                if (moedaAttr != null && valor != null)
                {
                    var decimalValor = Convert.ToDecimal(valor);
                    cell.Value = decimalValor;
                    var formato = $"\\{moedaAttr.Simbolo}#,##0." + new string('0', moedaAttr.CasasDecimais);
                    cell.Style.NumberFormat.Format = formato;
                }
                else if (prop.GetCustomAttribute<ColunaExcelAttribute>()?.Formato != null)
                {
                    cell.Value = valor;
                    cell.Style.NumberFormat.Format = prop.GetCustomAttribute<ColunaExcelAttribute>()!.Formato!;
                }
                else
                {
                    cell.Value = valor?.ToString() ?? "";
                }

                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }
            linha++;
        }

        ws.Range(1, 1, linha - 1, propriedades.Length).SetAutoFilter();
        ws.SheetView.FreezeRows(1);
        configurar?.Invoke(ws);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static byte[] ExportarMultiplasAbas(Dictionary<string, (IEnumerable<object> Dados, Type Tipo)> abas)
    {
        using var wb = new XLWorkbook();

        foreach (var (nomeAba, (dados, tipo)) in abas)
        {
            var ws = wb.Worksheets.Add(nomeAba);
            var metodo = typeof(ExcelExportador).GetMethod(nameof(ExportarGenerico))!
                .MakeGenericMethod(tipo);
            var bytes = (byte[])metodo.Invoke(null, [dados, nomeAba, null])!;

            using var ms = new MemoryStream(bytes);
            using var wbTemp = new XLWorkbook(ms);
            var wsTemp = wbTemp.Worksheet(1);
            wsTemp.CopyTo(ws);
        }

        using var msFinal = new MemoryStream();
        wb.SaveAs(msFinal);
        return msFinal.ToArray();
    }

    private static byte[] ExportarGenerico<T>(IEnumerable<T> dados, string nomePlanilha, Action<IXLWorksheet>? configurar)
        where T : class => Exportar(dados, nomePlanilha, configurar);

    private static string SepararPalavras(string texto)
    {
        return string.Concat(texto.Select((c, i) => i > 0 && char.IsUpper(c) && !char.IsUpper(texto[i - 1]) ? " " + c : c.ToString()));
    }
}

public static class ExcelExportadorExtensions
{
    public static byte[] ParaExcel<T>(this IEnumerable<T> dados, string nomePlanilha = "Dados")
        where T : class => ExcelExportador.Exportar(dados, nomePlanilha);

    public static byte[] ParaExcelComConfig<T>(this IEnumerable<T> dados, string nomePlanilha, Action<IXLWorksheet> configurar)
        where T : class => ExcelExportador.Exportar(dados, nomePlanilha, configurar);
}