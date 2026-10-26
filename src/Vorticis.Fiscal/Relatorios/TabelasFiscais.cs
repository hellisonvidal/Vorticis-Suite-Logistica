using Vorticis.Application.Relatorios;
using Vorticis.Domain.Comum;
using Vorticis.Fiscal.Documentos;
using Vorticis.Fiscal.Duplicatas;
using Vorticis.Fiscal.Guias;
using Vorticis.Fiscal.Lucro;
using Vorticis.Fiscal.Produtos;
using Vorticis.Fiscal.Retencoes;
using Vorticis.Fiscal.Simples;
using Vorticis.Fiscal.Sped;

namespace Vorticis.Fiscal.Relatorios;

/// <summary>
/// Tabelas dos relatórios fiscais no formato comum de exportação (<see cref="TabelaRelatorio"/>): a mesma tabela aparece na tela e sai em PDF,
/// Excel e CSV. Puro: só monta linhas a partir dos resultados dos serviços.
/// </summary>
public static class TabelasFiscais
{
    private static LinhaRelatorio L(bool destaque, params object?[] valores) => new(valores, destaque);

    /// <summary>Relatório "Duplicatas a pagar" ou "Duplicatas a receber": uma linha por parcela, com o atraso na data-base.</summary>
    public static TabelaRelatorio Duplicatas(TipoDuplicata tipo, IReadOnlyList<Duplicata> lista, DateOnly dataBase)
    {
        var colunas = new List<ColunaRelatorio>
        {
            new("Vencimento", TipoColuna.Data), new("Documento", LarguraCm: 2.2), new("Parcela", LarguraCm: 1.5), new(tipo == TipoDuplicata.Pagar ? "Fornecedor" : "Cliente"),
            new("Emissão", TipoColuna.Data), new("Valor", TipoColuna.Valor), new("Baixado", TipoColuna.Valor), new("Saldo", TipoColuna.Valor), new("Situação", LarguraCm: 2),
            new("Atraso (dias)", TipoColuna.Inteiro, 1.8),
        };
        var rows = lista.Select(d => L(false, d.Vencimento, d.NumeroDocumento, d.Parcela, d.ParticipanteNome ?? "", d.Emissao, d.Valor, d.Valor - d.Saldo, d.Saldo,
            d.Situacao.Rotulo(), d.Vencida(dataBase) ? d.DiasAtraso(dataBase) : null)).ToList();
        rows.Add(L(true, null, "TOTAL", "", "", null, lista.Sum(d => d.Valor), lista.Sum(d => d.Valor - d.Saldo), lista.Sum(d => d.Saldo), "", null));
        return new TabelaRelatorio(tipo == TipoDuplicata.Pagar ? "Duplicatas a pagar" : "Duplicatas a receber", colunas, rows,
            $"{lista.Count} duplicata(s); {lista.Count(d => d.Vencida(dataBase))} vencida(s) em {dataBase:dd/MM/yyyy}.");
    }

    /// <summary>Posição por vencimento (aging): saldo em aberto por participante em cada faixa de atraso.</summary>
    public static TabelaRelatorio PosicaoPorVencimento(TipoDuplicata tipo, IReadOnlyList<LinhaAging> linhas, DateOnly dataBase)
    {
        var colunas = new List<ColunaRelatorio> { new(tipo == TipoDuplicata.Pagar ? "Fornecedor" : "Cliente") };
        colunas.AddRange(Aging.Rotulos.Select(r => new ColunaRelatorio(r, TipoColuna.Valor)));
        colunas.Add(new ColunaRelatorio("Total", TipoColuna.Valor));
        var rows = linhas.Select(l => L(false, [l.Participante, .. l.Faixas.Cast<object?>(), l.Total])).ToList();
        rows.Add(L(true, ["TOTAL", .. Enumerable.Range(0, Aging.Rotulos.Count).Select(i => (object?)linhas.Sum(l => l.Faixas[i])), linhas.Sum(l => l.Total)]));
        return new TabelaRelatorio($"Posição por vencimento — duplicatas {tipo.Rotulo().ToLowerInvariant()}", colunas, rows,
            $"Saldo em aberto em {dataBase:dd/MM/yyyy}. Só duplicatas abertas ou com baixa parcial.");
    }

