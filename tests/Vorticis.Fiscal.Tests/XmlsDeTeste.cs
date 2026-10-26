using System.Globalization;
using System.Text;

namespace Vorticis.Fiscal.Tests;

/// <summary>Item de uma NF-e de teste: valores já calculados à mão.</summary>
public sealed record ItemNfe(string Cfop, decimal Valor, decimal Icms = 0, decimal Ipi = 0, decimal IcmsSt = 0, string CstPis = "01", decimal Pis = 0,
                             decimal Cofins = 0, string Ncm = "84714900", string Produto = "Produto de teste", decimal Desconto = 0, decimal Frete = 0,
                             decimal AliqIcms = 18m);

/// <summary>Gera XMLs fiscais de teste no formato real (nfeProc/NFe e NFSe nacional).</summary>
public static class XmlsDeTeste
{
    public const string CnpjEmpresa = "61695227000193";
    public const string CnpjCliente = "11222333000181";
    public const string CnpjFornecedor = "45997418000153";

    private static string N(decimal v, int casas = 2) => v.ToString("F" + casas, CultureInfo.InvariantCulture);

    /// <summary>Chave de 44 dígitos distinta por número (conteúdo sem validação de dígito verificador).</summary>
    public static string Chave(int numero, string modelo = "55") => $"3524{CnpjEmpresa}{modelo}001{numero:D9}1{numero:D8}".PadRight(44, '0')[..44];

