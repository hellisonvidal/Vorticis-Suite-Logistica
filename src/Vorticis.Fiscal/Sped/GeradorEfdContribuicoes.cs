using Vorticis.Fiscal.Documentos;

namespace Vorticis.Fiscal.Sped;

/// <summary>PIS e COFINS de um item como a EFD-Contribuições o informa (CST, base, alíquota e valor), de acordo com a apuração salva.</summary>
public sealed record ItemPisCofinsEfd(string CstPis, decimal Base, decimal AliqPis, decimal Pis, string CstCofins, decimal AliqCofins, decimal Cofins);

/// <summary>Totais da apuração do bloco M calculados das mesmas regras do módulo (para conferência e para a tela).</summary>
public sealed record ResumoContribuicoes(decimal ReceitaNotas, decimal BaseCreditoNotas, decimal PisDebitoNotas, decimal CofinsDebitoNotas, decimal PisCreditoNotas, decimal CofinsCreditoNotas);

/// <summary>Regras de PIS/COFINS por item para a EFD-Contribuições, alinhadas à apuração existente (fiscal.apuracao_pis_cofins): receita e crédito pelo CFOP.</summary>
public static class CalculoEfdContribuicoes
{
    private static decimal Arred(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    /// <summary>Base de cálculo do item: valor contábil sem IPI e sem ICMS-ST (a mesma da apuração).</summary>
    public static decimal Base(ItemEfd i) => i.ValorContabil - i.Ipi - i.IcmsSt;

    public static ItemPisCofinsEfd Item(DadosEfdContribuicoes d, DocumentoEfd doc, ItemEfd i)
    {
        var ap = d.Apuracao;
        var cfop = doc.Nfse ? Cfop.Servico : i.CfopNumerico;
        var aliqPis = ap?.AliquotaPis ?? i.AliqPis;
        var aliqCofins = ap?.AliquotaCofins ?? i.AliqCofins;
        var b = Base(i);
        if (!doc.Entrada)
        {
            if (Cfop.GeraReceita(cfop))
                return new ItemPisCofinsEfd("01", b, aliqPis, Arred(b * aliqPis / 100m), "01", aliqCofins, Arred(b * aliqCofins / 100m));
            return new ItemPisCofinsEfd("49", 0, 0, 0, "49", 0, 0);
        }
        if (ap is { Cumulativo: false } && Cfop.GeraCreditoPisCofins(cfop))
            return new ItemPisCofinsEfd("50", b, aliqPis, Arred(b * aliqPis / 100m), "50", aliqCofins, Arred(b * aliqCofins / 100m));
        return new ItemPisCofinsEfd("98", 0, 0, 0, "98", 0, 0);
    }

    public static ResumoContribuicoes Resumo(DadosEfdContribuicoes d)
    {
        decimal receita = 0, baseCred = 0, pisD = 0, cofD = 0, pisC = 0, cofC = 0;
        foreach (var doc in d.Documentos.Where(x => x.Autorizada))
            foreach (var i in doc.Itens)
            {
                var p = Item(d, doc, i);
                if (doc.Entrada) { if (p.CstPis == "50") { baseCred += p.Base; pisC += p.Pis; cofC += p.Cofins; } }
                else if (p.CstPis == "01") { receita += p.Base; pisD += p.Pis; cofD += p.Cofins; }
            }
        return new ResumoContribuicoes(receita, baseCred, pisD, cofD, pisC, cofC);
    }
}

/// <summary>
/// Gerador da EFD-Contribuições (leiaute 006): bloco 0 (0000, 0001, 0100, 0110, 0140, 0150, 0190, 0200), A (A100/A170 das NFS-e), C (C010, C100/C170 das NF-e),
/// D, F, I e P sem dados, M (M100/M105, M200, M210 e M500/M505, M600, M610 do PIS e da COFINS a partir da apuração salva), 1 e 9. Ver docs/08-efd.md.
/// </summary>
public static class GeradorEfdContribuicoes
{
    public static ArquivoGeradoEfd Gerar(DadosEfdContribuicoes d)
    {
        var o = d.Opcoes;
        var e = d.Empresa;
        var ap = d.Apuracao;
        var docs = d.Documentos.OrderBy(x => x.EntradaSaida).ThenBy(x => x.Entrada ? 0 : 1).ThenBy(x => x.Modelo).ThenBy(x => x.Serie)
            .ThenBy(x => long.TryParse(x.Numero, out var n) ? n : 0).ThenBy(x => x.Id).ToList();
        var servicos = docs.Where(x => x.Nfse).ToList();
        var mercadorias = docs.Where(x => !x.Nfse).ToList();
        var ativos = docs.Where(x => x.Autorizada).ToList();
        var w = new EscritorEfd();

        // ------------------------------------------------------------------ bloco 0
        w.Abrir('0', true);
        w.Reg("0000", o.CodigoVersao is { Length: > 0 } v ? v : "006", o.Retificadora ? "1" : "0", "", o.Retificadora ? CamposEfd.Digitos(o.NumeroReciboAnterior) : "",
              CamposEfd.D(d.Inicio), CamposEfd.D(d.Fim), CamposEfd.T(e.Nome, 100), e.Cnpj, e.Uf, e.CodMunicipio, "", o.IndicadorNaturezaPj, o.IndicadorAtividade.ToString());
        if (d.Contador is { } c)
            w.Reg("0100", CamposEfd.T(c.Nome, 100), CamposEfd.Digitos(c.Cpf), CamposEfd.T(c.Crc, 15), CamposEfd.Digitos(c.CnpjEscritorio), "", "", "", "", "",
                  CamposEfd.Fone(c.Telefone), "", CamposEfd.T(c.Email, 255), e.CodMunicipio);
        if (ap is not null)
        {
            var cum = ap.Cumulativo;
            w.Reg("0110", cum ? "2" : "1", cum ? "" : "1", cum ? "" : "1", cum ? (e.RegimeCaixa ? "1" : "2") : "");
        }
        w.Reg("0140", "01", CamposEfd.T(e.Nome, 100), e.Cnpj, e.Uf, CamposEfd.Digitos(e.InscricaoEstadual), e.CodMunicipio, CamposEfd.Digitos(e.InscricaoMunicipal), "");
        GeradorEfdIcms.EscreverCadastros(w, ativos.Select(x => x.Participante), ativos.SelectMany(x => x.Itens), comCest: false);
        w.Fechar();

        // ------------------------------------------------------------------ bloco A: serviços (NFS-e)
        w.Abrir('A', servicos.Count > 0);
        if (servicos.Count > 0)
        {
            w.Reg("A010", e.Cnpj);
            foreach (var doc in servicos) EscreverA100(w, d, doc);
        }
        w.Fechar();

        // ------------------------------------------------------------------ bloco C: mercadorias (NF-e e NFC-e)
        w.Abrir('C', mercadorias.Count > 0);
        if (mercadorias.Count > 0)
        {
            w.Reg("C010", e.Cnpj, "2");
            foreach (var doc in mercadorias)
            {
                var itens = doc.Autorizada ? doc.Itens.Select(i => (Item: i, Pc: CalculoEfdContribuicoes.Item(d, doc, i))).ToList() : [];
                GeradorEfdIcms.EscreverC100(w, e.Cnpj, doc, itens.Sum(x => x.Pc.Pis), itens.Sum(x => x.Pc.Cofins));
                foreach (var (i, pc) in itens) EscreverC170(w, i, pc);
            }
        }
        w.Fechar();

        w.Abrir('D', false); w.Fechar();
        w.Abrir('F', false); w.Fechar();
        w.Abrir('I', false); w.Fechar();

        // ------------------------------------------------------------------ bloco M: apuração da contribuição (PIS e COFINS)
        w.Abrir('M', ap is not null);
        if (ap is not null)
        {
            EscreverContribuicao(w, ap, pis: true);
            EscreverContribuicao(w, ap, pis: false);
        }
        w.Fechar();

        w.Abrir('P', false); w.Fechar();
        w.Abrir('1', false); w.Fechar();

        return w.Concluir();
    }