    public static TabelaRelatorio Livro(TipoOperacao operacao, IReadOnlyList<LinhaLivro> linhas)
    {
        var titulo = operacao == TipoOperacao.Entrada ? "Livro de Registro de Entradas" : "Livro de Registro de Saídas";
        var colunas = new List<ColunaRelatorio>
        {
            new(operacao == TipoOperacao.Entrada ? "Entrada" : "Saída", TipoColuna.Data), new("Mod.", LarguraCm: 1.1), new("Sér.", LarguraCm: 0.9), new("Número", LarguraCm: 1.5),
            new("Emissão", TipoColuna.Data), new(operacao == TipoOperacao.Entrada ? "Fornecedor" : "Cliente"), new("CNPJ/CPF", LarguraCm: 3.2), new("UF", LarguraCm: 0.8),
            new("CFOP", LarguraCm: 1.2), new("Valor contábil", TipoColuna.Valor), new("BC ICMS", TipoColuna.Valor), new("Alíq.", TipoColuna.Valor, 1.2), new("ICMS", TipoColuna.Valor),
            new("ICMS-ST", TipoColuna.Valor), new("IPI", TipoColuna.Valor), new("PIS", TipoColuna.Valor), new("COFINS", TipoColuna.Valor), new("ISS", TipoColuna.Valor), new("Obs.", LarguraCm: 2),
        };
        var rows = linhas.Select(l => L(false, l.DataLivro, l.Modelo.Codigo() == "NFSE" ? "NFS" : l.Modelo.Codigo(), l.Serie, l.Numero, l.Emissao, l.Participante ?? "", l.Documento ?? "",
            l.Uf ?? "", l.Cfop, l.ValorContabil, l.BcIcms, l.AliqIcms, l.Icms, l.IcmsSt, l.Ipi, l.Pis, l.Cofins, l.Iss, l.Observacao)).ToList();
        rows.Add(L(true, null, "", "", "TOTAL", null, "", "", "", "", linhas.Sum(l => l.ValorContabil), linhas.Sum(l => l.BcIcms), null, linhas.Sum(l => l.Icms), linhas.Sum(l => l.IcmsSt),
            linhas.Sum(l => l.Ipi), linhas.Sum(l => l.Pis), linhas.Sum(l => l.Cofins), linhas.Sum(l => l.Iss), ""));
        return new TabelaRelatorio(titulo, colunas, rows, $"{linhas.Select(l => l.DocumentoId).Distinct().Count()} documento(s), {linhas.Count} linha(s) por CFOP. Cancelados e denegados aparecem sem valores.");
    }

    private static readonly ColunaRelatorio[] ColunasApuracao =
        [new("Descrição"), new("CFOP", LarguraCm: 1.8), new("Valor contábil", TipoColuna.Valor), new("Base de cálculo", TipoColuna.Valor), new("Imposto", TipoColuna.Valor)];

    private static IEnumerable<LinhaRelatorio> BlocoCfop(string titulo, IReadOnlyList<LinhaApuracaoCfop> linhas, string total)
    {
        yield return L(true, titulo, "", null, null, null);
        foreach (var l in linhas) yield return L(false, "   Operações com o CFOP", l.Cfop, l.Operacao, l.Base, l.Imposto);
        yield return L(true, total, "", linhas.Sum(l => l.Operacao), linhas.Sum(l => l.Base), linhas.Sum(l => l.Imposto));
    }

    private static LinhaRelatorio Valor(string descricao, decimal v) => L(false, descricao, "", null, null, v);