    public static string Nfe(string cnpjEmit, string? cnpjDest, int tpNf, int numero, DateOnly emissao, IReadOnlyList<ItemNfe> itens, string? chave = null,
                             string modelo = "55", string cStat = "100", decimal retPis = 0, decimal retCofins = 0, decimal retCsll = 0, decimal irrf = 0,
                             decimal retPrev = 0, string nomeDest = "Cliente Teste Ltda", string nomeEmit = "Emitente Teste SA", string uf = "SP",
                             IReadOnlyList<(string Numero, DateOnly Vencimento, decimal Valor)>? cobranca = null)
    {
        chave ??= Chave(numero, modelo);
        var det = new StringBuilder();
        var n = 0;
        foreach (var i in itens)
        {
            n++;
            var bc = i.Icms > 0 ? i.Valor : 0m;
            det.Append($"""
                <det nItem="{n}"><prod><cProd>P{n}</cProd><cEAN>SEM GTIN</cEAN><xProd>{i.Produto}</xProd><NCM>{i.Ncm}</NCM><CFOP>{i.Cfop}</CFOP><uCom>UN</uCom>
                <qCom>1.0000</qCom><vUnCom>{N(i.Valor, 10)}</vUnCom><vProd>{N(i.Valor)}</vProd><vFrete>{N(i.Frete)}</vFrete><vDesc>{N(i.Desconto)}</vDesc></prod>
                <imposto><ICMS><ICMS00><orig>0</orig><CST>00</CST><modBC>3</modBC><vBC>{N(bc)}</vBC><pICMS>{N(i.AliqIcms, 4)}</pICMS><vICMS>{N(i.Icms)}</vICMS><vBCST>0.00</vBCST><vICMSST>{N(i.IcmsSt)}</vICMSST></ICMS00></ICMS>
                <IPI><cEnq>999</cEnq><IPITrib><CST>50</CST><vBC>{N(i.Valor)}</vBC><pIPI>0.0000</pIPI><vIPI>{N(i.Ipi)}</vIPI></IPITrib></IPI>
                <PIS><PISAliq><CST>{i.CstPis}</CST><vBC>{N(i.Valor)}</vBC><pPIS>1.6500</pPIS><vPIS>{N(i.Pis)}</vPIS></PISAliq></PIS>
                <COFINS><COFINSAliq><CST>{i.CstPis}</CST><vBC>{N(i.Valor)}</vBC><pCOFINS>7.6000</pCOFINS><vCOFINS>{N(i.Cofins)}</vCOFINS></COFINSAliq></COFINS></imposto></det>
                """);
        }
        var vProd = itens.Sum(i => i.Valor);
        var vDesc = itens.Sum(i => i.Desconto);
        var vFrete = itens.Sum(i => i.Frete);
        var vIpi = itens.Sum(i => i.Ipi);
        var vSt = itens.Sum(i => i.IcmsSt);
        var vNf = vProd - vDesc + vFrete + vIpi + vSt;
        var dest = cnpjDest is null ? "" : $"<dest><CNPJ>{cnpjDest}</CNPJ><xNome>{nomeDest}</xNome><enderDest><xMun>Rio</xMun><cMun>3304557</cMun><UF>RJ</UF></enderDest><IE>123456</IE></dest>";
        var ret = retPis + retCofins + retCsll + irrf + retPrev > 0
            ? $"<retTrib><vRetPIS>{N(retPis)}</vRetPIS><vRetCOFINS>{N(retCofins)}</vRetCOFINS><vRetCSLL>{N(retCsll)}</vRetCSLL><vBCIRRF>{N(vProd)}</vBCIRRF><vIRRF>{N(irrf)}</vIRRF><vBCRetPrev>{N(vProd)}</vBCRetPrev><vRetPrev>{N(retPrev)}</vRetPrev></retTrib>"
            : "";
        var cobr = cobranca is null ? "" : "<cobr><fat><nFat>" + numero + "</nFat></fat>" + string.Concat(cobranca.Select(c =>
            $"<dup><nDup>{c.Numero}</nDup><dVenc>{c.Vencimento:yyyy-MM-dd}</dVenc><vDup>{N(c.Valor)}</vDup></dup>")) + "</cobr>";
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00"><NFe><infNFe Id="NFe{chave}" versao="4.00">
            <ide><cUF>35</cUF><natOp>Venda de mercadoria</natOp><mod>{modelo}</mod><serie>1</serie><nNF>{numero}</nNF><dhEmi>{emissao:yyyy-MM-dd}T10:30:00-03:00</dhEmi><tpNF>{tpNf}</tpNF></ide>
            <emit><CNPJ>{cnpjEmit}</CNPJ><xNome>{nomeEmit}</xNome><enderEmit><xMun>Sao Paulo</xMun><cMun>3550308</cMun><UF>{uf}</UF></enderEmit><IE>111222</IE></emit>
            {dest}
            {det}
            <total><ICMSTot><vBC>{N(itens.Sum(i => i.Icms > 0 ? i.Valor : 0))}</vBC><vICMS>{N(itens.Sum(i => i.Icms))}</vICMS><vBCST>0.00</vBCST><vST>{N(vSt)}</vST><vProd>{N(vProd)}</vProd><vFrete>{N(vFrete)}</vFrete><vSeg>0.00</vSeg><vDesc>{N(vDesc)}</vDesc><vII>0.00</vII><vIPI>{N(vIpi)}</vIPI><vPIS>{N(itens.Sum(i => i.Pis))}</vPIS><vCOFINS>{N(itens.Sum(i => i.Cofins))}</vCOFINS><vOutro>0.00</vOutro><vNF>{N(vNf)}</vNF></ICMSTot>{ret}</total>
            {cobr}
            </infNFe></NFe><protNFe versao="4.00"><infProt><tpAmb>1</tpAmb><chNFe>{chave}</chNFe><cStat>{cStat}</cStat><xMotivo>Autorizado</xMotivo></infProt></protNFe></nfeProc>
            """;
    }

    /// <summary>Evento de cancelamento (procEventoNFe, tpEvento 110111).</summary>
    public static string CancelamentoNfe(string chave) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <procEventoNFe xmlns="http://www.portalfiscal.inf.br/nfe" versao="1.00"><evento versao="1.00"><infEvento Id="ID110111{chave}01"><cOrgao>35</cOrgao><chNFe>{chave}</chNFe><tpEvento>110111</tpEvento><nSeqEvento>1</nSeqEvento></infEvento></evento>
        <retEvento versao="1.00"><infEvento><tpEvento>110111</tpEvento><cStat>135</cStat><chNFe>{chave}</chNFe></infEvento></retEvento></procEventoNFe>
        """;

    /// <summary>NFS-e padrão nacional (ADN): prestador, tomador, ISS e retenções federais.</summary>
    public static string Nfse(string cnpjPrestador, string? cnpjTomador, int numero, DateOnly emissao, decimal valor, decimal aliqIss = 5m, decimal iss = 0,
                              bool issRetido = false, decimal retIrrf = 0, decimal retCsll = 0, decimal retCp = 0, decimal pis = 0, decimal cofins = 0,
                              bool pisCofinsRetido = false, string? chave = null)
    {
        chave ??= $"3550308{cnpjPrestador}{numero:D13}{emissao:yyMM}".PadRight(50, '0')[..50];
        var toma = cnpjTomador is null ? "" : $"<toma><CNPJ>{cnpjTomador}</CNPJ><xNome>Tomador Teste Ltda</xNome><end><endNac><cMun>3550308</cMun><CEP>01001000</CEP></endNac><xLgr>Rua A</xLgr></end></toma>";
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <NFSe xmlns="http://www.sped.fazenda.gov.br/nfse" versao="1.00"><infNFSe Id="NFS{chave}">
            <xLocEmi>Sao Paulo</xLocEmi><nNFSe>{numero}</nNFSe><cLocIncid>3550308</cLocIncid><xTribNac>Analise e desenvolvimento de sistemas</xTribNac><dhProc>{emissao:yyyy-MM-dd}T09:00:00-03:00</dhProc><cStat>100</cStat>
            <emit><CNPJ>{cnpjPrestador}</CNPJ><xNome>Prestador Teste SA</xNome><enderNac><xLgr>Rua B</xLgr><cMun>3550308</cMun><UF>SP</UF></enderNac></emit>
            <valores><vBC>{N(valor)}</vBC><pAliqAplic>{N(aliqIss, 2)}</pAliqAplic><vISSQN>{N(iss)}</vISSQN><vLiq>{N(valor - retIrrf - retCsll - retCp)}</vLiq></valores>
            <DPS><infDPS Id="DPS1"><tpAmb>1</tpAmb><dhEmi>{emissao:yyyy-MM-dd}T08:50:00-03:00</dhEmi><serie>1</serie><nDPS>{numero}</nDPS><dCompet>{emissao:yyyy-MM-dd}</dCompet><cLocEmi>3550308</cLocEmi>
            <prest><CNPJ>{cnpjPrestador}</CNPJ></prest>{toma}
            <serv><locPrest><cLocPrestacao>3550308</cLocPrestacao></locPrest><cServ><cTribNac>010101</cTribNac><xDescServ>Desenvolvimento de software</xDescServ></cServ></serv>
            <valores><vServPrest><vServ>{N(valor)}</vServ></vServPrest><trib><tribMun><tribISSQN>1</tribISSQN><pAliq>{N(aliqIss, 2)}</pAliq><tpRetISSQN>{(issRetido ? 2 : 1)}</tpRetISSQN></tribMun>
            <tribFed><piscofins><CST>01</CST><vBCPisCofins>{N(valor)}</vBCPisCofins><pAliqPis>0.65</pAliqPis><pAliqCofins>3.00</pAliqCofins><vPis>{N(pis)}</vPis><vCofins>{N(cofins)}</vCofins><tpRetPisCofins>{(pisCofinsRetido ? 1 : 2)}</tpRetPisCofins></piscofins>
            <vRetCP>{N(retCp)}</vRetCP><vRetIRRF>{N(retIrrf)}</vRetIRRF><vRetCSLL>{N(retCsll)}</vRetCSLL></tribFed></trib></valores></infDPS></DPS></infNFSe></NFSe>
            """;
    }

    public static string CancelamentoNfse(string chave) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <evento xmlns="http://www.sped.fazenda.gov.br/nfse" versao="1.00"><infPedReg Id="PRE{chave}101101"><tpAmb>1</tpAmb><chNFSe>{chave}</chNFSe><nPedRegEvento>001</nPedRegEvento><e101101><xDesc>Cancelamento de NFS-e</xDesc><cMotivo>1</cMotivo><xMotivo>Erro na emissao</xMotivo></e101101></infPedReg></evento>
        """;

    public static byte[] Bytes(string xml) => Encoding.UTF8.GetBytes(xml.TrimStart());
}
