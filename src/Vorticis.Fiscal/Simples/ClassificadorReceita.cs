using Vorticis.Domain.Comum;
using Vorticis.Fiscal.Documentos;

namespace Vorticis.Fiscal.Simples;

/// <summary>
/// Sugere a segregação da receita do mês por atividade e tratamento a partir das notas fiscais: o CFOP diz se é venda de produção
/// própria (indústria), revenda, serviço, com ST ou exportação; o CST de PIS/COFINS 04 indica produto monofásico; o ISS retido vem
/// da NFS-e. Devoluções de venda recebidas entram negativas. É uma sugestão: o usuário revisa antes de calcular.
/// </summary>
public static class ClassificadorReceita
{
    // Vendas de produção do estabelecimento (indústria): x.101, x.103, x.105, x.107, x.109, x.111, x.113, x.115, x.116, x.118 e x.124 (industrialização por encomenda).
    private static readonly HashSet<string> FinalIndustria = ["101", "103", "105", "107", "109", "111", "113", "115", "116", "118", "124"];

    public static ReceitaSimples? Classificar(LinhaItemFiscal l, AtividadeSimples atividadeServicos)
    {
        if (l.Situacao != SituacaoDocumento.Autorizada) return null;
        var cfop = l.Item.Cfop;
        // Valor da receita bruta: sem IPI e sem ICMS-ST cobrado como substituto (não integram a receita do vendedor).
        var valor = Dinheiro.Arredondar(l.Item.ValorContabil - l.Item.Ipi - l.Item.IcmsSt);

        if (l.Operacao == TipoOperacao.Entrada)
        {
            if (!Cfop.EhDevolucaoDeVenda(cfop)) return null;
            // A devolução não distingue a origem da mercadoria: entra como revenda (o usuário ajusta a segregação, se preciso).
            return new ReceitaSimples(AtividadeSimples.ComercioRevenda, -valor, TratamentoSimples.Normal, "Devoluções de vendas");
        }

        if (!Cfop.GeraReceita(cfop)) return null;
        if (cfop == Cfop.Servico || cfop is "5933" or "6933")
            return new ReceitaSimples(atividadeServicos, valor, l.IssRetido ? TratamentoSimples.IssRetido : TratamentoSimples.Normal, "Serviços");

        var terminacao = cfop[1..];
        var industriaPropria = FinalIndustria.Contains(terminacao) || cfop is "5401" or "6401" or "5402" or "6402";
        var atividade = industriaPropria ? AtividadeSimples.IndustriaVenda : AtividadeSimples.ComercioRevenda;
        var tratamento = TratamentoSimples.Normal;
        if (cfop[0] == '7') tratamento |= TratamentoSimples.Exportacao;
        if (cfop is "5401" or "5402" or "5403" or "5405" or "6401" or "6402" or "6403" or "6405") tratamento |= TratamentoSimples.SubstituicaoTributaria;
        if (l.Item.CstPis == "04") tratamento |= TratamentoSimples.Monofasico;
        return new ReceitaSimples(atividade, valor, tratamento, null);
    }

    public static IReadOnlyList<ReceitaSimples> Sugerir(IEnumerable<LinhaItemFiscal> itens, AtividadeSimples atividadeServicos) =>
        itens.Select(i => Classificar(i, atividadeServicos)).OfType<ReceitaSimples>()
             .GroupBy(r => (r.Atividade, r.Tratamento, r.Descricao))
             .Select(g => new ReceitaSimples(g.Key.Atividade, g.Sum(r => r.Valor), g.Key.Tratamento, g.Key.Descricao))
             .OrderBy(r => r.Atividade).ThenBy(r => r.Tratamento).ToList();
}
