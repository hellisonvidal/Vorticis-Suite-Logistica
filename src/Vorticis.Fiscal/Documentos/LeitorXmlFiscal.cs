using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Vorticis.Domain.Cadastros;

namespace Vorticis.Fiscal.Documentos;

/// <summary>
/// Lê o XML de NF-e/NFC-e (modelos 55 e 65: nfeProc ou NFe) e da NFS-e padrão nacional (ADN: elemento NFSe), além dos eventos de
/// cancelamento. Os nomes de elemento são comparados sem o namespace, e o XML é lido sem DTD (proteção contra entidades externas).
/// Pura: não acessa banco nem arquivos.
/// </summary>
public static class LeitorXmlFiscal
{
    private static readonly Dictionary<string, string> IbgeUf = new()
    {
        ["11"] = "RO", ["12"] = "AC", ["13"] = "AM", ["14"] = "RR", ["15"] = "PA", ["16"] = "AP", ["17"] = "TO", ["21"] = "MA",
        ["22"] = "PI", ["23"] = "CE", ["24"] = "RN", ["25"] = "PB", ["26"] = "PE", ["27"] = "AL", ["28"] = "SE", ["29"] = "BA",
        ["31"] = "MG", ["32"] = "ES", ["33"] = "RJ", ["35"] = "SP", ["41"] = "PR", ["42"] = "SC", ["43"] = "RS", ["50"] = "MS",
        ["51"] = "MT", ["52"] = "GO", ["53"] = "DF",
    };

