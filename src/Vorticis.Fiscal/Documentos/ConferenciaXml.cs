using System.Globalization;
using Vorticis.Domain.Comum;
using Vorticis.Fiscal.Sped;

namespace Vorticis.Fiscal.Documentos;

/// <summary>
/// Conferência dos dados lidos do XML antes de gravar: a chave de acesso (dígito verificador e coerência com o CNPJ do emitente, o modelo,
/// a série e o número da nota) e os itens (quantidade × valor unitário, bases × alíquotas de ICMS, IPI, PIS e COFINS, CFOP e NCM) e a soma
/// dos itens contra os totais. Não impede a importação: os avisos aparecem no resultado para o usuário conferir a nota.
/// </summary>
public static class ConferenciaXml
{
    private const decimal ToleranciaItem = 0.02m;
    private const decimal ToleranciaTotal = 0.05m;

    private static string M(decimal v) => Formato.Moeda(v);
    private static decimal Calc(decimal bc, decimal aliq) => Math.Round(bc * aliq / 100m, 2, MidpointRounding.AwayFromZero);

    public static IReadOnlyList<string> Conferir(DocumentoLido d)
    {
        var avisos = new List<string>();
        if (d.Modelo != ModeloDocumento.NFSe) ConferirChave(d, avisos);
        foreach (var i in d.Itens) ConferirItem(d, i, avisos);
        ConferirTotal("produtos", d.Itens.Sum(i => i.ValorProduto), d.Totais.Produtos, avisos);
        if (d.Modelo != ModeloDocumento.NFSe)
        {
            ConferirTotal("ICMS", d.Itens.Sum(i => i.Icms), d.Totais.Icms, avisos);
            ConferirTotal("IPI", d.Itens.Sum(i => i.Ipi), d.Totais.Ipi, avisos);
            ConferirTotal("ICMS-ST", d.Itens.Sum(i => i.IcmsSt), d.Totais.IcmsSt, avisos);
        }
        if (Math.Abs(d.DiferencaItens) > ToleranciaTotal) avisos.Add($"A soma dos itens difere do total da nota em {M(d.DiferencaItens)}.");
        return avisos;
    }

    private static void ConferirChave(DocumentoLido d, List<string> avisos)
    {
        var chave = new string([.. (d.Chave ?? "").Where(char.IsAsciiDigit)]);
        if (chave.Length != 44) { avisos.Add("A nota não tem chave de acesso de 44 dígitos."); return; }
        if (!CamposEfd.ChaveValida(chave)) { avisos.Add("Chave de acesso com dígito verificador inválido."); return; }
        var modelo = d.Modelo == ModeloDocumento.NFCe65 ? "65" : "55";
        if (chave.Substring(20, 2) != modelo) avisos.Add($"A chave de acesso é do modelo {chave.Substring(20, 2)}, mas a nota é modelo {modelo}.");
        var cnpj = new string([.. (d.Emitente.Documento ?? "").Where(char.IsAsciiDigit)]);
        if (cnpj.Length == 14 && chave.Substring(6, 14) != cnpj) avisos.Add("A chave de acesso não é do CNPJ do emitente.");
        if (int.TryParse(d.Serie, out var serie) && int.Parse(chave.Substring(22, 3), CultureInfo.InvariantCulture) != serie)
            avisos.Add($"A série da chave ({int.Parse(chave.Substring(22, 3), CultureInfo.InvariantCulture)}) não é a da nota ({serie}).");
        if (int.TryParse(d.Numero, out var numero) && int.Parse(chave.Substring(25, 9), CultureInfo.InvariantCulture) != numero)
            avisos.Add($"O número da chave ({int.Parse(chave.Substring(25, 9), CultureInfo.InvariantCulture)}) não é o da nota ({numero}).");
    }

    private static void ConferirItem(DocumentoLido d, ItemFiscal i, List<string> avisos)
    {
        var item = $"Item {i.Numero}";
        if (d.Modelo != ModeloDocumento.NFSe)
        {
            if (string.IsNullOrWhiteSpace(i.Cfop) || i.Cfop.Length != 4) avisos.Add($"{item}: CFOP ausente ou inválido.");
            if (string.IsNullOrWhiteSpace(i.Ncm)) avisos.Add($"{item}: sem NCM.");
            if (i.Quantidade > 0 && i.ValorUnitario > 0 && Math.Abs(Math.Round(i.Quantidade * i.ValorUnitario, 2, MidpointRounding.AwayFromZero) - i.ValorProduto) > ToleranciaItem)
                avisos.Add($"{item}: quantidade × valor unitário ({M(Math.Round(i.Quantidade * i.ValorUnitario, 2))}) difere do valor do produto ({M(i.ValorProduto)}).");
        }
        void Tributo(string nome, decimal bc, decimal aliq, decimal valor)
        {
            if (bc > 0 && aliq > 0 && Math.Abs(Calc(bc, aliq) - valor) > ToleranciaItem)
                avisos.Add($"{item}: {nome} de {M(valor)} não confere com base {M(bc)} × {aliq.ToString("0.####", Formato.PtBr)}% = {M(Calc(bc, aliq))}.");
        }
        Tributo("ICMS", i.BcIcms, i.AliqIcms, i.Icms);
        Tributo("IPI", i.BcIpi, i.AliqIpi, i.Ipi);
        Tributo("PIS", i.BcPis, i.AliqPis, i.Pis);
        Tributo("COFINS", i.BcCofins, i.AliqCofins, i.Cofins);
        Tributo("ISS", i.BcIss, i.AliqIss, i.Iss);
    }

    private static void ConferirTotal(string nome, decimal itens, decimal total, List<string> avisos)
    {
        if (total > 0 && Math.Abs(itens - total) > ToleranciaTotal) avisos.Add($"A soma de {nome} dos itens ({M(itens)}) difere do total de {nome} da nota ({M(total)}).");
    }
}
