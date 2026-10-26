using Vorticis.Domain.Comum;

namespace Vorticis.Fiscal.Documentos;

/// <summary>Um item de documento já gravado, com os dados do documento (base dos livros, resumos e apurações).</summary>
public sealed record LinhaItemFiscal(
    long DocumentoId, ModeloDocumento Modelo, TipoOperacao Operacao, string Numero, string Serie, DateOnly Emissao, DateOnly DataLivro,
    SituacaoDocumento Situacao, string? ParticipanteNome, string? ParticipanteDocumento, string? Uf, string Chave, bool IssRetido, ItemFiscal Item);

/// <summary>Linha do Livro de Entradas ou de Saídas: um documento por CFOP.</summary>
public sealed record LinhaLivro(
    DateOnly DataLivro, ModeloDocumento Modelo, string Serie, string Numero, DateOnly Emissao, string? Participante, string? Documento,
    string? Uf, string Cfop, decimal ValorContabil, decimal BcIcms, decimal AliqIcms, decimal Icms, decimal IcmsSt, decimal Ipi, decimal Pis,
    decimal Cofins, decimal Iss, string Observacao, long DocumentoId);

/// <summary>Resumo por CFOP (e por tributo) de um período.</summary>
public sealed record ResumoCfop(string Cfop, int Documentos, decimal ValorContabil, decimal BcIcms, decimal Icms, decimal IcmsSt, decimal Ipi,
                                decimal Pis, decimal Cofins, decimal Iss);

/// <summary>Montagem dos livros fiscais a partir dos itens: regras puras, testáveis sem banco.</summary>
public static class LivrosFiscais
{
    /// <summary>
    /// Livro de Entradas/Saídas: uma linha por documento e CFOP, ordenadas pela data do livro, série e número.
    /// Documentos cancelados e denegados aparecem identificados, sem valores (não compõem os totais).
    /// </summary>
    public static IReadOnlyList<LinhaLivro> Montar(IEnumerable<LinhaItemFiscal> itens, TipoOperacao operacao)
    {
        var linhas = new List<LinhaLivro>();
        foreach (var g in itens.Where(i => i.Operacao == operacao).GroupBy(i => (i.DocumentoId, i.Item.Cfop)))
        {
            var p = g.First();
            if (p.Situacao != SituacaoDocumento.Autorizada)
            {
                linhas.Add(new LinhaLivro(p.DataLivro, p.Modelo, p.Serie, p.Numero, p.Emissao, p.ParticipanteNome, p.ParticipanteDocumento, p.Uf,
                    g.Key.Cfop, 0, 0, 0, 0, 0, 0, 0, 0, 0, p.Situacao.Rotulo().ToUpperInvariant(), p.DocumentoId));
                continue;
            }
            var bc = g.Sum(i => i.Item.BcIcms);
            var icms = g.Sum(i => i.Item.Icms);
            linhas.Add(new LinhaLivro(p.DataLivro, p.Modelo, p.Serie, p.Numero, p.Emissao, p.ParticipanteNome, p.ParticipanteDocumento, p.Uf,
                g.Key.Cfop, g.Sum(i => i.Item.ValorContabil), bc, bc > 0 ? Math.Round(icms / bc * 100, 2) : 0, icms, g.Sum(i => i.Item.IcmsSt),
                g.Sum(i => i.Item.Ipi), g.Sum(i => i.Item.Pis), g.Sum(i => i.Item.Cofins), g.Sum(i => i.Item.Iss), "", p.DocumentoId));
        }
        return linhas.OrderBy(l => l.DataLivro).ThenBy(l => l.Serie, StringComparer.Ordinal)
                     .ThenBy(l => l.Numero.Length).ThenBy(l => l.Numero, StringComparer.Ordinal).ThenBy(l => l.Cfop, StringComparer.Ordinal).ToList();
    }

    /// <summary>Total por CFOP das linhas do livro (documentos autorizados), em ordem de CFOP.</summary>
    public static IReadOnlyList<ResumoCfop> ResumirPorCfop(IEnumerable<LinhaItemFiscal> itens)
        => itens.Where(i => i.Situacao == SituacaoDocumento.Autorizada)
                .GroupBy(i => i.Item.Cfop)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new ResumoCfop(g.Key, g.Select(i => i.DocumentoId).Distinct().Count(), g.Sum(i => i.Item.ValorContabil),
                    g.Sum(i => i.Item.BcIcms), g.Sum(i => i.Item.Icms), g.Sum(i => i.Item.IcmsSt), g.Sum(i => i.Item.Ipi),
                    g.Sum(i => i.Item.Pis), g.Sum(i => i.Item.Cofins), g.Sum(i => i.Item.Iss)))
                .ToList();

    public static ResumoCfop Total(IReadOnlyList<ResumoCfop> linhas, string rotulo = "TOTAL") =>
        new(rotulo, linhas.Sum(l => l.Documentos), linhas.Sum(l => l.ValorContabil), linhas.Sum(l => l.BcIcms), linhas.Sum(l => l.Icms),
            linhas.Sum(l => l.IcmsSt), linhas.Sum(l => l.Ipi), linhas.Sum(l => l.Pis), linhas.Sum(l => l.Cofins), linhas.Sum(l => l.Iss));

    /// <summary>Soma da receita bruta de saída (CFOP de venda/serviço) menos as devoluções de venda recebidas, de documentos autorizados.</summary>
    public static decimal ReceitaLiquida(IEnumerable<LinhaItemFiscal> itens)
    {
        var autorizados = itens.Where(i => i.Situacao == SituacaoDocumento.Autorizada).ToList();
        var vendas = autorizados.Where(i => i.Operacao == TipoOperacao.Saida && Cfop.GeraReceita(i.Item.Cfop)).Sum(i => i.Item.ValorContabil - i.Item.Ipi - i.Item.IcmsSt);
        var devolucoes = autorizados.Where(i => i.Operacao == TipoOperacao.Entrada && Cfop.EhDevolucaoDeVenda(i.Item.Cfop)).Sum(i => i.Item.ValorContabil - i.Item.Ipi - i.Item.IcmsSt);
        return Dinheiro.Arredondar(vendas - devolucoes);
    }
}