    /// <summary>Livro de Registro de Apuração do ICMS (modelo 9); os valores são os do registro E110 da EFD ICMS/IPI.</summary>
    public static TabelaRelatorio LivroApuracaoIcms(LivroApuracao livro)
    {
        var a = livro.Icms;
        var rows = new List<LinhaRelatorio>();
        rows.AddRange(BlocoCfop("1. DÉBITO DO IMPOSTO — saídas e prestações", a.DebitosCfop ?? [], "Total dos débitos por saídas e prestações"));
        rows.Add(Valor("   Outros débitos", 0));
        rows.Add(Valor("   Estornos de créditos", 0));
        rows.Add(L(true, "Total dos débitos", "", null, null, a.Debitos));
        rows.AddRange(BlocoCfop("2. CRÉDITO DO IMPOSTO — entradas e aquisições", a.CreditosCfop ?? [], "Total dos créditos por entradas"));
        rows.Add(Valor("   Outros créditos", 0));
        rows.Add(Valor("   Estornos de débitos", 0));
        rows.Add(L(true, "Total dos créditos", "", null, null, a.Creditos));
        rows.Add(L(true, "3. APURAÇÃO DO SALDO", "", null, null, null));
        rows.Add(Valor("   Saldo credor do período anterior", a.SaldoCredorAnterior));
        rows.Add(Valor("   Saldo devedor apurado", Math.Max(0, a.SaldoApurado)));
        rows.Add(Valor("   Deduções", a.Deducoes));
        rows.Add(L(true, "Imposto a recolher", "", null, null, a.Recolher));
        rows.Add(L(true, "Saldo credor a transportar para o período seguinte", "", null, null, a.SaldoCredorTransportar));
        return new TabelaRelatorio($"Livro de Registro de Apuração do ICMS — {livro.Inicio:MM/yyyy}", ColunasApuracao, rows,
            "Mesmos valores do registro E110 da EFD ICMS/IPI. Notas canceladas e denegadas e NFS-e não entram.");
    }

    /// <summary>Livro de Registro de Apuração do IPI (modelo 8); os valores são os do registro E520 da EFD ICMS/IPI.</summary>
    public static TabelaRelatorio LivroApuracaoIpi(LivroApuracao livro)
    {
        var ipi = livro.Ipi;
        var linhas = ipi?.Linhas ?? [];
        var rows = new List<LinhaRelatorio>();
        LinhaApuracaoCfop[] Por(Func<string, bool> f) => [.. linhas.Where(l => f(l.Cfop)).GroupBy(l => l.Cfop)
            .Select(g => new LinhaApuracaoCfop(g.Key, g.Sum(l => l.Contabil), g.Sum(l => l.Base), g.Sum(l => l.Ipi)))];
        rows.AddRange(BlocoCfop("1. SAÍDAS — débito do imposto", Por(CamposEfd.CfopSaida), "Total dos débitos por saídas"));
        rows.Add(Valor("   Outros débitos", 0));
        rows.Add(Valor("   Estornos de créditos", 0));
        rows.Add(L(true, "Total dos débitos", "", null, null, ipi?.Debitos ?? 0));
        rows.AddRange(BlocoCfop("2. ENTRADAS — crédito do imposto", Por(CamposEfd.CfopEntrada), "Total dos créditos por entradas"));
        rows.Add(Valor("   Outros créditos", 0));
        rows.Add(Valor("   Estornos de débitos", 0));
        rows.Add(L(true, "Total dos créditos", "", null, null, ipi?.Creditos ?? 0));
        rows.Add(L(true, "3. APURAÇÃO DO SALDO", "", null, null, null));
        rows.Add(Valor("   Saldo credor do período anterior", 0));
        rows.Add(L(true, "Saldo devedor (imposto a recolher)", "", null, null, ipi?.SaldoDevedor ?? 0));
        rows.Add(L(true, "Saldo credor a transportar para o período seguinte", "", null, null, ipi?.SaldoCredor ?? 0));
        return new TabelaRelatorio($"Livro de Registro de Apuração do IPI — {livro.Inicio:MM/yyyy}", ColunasApuracao, rows,
            "Mesmos valores dos registros E510 e E520 da EFD ICMS/IPI. Só entram os itens com situação tributária de IPI informada.");
    }

