using System.Globalization;
using System.Text.RegularExpressions;
using Vorticis.Domain.Comum;

namespace Vorticis.Domain.Contabil;

/// <summary>Movimento do extrato: valor positivo é entrada (crédito na conta do banco), negativo é saída.</summary>
public sealed record TransacaoExtrato(string Id, DateOnly Data, decimal Valor, string Descricao, string? Documento);

public sealed record ExtratoBancario(string Banco, string Agencia, string Conta, DateOnly? Inicio, DateOnly? Fim, decimal? SaldoFinal,
                                     IReadOnlyList<TransacaoExtrato> Transacoes)
{
    /// <summary>Chave da transação para o controle de duplicidade (banco/conta/FITID).</summary>
    public string Chave(TransacaoExtrato t) => $"OFX:{Banco}/{Agencia}/{Conta}/{t.Id}";
}

/// <summary>
/// Lê extratos OFX (versão 1.x SGML, sem fechamento de tags, e 2.x XML), o formato que os bancos brasileiros
/// oferecem no internet banking. Usa STMTTRN (DTPOSTED, TRNAMT, FITID, MEMO/NAME, CHECKNUM) e BANKACCTFROM.
/// </summary>
public static partial class LeitorOfx
{
    [GeneratedRegex(@"<STMTTRN>(.*?)(</STMTTRN>|(?=<STMTTRN>)|(?=</BANKTRANLIST>))", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Transacoes();

    public static Resultado<ExtratoBancario> Ler(string texto)
    {
        if (!texto.Contains("<OFX>", StringComparison.OrdinalIgnoreCase))
            return Resultado<ExtratoBancario>.Falha("O arquivo não é um extrato OFX (não encontrei a marca <OFX>).");
        var erros = new List<string>();
        var lista = new List<TransacaoExtrato>();
        var n = 0;
        foreach (Match m in Transacoes().Matches(texto))
        {
            n++;
            var bloco = m.Groups[1].Value;
            var data = Formato.LerData(Tag(bloco, "DTPOSTED"));
            var valor = Valor(Tag(bloco, "TRNAMT"));
            if (data is null || valor is null) { erros.Add($"Transação {n}: data ou valor inválido."); continue; }
            var memo = Tag(bloco, "MEMO");
            var nome = Tag(bloco, "NAME");
            var descricao = string.Join(" - ", new[] { nome, memo }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct()).Trim();
            var id = Tag(bloco, "FITID") ?? $"{data:yyyyMMdd}-{valor.Value.ToString(CultureInfo.InvariantCulture)}-{n}";
            lista.Add(new TransacaoExtrato(id, data.Value, valor.Value, descricao.Length == 0 ? Tag(bloco, "TRNTYPE") ?? "" : descricao,
                                           Tag(bloco, "CHECKNUM") ?? Tag(bloco, "REFNUM")));
        }
        if (erros.Count > 0) return Resultado<ExtratoBancario>.Falha(erros);
        if (lista.Count == 0) return Resultado<ExtratoBancario>.Falha("O extrato não tem movimentos.");
        var ledger = Regex.Match(texto, "<LEDGERBAL>(.*?)(</LEDGERBAL>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        return Resultado<ExtratoBancario>.Ok(new ExtratoBancario(
            Tag(texto, "BANKID") ?? "", Tag(texto, "BRANCHID") ?? "", Tag(texto, "ACCTID") ?? "",
            Formato.LerData(Tag(texto, "DTSTART")), Formato.LerData(Tag(texto, "DTEND")),
            ledger.Success ? Valor(Tag(ledger.Groups[1].Value, "BALAMT")) : null, lista));
    }

    /// <summary>Conteúdo da tag: até o próximo "&lt;" ou fim de linha (SGML não fecha as tags).</summary>
    private static string? Tag(string bloco, string tag)
    {
        var m = Regex.Match(bloco, $@"<{tag}>([^<\r\n]*)", RegexOptions.IgnoreCase);
        var v = m.Success ? m.Groups[1].Value.Trim() : null;
        return string.IsNullOrEmpty(v) ? null : System.Net.WebUtility.HtmlDecode(v);
    }

    /// <summary>OFX usa ponto decimal; alguns bancos mandam vírgula.</summary>
    private static decimal? Valor(string? s) =>
        s is null ? null
        : decimal.TryParse(s.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) ? v
        : null;
}
