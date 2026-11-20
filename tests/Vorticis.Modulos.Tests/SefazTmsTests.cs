using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Vorticis.Fiscal.Sefaz;
using Vorticis.Frota;
using Vorticis.Infra.Tests;

namespace Vorticis.Modulos.Tests;

internal static class SefazTesteApoio
{
    internal static X509Certificate2 CriarCertificado()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=Transportadora Teste LTDA, OU=04252011000110", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    internal static ParteFiscal Emitente(string cnpj = "04252011000110", string uf = "SP") =>
        new(cnpj, "Transportadora Teste LTDA", "TransTeste", "123456789012", uf, "3509502", "Campinas", "13010000",
            "Av. Brasil", "100", null, "Centro", "1932321000", "fiscal@teste.com");

    internal static ParteFiscal Parte(string doc, string nome, string uf, string ibge, string mun, string? ie = null) =>
        new(doc, nome, null, ie, uf, ibge, mun, "01001000", "Rua A", "10", null, "Centro", null, null);
}

internal sealed class ClienteSefazFalso : IClienteSefazTms
{
    public List<(int Ambiente, int TipoEmissao, string Xml)> CtesEnviados { get; } = [];
    public List<(int Ambiente, string Xml)> MdfesEnviados { get; } = [];
    public List<(string Tipo, int TipoEmissao, string Xml)> EventosEnviados { get; } = [];

    public bool FalharCteNormal { get; set; }
    public int? RejeitarCteNormalComCStat { get; set; }
    public int CStatEvento { get; set; } = 135;

    public Task<RespostaAutorizacaoSefaz> AutorizarCteAsync(string uf, int ambiente, int tipoEmissao, string xmlAssinado, CancellationToken ct = default)
    {
        CtesEnviados.Add((ambiente, tipoEmissao, xmlAssinado));
        if (tipoEmissao == EndpointsSefazTms.EmissaoNormal && FalharCteNormal)
            throw new ErroComunicacaoSefaz("SEFAZ SP indisponível (timeout simulado).");
        if (tipoEmissao == EndpointsSefazTms.EmissaoNormal && RejeitarCteNormalComCStat is { } cs)
            return Task.FromResult(new RespostaAutorizacaoSefaz(cs, "Serviço paralisado momentaneamente", null, null, null, null, $"<retCTe><cStat>{cs}</cStat></retCTe>"));

        var doc = XDocument.Parse(xmlAssinado);
        var id = doc.Descendants().First(e => e.Name.LocalName == "infCte").Attribute("Id")!.Value;
        var chave = id["CTe".Length..];
        const string prot = "135260000999888";
        var protXml = $"<protCTe xmlns=\"{GeradorXmlSefazTms.NsCte}\" versao=\"4.00\"><infProt><tpAmb>{ambiente}</tpAmb><chCTe>{chave}</chCTe><dhRecbto>2026-06-01T12:00:00-03:00</dhRecbto><nProt>{prot}</nProt><cStat>100</cStat><xMotivo>Autorizado o uso do CT-e</xMotivo></infProt></protCTe>";
        return Task.FromResult(new RespostaAutorizacaoSefaz(100, "Autorizado o uso do CT-e", prot, chave, DateTimeOffset.UtcNow, protXml, $"<retCTe>{protXml}</retCTe>"));
    }

    public Task<RespostaEventoSefaz> EnviarEventoCteAsync(string uf, int ambiente, int tipoEmissao, string xmlEventoAssinado, CancellationToken ct = default)
    {
        EventosEnviados.Add(("CTE", tipoEmissao, xmlEventoAssinado));
        var mot = CStatEvento is 135 or 136 ? "Evento registrado e vinculado ao CT-e" : "Evento rejeitado";
        return Task.FromResult(new RespostaEventoSefaz(CStatEvento, mot, CStatEvento is 135 or 136 ? "135260000999999" : null, $"<retEventoCTe><cStat>{CStatEvento}</cStat></retEventoCTe>"));
    }

