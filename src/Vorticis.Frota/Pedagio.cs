using System.Globalization;
using Vorticis.Domain.Comum;

namespace Vorticis.Frota;

/// <summary>
/// Layout do arquivo de pedágio de uma operadora (Sem Parar, ConectCar, Veloe...): o nome das colunas da planilha ou do CSV
/// que trazem a data (com ou sem hora), a hora (opcional), a placa, a praça (opcional) e o valor; a operadora é o fornecedor da nota.
/// </summary>
public sealed record LayoutPedagio(long Id, string Nome, long OperadoraId, string ColunaData, string? ColunaHora, string ColunaPlaca, string? ColunaPraca,
                                  string ColunaValor, string Cfop = "1949", bool Ativo = true, string? Operadora = null)
{
    public override string ToString() => $"{Nome} ({Operadora})";
}

public sealed record PassagemPedagio(long Id, DateTime DataHora, string Placa, long? VeiculoId, string Praca, decimal Valor, long? DocumentoId = null,
                                     long? LayoutId = null, string? Veiculo = null, string? Nota = null);

/// <summary>Resultado da importação de um arquivo de pedágio: passagens novas, já importadas, sem veículo cadastrado e linhas recusadas.</summary>
public sealed record ResultadoImportacaoPedagio(int Importadas, int Duplicadas, int SemVeiculo, decimal Valor, IReadOnlyList<string> Erros);

public static class RegrasPedagio
{
    private static readonly string[] FormatosDataHora =
        ["dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "d/M/yyyy HH:mm:ss", "d/M/yyyy HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm",
         "dd/MM/yy HH:mm", "dd-MM-yyyy HH:mm:ss", "dd-MM-yyyy HH:mm"];

    public static IReadOnlyList<string> Validar(LayoutPedagio l)
    {
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(l.Nome)) erros.Add("Informe o nome do layout (ex.: Sem Parar).");
        if (l.OperadoraId <= 0) erros.Add("Escolha a operadora (fornecedor da nota de pedágio).");
        if (string.IsNullOrWhiteSpace(l.ColunaData)) erros.Add("Escolha a coluna da data.");
        if (string.IsNullOrWhiteSpace(l.ColunaPlaca)) erros.Add("Escolha a coluna da placa.");
        if (string.IsNullOrWhiteSpace(l.ColunaValor)) erros.Add("Escolha a coluna do valor.");
        if (l.Cfop is not { Length: 4 } c || !c.All(char.IsAsciiDigit) || c[0] is not ('1' or '2' or '3')) erros.Add("CFOP de entrada da nota: 4 dígitos começando por 1, 2 ou 3.");
        return erros;
    }

    /// <summary>Data e hora da passagem: a coluna de data pode já trazer a hora; a coluna de hora (se houver) completa.</summary>
    public static DateTime? LerDataHora(string? data, string? hora)
    {
        var d = (data ?? "").Trim();
        var h = (hora ?? "").Trim();
        if (h.Length > 0 && DateTime.TryParseExact($"{d} {h}", FormatosDataHora, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dh)) return dh;
        if (DateTime.TryParseExact(d, FormatosDataHora, CultureInfo.InvariantCulture, DateTimeStyles.None, out dh)) return dh;
        if (Formato.LerData(d) is not { } dia) return null;
        return TimeOnly.TryParse(h, CultureInfo.InvariantCulture, out var t) ? dia.ToDateTime(t) : dia.ToDateTime(TimeOnly.MinValue);
    }

    /// <summary>
    /// Lê as passagens da planilha pelo layout. Linha sem data, placa válida ou valor positivo é recusada (com o número da linha);
    /// as demais vêm com a placa normalizada (sem traço, maiúsculas).
    /// </summary>
    public static (IReadOnlyList<PassagemPedagio> Passagens, IReadOnlyList<string> Erros) Ler(TabelaImportada tabela, LayoutPedagio layout)
    {
        int Col(string? nome) => string.IsNullOrWhiteSpace(nome) ? -1 : tabela.Coluna(TabelaImportada.Normalizar(nome));
        var (cData, cHora, cPlaca, cPraca, cValor) = (Col(layout.ColunaData), Col(layout.ColunaHora), Col(layout.ColunaPlaca), Col(layout.ColunaPraca), Col(layout.ColunaValor));
        var faltam = new[] { (cData, layout.ColunaData), (cPlaca, layout.ColunaPlaca), (cValor, layout.ColunaValor) }.Where(c => c.Item1 < 0).Select(c => c.Item2).ToList();
        if (layout.ColunaHora is { Length: > 0 } && cHora < 0) faltam.Add(layout.ColunaHora);
        if (layout.ColunaPraca is { Length: > 0 } && cPraca < 0) faltam.Add(layout.ColunaPraca);
        if (faltam.Count > 0) return ([], [$"O arquivo não tem a(s) coluna(s) do layout: {string.Join(", ", faltam)}. Confira se é o arquivo da operadora {layout.Operadora ?? layout.Nome}."]);

        var passagens = new List<PassagemPedagio>();
        var erros = new List<string>();
        for (var i = 0; i < tabela.Linhas.Count; i++)
        {
            var linha = tabela.NumerosLinha[i];
            var placa = RegrasFrota.NormalizarPlaca(tabela.Valor(i, cPlaca));
            var valor = Formato.LerValor(tabela.Valor(i, cValor));
            var dh = LerDataHora(tabela.Valor(i, cData), cHora >= 0 ? tabela.Valor(i, cHora) : null);
            if (placa.Length == 0) continue;   // linha de total ou rodapé (sem placa)
            var problemas = new List<string>();
            if (dh is null) problemas.Add("data inválida");
            if (!RegrasFrota.PlacaValida(placa)) problemas.Add($"placa inválida ({tabela.Valor(i, cPlaca)})");
            if (valor is not > 0) problemas.Add("valor inválido");
            if (problemas.Count > 0) { erros.Add($"Linha {linha}: {string.Join(", ", problemas)}."); continue; }
            var praca = cPraca >= 0 ? tabela.Valor(i, cPraca) : "";
            passagens.Add(new PassagemPedagio(0, dh!.Value, placa, null, praca.Length > 150 ? praca[..150] : praca, Math.Round(valor!.Value, 2)));
        }
        return (passagens, erros);
    }

    /// <summary>Itens da nota de pedágio: um por placa, com a quantidade de passagens na descrição e o valor somado.</summary>
    public static IReadOnlyList<(string Placa, int Passagens, decimal Valor, string Descricao)> ItensNota(IEnumerable<PassagemPedagio> passagens)
    {
        return [.. passagens.GroupBy(p => p.Placa).OrderBy(g => g.Key).Select(g =>
        {
            var (de, ate) = (g.Min(p => p.DataHora), g.Max(p => p.DataHora));
            return (g.Key, g.Count(), g.Sum(p => p.Valor),
                    $"Pedágio – placa {RegrasFrota.FormatarPlaca(g.Key)} – {g.Count()} passagem(ns) de {de:dd/MM/yyyy} a {ate:dd/MM/yyyy}");
        })];
    }
}