    private static void EscreverA100(EscritorEfd w, DadosEfdContribuicoes d, DocumentoEfd doc)
    {
        var codSit = doc.Situacao switch { "CANCELADA" => "02", "DENEGADA" => "04", _ => "00" };
        var oper = doc.Entrada ? "0" : "1";
        var emit = doc.Entrada ? "1" : "0";
        var ser = CamposEfd.T(CamposEfd.Digitos(doc.Serie), 20);
        var num = CamposEfd.Digitos(doc.Numero);
        if (!doc.Autorizada)
        {
            w.Reg("A100", oper, emit, "", codSit, ser, "", num, "", "", "", "", "", "", "", "", "", "", "", "", "");
            return;
        }
        var itens = doc.Itens.Select(i => (Item: i, Pc: CalculoEfdContribuicoes.Item(d, doc, i))).ToList();
        w.Reg("A100", oper, emit, doc.Participante?.Codigo, codSit, ser, "", num, CamposEfd.T(doc.Chave, 60), CamposEfd.D(doc.Emissao), CamposEfd.D(doc.Emissao),
              CamposEfd.V(doc.Total), "2", CamposEfd.V(doc.Desconto), CamposEfd.V(itens.Sum(x => x.Pc.Base)), CamposEfd.V(itens.Sum(x => x.Pc.Pis)),
              CamposEfd.V(itens.Sum(x => x.Pc.Base)), CamposEfd.V(itens.Sum(x => x.Pc.Cofins)), CamposEfd.V(doc.RetPis), CamposEfd.V(doc.RetCofins), CamposEfd.V(doc.Iss));
        foreach (var (i, pc) in itens)
        {
            var credito = doc.Entrada && pc.CstPis == "50";
            w.Reg("A170", i.NumeroItem.ToString(), CamposEfd.T(i.CodigoItem, 60), "", CamposEfd.V(i.ValorProduto), CamposEfd.V(i.Desconto), credito ? "03" : "", credito ? "0" : "",
                  pc.CstPis, CamposEfd.V(pc.Base), CamposEfd.Aliq(pc.AliqPis), CamposEfd.V(pc.Pis), pc.CstCofins, CamposEfd.V(pc.Base), CamposEfd.Aliq(pc.AliqCofins),
                  CamposEfd.V(pc.Cofins), "", "");
        }
    }

