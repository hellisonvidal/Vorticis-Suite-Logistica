using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Vorticis.Fiscal.Sefaz;

namespace Vorticis.Frota;

public sealed record ConfiguracaoSefazTms(
    int Ambiente = 2,
    string? Rntrc = null,
    bool ContingenciaSvcAutomatica = true,
    string? SeguradoraNome = null,
    string? SeguradoraCnpj = null,
    string? ApoliceSeguro = null,
    string? AverbacaoPadrao = null);

public sealed record ParametrosIbsCbs(
    string Cst,
    string ClassTrib,
    decimal IbsUfPct,
    decimal IbsMunPct,
    decimal CbsPct,
    bool IncluirNoXml);

public sealed record TributosIbsCbs(
    decimal BaseCalculo,
    decimal IbsUfPct,
    decimal IbsUf,
    decimal IbsMunPct,
    decimal IbsMun,
    decimal CbsPct,
    decimal Cbs,
    string Cst,
    string ClassTrib)
{
    public decimal IbsTotal => IbsUf + IbsMun;
}

public sealed record ParteFiscal(
    string Documento,
    string Nome,
    string? NomeFantasia,
    string? InscricaoEstadual,
    string Uf,
    string MunicipioIbge,
    string Municipio,
    string? Cep,
    string? Logradouro,
    string? Numero,
    string? Complemento,
    string? Bairro,
    string? Telefone,
    string? Email);

public sealed record DadosXmlCte(
    int Ambiente,
    int TipoEmissao,
    DateTimeOffset Emissao,
    string CodigoNumerico,
    int Serie,
    int Numero,
    string Cfop,
    string NaturezaOperacao,
    int TipoTomador,
    ParadaRota Origem,
    ParadaRota Destino,
    ParteFiscal Emitente,
    ParteFiscal Remetente,
    ParteFiscal Destinatario,
    ParteFiscal? TomadorTerceiro,
    string ProdutoPredominante,
    decimal PesoKg,
    decimal VolumeM3,
    int Volumes,
    decimal ValorMercadoria,
    ComponentesFrete Frete,
    IReadOnlyList<string> ChavesNfe,
    string Rntrc,
    TributosIbsCbs? IbsCbs,
    string? Observacao = null,
    int Crt = 3);

public sealed record CiotParaMdfe(string Ciot, string DocumentoContratado, string? Rntrc = null, decimal ValePedagio = 0);

public sealed record DadosXmlMdfe(
    int Ambiente,
    DateTimeOffset Emissao,
    string CodigoNumerico,
    int Serie,
    int Numero,
    string UfInicio,
    string UfFim,
    IReadOnlyList<string> UfsPercurso,
    ParadaRota MunCarrega,
    ParadaRota MunDescarga,
    ParteFiscal Emitente,
    string Rntrc,
    string Placa,
    string? Renavam,
    decimal CapKg,
    int TaraKg,
    string TpRod,
    string TpCar,
    string MotoristaNome,
    string MotoristaCpf,
    IReadOnlyList<string> ChavesCte,
    decimal PesoTotalKg,
    decimal ValorCarga,
    IReadOnlyList<CiotParaMdfe> Ciots,
    string? SeguradoraNome = null,
    string? SeguradoraCnpj = null,
    string? ApoliceSeguro = null,
    string? Averbacao = null,
    string? Observacao = null);

public sealed record RespostaAutorizacaoSefaz(
    int CStat,
    string Motivo,
    string? Protocolo,
    string? Chave,
    DateTimeOffset? RecebidoEm,
    string? ProtXml,
    string XmlRetorno)
{
    public bool Autorizado => CStat is 100 or 150;
}

public sealed record RespostaEventoSefaz(int CStat, string Motivo, string? Protocolo, string XmlRetorno)
{
    public bool Registrado => CStat is 135 or 136;
}

public sealed record RegistroTransmissaoSefaz(
    long Id,
    string Modelo,
    long DocumentoId,
    string Acao,
    int Ambiente,
    int TipoEmissao,
    bool ContingenciaSvc,
    string? Chave,
    int? CStat,
    string? Motivo,
    string? Protocolo,
    DateTimeOffset EnviadoEm,
    string? EnviadoPor = null);

public sealed record ResultadoTransmissaoCte(
    string Chave,
    string Protocolo,
    int TipoEmissao,
    bool UsouContingenciaSvc,
    decimal Ibs,
    decimal Cbs,
    string Motivo);

public sealed record ResultadoTransmissaoMdfe(string Chave, string Protocolo, string Motivo);

/// <summary>Endereços oficiais da SEFAZ para CT-e 4.00 (com roteamento automático para SVC-RS e SVC-SP) e MDF-e 3.00 (SVRS).</summary>
public static class EndpointsSefazTms
{
    public const int EmissaoNormal = 1;
    public const int ContingenciaSvcRs = 7;
    public const int ContingenciaSvcSp = 8;

    private static readonly HashSet<string> UfsAtendidasPorSvcRs = new(StringComparer.OrdinalIgnoreCase)
    {
        "SP", "PR", "MT", "MS", "AP", "PE", "RR",
    };

    /// <summary>Define o tipo de emissão em contingência SVC conforme tabela oficial da SEFAZ: 7 = SVC-RS, 8 = SVC-SP.</summary>
    public static int TipoContingenciaSvc(string uf)
        => UfsAtendidasPorSvcRs.Contains((uf ?? "").Trim()) ? ContingenciaSvcRs : ContingenciaSvcSp;

    public static string NomeAutorizador(string uf, int tipoEmissao)
    {
        var u = (uf ?? "").Trim().ToUpperInvariant();
        return tipoEmissao switch
        {
            ContingenciaSvcRs => "SVC-RS",
            ContingenciaSvcSp => "SVC-SP",
            _ => u is "SP" or "MG" or "PR" or "RS" or "MT" or "MS" ? u : "SVRS",
        };
    }