    public static TabelaRelatorio Produtos(IReadOnlyList<Produto> produtos)
    {
        var rows = produtos.Select(p => L(false, p.Codigo, p.Descricao, p.Unidade, p.Ncm ?? "", p.Cest ?? "", p.Gtin ?? "", p.Origem, TiposItem.Rotulo(p.TipoItem),
            p.CfopEntrada ?? "", p.CfopSaida ?? "", p.AliqIcms, p.AliqIpi, p.AliqPis, p.AliqCofins, p.Ativo ? "Sim" : "Não")).ToList();
        return new TabelaRelatorio("Produtos cadastrados",
        [
            new("Código", TipoColuna.Inteiro), new("Descrição"), new("Un.", LarguraCm: 1), new("NCM", LarguraCm: 2), new("CEST", LarguraCm: 1.8), new("GTIN", LarguraCm: 3.2),
            new("Orig.", LarguraCm: 1), new("Tipo do item", LarguraCm: 4), new("CFOP ent.", LarguraCm: 1.6), new("CFOP saí.", LarguraCm: 1.6), new("ICMS %", TipoColuna.Valor, 1.5),
            new("IPI %", TipoColuna.Valor, 1.5), new("PIS %", TipoColuna.Valor, 1.5), new("COFINS %", TipoColuna.Valor, 1.6), new("Ativo", LarguraCm: 1.1),
        ], rows, $"{produtos.Count} produto(s).");
    }

    public static TabelaRelatorio ItensNotas(IReadOnlyList<LinhaItemNota> itens)
    {
        var rows = itens.Select(i => L(false, i.DataLivro, i.Operacao.Rotulo(), i.Modelo.Codigo() == "NFSE" ? "NFS" : i.Modelo.Codigo(), i.Numero, i.Participante ?? "", i.CodigoProduto,
            i.Descricao ?? "", i.Ncm ?? "", i.Cfop, i.Unidade ?? "", i.Quantidade, i.ValorUnitario, i.ValorProduto, i.ValorContabil, i.Icms, i.IcmsSt, i.Ipi, i.Pis, i.Cofins, i.Iss)).ToList();
        rows.Add(L(true, null, "", "", "TOTAL", "", null, "", "", "", "", null, null, itens.Sum(i => i.ValorProduto), itens.Sum(i => i.ValorContabil), itens.Sum(i => i.Icms),
            itens.Sum(i => i.IcmsSt), itens.Sum(i => i.Ipi), itens.Sum(i => i.Pis), itens.Sum(i => i.Cofins), itens.Sum(i => i.Iss)));
        return new TabelaRelatorio("Itens das notas fiscais",
        [
            new("Data", TipoColuna.Data), new("Oper.", LarguraCm: 1.6), new("Mod.", LarguraCm: 1.1), new("Número", LarguraCm: 1.6), new("Participante"), new("Cód.", TipoColuna.Inteiro, 1.2),
            new("Produto/descrição"), new("NCM", LarguraCm: 2), new("CFOP", LarguraCm: 1.3), new("Un.", LarguraCm: 1), new("Qtde", TipoColuna.Valor), new("Unitário", TipoColuna.Valor),
            new("Valor produto", TipoColuna.Valor), new("Valor contábil", TipoColuna.Valor), new("ICMS", TipoColuna.Valor), new("ICMS-ST", TipoColuna.Valor), new("IPI", TipoColuna.Valor),
            new("PIS", TipoColuna.Valor), new("COFINS", TipoColuna.Valor), new("ISS", TipoColuna.Valor),
        ], rows, $"{itens.Count} item(ns) de notas autorizadas. Cancelados e denegados não entram.");
    }

