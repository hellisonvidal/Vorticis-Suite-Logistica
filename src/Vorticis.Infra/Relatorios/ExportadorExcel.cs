using MiniExcelLibs;
using Vorticis.Application.Relatorios;

namespace Vorticis.Infra.Relatorios;

/// <summary>Planilha .xlsx com uma aba por tabela; valores numéricos e datas como números/datas do Excel.</summary>
public static class ExportadorExcel
{
    public static byte[] Gerar(IReadOnlyList<TabelaRelatorio> tabelas)
    {
        var abas = new Dictionary<string, object>();
        foreach (var t in tabelas)
        {
            var titulos = TitulosUnicos(t.Colunas);
            var linhas = t.Linhas.Select(l =>
            {
                var d = new Dictionary<string, object?>();
                for (var i = 0; i < titulos.Count; i++)
                    d[titulos[i]] = l.Valores[i] switch
                    {
                        DateOnly data => data.ToDateTime(TimeOnly.MinValue),   // códigos ficam sem recuo (filtros do Excel)
                        var v => v,
                    };
                return d;
            }).ToList();
            abas[NomeAba(t.Titulo, abas.Keys)] = linhas;
        }
        using var ms = new MemoryStream();
        ms.SaveAs(abas, excelType: ExcelType.XLSX);
        return ms.ToArray();
    }

    /// <summary>Lê de volta a primeira aba (usado nos testes e na importação).</summary>
    public static IReadOnlyList<IDictionary<string, object?>> Ler(byte[] xlsx)
    {
        using var ms = new MemoryStream(xlsx);
        return ms.Query(useHeaderRow: true, excelType: ExcelType.XLSX)
                 .Cast<IDictionary<string, object?>>().ToList();
    }

    private static List<string> TitulosUnicos(IReadOnlyList<ColunaRelatorio> colunas)
    {
        var res = new List<string>();
        foreach (var c in colunas)
        {
            var t = string.IsNullOrWhiteSpace(c.Titulo) ? "Coluna" : c.Titulo;
            var nome = t;
            for (var n = 2; res.Contains(nome); n++) nome = $"{t} ({n})";
            res.Add(nome);
        }
        return res;
    }

    private static string NomeAba(string titulo, IEnumerable<string> existentes)
    {
        var limpo = new string(titulo.Select(ch => "[]:*?/\\".Contains(ch) ? '-' : ch).ToArray()).Trim();
        if (limpo.Length == 0) limpo = "Relatório";
        if (limpo.Length > 31) limpo = limpo[..31];
        var nome = limpo;
        for (var n = 2; existentes.Contains(nome); n++) nome = $"{limpo[..Math.Min(limpo.Length, 27)]} ({n})";
        return nome;
    }
}