    public static Uri UrlRecepcaoCte(string uf, int ambiente, int tipoEmissao)
    {
        var prod = ambiente == 1;
        if (tipoEmissao == ContingenciaSvcRs)
            return new(prod
                ? "https://cte.svrs.rs.gov.br/ws/CTeRecepcaoSincV4/CTeRecepcaoSincV4.asmx"
                : "https://cte-homologacao.svrs.rs.gov.br/ws/CTeRecepcaoSincV4/CTeRecepcaoSincV4.asmx");
        if (tipoEmissao == ContingenciaSvcSp)
            return new(prod
                ? "https://nfe.fazenda.sp.gov.br/CTeWS/WS/CTeRecepcaoSincV4.asmx"
                : "https://homologacao.nfe.fazenda.sp.gov.br/CTeWS/WS/CTeRecepcaoSincV4.asmx");

        return (uf ?? "").Trim().ToUpperInvariant() switch
        {
            "SP" => new(prod
                ? "https://nfe.fazenda.sp.gov.br/CTeWS/WS/CTeRecepcaoSincV4.asmx"
                : "https://homologacao.nfe.fazenda.sp.gov.br/CTeWS/WS/CTeRecepcaoSincV4.asmx"),
            "MG" => new(prod
                ? "https://cte.fazenda.mg.gov.br/cte/services/CTeRecepcaoSincV4"
                : "https://hcte.fazenda.mg.gov.br/cte/services/CTeRecepcaoSincV4"),
            "PR" => new(prod
                ? "https://cte.fazenda.pr.gov.br/cte4/CTeRecepcaoSincV4"
                : "https://homologacao.cte.fazenda.pr.gov.br/cte4/CTeRecepcaoSincV4"),
            "RS" => new(prod
                ? "https://cte.svrs.rs.gov.br/ws/CTeRecepcaoSincV4/CTeRecepcaoSincV4.asmx"
                : "https://cte-homologacao.svrs.rs.gov.br/ws/CTeRecepcaoSincV4/CTeRecepcaoSincV4.asmx"),
            "MT" => new(prod
                ? "https://cte.sefaz.mt.gov.br/ctews2/services/CTeRecepcaoSincV4"
                : "https://homologacao.sefaz.mt.gov.br/ctews2/services/CTeRecepcaoSincV4"),
            "MS" => new(prod
                ? "https://producao.cte.ms.gov.br/ws/CTeRecepcaoSincV4"
                : "https://homologacao.cte.ms.gov.br/ws/CTeRecepcaoSincV4"),
            _ => new(prod
                ? "https://cte.svrs.rs.gov.br/ws/CTeRecepcaoSincV4/CTeRecepcaoSincV4.asmx"
                : "https://cte-homologacao.svrs.rs.gov.br/ws/CTeRecepcaoSincV4/CTeRecepcaoSincV4.asmx"),
        };
    }

    public static Uri UrlEventoCte(string uf, int ambiente, int tipoEmissao)
    {
        var prod = ambiente == 1;
        if (tipoEmissao == ContingenciaSvcRs)
            return new(prod
                ? "https://cte.svrs.rs.gov.br/ws/CTeRecepcaoEventoV4/CTeRecepcaoEventoV4.asmx"
                : "https://cte-homologacao.svrs.rs.gov.br/ws/CTeRecepcaoEventoV4/CTeRecepcaoEventoV4.asmx");
        if (tipoEmissao == ContingenciaSvcSp)
            return new(prod
                ? "https://nfe.fazenda.sp.gov.br/CTeWS/WS/CTeRecepcaoEventoV4.asmx"
                : "https://homologacao.nfe.fazenda.sp.gov.br/CTeWS/WS/CTeRecepcaoEventoV4.asmx");

        return (uf ?? "").Trim().ToUpperInvariant() switch
        {
            "SP" => new(prod
                ? "https://nfe.fazenda.sp.gov.br/CTeWS/WS/CTeRecepcaoEventoV4.asmx"
                : "https://homologacao.nfe.fazenda.sp.gov.br/CTeWS/WS/CTeRecepcaoEventoV4.asmx"),
            "MG" => new(prod
                ? "https://cte.fazenda.mg.gov.br/cte/services/CTeRecepcaoEventoV4"
                : "https://hcte.fazenda.mg.gov.br/cte/services/CTeRecepcaoEventoV4"),
            "PR" => new(prod
                ? "https://cte.fazenda.pr.gov.br/cte4/CTeRecepcaoEventoV4"
                : "https://homologacao.cte.fazenda.pr.gov.br/cte4/CTeRecepcaoEventoV4"),
            "RS" => new(prod
                ? "https://cte.svrs.rs.gov.br/ws/CTeRecepcaoEventoV4/CTeRecepcaoEventoV4.asmx"
                : "https://cte-homologacao.svrs.rs.gov.br/ws/CTeRecepcaoEventoV4/CTeRecepcaoEventoV4.asmx"),
            "MT" => new(prod
                ? "https://cte.sefaz.mt.gov.br/ctews2/services/CTeRecepcaoEventoV4"
                : "https://homologacao.sefaz.mt.gov.br/ctews2/services/CTeRecepcaoEventoV4"),
            "MS" => new(prod
                ? "https://producao.cte.ms.gov.br/ws/CTeRecepcaoEventoV4"
                : "https://homologacao.cte.ms.gov.br/ws/CTeRecepcaoEventoV4"),
            _ => new(prod
                ? "https://cte.svrs.rs.gov.br/ws/CTeRecepcaoEventoV4/CTeRecepcaoEventoV4.asmx"
                : "https://cte-homologacao.svrs.rs.gov.br/ws/CTeRecepcaoEventoV4/CTeRecepcaoEventoV4.asmx"),
        };
    }

    public static Uri UrlRecepcaoMdfe(int ambiente) => new(ambiente == 1
        ? "https://mdfe.svrs.rs.gov.br/ws/MDFeRecepcaoSinc/MDFeRecepcaoSinc.asmx"
        : "https://mdfe-homologacao.svrs.rs.gov.br/ws/MDFeRecepcaoSinc/MDFeRecepcaoSinc.asmx");

    public static Uri UrlEventoMdfe(int ambiente) => new(ambiente == 1
        ? "https://mdfe.svrs.rs.gov.br/ws/MDFeRecepcaoEvento/MDFeRecepcaoEvento.asmx"
        : "https://mdfe-homologacao.svrs.rs.gov.br/ws/MDFeRecepcaoEvento/MDFeRecepcaoEvento.asmx");
}

/// <summary>Geração de XML CT-e 4.00 (com IBS/CBS EC 132/2023) e MDF-e 3.00, eventos, assinatura XMLDSig (ICP-Brasil) e leitura dos retornos SOAP.</summary>
public static class GeradorXmlSefazTms
{
    public const string NsCte = "http://www.portalfiscal.inf.br/cte";
    public const string NsMdfe = "http://www.portalfiscal.inf.br/mdfe";
    private const string NomeHomologacao = "CTE EMITIDO EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL";