    public static LeituraXml Ler(byte[] xml)
    {
        XElement raiz;
        try
        {
            var configuracao = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var ms = new MemoryStream(xml);
            using var leitor = XmlReader.Create(ms, configuracao);
            raiz = XDocument.Load(leitor).Root ?? throw new XmlException("XML vazio.");
        }
        catch (XmlException ex)
        {
            return new LeituraInvalida($"XML inválido: {ex.Message}");
        }

        try
        {
            return raiz.Name.LocalName switch
            {
                "nfeProc" or "NFe" => LerNfe(raiz, xml),
                "NFSe" => LerNfse(raiz, xml),
                _ => LerEvento(raiz),
            };
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or OverflowException)
        {
            return new LeituraInvalida($"Não foi possível interpretar o documento: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ eventos

    private static LeituraXml LerEvento(XElement raiz)
    {
        var tipo = Valor(raiz, "tpEvento");
        var chave = Valor(raiz, "chNFe") ?? Valor(raiz, "chNFSe");
        var cancelamento = tipo == "110111" || Desc(raiz, "e101101") is not null;
        if (chave is not null && cancelamento) return new LeituraCancelamento(chave.Trim());
        if (chave is not null) return new LeituraInvalida($"Evento {tipo ?? "desconhecido"} ignorado (só cancelamento altera o documento).");
        return new LeituraInvalida($"Arquivo não reconhecido como NF-e, NFC-e ou NFS-e nacional (elemento raiz \"{raiz.Name.LocalName}\").");
    }

    // ------------------------------------------------------------------ NF-e / NFC-e

    private static LeituraXml LerNfe(XElement raiz, byte[] xml)
    {
        var inf = Desc(raiz, "infNFe");
        if (inf is null) return new LeituraInvalida("NF-e sem o grupo infNFe.");
        var ide = Filho(inf, "ide");
        var chave = (inf.Attribute("Id")?.Value ?? Valor(raiz, "chNFe") ?? "").Replace("NFe", "").Trim();
        if (chave.Length != 44 || !chave.All(char.IsAsciiDigit)) return new LeituraInvalida("Chave de acesso inválida ou ausente (44 dígitos).");
        var modelo = Valor(ide, "mod") == "65" ? ModeloDocumento.NFCe65 : ModeloDocumento.NFe55;
        if (Valor(ide, "mod") is { } m && m != "55" && m != "65") return new LeituraInvalida($"Modelo {m} não suportado (somente 55 e 65).");

        var emit = Filho(inf, "emit");
        var dest = Filho(inf, "dest");
        var totais = Filho(Filho(inf, "total"), "ICMSTot");
        var ret = Filho(Filho(inf, "total"), "retTrib");

        var itens = inf.Elements().Where(e => e.Name.LocalName == "det").Select(LerItemNfe).ToList();
        if (itens.Count == 0) return new LeituraInvalida("NF-e sem itens (det).");

        var protocolo = Valor(Desc(raiz, "protNFe"), "cStat");
        var situacao = protocolo switch
        {
            "101" or "135" or "151" or "155" => SituacaoDocumento.Cancelada,
            "110" or "301" or "302" or "303" => SituacaoDocumento.Denegada,
            _ => SituacaoDocumento.Autorizada,
        };

        var emissao = Data(Valor(ide, "dhEmi") ?? Valor(ide, "dEmi")) ?? throw new FormatException("Data de emissão ausente.");
        var vNf = Num(Valor(totais, "vNF"));
        var total = new TotaisDocumento(
            Num(Valor(totais, "vProd")), Num(Valor(totais, "vDesc")), Num(Valor(totais, "vFrete")), Num(Valor(totais, "vSeg")),
            Num(Valor(totais, "vOutro")), vNf,
            Num(Valor(totais, "vBC")), Num(Valor(totais, "vICMS")), Num(Valor(totais, "vBCST")), Num(Valor(totais, "vST")),
            Num(Valor(totais, "vIPI")), Num(Valor(totais, "vPIS")), Num(Valor(totais, "vCOFINS")), 0m, false,
            Num(Valor(ret, "vRetPIS")), Num(Valor(ret, "vRetCOFINS")), Num(Valor(ret, "vRetCSLL")), Num(Valor(ret, "vIRRF")), Num(Valor(ret, "vRetPrev")));

        return new LeituraDocumento(new DocumentoLido(
            modelo, chave, (Valor(ide, "nNF") ?? "").Trim(), (Valor(ide, "serie") ?? "").Trim(), emissao,
            Data(Valor(ide, "dhSaiEnt") ?? Valor(ide, "dSaiEnt")), Valor(ide, "natOp"), situacao,
            int.TryParse(Valor(ide, "tpNF"), out var tp) ? tp : null,
            Parte(emit, "enderEmit"), dest is null ? null : Parte(dest, "enderDest"), total, itens, xml, LerCobranca(inf, emissao)));
    }

    /// <summary>Parcelas da cobrança (&lt;cobr&gt;&lt;dup&gt;: nDup, dVenc, vDup); vazio quando a nota não traz cobrança.</summary>
    private static IReadOnlyList<Vorticis.Fiscal.Duplicatas.ParcelaDuplicata> LerCobranca(XElement inf, DateOnly emissao) =>
        inf.Elements().Where(e => e.Name.LocalName == "cobr").SelectMany(c => c.Elements().Where(e => e.Name.LocalName == "dup"))
           .Select(d => new Vorticis.Fiscal.Duplicatas.ParcelaDuplicata((Valor(d, "nDup") ?? "").Trim(), Data(Valor(d, "dVenc")) ?? emissao, Num(Valor(d, "vDup"))))
           .Where(p => p.Valor > 0).ToList();

    private static ParteDocumento Parte(XElement? e, string endereco)
    {
        var end = Filho(e, endereco);
        var doc = Cnpj.SomenteDigitos(Valor(e, "CNPJ") ?? Valor(e, "CPF"));
        var cMun = Valor(end, "cMun");
        return new ParteDocumento(doc.Length == 0 ? null : doc, Valor(e, "xNome"), Valor(end, "UF") ?? UfDe(cMun), cMun, Valor(end, "xMun"),
                                  Valor(e, "IE"));
    }

    private static ItemFiscal LerItemNfe(XElement det)
    {
        var prod = Filho(det, "prod");
        var imposto = Filho(det, "imposto");
        var icms = Filho(Filho(imposto, "ICMS"), null);          // ICMS00, ICMS10, ICMSSN101...
        var ipi = Filho(Filho(imposto, "IPI"), "IPITrib") ?? Filho(Filho(imposto, "IPI"), "IPINT");
        var pis = Filho(Filho(imposto, "PIS"), null);
        var cofins = Filho(Filho(imposto, "COFINS"), null);
        var issqn = Filho(imposto, "ISSQN");

        var vProd = Num(Valor(prod, "vProd"));
        var desc = Num(Valor(prod, "vDesc"));
        var frete = Num(Valor(prod, "vFrete"));
        var seg = Num(Valor(prod, "vSeg"));
        var outras = Num(Valor(prod, "vOutro"));
        var vIpi = Num(Valor(ipi, "vIPI"));
        var vIcmsSt = Num(Valor(icms, "vICMSST"));
        var deson = Num(Valor(icms, "vICMSDeson"));
        // Valor contábil do item = o que compõe o vNF: produto - desconto + frete + seguro + outras + IPI + ICMS-ST (- ICMS desonerado).
        var contabil = vProd - desc + frete + seg + outras + vIpi + vIcmsSt - deson;

        return new ItemFiscal(
            int.TryParse(det.Attribute("nItem")?.Value, out var n) ? n : 0, Valor(prod, "cProd"), Valor(prod, "xProd"), Valor(prod, "NCM"),
            Valor(prod, "CEST"), (Valor(prod, "CFOP") ?? "").Trim(), Valor(prod, "uCom"), Num(Valor(prod, "qCom"), 4), Num(Valor(prod, "vUnCom"), 10),
            vProd, desc, frete, seg, outras, Math.Round(contabil, 2),
            Valor(icms, "CST") ?? Valor(icms, "CSOSN"), Valor(icms, "orig"), Num(Valor(icms, "vBC")), Num(Valor(icms, "pICMS"), 4), Num(Valor(icms, "vICMS")),
            Num(Valor(icms, "vBCST")), vIcmsSt,
            Valor(ipi, "CST"), Num(Valor(ipi, "vBC")), Num(Valor(ipi, "pIPI"), 4), vIpi,
            Valor(pis, "CST"), Num(Valor(pis, "vBC")), Num(Valor(pis, "pPIS"), 4), Num(Valor(pis, "vPIS")),
            Valor(cofins, "CST"), Num(Valor(cofins, "vBC")), Num(Valor(cofins, "pCOFINS"), 4), Num(Valor(cofins, "vCOFINS")),
            Num(Valor(issqn, "vBC")), Num(Valor(issqn, "vAliq"), 4), Num(Valor(issqn, "vISSQN")),
            Gtin: Produtos.ValidadorGtin.Normalizar(Valor(prod, "cEAN")) ?? Produtos.ValidadorGtin.Normalizar(Valor(prod, "cEANTrib")));
    }

    // ------------------------------------------------------------------ NFS-e padrão nacional (ADN)

    private static LeituraXml LerNfse(XElement raiz, byte[] xml)
    {
        var inf = Desc(raiz, "infNFSe");
        var dps = Desc(raiz, "infDPS");
        if (inf is null || dps is null) return new LeituraInvalida("NFS-e sem os grupos infNFSe/infDPS (padrão nacional).");
        var chave = (inf.Attribute("Id")?.Value ?? "").Replace("NFS", "").Trim();
        if (chave.Length == 0) return new LeituraInvalida("NFS-e sem identificador (Id).");

        var emit = Filho(inf, "emit");
        var toma = Filho(dps, "toma");
        var valoresNfse = Filho(inf, "valores");
        var valoresDps = Filho(dps, "valores");
        var trib = Filho(valoresDps, "trib");
        var tribMun = Filho(trib, "tribMun");
        var tribFed = Filho(trib, "tribFed");
        var pisCofins = Filho(tribFed, "piscofins");

        var vServ = Num(Valor(Filho(valoresDps, "vServPrest"), "vServ"));
        if (vServ == 0) vServ = Num(Valor(valoresNfse, "vLiq"));
        var desconto = Num(Valor(Filho(valoresDps, "vDescCondIncond"), "vDescIncond"));
        var iss = Num(Valor(valoresNfse, "vISSQN"));
        var bcIss = Num(Valor(valoresNfse, "vBC"));
        var aliqIss = Num(Valor(valoresNfse, "pAliqAplic"), 4);
        if (aliqIss == 0) aliqIss = Num(Valor(tribMun, "pAliq"), 4);
        var issRetido = Valor(tribMun, "tpRetISSQN") is "2" or "3";
        var vPis = Num(Valor(pisCofins, "vPis"));
        var vCofins = Num(Valor(pisCofins, "vCofins"));
        var pisCofinsRetido = Valor(pisCofins, "tpRetPisCofins") == "1";

        var servico = Filho(dps, "serv");
        var cServ = Filho(servico, "cServ");
        var emissao = Data(Valor(dps, "dhEmi") ?? Valor(inf, "dhProc")) ?? throw new FormatException("Data de emissão ausente.");
        var competencia = Data(Valor(dps, "dCompet"));
        var situacaoCStat = Valor(inf, "cStat");

        var item = new ItemFiscal(1, Valor(cServ, "cTribNac"), Valor(cServ, "xDescServ"), Valor(cServ, "cNBS"), null, Cfop.Servico, null, 1, vServ,
            vServ, desconto, 0, 0, 0, Math.Round(vServ - desconto, 2),
            null, null, 0, 0, 0, 0, 0, null, 0, 0, 0,
            Valor(pisCofins, "CST"), Num(Valor(pisCofins, "vBCPisCofins")), Num(Valor(pisCofins, "pAliqPis"), 4), vPis,
            Valor(pisCofins, "CST"), Num(Valor(pisCofins, "vBCPisCofins")), Num(Valor(pisCofins, "pAliqCofins"), 4), vCofins,
            bcIss, aliqIss, iss);

        var totais = new TotaisDocumento(vServ, desconto, 0, 0, 0, vServ, 0, 0, 0, 0, 0, vPis, vCofins, iss, issRetido,
            pisCofinsRetido ? vPis : 0, pisCofinsRetido ? vCofins : 0, Num(Valor(tribFed, "vRetCSLL")), Num(Valor(tribFed, "vRetIRRF")),
            Num(Valor(tribFed, "vRetCP")));

        var prestador = ParteNfse(emit, "enderNac");
        var tomador = toma is null ? null : ParteNfse(toma, "end");
        return new LeituraDocumento(new DocumentoLido(
            ModeloDocumento.NFSe, chave, (Valor(inf, "nNFSe") ?? Valor(dps, "nDPS") ?? "").Trim(), (Valor(dps, "serie") ?? "").Trim(), emissao,
            competencia, Valor(inf, "xTribNac") ?? Valor(cServ, "xDescServ"),
            situacaoCStat is "101" or "135" ? SituacaoDocumento.Cancelada : SituacaoDocumento.Autorizada, null,
            prestador, tomador, totais, [item], xml));
    }

    private static ParteDocumento ParteNfse(XElement? e, string endereco)
    {
        var end = Filho(e, endereco);
        var endNac = Filho(end, "endNac");
        var doc = Cnpj.SomenteDigitos(Valor(e, "CNPJ") ?? Valor(e, "CPF"));
        var cMun = Valor(end, "cMun") ?? Valor(endNac, "cMun");
        return new ParteDocumento(doc.Length == 0 ? null : doc, Valor(e, "xNome"), Valor(end, "UF") ?? UfDe(cMun), cMun, null, null);
    }

    // ------------------------------------------------------------------ utilitários (sem namespace)

    private static string? UfDe(string? cMun) => cMun is { Length: >= 2 } && IbgeUf.TryGetValue(cMun[..2], out var uf) ? uf : null;

    /// <summary>Primeiro filho direto com o nome (ou o primeiro filho, se o nome é null).</summary>
    private static XElement? Filho(XElement? e, string? nome) =>
        e?.Elements().FirstOrDefault(x => nome is null || x.Name.LocalName == nome);

    private static XElement? Desc(XElement? e, string nome) => e?.DescendantsAndSelf().FirstOrDefault(x => x.Name.LocalName == nome);

    /// <summary>Texto do primeiro descendente com o nome; null se ausente ou vazio.</summary>
    private static string? Valor(XElement? e, string nome)
    {
        if (e is null) return null;
        var v = e.Elements().FirstOrDefault(x => x.Name.LocalName == nome) ?? Desc(e, nome);
        return string.IsNullOrWhiteSpace(v?.Value) ? null : v!.Value.Trim();
    }

    private static decimal Num(string? texto, int casas = 2) =>
        decimal.TryParse(texto, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) ? Math.Round(v, casas) : 0m;

    private static DateOnly? Data(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        // Datas com fuso ("2024-03-05T23:50:00-03:00"): vale o dia do próprio documento, sem converter o fuso.
        if (texto.Length >= 10 && DateOnly.TryParseExact(texto[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        return null;
    }
}