    public static TabelaRelatorio ResumoCfop(IReadOnlyList<ResumoCfop> resumo)
    {
        var rows = resumo.Select(r => L(false, r.Cfop, Cfop.Ambito(r.Cfop), r.Documentos, r.ValorContabil, r.BcIcms, r.Icms, r.IcmsSt, r.Ipi, r.Pis, r.Cofins, r.Iss)).ToList();
        var t = LivrosFiscais.Total(resumo);
        rows.Add(L(true, "TOTAL", "", t.Documentos, t.ValorContabil, t.BcIcms, t.Icms, t.IcmsSt, t.Ipi, t.Pis, t.Cofins, t.Iss));
        return new TabelaRelatorio("Resumo por CFOP",
        [
            new("CFOP", LarguraCm: 1.8), new("Âmbito", LarguraCm: 3.5), new("Docs.", TipoColuna.Inteiro), new("Valor contábil", TipoColuna.Valor), new("BC ICMS", TipoColuna.Valor),
            new("ICMS", TipoColuna.Valor), new("ICMS-ST", TipoColuna.Valor), new("IPI", TipoColuna.Valor), new("PIS", TipoColuna.Valor), new("COFINS", TipoColuna.Valor), new("ISS", TipoColuna.Valor),
        ], rows);
    }

    public static IReadOnlyList<TabelaRelatorio> Simples(ResultadoSimples r)
    {
        var seg = new TabelaRelatorio($"Simples Nacional · {r.Competencia:MM/yyyy}",
        [
            new("Anexo", LarguraCm: 1.4), new("Atividade"), new("Tratamento"), new("Receita", TipoColuna.Valor), new("Faixa", TipoColuna.Inteiro), new("Alíq. nominal", TipoColuna.Valor, 2.2),
            new("Parcela a deduzir", TipoColuna.Valor, 2.8), new("Alíq. efetiva", TipoColuna.Valor, 2.2), new("Alíq. aplicada", TipoColuna.Valor, 2.2), new("DAS", TipoColuna.Valor),
        ],
        r.Segmentos.Select(s => L(false, s.Anexo.ToString(), s.Atividade.ToString(), s.Tratamento == TratamentoSimples.Normal ? "Normal" : s.Tratamento.ToString(), s.Receita, s.Faixa,
            s.AliquotaNominal, s.ParcelaDeduzir, s.AliquotaEfetiva, s.AliquotaAplicada, s.ValorDas))
         .Append(L(true, "", "TOTAL", "", r.ReceitaMes, null, null, null, null, null, r.ValorDas)).ToList(),
        $"RBT12 {Formato.Moeda(r.Rbt12)} · folha 12 meses {Formato.Moeda(r.Folha12)} · Fator R {r.FatorR * 100:0.00}% · vencimento {r.Vencimento:dd/MM/yyyy}");
        var tributos = new TabelaRelatorio("Repartição do DAS por tributo", [new("Tributo"), new("Valor", TipoColuna.Valor)],
            r.TotalPorTributo.OrderBy(t => t.Key).Select(t => L(false, t.Key, t.Value)).Append(L(true, "TOTAL", r.ValorDas)).ToList());
        var memoria = new TabelaRelatorio("Memória de cálculo", [new("Descrição")], r.Memoria.Concat(r.Avisos.Select(a => "ATENÇÃO: " + a)).Select(m => L(false, m)).ToList());
        return [seg, tributos, memoria];
    }