    private static decimal R2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
    private static string F2(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    private static string F4(decimal v) => v.ToString("0.0000", CultureInfo.InvariantCulture);
    private static string DataIso(DateTimeOffset d) => d.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    private static string Txt(string? s, string padrao, int max)
    {
        var t = (s ?? "").Trim();
        if (t.Length == 0) t = padrao;
        return t.Length > max ? t[..max] : t;
    }

    private static string? Opt(string? s, int max)
    {
        var t = (s ?? "").Trim();
        return t.Length == 0 ? null : t.Length > max ? t[..max] : t;
    }

    private static string Dig(string? s, string padrao = "")
    {
        var d = RegrasTms.Digitos(s);
        return d.Length == 0 ? padrao : d;
    }

    public static TributosIbsCbs CalcularIbsCbs(decimal baseCalculo, ParametrosIbsCbs p)
    {
        var bc = Math.Max(0m, R2(baseCalculo));
        return new TributosIbsCbs(bc, p.IbsUfPct, R2(bc * p.IbsUfPct / 100m), p.IbsMunPct, R2(bc * p.IbsMunPct / 100m),
                                  p.CbsPct, R2(bc * p.CbsPct / 100m), p.Cst, p.ClassTrib);
    }

    public static string GerarCodigoNumerico() => RandomNumberGenerator.GetInt32(10_000_000, 99_999_999).ToString("D8", CultureInfo.InvariantCulture);

    /// <summary>Monta os 44 dígitos da chave de acesso (cUF + AAMM + CNPJ + mod + serie + nDoc + tpEmis + cNum + cDV).</summary>
    public static string MontarChave(int codigoUf, DateTimeOffset emissao, string cnpj, string modelo, int serie, int numero, int tipoEmissao, string codigoNumerico)
    {
        var cnpjLimpo = new string((cnpj ?? "").Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray()).PadLeft(14, '0')[..14];
        var cod = Dig(codigoNumerico, "12345678").PadLeft(8, '0')[..8];
        var c43 = string.Create(CultureInfo.InvariantCulture,
            $"{codigoUf:00}{emissao:yyMM}{cnpjLimpo}{modelo}{serie:000}{numero:000000000}{tipoEmissao}{cod}");
        return c43 + RegrasTms.DigitoChave(c43);
    }

    /// <summary>Gera o XML completo do CT-e 4.00 com grupo IBS/CBS (NT 2025.001) e marcações SVC quando tpEmis != 1.</summary>
    public static (string Chave, string Xml) GerarXmlCte(DadosXmlCte d)
    {
        var codUf = CodigosUf.De(d.Emitente.Uf) ?? 35;
        var codNum = Dig(d.CodigoNumerico, "12345678").PadLeft(8, '0')[..8];
        var chave = MontarChave(codUf, d.Emissao, d.Emitente.Documento, RegrasTms.ModeloCte, d.Serie, d.Numero, d.TipoEmissao, codNum);
        XNamespace ns = NsCte;
        XElement E(string nome, params object?[] conteudo) => new(ns + nome, conteudo.Where(c => c is not null)!);

        XElement Documento(string? doc)
        {
            var g = Dig(doc);
            return g.Length == 11 ? E("CPF", g) : E("CNPJ", g.PadLeft(14, '0')[..14]);
        }

        XElement? Ie(string? ie)
        {
            var limpo = Opt(ie, 14)?.ToUpperInvariant();
            if (limpo is null or "ISENTO") return null;
            var dig = Dig(limpo);
            return dig.Length >= 2 ? E("IE", dig) : null;
        }

        XElement Endereco(string nomeEl, ParteFiscal p, bool comPais) => E(nomeEl,
            E("xLgr", Txt(p.Logradouro, "NAO INFORMADO", 60)),
            E("nro", Txt(p.Numero, "S/N", 60)),
            Opt(p.Complemento, 60) is { } cpl ? E("xCpl", cpl) : null,
            E("xBairro", Txt(p.Bairro, "CENTRO", 60)),
            E("cMun", Dig(p.MunicipioIbge, "3550308").PadLeft(7, '0')[..7]),
            E("xMun", Txt(p.Municipio, "SAO PAULO", 60)),
            E("CEP", Dig(p.Cep, "01001000").PadLeft(8, '0')[..8]),
            E("UF", Txt(p.Uf, "SP", 2).ToUpperInvariant()),
            comPais ? E("cPais", "1058") : null,
            comPais ? E("xPais", "BRASIL") : null);

        var tomaCodigo = d.TipoTomador is 0 or 1 or 2 or 3 or 4 ? d.TipoTomador : 0;
        var tomaParte = tomaCodigo switch
        {
            3 => d.Destinatario,
            4 => d.TomadorTerceiro ?? d.Remetente,
            _ => d.Remetente,
        };
        var indIeToma = Ie(tomaParte.InscricaoEstadual) is not null ? "1" : "9";

        var tomaXml = tomaCodigo == 4
            ? E("toma4",
                E("toma", "4"),
                Documento(tomaParte.Documento),
                Ie(tomaParte.InscricaoEstadual),
                E("xNome", d.Ambiente == 2 ? NomeHomologacao : Txt(tomaParte.Nome, "TOMADOR", 60)),
                Endereco("enderToma", tomaParte, comPais: true))
            : E("toma3", E("toma", tomaCodigo.ToString(CultureInfo.InvariantCulture)));

        var ide = E("ide",
            E("cUF", codUf.ToString("00", CultureInfo.InvariantCulture)),
            E("cCT", codNum),
            E("CFOP", d.Cfop),
            E("natOp", Txt(d.NaturezaOperacao, "PRESTACAO DE SERVICO DE TRANSPORTE", 60)),
            E("mod", RegrasTms.ModeloCte),
            E("serie", d.Serie.ToString(CultureInfo.InvariantCulture)),
            E("nCT", d.Numero.ToString(CultureInfo.InvariantCulture)),
            E("dhEmi", DataIso(d.Emissao)),
            E("tpImp", "1"),
            E("tpEmis", d.TipoEmissao.ToString(CultureInfo.InvariantCulture)),
            E("cDV", chave[^1].ToString()),
            E("tpAmb", d.Ambiente.ToString(CultureInfo.InvariantCulture)),
            E("tpCTe", "0"),
            E("procEmi", "0"),
            E("verProc", "Vorticis 1.0"),
            E("cMunEnv", Dig(d.Emitente.MunicipioIbge, "3550308").PadLeft(7, '0')[..7]),
            E("xMunEnv", Txt(d.Emitente.Municipio, "SAO PAULO", 60)),
            E("UFEnv", Txt(d.Emitente.Uf, "SP", 2).ToUpperInvariant()),
            E("modal", "01"),
            E("tpServ", "0"),
            E("cMunIni", Dig(d.Origem.MunicipioIbge, "3550308").PadLeft(7, '0')[..7]),
            E("xMunIni", Txt(d.Origem.Municipio, "SAO PAULO", 60)),
            E("UFIni", Txt(d.Origem.Uf, "SP", 2).ToUpperInvariant()),
            E("cMunFim", Dig(d.Destino.MunicipioIbge, "3304557").PadLeft(7, '0')[..7]),
            E("xMunFim", Txt(d.Destino.Municipio, "RIO DE JANEIRO", 60)),
            E("UFFim", Txt(d.Destino.Uf, "RJ", 2).ToUpperInvariant()),
            E("retira", "1"),
            E("indIEToma", indIeToma),
            tomaXml,
            d.TipoEmissao != EndpointsSefazTms.EmissaoNormal ? E("dhCont", DataIso(d.Emissao)) : null,
            d.TipoEmissao != EndpointsSefazTms.EmissaoNormal ? E("xJust", "Contingencia automatica SVC por indisponibilidade da SEFAZ") : null);

        var comps = new List<XElement>();
        void AddComp(string nome, decimal valor)
        {
            if (valor > 0) comps.Add(E("Comp", E("xNome", nome), E("vComp", F2(valor))));
        }
        AddComp("FRETE PESO", d.Frete.FretePeso);
        AddComp("AD VALOREM", d.Frete.AdValorem);
        AddComp("GRIS", d.Frete.Gris);
        AddComp("PEDAGIO", d.Frete.Pedagio);
        AddComp("TAXAS", d.Frete.Taxas);

        var icmsEl = d.Frete.AliquotaIcms > 0
            ? E("ICMS00", E("CST", "00"), E("vBC", F2(d.Frete.Total)), E("pICMS", F2(d.Frete.AliquotaIcms)), E("vICMS", F2(d.Frete.Icms)))
            : E("ICMS45", E("CST", "40"));

        XElement? ibsCbsEl = d.IbsCbs is { } ibs
            ? E("IBSCBS",
                E("CST", Txt(ibs.Cst, "000", 3)),
                E("cClassTrib", Txt(ibs.ClassTrib, "000001", 6)),
                E("gIBSCBS",
                    E("vBC", F2(ibs.BaseCalculo)),
                    E("gIBSUF", E("pIBSUF", F4(ibs.IbsUfPct)), E("vIBSUF", F2(ibs.IbsUf))),
                    E("gIBSMun", E("pIBSMun", F4(ibs.IbsMunPct)), E("vIBSMun", F2(ibs.IbsMun))),
                    E("vIBS", F2(ibs.IbsTotal)),
                    E("gCBS", E("pCBS", F4(ibs.CbsPct)), E("vCBS", F2(ibs.Cbs)))))
            : null;

        var docChilds = d.ChavesNfe.Count > 0
            ? d.ChavesNfe.Select(ch => (object)E("infNFe", E("chave", ch))).ToArray()
            : [E("infOutros", E("tpDoc", "99"), E("descOutros", "Documento de transporte"))];

        var cargaEl = E("infCarga",
            E("vCarga", F2(d.ValorMercadoria)),
            E("proPred", Txt(d.ProdutoPredominante, "CARGA GERAL", 60)),
            E("infQ", E("cUnid", "01"), E("tpMed", "PESO BRUTO"), E("qCarga", F4(d.PesoKg > 0 ? d.PesoKg : 1m))),
            d.Volumes > 0 ? E("infQ", E("cUnid", "03"), E("tpMed", "VOLUMES"), E("qCarga", F4(d.Volumes))) : null,
            d.VolumeM3 > 0 ? E("infQ", E("cUnid", "00"), E("tpMed", "CUBAGEM M3"), E("qCarga", F4(d.VolumeM3))) : null);

        var rodo = new XElement(ns + "infModal",
            new XAttribute("versaoModal", "4.00"),
            E("rodo", E("RNTRC", Dig(d.Rntrc).PadLeft(8, '0')[..8])));

        var cte = E("CTe",
            new XElement(ns + "infCte",
                new XAttribute("versao", "4.00"),
                new XAttribute("Id", "CTe" + chave),
                ide,
                Opt(d.Observacao, 2000) is { } obs ? E("compl", E("xObs", obs)) : null,
                E("emit",
                    E("CNPJ", Dig(d.Emitente.Documento).PadLeft(14, '0')[..14]),
                    Ie(d.Emitente.InscricaoEstadual),
                    E("xNome", Txt(d.Emitente.Nome, "TRANSPORTADORA", 60)),
                    Opt(d.Emitente.NomeFantasia, 60) is { } xFant ? E("xFant", xFant) : null,
                    Endereco("enderEmit", d.Emitente, comPais: false),
                    E("CRT", d.Crt.ToString(CultureInfo.InvariantCulture))),
                E("rem",
                    Documento(d.Remetente.Documento),
                    Ie(d.Remetente.InscricaoEstadual),
                    E("xNome", d.Ambiente == 2 ? NomeHomologacao : Txt(d.Remetente.Nome, "REMETENTE", 60)),
                    Endereco("enderReme", d.Remetente, comPais: true)),
                E("dest",
                    Documento(d.Destinatario.Documento),
                    Ie(d.Destinatario.InscricaoEstadual),
                    E("xNome", d.Ambiente == 2 ? NomeHomologacao : Txt(d.Destinatario.Nome, "DESTINATARIO", 60)),
                    Endereco("enderDest", d.Destinatario, comPais: true)),
                E("vPrest",
                    E("vTPrest", F2(d.Frete.Total)),
                    E("vRec", F2(d.Frete.Total)),
                    comps.ToArray()),
                E("imp",
                    E("ICMS", icmsEl),
                    ibsCbsEl),
                E("infCTeNorm",
                    cargaEl,
                    E("infDoc", docChilds),
                    rodo)));

        return (chave, cte.ToString(SaveOptions.DisableFormatting));
    }

    /// <summary>Gera o XML completo do MDF-e 3.00 com CIOTs declarados, vale-pedágio, seguro e CT-es vinculados.</summary>
    public static (string Chave, string Xml) GerarXmlMdfe(DadosXmlMdfe d)
    {
        var codUf = CodigosUf.De(d.Emitente.Uf) ?? 35;
        var codNum = Dig(d.CodigoNumerico, "12345678").PadLeft(8, '0')[..8];
        var chave = MontarChave(codUf, d.Emissao, d.Emitente.Documento, RegrasTms.ModeloMdfe, d.Serie, d.Numero, EndpointsSefazTms.EmissaoNormal, codNum);
        XNamespace ns = NsMdfe;
        XElement E(string nome, params object?[] conteudo) => new(ns + nome, conteudo.Where(c => c is not null)!);

        var ide = E("ide",
            E("cUF", codUf.ToString("00", CultureInfo.InvariantCulture)),
            E("tpAmb", d.Ambiente.ToString(CultureInfo.InvariantCulture)),
            E("tpEmit", "1"),
            E("mod", RegrasTms.ModeloMdfe),
            E("serie", d.Serie.ToString(CultureInfo.InvariantCulture)),
            E("nMDF", d.Numero.ToString(CultureInfo.InvariantCulture)),
            E("cMDF", codNum),
            E("cDV", chave[^1].ToString()),
            E("modal", "1"),
            E("dhEmi", DataIso(d.Emissao)),
            E("tpEmis", "1"),
            E("procEmi", "0"),
            E("verProc", "Vorticis 1.0"),
            E("UFIni", Txt(d.UfInicio, "SP", 2).ToUpperInvariant()),
            E("UFFim", Txt(d.UfFim, "RJ", 2).ToUpperInvariant()),
            E("infMunCarrega",
                E("cMunCarrega", Dig(d.MunCarrega.MunicipioIbge, "3550308").PadLeft(7, '0')[..7]),
                E("xMunCarrega", Txt(d.MunCarrega.Municipio, "SAO PAULO", 60))),
            d.UfsPercurso.Select(u => (object)E("infPercurso", E("UFPer", Txt(u, "MG", 2).ToUpperInvariant()))).ToArray());

        var emitIe = Opt(d.Emitente.InscricaoEstadual, 14);
        var emit = E("emit",
            E("CNPJ", Dig(d.Emitente.Documento).PadLeft(14, '0')[..14]),
            emitIe is not null && !emitIe.Equals("ISENTO", StringComparison.OrdinalIgnoreCase) ? E("IE", Dig(emitIe)) : null,
            E("xNome", Txt(d.Emitente.Nome, "TRANSPORTADORA", 60)),
            Opt(d.Emitente.NomeFantasia, 60) is { } fant ? E("xFant", fant) : null,
            E("enderEmit",
                E("xLgr", Txt(d.Emitente.Logradouro, "NAO INFORMADO", 60)),
                E("nro", Txt(d.Emitente.Numero, "S/N", 60)),
                Opt(d.Emitente.Complemento, 60) is { } cpl ? E("xCpl", cpl) : null,
                E("xBairro", Txt(d.Emitente.Bairro, "CENTRO", 60)),
                E("cMun", Dig(d.Emitente.MunicipioIbge, "3550308").PadLeft(7, '0')[..7]),
                E("xMun", Txt(d.Emitente.Municipio, "SAO PAULO", 60)),
                E("CEP", Dig(d.Emitente.Cep, "01001000").PadLeft(8, '0')[..8]),
                E("UF", Txt(d.Emitente.Uf, "SP", 2).ToUpperInvariant())));

        var ciotEls = d.Ciots.Select(c =>
        {
            var docContratado = Dig(c.DocumentoContratado);
            return (object)E("infCIOT",
                E("CIOT", Dig(c.Ciot).PadLeft(12, '0')[..12]),
                docContratado.Length == 11 ? E("CPF", docContratado) : E("CNPJ", docContratado.PadLeft(14, '0')[..14]));
        }).ToArray();

        var comPedagio = d.Ciots.Where(c => c.ValePedagio > 0).ToList();
        XElement? valePedEl = comPedagio.Count > 0
            ? E("valePed", comPedagio.Select(c =>
            {
                var docContratado = Dig(c.DocumentoContratado);
                return (object)E("disp",
                    E("CNPJForn", Dig(d.Emitente.Documento).PadLeft(14, '0')[..14]),
                    docContratado.Length == 11 ? E("CPFPg", docContratado) : E("CNPJPg", docContratado.PadLeft(14, '0')[..14]),
                    E("nCompra", Dig(c.Ciot).PadLeft(12, '0')[..12]),
                    E("vValePed", F2(c.ValePedagio)));
            }).ToArray())
            : null;

        var rntrcPadrao = Dig(d.Rntrc).PadLeft(8, '0')[..8];
        var rodo = new XElement(ns + "infModal",
            new XAttribute("versaoModal", "3.00"),
            E("rodo",
                E("infANTT",
                    E("RNTRC", rntrcPadrao),
                    ciotEls,
                    valePedEl),
                E("veicTracao",
                    E("placa", new string((d.Placa ?? "").Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray())),
                    Dig(d.Renavam) is { Length: > 0 } ren ? E("RENAVAM", ren.Length > 11 ? ren[..11] : ren) : null,
                    E("tara", Math.Max(0, d.TaraKg).ToString(CultureInfo.InvariantCulture)),
                    E("capKG", Math.Max(0, (int)Math.Round(d.CapKg, 0)).ToString(CultureInfo.InvariantCulture)),
                    E("condutor",
                        E("xNome", Txt(d.MotoristaNome, "MOTORISTA", 60)),
                        E("CPF", Dig(d.MotoristaCpf).PadLeft(11, '0')[..11])),
                    E("tpRod", Txt(d.TpRod, "01", 2)),
                    E("tpCar", Txt(d.TpCar, "00", 2)),
                    E("UF", Txt(d.Emitente.Uf, "SP", 2).ToUpperInvariant()))));

        var infDoc = E("infDoc",
            E("infMunDescarga",
                E("cMunDescarga", Dig(d.MunDescarga.MunicipioIbge, "3304557").PadLeft(7, '0')[..7]),
                E("xMunDescarga", Txt(d.MunDescarga.Municipio, "RIO DE JANEIRO", 60)),
                d.ChavesCte.Select(c => (object)E("infCTe", E("chCTe", c))).ToArray()));

        XElement? segEl = null;
        if (!string.IsNullOrWhiteSpace(d.SeguradoraNome) || !string.IsNullOrWhiteSpace(d.ApoliceSeguro) || !string.IsNullOrWhiteSpace(d.SeguradoraCnpj))
        {
            segEl = E("seg",
                E("infResp", E("respSeg", "1"), E("CNPJ", Dig(d.Emitente.Documento).PadLeft(14, '0')[..14])),
                (!string.IsNullOrWhiteSpace(d.SeguradoraNome) || !string.IsNullOrWhiteSpace(d.SeguradoraCnpj))
                    ? E("infSeg",
                        E("xSeg", Txt(d.SeguradoraNome, "SEGURADORA", 30)),
                        Dig(d.SeguradoraCnpj) is { Length: 14 } sc ? E("CNPJ", sc) : null)
                    : null,
                Opt(d.ApoliceSeguro, 20) is { } ap ? E("nApol", ap) : null,
                Opt(d.Averbacao, 40) is { } av ? E("nAver", av) : null);
        }

        var tot = E("tot",
            E("qCTe", d.ChavesCte.Count.ToString(CultureInfo.InvariantCulture)),
            E("vCarga", F2(d.ValorCarga)),
            E("cUnid", "01"),
            E("qCarga", F4(d.PesoTotalKg)));

        var mdfe = E("MDFe",
            new XElement(ns + "infMDFe",
                new XAttribute("versao", "3.00"),
                new XAttribute("Id", "MDFe" + chave),
                ide,
                emit,
                rodo,
                infDoc,
                segEl,
                tot,
                Opt(d.Observacao, 2000) is { } obs ? E("infAdic", E("infCpl", obs)) : null));

        return (chave, mdfe.ToString(SaveOptions.DisableFormatting));
    }

    public static string GerarEventoCancelamentoCte(
        string ufEmitente, int ambiente, string cnpj, string chaveCte, string protocoloAutorizacao,
        string justificativa, DateTimeOffset quando, int sequencia = 1)
    {
        var cOrgao = (CodigosUf.De(ufEmitente) ?? 35).ToString("00", CultureInfo.InvariantCulture);
        var id = $"ID110111{chaveCte}{sequencia:000}";
        XNamespace ns = NsCte;
        var doc = new XElement(ns + "eventoCTe",
            new XAttribute("versao", "4.00"),
            new XElement(ns + "infEvento",
                new XAttribute("Id", id),
                new XElement(ns + "cOrgao", cOrgao),
                new XElement(ns + "tpAmb", ambiente.ToString(CultureInfo.InvariantCulture)),
                new XElement(ns + "CNPJ", Dig(cnpj).PadLeft(14, '0')[..14]),
                new XElement(ns + "chCTe", chaveCte),
                new XElement(ns + "dhEvento", DataIso(quando)),
                new XElement(ns + "tpEvento", "110111"),
                new XElement(ns + "nSeqEvento", sequencia.ToString(CultureInfo.InvariantCulture)),
                new XElement(ns + "detEvento",
                    new XAttribute("versaoEvento", "4.00"),
                    new XElement(ns + "evCancCTe",
                        new XElement(ns + "descEvento", "Cancelamento"),
                        new XElement(ns + "nProt", Txt(protocoloAutorizacao, "", 15)),
                        new XElement(ns + "xJust", Txt(justificativa, "Cancelamento solicitado", 255))))));
        return doc.ToString(SaveOptions.DisableFormatting);
    }

    public static string GerarEventoEncerramentoMdfe(
        string ufEmitente, int ambiente, string cnpj, string chaveMdfe, string protocoloAutorizacao,
        DateOnly dataEncerramento, string ufEncerramento, string municipioIbgeEncerramento, DateTimeOffset quando, int sequencia = 1)
    {
        var cOrgao = (CodigosUf.De(ufEmitente) ?? 35).ToString("00", CultureInfo.InvariantCulture);
        var cUfEnc = (CodigosUf.De(ufEncerramento) ?? 35).ToString("00", CultureInfo.InvariantCulture);
        var id = $"ID110112{chaveMdfe}{sequencia:000}";
        XNamespace ns = NsMdfe;
        var doc = new XElement(ns + "eventoMDFe",
            new XAttribute("versao", "3.00"),
            new XElement(ns + "infEvento",
                new XAttribute("Id", id),
                new XElement(ns + "cOrgao", cOrgao),
                new XElement(ns + "tpAmb", ambiente.ToString(CultureInfo.InvariantCulture)),
                new XElement(ns + "CNPJ", Dig(cnpj).PadLeft(14, '0')[..14]),
                new XElement(ns + "chMDFe", chaveMdfe),
                new XElement(ns + "dhEvento", DataIso(quando)),
                new XElement(ns + "tpEvento", "110112"),
                new XElement(ns + "nSeqEvento", sequencia.ToString(CultureInfo.InvariantCulture)),
                new XElement(ns + "detEvento",
                    new XAttribute("versaoEvento", "3.00"),
                    new XElement(ns + "evEncMDFe",
                        new XElement(ns + "descEvento", "Encerramento"),
                        new XElement(ns + "nProt", Txt(protocoloAutorizacao, "", 15)),
                        new XElement(ns + "dtEnc", dataEncerramento.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                        new XElement(ns + "cUF", cUfEnc),
                        new XElement(ns + "cMun", Dig(municipioIbgeEncerramento, "3550308").PadLeft(7, '0')[..7])))));
        return doc.ToString(SaveOptions.DisableFormatting);
    }

    public static string GerarEventoCancelamentoMdfe(
        string ufEmitente, int ambiente, string cnpj, string chaveMdfe, string protocoloAutorizacao,
        string justificativa, DateTimeOffset quando, int sequencia = 1)
    {
        var cOrgao = (CodigosUf.De(ufEmitente) ?? 35).ToString("00", CultureInfo.InvariantCulture);
        var id = $"ID110111{chaveMdfe}{sequencia:000}";
        XNamespace ns = NsMdfe;
        var doc = new XElement(ns + "eventoMDFe",
            new XAttribute("versao", "3.00"),
            new XElement(ns + "infEvento",
                new XAttribute("Id", id),
                new XElement(ns + "cOrgao", cOrgao),
                new XElement(ns + "tpAmb", ambiente.ToString(CultureInfo.InvariantCulture)),
                new XElement(ns + "CNPJ", Dig(cnpj).PadLeft(14, '0')[..14]),
                new XElement(ns + "chMDFe", chaveMdfe),
                new XElement(ns + "dhEvento", DataIso(quando)),
                new XElement(ns + "tpEvento", "110111"),
                new XElement(ns + "nSeqEvento", sequencia.ToString(CultureInfo.InvariantCulture)),
                new XElement(ns + "detEvento",
                    new XAttribute("versaoEvento", "3.00"),
                    new XElement(ns + "evCancMDFe",
                        new XElement(ns + "descEvento", "Cancelamento"),
                        new XElement(ns + "nProt", Txt(protocoloAutorizacao, "", 15)),
                        new XElement(ns + "xJust", Txt(justificativa, "Cancelamento solicitado", 255))))));
        return doc.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>Assinatura XMLDSig enveloped sobre o atributo Id da tag informada (infCte, infMDFe ou infEvento), conforme MOC SEFAZ.</summary>
    public static string AssinarXml(string xml, string tagReferencia, X509Certificate2 certificado)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);
        var elementos = doc.GetElementsByTagName(tagReferencia);
        if (elementos.Count == 0 || elementos[0] is not XmlElement alvo)
            throw new InvalidOperationException($"Tag <{tagReferencia}> não encontrada para assinatura.");
        var id = alvo.GetAttribute("Id");
        if (string.IsNullOrEmpty(id))
            throw new InvalidOperationException($"Atributo Id ausente em <{tagReferencia}>.");

        using var chave = certificado.GetRSAPrivateKey()
                          ?? throw new InvalidOperationException("O certificado digital não possui chave privada RSA.");
        var assinado = new SignedXml(doc) { SigningKey = chave };
        assinado.SignedInfo!.CanonicalizationMethod = SignedXml.XmlDsigC14NTransformUrl;
        assinado.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA1Url;
        var referencia = new Reference("#" + id) { DigestMethod = SignedXml.XmlDsigSHA1Url };
        referencia.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        referencia.AddTransform(new XmlDsigC14NTransform());
        assinado.AddReference(referencia);
        var info = new KeyInfo();
        info.AddClause(new KeyInfoX509Data(certificado));
        assinado.KeyInfo = info;
        assinado.ComputeSignature();
        doc.DocumentElement!.AppendChild(doc.ImportNode(assinado.GetXml(), true));
        return doc.DocumentElement.OuterXml;
    }

    public static bool VerificarAssinatura(string xmlAssinado)
    {
        try
        {
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml(xmlAssinado);
            var sigs = doc.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl);
            if (sigs.Count != 1 || sigs[0] is not XmlElement sig) return false;
            var signedXml = new SignedXml(doc);
            signedXml.LoadXml(sig);
            return signedXml.CheckSignature();
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static string CompactarBase64Gzip(string xml)
    {
        using var saida = new MemoryStream();
        using (var gzip = new GZipStream(saida, CompressionMode.Compress, leaveOpen: true))
        using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)))
            writer.Write(xml);
        return Convert.ToBase64String(saida.ToArray());
    }

    public static string DescompactarBase64Gzip(string base64)
    {
        using var entrada = new MemoryStream(Convert.FromBase64String(base64.Trim()));
        using var gzip = new GZipStream(entrada, CompressionMode.Decompress);
        using var leitor = new StreamReader(gzip, Encoding.UTF8);
        return leitor.ReadToEnd();
    }

    public static string MontarCteProc(string xmlCteAssinado, string protCteXml)
    {
        XNamespace ns = NsCte;
        var proc = new XElement(ns + "cteProc",
            new XAttribute("versao", "4.00"),
            XElement.Parse(xmlCteAssinado),
            XElement.Parse(protCteXml));
        return proc.ToString(SaveOptions.DisableFormatting);
    }

    public static string MontarMdfeProc(string xmlMdfeAssinado, string protMdfeXml)
    {
        XNamespace ns = NsMdfe;
        var proc = new XElement(ns + "mdfeProc",
            new XAttribute("versao", "3.00"),
            XElement.Parse(xmlMdfeAssinado),
            XElement.Parse(protMdfeXml));
        return proc.ToString(SaveOptions.DisableFormatting);
    }

    private static string TextoEl(XElement? e, string nome)
        => e?.Elements().FirstOrDefault(x => x.Name.LocalName == nome)?.Value ?? "";

    private static int NumEl(XElement? e, string nome)
        => int.TryParse(TextoEl(e, nome), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    public static RespostaAutorizacaoSefaz LerRetornoCte(string xmlSoap)
    {
        var raiz = XDocument.Parse(xmlSoap);
        var ret = raiz.Descendants().FirstOrDefault(e => e.Name.LocalName == "retCTe")
                  ?? throw new ErroComunicacaoSefaz("Resposta da SEFAZ sem retCTe.");
        var prot = ret.Descendants().FirstOrDefault(e => e.Name.LocalName == "protCTe");
        var infProt = prot?.Descendants().FirstOrDefault(e => e.Name.LocalName == "infProt");
        var cStat = infProt is not null ? NumEl(infProt, "cStat") : NumEl(ret, "cStat");
        var xMotivo = infProt is not null ? TextoEl(infProt, "xMotivo") : TextoEl(ret, "xMotivo");
        var nProt = TextoEl(infProt, "nProt");
        var ch = TextoEl(infProt, "chCTe");
        DateTimeOffset? dh = DateTimeOffset.TryParse(TextoEl(infProt, "dhRecbto"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
            ? dto
            : null;
        return new RespostaAutorizacaoSefaz(cStat, xMotivo, string.IsNullOrEmpty(nProt) ? null : nProt, string.IsNullOrEmpty(ch) ? null : ch,
                                            dh, prot?.ToString(SaveOptions.DisableFormatting), xmlSoap);
    }

    public static RespostaAutorizacaoSefaz LerRetornoMdfe(string xmlSoap)
    {
        var raiz = XDocument.Parse(xmlSoap);
        var ret = raiz.Descendants().FirstOrDefault(e => e.Name.LocalName == "retMDFe")
                  ?? throw new ErroComunicacaoSefaz("Resposta da SEFAZ sem retMDFe.");
        var prot = ret.Descendants().FirstOrDefault(e => e.Name.LocalName == "protMDFe");
        var infProt = prot?.Descendants().FirstOrDefault(e => e.Name.LocalName == "infProt");
        var cStat = infProt is not null ? NumEl(infProt, "cStat") : NumEl(ret, "cStat");
        var xMotivo = infProt is not null ? TextoEl(infProt, "xMotivo") : TextoEl(ret, "xMotivo");
        var nProt = TextoEl(infProt, "nProt");
        var ch = TextoEl(infProt, "chMDFe");
        DateTimeOffset? dh = DateTimeOffset.TryParse(TextoEl(infProt, "dhRecbto"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
            ? dto
            : null;
        return new RespostaAutorizacaoSefaz(cStat, xMotivo, string.IsNullOrEmpty(nProt) ? null : nProt, string.IsNullOrEmpty(ch) ? null : ch,
                                            dh, prot?.ToString(SaveOptions.DisableFormatting), xmlSoap);
    }

    public static RespostaEventoSefaz LerRetornoEvento(string xmlSoap)
    {
        var raiz = XDocument.Parse(xmlSoap);
        var ret = raiz.Descendants().FirstOrDefault(e => e.Name.LocalName is "retEventoCTe" or "retEventoMDFe")
                  ?? throw new ErroComunicacaoSefaz("Resposta da SEFAZ sem retEventoCTe/retEventoMDFe.");
        var inf = ret.Descendants().FirstOrDefault(e => e.Name.LocalName == "infEvento");
        var cStat = inf is not null ? NumEl(inf, "cStat") : NumEl(ret, "cStat");
        var xMotivo = inf is not null ? TextoEl(inf, "xMotivo") : TextoEl(ret, "xMotivo");
        var nProt = TextoEl(inf, "nProt");
        return new RespostaEventoSefaz(cStat, xMotivo, string.IsNullOrEmpty(nProt) ? null : nProt, xmlSoap);
    }
}

public interface IClienteSefazTms
{
    Task<RespostaAutorizacaoSefaz> AutorizarCteAsync(string uf, int ambiente, int tipoEmissao, string xmlAssinado, CancellationToken ct = default);
    Task<RespostaEventoSefaz> EnviarEventoCteAsync(string uf, int ambiente, int tipoEmissao, string xmlEventoAssinado, CancellationToken ct = default);
    Task<RespostaAutorizacaoSefaz> AutorizarMdfeAsync(int ambiente, string xmlAssinado, CancellationToken ct = default);
    Task<RespostaEventoSefaz> EnviarEventoMdfeAsync(int ambiente, string xmlEventoAssinado, CancellationToken ct = default);
}

/// <summary>Cliente SOAP 1.2 (TLS mútuo com certificado ICP-Brasil) para transmissão de CT-e 4.00 e MDF-e 3.00 à SEFAZ.</summary>
public sealed class ClienteSefazTms : IClienteSefazTms, IDisposable
{
    private const string NsWsdlCteRec = "http://www.portalfiscal.inf.br/cte/wsdl/CTeRecepcaoSincV4";
    private const string NsWsdlCteEvt = "http://www.portalfiscal.inf.br/cte/wsdl/CTeRecepcaoEventoV4";
    private const string NsWsdlMdfeRec = "http://www.portalfiscal.inf.br/mdfe/wsdl/MDFeRecepcaoSinc";
    private const string NsWsdlMdfeEvt = "http://www.portalfiscal.inf.br/mdfe/wsdl/MDFeRecepcaoEvento";

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public ClienteSefazTms(HttpClient http, bool ownsHttp = false)
    {
        _http = http;
        _ownsHttp = ownsHttp;
    }

    public static ClienteSefazTms Criar(X509Certificate2 certificado)
    {
        var handler = new SocketsHttpHandler();
        handler.SslOptions.ClientCertificates = [certificado];
        return new ClienteSefazTms(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) }, ownsHttp: true);
    }

    public async Task<RespostaAutorizacaoSefaz> AutorizarCteAsync(string uf, int ambiente, int tipoEmissao, string xmlAssinado, CancellationToken ct = default)
    {
        var url = EndpointsSefazTms.UrlRecepcaoCte(uf, ambiente, tipoEmissao);
        var corpo = new XElement(XName.Get("cteDadosMsg", NsWsdlCteRec), GeradorXmlSefazTms.CompactarBase64Gzip(xmlAssinado));
        var ret = await PostarAsync(url, NsWsdlCteRec + "/cteRecepcao", corpo, ct);
        return GeradorXmlSefazTms.LerRetornoCte(ret);
    }

    public async Task<RespostaEventoSefaz> EnviarEventoCteAsync(string uf, int ambiente, int tipoEmissao, string xmlEventoAssinado, CancellationToken ct = default)
    {
        var url = EndpointsSefazTms.UrlEventoCte(uf, ambiente, tipoEmissao);
        var corpo = new XElement(XName.Get("cteDadosMsg", NsWsdlCteEvt), XElement.Parse(xmlEventoAssinado));
        var ret = await PostarAsync(url, NsWsdlCteEvt + "/cteRecepcaoEvento", corpo, ct);
        return GeradorXmlSefazTms.LerRetornoEvento(ret);
    }

    public async Task<RespostaAutorizacaoSefaz> AutorizarMdfeAsync(int ambiente, string xmlAssinado, CancellationToken ct = default)
    {
        var url = EndpointsSefazTms.UrlRecepcaoMdfe(ambiente);
        var corpo = new XElement(XName.Get("mdfeDadosMsg", NsWsdlMdfeRec), GeradorXmlSefazTms.CompactarBase64Gzip(xmlAssinado));
        var ret = await PostarAsync(url, NsWsdlMdfeRec + "/mdfeRecepcao", corpo, ct);
        return GeradorXmlSefazTms.LerRetornoMdfe(ret);
    }

    public async Task<RespostaEventoSefaz> EnviarEventoMdfeAsync(int ambiente, string xmlEventoAssinado, CancellationToken ct = default)
    {
        var url = EndpointsSefazTms.UrlEventoMdfe(ambiente);
        var corpo = new XElement(XName.Get("mdfeDadosMsg", NsWsdlMdfeEvt), XElement.Parse(xmlEventoAssinado));
        var ret = await PostarAsync(url, NsWsdlMdfeEvt + "/mdfeRecepcaoEvento", corpo, ct);
        return GeradorXmlSefazTms.LerRetornoEvento(ret);
    }

    private async Task<string> PostarAsync(Uri url, string acao, XElement corpo, CancellationToken ct)
    {
        XNamespace soap = "http://www.w3.org/2003/05/soap-envelope";
        var envelope = new XElement(soap + "Envelope", new XAttribute(XNamespace.Xmlns + "soap12", soap), new XElement(soap + "Body", corpo));
        using var conteudo = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8);
        conteudo.Headers.ContentType = MediaTypeHeaderValue.Parse($"application/soap+xml; charset=utf-8; action=\"{acao}\"");
        try
        {
            using var r = await _http.PostAsync(url, conteudo, ct);
            var texto = await r.Content.ReadAsStringAsync(ct);
            if (!r.IsSuccessStatusCode && !texto.Contains("Envelope", StringComparison.Ordinal))
                throw new ErroComunicacaoSefaz($"A SEFAZ ({url.Host}) respondeu HTTP {(int)r.StatusCode} ({r.ReasonPhrase}).");
            return texto;
        }
        catch (HttpRequestException ex) { throw new ErroComunicacaoSefaz($"Sem comunicação com a SEFAZ ({url.Host}): {ex.Message}", ex); }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw new ErroComunicacaoSefaz($"A SEFAZ ({url.Host}) não respondeu a tempo.", ex); }
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