    private static void EscreverC170(EscritorEfd w, ItemEfd i, ItemPisCofinsEfd pc)
    {
        var temIpi = !string.IsNullOrWhiteSpace(i.CstIpi);
        w.Reg("C170", i.NumeroItem.ToString(), CamposEfd.T(i.CodigoItem, 60), "", CamposEfd.Qtd(i.Quantidade), CamposEfd.Unidade(i.Unidade), CamposEfd.V(i.ValorProduto),
              CamposEfd.V(i.Desconto), "0", CamposEfd.CstIcms(i.CstIcms, i.Origem), i.CfopNumerico, "", CamposEfd.V(i.BcIcms), CamposEfd.Aliq(i.AliqIcms), CamposEfd.V(i.Icms),
              CamposEfd.V(i.BcIcmsSt), i.IcmsSt != 0 ? CamposEfd.Aliq(0) : "", CamposEfd.V(i.IcmsSt), temIpi ? "0" : "", temIpi ? CamposEfd.Cst2(i.CstIpi, "99") : "",
              temIpi ? "999" : "", temIpi ? CamposEfd.V(i.BcIpi) : "", temIpi ? CamposEfd.Aliq(i.AliqIpi) : "", temIpi ? CamposEfd.V(i.Ipi) : "",
              pc.CstPis, CamposEfd.V(pc.Base), CamposEfd.Aliq(pc.AliqPis), "", "", CamposEfd.V(pc.Pis),
              pc.CstCofins, CamposEfd.V(pc.Base), CamposEfd.Aliq(pc.AliqCofins), "", "", CamposEfd.V(pc.Cofins), "");
    }