    public static IReadOnlyList<TabelaRelatorio> Lucro(ResultadoLucro r)
    {
        var real = r.Regime == RegimeLucro.Real;
        var linhas = new List<LinhaRelatorio>();
        void Add(string d, decimal v, bool destaque = false) => linhas.Add(L(destaque, d, v));
        Add("Receita bruta do trimestre", r.ReceitaBruta);
        if (real)
        {
            Add("Lucro contábil antes do IRPJ e da CSLL", r.LucroContabil ?? 0);
            Add("(+) Adições IRPJ (LALUR)", r.AdicoesIrpj);
            Add("(−) Exclusões IRPJ (LALUR)", r.ExclusoesIrpj);
            Add("(−) Compensação de prejuízos fiscais (limite 30%)", r.CompensacaoIrpj);
        }
        Add(real ? "Lucro real (base do IRPJ)" : "Base de cálculo do IRPJ (presunção + outras receitas)", r.BaseIrpj, true);
        Add("IRPJ 15%", r.IrpjNormal);
        Add("Adicional de IRPJ 10%", r.IrpjAdicional);
        Add("(−) IRRF e deduções", r.IrpjDeducoes);
        Add("IRPJ a pagar", r.IrpjAPagar, true);
        if (real)
        {
            Add("(+) Adições CSLL (LACS)", r.AdicoesCsll);
            Add("(−) Exclusões CSLL (LACS)", r.ExclusoesCsll);
            Add("(−) Compensação de base negativa (limite 30%)", r.CompensacaoCsll);
        }
        Add("Base de cálculo da CSLL", r.BaseCsll, true);
        Add("CSLL 9%", r.CsllDevida);
        Add("(−) CSLL retida", r.CsllDeducoes);
        Add("CSLL a pagar", r.CsllAPagar, true);
        if (r.PrejuizoIrpj > 0) Add("Prejuízo fiscal do trimestre (Parte B)", r.PrejuizoIrpj);
        if (r.PrejuizoCsll > 0) Add("Base negativa da CSLL do trimestre (Parte B)", r.PrejuizoCsll);
        var titulo = $"{(real ? "Lucro Real" : "Lucro Presumido")} · {r.Trimestre}º trimestre de {r.Ano}";
        return [new TabelaRelatorio(titulo, [new("Descrição"), new("Valor", TipoColuna.Valor)], linhas, $"Vencimento (cota única) {r.Vencimento:dd/MM/yyyy}"),
                new TabelaRelatorio("Memória de cálculo", [new("Descrição")], r.Memoria.Concat(r.Avisos.Select(a => "ATENÇÃO: " + a)).Select(m => L(false, m)).ToList())];
    }

    public static IReadOnlyList<TabelaRelatorio> PisCofins(ResultadoPisCofins r)
    {
        var linhas = new List<LinhaRelatorio>
        {
            L(false, "Receita da base de cálculo", r.ReceitaBase, null), L(false, "Alíquota (%)", r.AliquotaPis, r.AliquotaCofins),
            L(false, "Débito", r.PisDebito, r.CofinsDebito), L(false, "(−) Créditos", r.PisCredito, r.CofinsCredito), L(false, "(−) Retenções na fonte", r.PisRetido, r.CofinsRetido),
            L(true, "A pagar", r.PisAPagar, r.CofinsAPagar), L(false, "Saldo credor a transportar", r.SaldoCredorPis, r.SaldoCredorCofins),
        };
        return [new TabelaRelatorio($"PIS e COFINS {(r.Regime == RegimePisCofins.Cumulativo ? "cumulativos" : "não cumulativos")} · {r.Competencia:MM/yyyy}",
                    [new("Descrição"), new("PIS", TipoColuna.Valor), new("COFINS", TipoColuna.Valor)], linhas, $"Base dos créditos {Formato.Moeda(r.BaseCreditos)} · vencimento {r.Vencimento:dd/MM/yyyy}"),
                new TabelaRelatorio("Memória de cálculo", [new("Descrição")], r.Memoria.Concat(r.Avisos.Select(a => "ATENÇÃO: " + a)).Select(m => L(false, m)).ToList())];
    }

    public static TabelaRelatorio Guias(IReadOnlyList<Guia> guias)
    {
        var rows = guias.Select(g => L(false, g.Vencimento, g.Competencia.ToString("MM/yyyy"), g.RotuloDocumento, g.Descricao, g.CodigoReceita ?? "", g.Valor, g.Paga ? "Paga" : "A pagar",
            g.DataPagamento, g.Observacao ?? "")).ToList();
        rows.Add(L(true, null, "", "", "TOTAL A PAGAR", "", guias.Where(g => !g.Paga).Sum(g => g.Valor), "", null, ""));
        return new TabelaRelatorio("Guias de recolhimento a pagar",
            [new("Vencimento", TipoColuna.Data), new("Compet.", LarguraCm: 1.8), new("Doc.", LarguraCm: 1.6), new("Descrição"), new("Cód. receita", LarguraCm: 2), new("Valor", TipoColuna.Valor),
             new("Situação", LarguraCm: 1.8), new("Pago em", TipoColuna.Data), new("Observação")], rows,
            "DAS vence no dia 20 (prorroga); DARF federal antecipa para o dia útil anterior; GPS/DCTFWeb é informativo; o ISS segue a legislação do município.");
    }