    public Task<RespostaAutorizacaoSefaz> AutorizarMdfeAsync(int ambiente, string xmlAssinado, CancellationToken ct = default)
    {
        MdfesEnviados.Add((ambiente, xmlAssinado));
        var doc = XDocument.Parse(xmlAssinado);
        var id = doc.Descendants().First(e => e.Name.LocalName == "infMDFe").Attribute("Id")!.Value;
        var chave = id["MDFe".Length..];
        const string prot = "999260000888777";
        var protXml = $"<protMDFe xmlns=\"{GeradorXmlSefazTms.NsMdfe}\" versao=\"3.00\"><infProt><tpAmb>{ambiente}</tpAmb><chMDFe>{chave}</chMDFe><dhRecbto>2026-06-01T13:00:00-03:00</dhRecbto><nProt>{prot}</nProt><cStat>100</cStat><xMotivo>Autorizado o uso do MDF-e</xMotivo></infProt></protMDFe>";
        return Task.FromResult(new RespostaAutorizacaoSefaz(100, "Autorizado o uso do MDF-e", prot, chave, DateTimeOffset.UtcNow, protXml, $"<retMDFe>{protXml}</retMDFe>"));
    }

    public Task<RespostaEventoSefaz> EnviarEventoMdfeAsync(int ambiente, string xmlEventoAssinado, CancellationToken ct = default)
    {
        EventosEnviados.Add(("MDFE", 1, xmlEventoAssinado));
        return Task.FromResult(new RespostaEventoSefaz(CStatEvento, "Evento registrado e vinculado ao MDF-e", "999260000888999", $"<retEventoMDFe><cStat>{CStatEvento}</cStat></retEventoMDFe>"));
    }
}

public class SefazTmsUnitariosTests
{
    [Fact]
    public void Chave_acesso_valida_em_emissao_normal_svc_rs_svc_sp_e_mdfe()
    {
        var em = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.FromHours(-3));
        var cNormal = GeradorXmlSefazTms.MontarChave(35, em, "04252011000110", RegrasTms.ModeloCte, 1, 42, EndpointsSefazTms.EmissaoNormal, "12345678");
        var cSvcRs = GeradorXmlSefazTms.MontarChave(35, em, "04252011000110", RegrasTms.ModeloCte, 1, 42, EndpointsSefazTms.ContingenciaSvcRs, "12345678");
        var cSvcSp = GeradorXmlSefazTms.MontarChave(31, em, "04252011000110", RegrasTms.ModeloCte, 1, 42, EndpointsSefazTms.ContingenciaSvcSp, "12345678");
        var cMdfe = GeradorXmlSefazTms.MontarChave(35, em, "04252011000110", RegrasTms.ModeloMdfe, 1, 7, EndpointsSefazTms.EmissaoNormal, "87654321");