    /// <summary>
    /// Valores do bloco M de uma contribuição. PIS: M100/M105 (crédito), M200 (apuração) e M210 (detalhamento); COFINS: M500/M505, M600 e M610.
    /// A apuração é a salva em fiscal.apuracao_pis_cofins: crédito descontado até o limite da contribuição do mês, retenções abatidas.
    /// </summary>
    public static ContribuicaoM CalcularM(ApuracaoPisCofinsEfd ap, bool pis)
    {
        var deb = pis ? ap.PisDebito : ap.CofinsDebito;
        var cred = ap.Cumulativo ? 0m : pis ? ap.PisCredito : ap.CofinsCredito;
        return new ContribuicaoM(ap.Cumulativo, deb, cred, Math.Min(cred, deb), pis ? ap.PisRetido : ap.CofinsRetido);
    }

    private static void EscreverContribuicao(EscritorEfd w, ApuracaoPisCofinsEfd ap, bool pis)
    {
        var m = CalcularM(ap, pis);
        var (r100, r105, r200, r210) = pis ? ("M100", "M105", "M200", "M210") : ("M500", "M505", "M600", "M610");
        var aliq = pis ? ap.AliquotaPis : ap.AliquotaCofins;
        if (m.Credito > 0)
        {
            w.Reg(r100, "101", "0", CamposEfd.V(ap.BaseCredito), CamposEfd.Aliq(aliq), "", "", CamposEfd.V(m.Credito), "0,00", "0,00", "0,00", CamposEfd.V(m.Credito),
                  m.CreditoDescontado >= m.Credito ? "0" : "1", CamposEfd.V(m.CreditoDescontado), CamposEfd.V(m.Credito - m.CreditoDescontado));
            w.Reg(r105, "01", "50", CamposEfd.V(ap.BaseCredito), "0,00", CamposEfd.V(ap.BaseCredito), CamposEfd.V(ap.BaseCredito), "", "", "");
        }
        w.Reg(r200, CamposEfd.V(m.NaoCumulativaPeriodo), CamposEfd.V(m.CreditoDescontado), "0,00", CamposEfd.V(m.NaoCumulativaDevida), CamposEfd.V(m.RetidoNaoCumulativa),
              "0,00", CamposEfd.V(m.NaoCumulativaARecolher), CamposEfd.V(m.CumulativaPeriodo), CamposEfd.V(m.RetidoCumulativa), "0,00", CamposEfd.V(m.CumulativaARecolher),
              CamposEfd.V(m.TotalARecolher));
        w.Reg(r210, ap.Cumulativo ? "51" : "01", CamposEfd.V(ap.ReceitaBase), CamposEfd.V(ap.ReceitaBase), CamposEfd.Aliq(aliq), "", "", CamposEfd.V(m.Debito), "0,00", "0,00",
              "0,00", "0,00", CamposEfd.V(m.Debito));
    }
}

/// <summary>Valores de uma contribuição (PIS ou COFINS) no bloco M, separados em não cumulativa e cumulativa; <see cref="TotalARecolher"/> é o M200.VL_TOT_CONT_REC.</summary>
public sealed record ContribuicaoM(bool Cumulativo, decimal Debito, decimal Credito, decimal CreditoDescontado, decimal Retido)
{
    public decimal NaoCumulativaPeriodo => Cumulativo ? 0 : Debito;
    public decimal NaoCumulativaDevida => Cumulativo ? 0 : Debito - CreditoDescontado;
    public decimal RetidoNaoCumulativa => Cumulativo ? 0 : Retido;
    public decimal NaoCumulativaARecolher => Cumulativo ? 0 : Math.Max(0, NaoCumulativaDevida - Retido);
    public decimal CumulativaPeriodo => Cumulativo ? Debito : 0;
    public decimal RetidoCumulativa => Cumulativo ? Retido : 0;
    public decimal CumulativaARecolher => Cumulativo ? Math.Max(0, Debito - Retido) : 0;
    public decimal TotalARecolher => NaoCumulativaARecolher + CumulativaARecolher;
}