    public static TabelaRelatorio Retencoes(IReadOnlyList<Retencao> retencoes)
    {
        var rows = retencoes.Select(r => L(false, r.Competencia.ToString("MM/yyyy"), r.Sentido == SentidoRetencao.Sofrida ? "Sofrida" : "Efetuada", r.Tributo.ToString(), r.DocumentoNumero ?? "",
            r.ParticipanteNome ?? "", r.BaseCalculo, r.Aliquota, r.Valor, r.CodigoReceita ?? "", r.Origem == "NOTA" ? "Nota" : "Manual", r.Descricao ?? "")).ToList();
        rows.Add(L(true, "", "", "", "", "TOTAL", null, null, retencoes.Sum(r => r.Valor), "", "", ""));
        return new TabelaRelatorio("Retenções (IRRF, PIS, COFINS, CSLL, INSS, ISS)",
            [new("Compet.", LarguraCm: 1.8), new("Sentido", LarguraCm: 1.9), new("Tributo", LarguraCm: 1.8), new("Nota", LarguraCm: 1.5), new("Participante"), new("Base", TipoColuna.Valor),
             new("Alíq. %", TipoColuna.Valor, 1.6), new("Valor", TipoColuna.Valor), new("Cód. receita", LarguraCm: 2), new("Origem", LarguraCm: 1.6), new("Descrição")], rows,
            "Sofrida: retida por clientes (crédito a compensar). Efetuada: retida de fornecedores (a recolher).");
    }

    /// <summary>Agrupamento dos resumos de vendas e de compras.</summary>
    public static IReadOnlyList<string> AgrupamentosResumo { get; } = ["Por nota", "Por cliente/fornecedor", "Por mês"];