        Assert.True(RegrasTms.ChaveValida(cNormal, RegrasTms.ModeloCte));
        Assert.Equal('1', cNormal[34]);
        Assert.True(RegrasTms.ChaveValida(cSvcRs, RegrasTms.ModeloCte));
        Assert.Equal('7', cSvcRs[34]);
        Assert.True(RegrasTms.ChaveValida(cSvcSp, RegrasTms.ModeloCte));
        Assert.Equal('8', cSvcSp[34]);
        Assert.True(RegrasTms.ChaveValida(cMdfe, RegrasTms.ModeloMdfe));
    }

    [Fact]
    public void Roteamento_svc_segue_tabela_sefaz()
    {
        foreach (var uf in new[] { "SP", "PR", "MT", "MS", "AP", "PE", "RR" })
            Assert.Equal(EndpointsSefazTms.ContingenciaSvcRs, EndpointsSefazTms.TipoContingenciaSvc(uf));
        foreach (var uf in new[] { "MG", "RJ", "RS", "BA", "GO", "SC" })
            Assert.Equal(EndpointsSefazTms.ContingenciaSvcSp, EndpointsSefazTms.TipoContingenciaSvc(uf));

        Assert.Contains("svrs.rs.gov.br", EndpointsSefazTms.UrlRecepcaoCte("SP", 2, EndpointsSefazTms.ContingenciaSvcRs).Host);
        Assert.Contains("fazenda.sp.gov.br", EndpointsSefazTms.UrlRecepcaoCte("MG", 1, EndpointsSefazTms.ContingenciaSvcSp).Host);
        Assert.Contains("mdfe-homologacao.svrs.rs.gov.br", EndpointsSefazTms.UrlRecepcaoMdfe(2).Host);
    }

    [Fact]
    public void Xml_cte_400_gera_ibscbs_svc_e_assinatura_xmldsig_detecta_adulteracao()
    {
        using var cert = SefazTesteApoio.CriarCertificado();
        var frete = RegrasTms.Calcular(RegrasTmsTests.Tabela(), 1000m, 4m, 50000m, coleta: true, entrega: true);
        var ibs = GeradorXmlSefazTms.CalcularIbsCbs(frete.Total, new ParametrosIbsCbs("000", "000001", 0.10m, 0m, 0.90m, true));
        Assert.Equal((1.11m, 10.02m), (ibs.IbsTotal, ibs.Cbs));

        var emit = SefazTesteApoio.Emitente();
        var rem = SefazTesteApoio.Parte("11222333000181", "Indústria Alfa", "SP", "3509502", "Campinas", "123456789");
        var dest = SefazTesteApoio.Parte("27183946000359", "Comércio Beta", "RJ", "3304557", "Rio de Janeiro");
        var tom = SefazTesteApoio.Parte("60701190000104", "Tomador Terceiro", "SP", "3550308", "São Paulo");
        var (chave, xml) = GeradorXmlSefazTms.GerarXmlCte(new DadosXmlCte(
            2, EndpointsSefazTms.ContingenciaSvcRs, new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.FromHours(-3)),
            "12345678", 1, 5, "6353", "PRESTACAO INTERESTADUAL", 4, RegrasTmsTests.Campinas, RegrasTmsTests.Rio,
            emit, rem, dest, tom, "Peças", 1000m, 4m, 10, 50000m, frete, [RegrasTmsTests.Chave("11222333000181", "55", 1, 99)], "12345678", ibs));

        Assert.True(RegrasTms.ChaveValida(chave, RegrasTms.ModeloCte));
        var doc = XDocument.Parse(xml);
        Assert.Equal(GeradorXmlSefazTms.NsCte, doc.Root!.Name.NamespaceName);
        Assert.NotNull(doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "IBSCBS"));
        Assert.NotNull(doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "toma4"));
        Assert.NotNull(doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "dhCont"));
        Assert.Equal("0.1000", doc.Descendants().First(e => e.Name.LocalName == "pIBSUF").Value);
        Assert.Equal("10.02", doc.Descendants().First(e => e.Name.LocalName == "vCBS").Value);

        var assinado = GeradorXmlSefazTms.AssinarXml(xml, "infCte", cert);
        Assert.True(GeradorXmlSefazTms.VerificarAssinatura(assinado));

        var adulterado = assinado.Replace("<vTPrest>1113.64</vTPrest>", "<vTPrest>1.00</vTPrest>");
        Assert.False(GeradorXmlSefazTms.VerificarAssinatura(adulterado));
    }

    [Fact]
    public void Xml_mdfe_300_inclui_ciot_seguro_e_assinatura_valida()
    {
        using var cert = SefazTesteApoio.CriarCertificado();
        var cteChave = RegrasTmsTests.Chave("04252011000110", "57", 1, 1);
        var (chave, xml) = GeradorXmlSefazTms.GerarXmlMdfe(new DadosXmlMdfe(
            2, new DateTimeOffset(2026, 6, 1, 11, 0, 0, TimeSpan.FromHours(-3)), "87654321", 1, 3, "SP", "RJ", ["MG"],
            RegrasTmsTests.Campinas, RegrasTmsTests.Rio, SefazTesteApoio.Emitente(), "12345678", "BRA2E19", "12345678901", 25000m, 0,
            "01", "00", "Pedro Alves", "52998224725", [cteChave], 1000m, 50000m,
            [new CiotParaMdfe("123456789012", "52998224725", "87654321", 150m)],
            "Porto Seguro", "61198164000160", "AP-2026-01", "AV-2026-01"));

        Assert.True(RegrasTms.ChaveValida(chave, RegrasTms.ModeloMdfe));
        var doc = XDocument.Parse(xml);
        Assert.NotNull(doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "infCIOT"));
        Assert.NotNull(doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "valePed"));
        Assert.NotNull(doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "seg"));

        var assinado = GeradorXmlSefazTms.AssinarXml(xml, "infMDFe", cert);
        Assert.True(GeradorXmlSefazTms.VerificarAssinatura(assinado));
        Assert.False(GeradorXmlSefazTms.VerificarAssinatura(assinado.Replace("BRA2E19", "AAA0000")));
    }

    [Fact]
    public void Eventos_gzip_e_parsers_soap_funcionam()
    {
        using var cert = SefazTesteApoio.CriarCertificado();
        var cteChave = RegrasTmsTests.Chave("04252011000110", "57", 1, 9);
        var mdfeChave = RegrasTmsTests.Chave("04252011000110", "58", 1, 2);

        var canc = GeradorXmlSefazTms.AssinarXml(
            GeradorXmlSefazTms.GerarEventoCancelamentoCte("SP", 2, "04252011000110", cteChave, "135260000123456", "Erro no valor do frete informado", DateTimeOffset.UtcNow),
            "infEvento", cert);
        var enc = GeradorXmlSefazTms.AssinarXml(
            GeradorXmlSefazTms.GerarEventoEncerramentoMdfe("SP", 2, "04252011000110", mdfeChave, "999260000123456", new DateOnly(2026, 6, 2), "RJ", "3304557", DateTimeOffset.UtcNow),
            "infEvento", cert);

        Assert.True(GeradorXmlSefazTms.VerificarAssinatura(canc));
        Assert.True(GeradorXmlSefazTms.VerificarAssinatura(enc));

        var zip = GeradorXmlSefazTms.CompactarBase64Gzip(canc);
        Assert.Equal(canc, GeradorXmlSefazTms.DescompactarBase64Gzip(zip));

        var soapCte = $"""
            <soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope"><soap:Body>
              <retCTe xmlns="http://www.portalfiscal.inf.br/cte" versao="4.00">
                <protCTe versao="4.00"><infProt><cStat>100</cStat><xMotivo>Autorizado</xMotivo><chCTe>{cteChave}</chCTe><nProt>135260000000001</nProt></infProt></protCTe>
              </retCTe>
            </soap:Body></soap:Envelope>
            """;
        var resp = GeradorXmlSefazTms.LerRetornoCte(soapCte);
        Assert.True(resp.Autorizado);
        Assert.Equal((cteChave, "135260000000001"), (resp.Chave, resp.Protocolo));
    }
}

public class SefazTmsIntegracaoTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    [Fact]
    public async Task Transmitir_cte_aciona_svc_automatico_grava_ibscbs_audita_e_cancela()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        const string cnpj = "04252011000110";
        var empresa = await db.CriarEmpresaAsync(cnpj);
        await db.EscalarAsync<int>("UPDATE core.empresa SET modulo_frota = TRUE WHERE id = @e RETURNING 1", ("e", empresa));
        var svc = ServicosFrota.De(db.App);

        var alfa = await db.EscalarAsync<long>("""
            INSERT INTO core.participante (empresa_id, codigo, nome, cpf_cnpj, uf, cod_municipio_ibge, municipio)
            VALUES (@e, 'S1', 'Indústria Alfa', '11222333000181', 'SP', '3509502', 'Campinas') RETURNING id
            """, ("e", empresa));
        var beta = await db.EscalarAsync<long>("""
            INSERT INTO core.participante (empresa_id, codigo, nome, cpf_cnpj, uf, cod_municipio_ibge, municipio)
            VALUES (@e, 'S2', 'Comércio Beta', '27183946000359', 'RJ', '3304557', 'Rio de Janeiro') RETURNING id
            """, ("e", empresa));

        // Campos opcionais vazios continuam aceitos (compatível com ParametrosFrotaViewModel)
        Assert.True((await svc.GravarConfiguracaoSefazAsync(empresa, new ConfiguracaoSefazTms(), adm, ct)).Sucesso);
        Assert.True((await svc.GravarConfiguracaoSefazAsync(empresa, new ConfiguracaoSefazTms(
            2, "12345678", true, "Porto Seguro", "61198164000160", "AP-2026", "AV-2026"), adm, ct)).Sucesso);

        var cte = await svc.GravarCteAsync(empresa, RegrasTmsTests.NovoCte(alfa, beta, alfa) with { Emissao = new DateOnly(2026, 6, 1) }, adm, ct);
        Assert.True(cte.Sucesso, string.Join(" ", cte.Erros));

        using var cert = SefazTesteApoio.CriarCertificado();
        var falso = new ClienteSefazFalso { FalharCteNormal = true };
        var res = await svc.TransmitirCteAsync(empresa, cte.Valor.Id, cert, adm, falso, ct);
        Assert.True(res.Sucesso, string.Join(" ", res.Erros));
        Assert.True(res.Valor.UsouContingenciaSvc);
        Assert.Equal(EndpointsSefazTms.ContingenciaSvcRs, res.Valor.TipoEmissao);
        Assert.True(res.Valor.Ibs > 0 && res.Valor.Cbs > 0);
        Assert.Equal(2, falso.CtesEnviados.Count);

        var emitido = (await svc.CtesAsync(empresa, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null, adm, ct)).Single(c => c.Id == cte.Valor.Id);
        Assert.Equal("EMITIDO", emitido.Situacao);
        Assert.True(emitido.TemXmlAutorizado);
        Assert.Equal(EndpointsSefazTms.ContingenciaSvcRs, emitido.TipoEmissao);

        var xmlAut = await svc.ObterXmlAutorizadoAsync(empresa, RegrasTms.ModeloCte, cte.Valor.Id, adm, ct);
        Assert.True(xmlAut.Sucesso);
        Assert.EndsWith("-procCTe.xml", xmlAut.Valor.NomeArquivo);
        Assert.Contains("<IBSCBS>", xmlAut.Valor.Xml);

        // Cancelamento na SEFAZ (110111)
        Assert.False((await svc.CancelarCteSefazAsync(empresa, cte.Valor.Id, "curta", cert, adm, falso, ct)).Sucesso);
        var canc = await svc.CancelarCteSefazAsync(empresa, cte.Valor.Id, "Cancelamento por erro operacional de digitação", cert, adm, falso, ct);
        Assert.True(canc.Sucesso, string.Join(" ", canc.Erros));

        var cancelado = (await svc.CtesAsync(empresa, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null, adm, ct)).Single(c => c.Id == cte.Valor.Id);
        Assert.Equal("CANCELADO", cancelado.Situacao);

        var hist = await svc.TransmissoesSefazAsync(empresa, RegrasTms.ModeloCte, cte.Valor.Id, adm, ct);
        Assert.Equal(3, hist.Count); // tentativa normal (falha), autorização SVC-RS (100), cancelamento (135)
    }

    [Fact]
    public async Task Transmitir_mdfe_com_ciot_e_encerrar_na_sefaz()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        const string cnpj = "60701190000104";
        var empresa = await db.CriarEmpresaAsync(cnpj);
        await db.EscalarAsync<int>("UPDATE core.empresa SET modulo_frota = TRUE WHERE id = @e RETURNING 1", ("e", empresa));
        var svc = ServicosFrota.De(db.App);

        Assert.True((await svc.GravarConfiguracaoSefazAsync(empresa, new ConfiguracaoSefazTms(
            2, "12345678", true, "Porto Seguro", "61198164000160", "AP-99", "AV-99"), adm, ct)).Sucesso);

        var rem = await db.EscalarAsync<long>("""
            INSERT INTO core.participante (empresa_id, codigo, nome, cpf_cnpj, uf, cod_municipio_ibge, municipio)
            VALUES (@e, 'M1', 'Remetente', '11222333000181', 'SP', '3509502', 'Campinas') RETURNING id
            """, ("e", empresa));
        var dest = await db.EscalarAsync<long>("""
            INSERT INTO core.participante (empresa_id, codigo, nome, cpf_cnpj, uf, cod_municipio_ibge, municipio)
            VALUES (@e, 'M2', 'Destinatário', '27183946000359', 'RJ', '3304557', 'Rio de Janeiro') RETURNING id
            """, ("e", empresa));
        var mot = (await svc.GravarMotoristaAsync(empresa, new Motorista(0, 1, "Pedro Alves", "52998224725", null, null, null, null), adm, ct)).Valor;
        var vei = (await svc.GravarVeiculoAsync(empresa, new Veiculo(0, 1, "BRA2E19", null, "CAVALO", null, null, null, null, "DIESEL_S10", 25000m, 0, null), adm, ct)).Valor;

        var cte = (await svc.GravarCteAsync(empresa, RegrasTmsTests.NovoCte(rem, dest, rem) with { Emissao = new DateOnly(2026, 6, 1) }, adm, ct)).Valor;
        using var cert = SefazTesteApoio.CriarCertificado();
        var falso = new ClienteSefazFalso();
        Assert.True((await svc.TransmitirCteAsync(empresa, cte.Id, cert, adm, falso, ct)).Sucesso);

        var ciotId = await db.EscalarAsync<long>("""
            INSERT INTO tms.ciot (empresa_id, numero, ciot, codigo_verificador, ipef, contratado_id, rntrc, placa,
                                  origem_uf, origem_ibge, origem_municipio, destino_uf, destino_ibge, destino_municipio,
                                  inicio, fim, valor_frete, vale_pedagio, situacao)
            VALUES (@e, 1, '123456789012', '1234', 'Repom', @p, '12345678', 'BRA2E19',
                    'SP', '3509502', 'Campinas', 'RJ', '3304557', 'Rio de Janeiro',
                    '2026-06-01', '2026-06-05', 1000.00, 120.00, 'DECLARADO') RETURNING id
            """, ("e", empresa), ("p", rem));
        await db.EscalarAsync<long>("WITH x AS (INSERT INTO tms.ciot_cte (ciot_id, cte_id) VALUES (@c, @t) RETURNING 1) SELECT count(*) FROM x",
                                    ("c", ciotId), ("t", cte.Id));

        var man = await svc.GravarManifestoAsync(empresa, new Manifesto(0, 1, 0, new DateOnly(2026, 6, 1), "SP", "RJ", "MG", vei, mot, null, null, "ABERTO", null, null, [cte.Id]), adm, ct);
        Assert.True(man.Sucesso, string.Join(" ", man.Erros));

        var trMdfe = await svc.TransmitirMdfeAsync(empresa, man.Valor.Id, cert, adm, falso, ct);
        Assert.True(trMdfe.Sucesso, string.Join(" ", trMdfe.Erros));
        Assert.Single(falso.MdfesEnviados);
        Assert.Contains("<infCIOT>", falso.MdfesEnviados[0].Xml);
        Assert.Contains("<valePed>", falso.MdfesEnviados[0].Xml);
        Assert.Contains("<seg>", falso.MdfesEnviados[0].Xml);

        var xmlMdfe = await svc.ObterXmlAutorizadoAsync(empresa, RegrasTms.ModeloMdfe, man.Valor.Id, adm, ct);
        Assert.True(xmlMdfe.Sucesso);
        Assert.EndsWith("-procMDFe.xml", xmlMdfe.Valor.NomeArquivo);

        var enc = await svc.EncerrarMdfeSefazAsync(empresa, man.Valor.Id, new DateOnly(2026, 6, 2), cert, adm, falso, ct);
        Assert.True(enc.Sucesso, string.Join(" ", enc.Erros));
        var encerrado = (await svc.ManifestosAsync(empresa, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), adm, ct)).Single(m => m.Id == man.Valor.Id);
        Assert.Equal("ENCERRADO", encerrado.Situacao);
    }

    [Fact]
    public async Task Permissao_FROTA_TMS_exigida_para_transmissao()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("33000167000101");
        await db.EscalarAsync<int>("UPDATE core.empresa SET modulo_frota = TRUE WHERE id = @e RETURNING 1", ("e", empresa));
        var svc = ServicosFrota.De(db.App);

        var usuario = await db.EscalarAsync<long>("""
            INSERT INTO core.usuario (login, nome, senha_hash, perfil_id) VALUES ('consulta_sefaz_tms', 'Consulta', 'x', (SELECT id FROM core.perfil WHERE nome = 'Consulta')) RETURNING id
            """);
        await db.EscalarAsync<int>("INSERT INTO core.usuario_empresa (usuario_id, empresa_id, perfil_id) SELECT @u, @e, perfil_id FROM core.usuario WHERE id = @u RETURNING 1",
                                   ("u", usuario), ("e", empresa));

        using var cert = SefazTesteApoio.CriarCertificado();
        var falso = new ClienteSefazFalso();
        Assert.False((await svc.GravarConfiguracaoSefazAsync(empresa, new ConfiguracaoSefazTms(), usuario, ct)).Sucesso);
        Assert.False((await svc.TransmitirCteAsync(empresa, 1, cert, usuario, falso, ct)).Sucesso);
        Assert.False((await svc.CancelarCteSefazAsync(empresa, 1, "Justificativa com mais de quinze chars", cert, usuario, falso, ct)).Sucesso);
        Assert.False((await svc.TransmitirMdfeAsync(empresa, 1, cert, usuario, falso, ct)).Sucesso);
        Assert.False((await svc.EncerrarMdfeSefazAsync(empresa, 1, new DateOnly(2026, 6, 2), cert, usuario, falso, ct)).Sucesso);
        Assert.True((await svc.GravarConfiguracaoSefazAsync(empresa, new ConfiguracaoSefazTms(), adm, ct)).Sucesso);
    }
}
