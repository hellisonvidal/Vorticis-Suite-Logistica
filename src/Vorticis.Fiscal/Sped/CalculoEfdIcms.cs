namespace Vorticis.Fiscal.Sped;

/// <summary>Linha do registro analítico C190: agrupamento do documento por CST, CFOP e alíquota do ICMS.</summary>
public sealed record AnaliticoC190(string Cst, string Cfop, decimal Aliquota, decimal Operacao, decimal BaseIcms, decimal Icms, decimal BaseSt, decimal IcmsSt,
                                   decimal Reducao, decimal Ipi);

/// <summary>Apuração do ICMS-ST de uma UF (E210): retenção nas saídas e o que recolher.</summary>
public sealed record ApuracaoStUf(string Uf, decimal Retencao, decimal Recolher, decimal SaldoCredorTransportar);

/// <summary>Linha do E510 (IPI por CFOP e CST).</summary>
public sealed record LinhaIpi(string Cfop, string Cst, decimal Contabil, decimal Base, decimal Ipi);

/// <summary>Apuração do IPI (E520).</summary>
public sealed record ApuracaoIpiEfd(IReadOnlyList<LinhaIpi> Linhas, decimal Debitos, decimal Creditos, decimal SaldoDevedor, decimal SaldoCredor);

/// <summary>Valores de um CFOP na apuração (livro de apuração): valor contábil, base de cálculo e imposto.</summary>
public sealed record LinhaApuracaoCfop(string Cfop, decimal Operacao, decimal Base, decimal Imposto);

/// <summary>Apuração do ICMS (E110) e, quando houver, do ICMS-ST (E210) e do IPI (E520), calculada dos registros analíticos C190.</summary>
public sealed record ApuracaoEfdIcms(decimal Debitos, decimal Creditos, decimal SaldoCredorAnterior, decimal SaldoApurado, decimal Deducoes, decimal Recolher,
                                     decimal SaldoCredorTransportar, IReadOnlyList<ApuracaoStUf> St, ApuracaoIpiEfd? Ipi,
                                     IReadOnlyList<LinhaApuracaoCfop>? DebitosCfop = null, IReadOnlyList<LinhaApuracaoCfop>? CreditosCfop = null,
                                     decimal AjustesDebitos = 0, decimal EstornosCreditos = 0, decimal AjustesCreditos = 0, decimal EstornosDebitos = 0,
                                     decimal DebitosEspeciais = 0)
{
    /// <summary>Total do E116 (obrigações do ICMS a recolher): o ICMS a recolher mais os débitos especiais.</summary>
    public decimal TotalObrigacoes => Recolher + DebitosEspeciais;
}

/// <summary>Cálculo da EFD ICMS/IPI: registros analíticos por documento e apuração do período. Puro e determinístico.</summary>
public static class CalculoEfdIcms
{
    /// <summary>Documentos que entram na escrituração do ICMS/IPI: notas de mercadoria (55 e 65); a NFS-e é do ISS e fica fora.</summary>
    public static IEnumerable<DocumentoEfd> Escrituraveis(IEnumerable<DocumentoEfd> docs) => docs.Where(d => !d.Nfse);

    /// <summary>C190 do documento: itens agrupados por CST (origem + CST), CFOP e alíquota; ordenado por CST, CFOP e alíquota.</summary>
    public static IReadOnlyList<AnaliticoC190> Analitico(DocumentoEfd doc) =>
        doc.Itens.GroupBy(i => (Cst: CamposEfd.CstIcms(i.CstIcms, i.Origem), Cfop: i.CfopNumerico, Aliq: i.AliqIcms))
            .OrderBy(g => g.Key.Cst, StringComparer.Ordinal).ThenBy(g => g.Key.Cfop, StringComparer.Ordinal).ThenBy(g => g.Key.Aliq)
            .Select(g => new AnaliticoC190(g.Key.Cst, g.Key.Cfop, g.Key.Aliq, g.Sum(i => i.ValorContabil), g.Sum(i => i.BcIcms), g.Sum(i => i.Icms),
                g.Sum(i => i.BcIcmsSt), g.Sum(i => i.IcmsSt), 0m, g.Sum(i => i.Ipi)))
            .ToList();

    /// <summary>CST do IPI na escrituração (E510): 00, 49, 50 ou 99 (as demais da tabela 4.3.2 são reduzidas a elas).</summary>
    public static string CstIpiEscrituracao(string? cst) => CamposEfd.Cst2(cst, "99") switch
    {
        "00" => "00",
        "01" or "02" or "03" or "04" or "05" or "49" => "49",
        "50" => "50",
        _ => "99",
    };