    /// <summary>
    /// Resumo de vendas (notas de saída) ou de compras (notas de entrada) do período, por nota, por cliente/fornecedor ou
    /// por mês. Notas canceladas e denegadas ficam de fora.
    /// </summary>
    public static TabelaRelatorio ResumoNotas(IReadOnlyList<DocumentoFiscal> documentos, TipoOperacao operacao, int agrupamento)
    {
        var validas = documentos.Where(d => d.Operacao == operacao && d.Situacao == SituacaoDocumento.Autorizada).OrderBy(d => d.DataLivro).ThenBy(d => d.Numero).ToList();
        var quem = operacao == TipoOperacao.Saida ? "Cliente" : "Fornecedor";
        var titulo = operacao == TipoOperacao.Saida ? "Resumo de vendas" : "Resumo de compras";
        LinhaRelatorio Total(IEnumerable<DocumentoFiscal> ds) => L(true, "TOTAL", "", "", ds.Count(), ds.Sum(d => d.Totais.Total), ds.Sum(d => d.Totais.Icms),
            ds.Sum(d => d.Totais.Ipi), ds.Sum(d => d.Totais.Pis + d.Totais.Cofins), ds.Sum(d => d.Totais.Iss));
        ColunaRelatorio[] valores = [new("Notas", TipoColuna.Inteiro), new("Valor total", TipoColuna.Valor), new("ICMS", TipoColuna.Valor), new("IPI", TipoColuna.Valor),
                                     new("PIS+COFINS", TipoColuna.Valor), new("ISS", TipoColuna.Valor)];
        LinhaRelatorio Grupo(string a, string b, string c, IReadOnlyCollection<DocumentoFiscal> ds) => L(false, a, b, c, ds.Count, ds.Sum(d => d.Totais.Total),
            ds.Sum(d => d.Totais.Icms), ds.Sum(d => d.Totais.Ipi), ds.Sum(d => d.Totais.Pis + d.Totais.Cofins), ds.Sum(d => d.Totais.Iss));
        List<LinhaRelatorio> linhas;
        ColunaRelatorio[] chaves;
        switch (agrupamento)
        {
            case 1:
                chaves = [new(quem), new("CNPJ/CPF", LarguraCm: 3.4), new("", LarguraCm: 0.5)];
                linhas = validas.GroupBy(d => (Nome: d.NomeParticipante ?? "(sem identificação)", Doc: d.DocumentoParticipante ?? ""))
                                .OrderByDescending(g => g.Sum(d => d.Totais.Total))
                                .Select(g => Grupo(g.Key.Nome, Formatar(g.Key.Doc), "", g.ToList())).ToList();
                break;
            case 2:
                chaves = [new("Mês", LarguraCm: 2), new("", LarguraCm: 0.5), new("", LarguraCm: 0.5)];
                linhas = validas.GroupBy(d => new DateOnly(d.DataLivro.Year, d.DataLivro.Month, 1)).OrderBy(g => g.Key)
                                .Select(g => Grupo(g.Key.ToString("MM/yyyy"), "", "", g.ToList())).ToList();
                break;
            default:
                chaves = [new("Data", TipoColuna.Data), new("Nota", LarguraCm: 2.4), new(quem)];
                linhas = validas.Select(d => Grupo(d.DataLivro.ToString("dd/MM/yyyy"), $"{d.Modelo.Codigo()} {d.Numero}", d.NomeParticipante ?? "", [d])).ToList();
                break;
        }
        linhas.Add(Total(validas));
        return new TabelaRelatorio(titulo, [.. chaves, .. valores], linhas);
    }

    /// <summary>Recebimentos (ou pagamentos) do período: as baixas das duplicatas, com acréscimos, descontos e a conta movimentada.</summary>
    public static TabelaRelatorio Baixas(IReadOnlyList<(Duplicata Duplicata, BaixaDuplicata Baixa)> baixas, TipoDuplicata tipo)
    {
        var rows = baixas.OrderBy(b => b.Baixa.Data).ThenBy(b => b.Duplicata.ParticipanteNome)
            .Select(b => L(false, b.Baixa.Data, b.Duplicata.ParticipanteNome ?? "", $"{b.Duplicata.NumeroDocumento}/{b.Duplicata.Parcela}", b.Duplicata.Vencimento,
                           b.Baixa.Principal, b.Baixa.Juros + b.Baixa.Multa, b.Baixa.Desconto, b.Baixa.Liquido, b.Baixa.ContaDisponivel ?? "")).ToList();
        rows.Add(L(true, null, "TOTAL", $"{baixas.Count} baixa(s)", null, baixas.Sum(b => b.Baixa.Principal), baixas.Sum(b => b.Baixa.Juros + b.Baixa.Multa),
                   baixas.Sum(b => b.Baixa.Desconto), baixas.Sum(b => b.Baixa.Liquido), ""));
        var recebe = tipo == TipoDuplicata.Receber;
        return new TabelaRelatorio(recebe ? "Relatório de recebimentos" : "Relatório de pagamentos",
        [
            new("Data", TipoColuna.Data), new(recebe ? "Cliente" : "Fornecedor"), new("Documento/parcela", LarguraCm: 3), new("Vencimento", TipoColuna.Data),
            new("Principal", TipoColuna.Valor), new("Juros e multa", TipoColuna.Valor), new("Desconto", TipoColuna.Valor),
            new(recebe ? "Recebido" : "Pago", TipoColuna.Valor), new("Conta", LarguraCm: 2.6),
        ], rows);
    }

    private static string Formatar(string doc) => doc.Length switch
    {
        14 => Domain.Cadastros.Cnpj.Formatar(doc),
        11 => Domain.Cadastros.Cpf.Formatar(doc),
        _ => doc,
    };
}