    public static ApuracaoEfdIcms Apurar(DadosEfdIcms d)
    {
        var docs = Escrituraveis(d.Documentos).Where(x => x.Autorizada).ToList();
        decimal debitos = 0, creditos = 0;
        var st = new Dictionary<string, decimal>();
        var deb = new SortedDictionary<string, LinhaApuracaoCfop>(StringComparer.Ordinal);
        var cred = new SortedDictionary<string, LinhaApuracaoCfop>(StringComparer.Ordinal);
        static void Somar(SortedDictionary<string, LinhaApuracaoCfop> d, AnaliticoC190 a) =>
            d[a.Cfop] = d.TryGetValue(a.Cfop, out var x) ? new(a.Cfop, x.Operacao + a.Operacao, x.Base + a.BaseIcms, x.Imposto + a.Icms) : new(a.Cfop, a.Operacao, a.BaseIcms, a.Icms);
        foreach (var doc in docs)
            foreach (var a in Analitico(doc))
            {
                if (CamposEfd.CfopSaida(a.Cfop))
                {
                    debitos += a.Icms;
                    Somar(deb, a);
                    if (a.IcmsSt != 0) st[doc.DestinatarioUf ?? d.Empresa.Uf] = st.GetValueOrDefault(doc.DestinatarioUf ?? d.Empresa.Uf) + a.IcmsSt;
                }
                else if (CamposEfd.CfopEntrada(a.Cfop)) { creditos += a.Icms; Somar(cred, a); }
            }
        // contas de energia, gás, água e comunicação recebidas (C590/D590): crédito do ICMS destacado
        foreach (var c in d.Consumo ?? [])
        {
            creditos += c.Icms;
            Somar(cred, new AnaliticoC190(c.CstIcms, c.Cfop, c.AliqIcms, c.Valor, c.BaseIcms, c.Icms, 0, 0, 0, 0));
        }
        // ajustes da apuração (E111), pelo tipo do código
        decimal Aj(TipoAjusteIcms t) => (d.Ajustes ?? []).Where(a => a.Tipo == t).Sum(a => a.Valor);
        var ajDeb = Aj(TipoAjusteIcms.OutrosDebitos);
        var estCred = Aj(TipoAjusteIcms.EstornoCreditos);
        var ajCred = Aj(TipoAjusteIcms.OutrosCreditos);
        var estDeb = Aj(TipoAjusteIcms.EstornoDebitos);
        var debEsp = Aj(TipoAjusteIcms.DebitosEspeciais);

        var saldoAnterior = d.Opcoes.SaldoCredorAnterior;
        var apurado = debitos + ajDeb + estCred - creditos - ajCred - estDeb - saldoAnterior;
        // as deduções só abatem o saldo devedor apurado (o ICMS a recolher não fica negativo)
        var deducoes = Math.Min(Aj(TipoAjusteIcms.Deducoes), Math.Max(0, apurado));
        var recolher = Math.Max(0, apurado - deducoes);
        var transportar = Math.Max(0, -apurado);

        var stUf = st.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => new ApuracaoStUf(k.Key, k.Value, k.Value, 0)).ToList();
        return new ApuracaoEfdIcms(debitos, creditos, saldoAnterior, apurado, deducoes, recolher, transportar, stUf, ApurarIpi(d, docs),
            [.. deb.Values], [.. cred.Values], ajDeb, estCred, ajCred, estDeb, debEsp);
    }

    private static ApuracaoIpiEfd? ApurarIpi(DadosEfdIcms d, List<DocumentoEfd> docs)
    {
        return d.Opcoes.IndicadorAtividade != '0' ? null : CalcularIpi(docs);
    }

    /// <summary>Apuração do IPI (E510/E520) das notas autorizadas, sem considerar o indicador de atividade; o livro de apuração do IPI usa o mesmo cálculo da EFD.</summary>
    public static ApuracaoIpiEfd? CalcularIpi(IEnumerable<DocumentoEfd> documentos)
    {
        var linhas = Escrituraveis(documentos).Where(x => x.Autorizada).SelectMany(doc => doc.Itens).Where(i => !string.IsNullOrWhiteSpace(i.CstIpi))
            .GroupBy(i => (Cfop: i.CfopNumerico, Cst: CstIpiEscrituracao(i.CstIpi)))
            .OrderBy(g => g.Key.Cfop, StringComparer.Ordinal).ThenBy(g => g.Key.Cst, StringComparer.Ordinal)
            .Select(g => new LinhaIpi(g.Key.Cfop, g.Key.Cst, g.Sum(i => i.ValorContabil), g.Sum(i => i.BcIpi), g.Sum(i => i.Ipi))).ToList();
        if (linhas.Count == 0) return null;
        var deb = linhas.Where(l => CamposEfd.CfopSaida(l.Cfop)).Sum(l => l.Ipi);
        var cred = linhas.Where(l => CamposEfd.CfopEntrada(l.Cfop)).Sum(l => l.Ipi);
        var saldo = deb - cred;
        return new ApuracaoIpiEfd(linhas, deb, cred, Math.Max(0, saldo), Math.Max(0, -saldo));
    }
}
