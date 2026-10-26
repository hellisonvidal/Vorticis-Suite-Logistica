using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Vorticis.Fiscal.Sefaz;

namespace Vorticis.Fiscal.Tests;

public class DistribuicaoDfeTests
{
    internal static string DocZip(string xml)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true)) gz.Write(Encoding.UTF8.GetBytes(xml));
        return Convert.ToBase64String(ms.ToArray());
    }

    internal static string Retorno(int cStat, string motivo, params (string Schema, string Xml)[] docs)
        => "<soap:Envelope xmlns:soap=\"http://www.w3.org/2003/05/soap-envelope\"><soap:Body><nfeDistDFeInteresseResponse xmlns=\"http://www.portalfiscal.inf.br/nfe/wsdl/NFeDistribuicaoDFe\">"
           + $"<nfeDistDFeInteresseResult><retDistDFeInt xmlns=\"http://www.portalfiscal.inf.br/nfe\" versao=\"1.01\"><tpAmb>1</tpAmb><cStat>{cStat}</cStat><xMotivo>{motivo}</xMotivo>"
           + (docs.Length == 0 ? "" : "<loteDistDFeInt>" + string.Concat(docs.Select((d, i) => $"<docZip NSU=\"{i + 1:D15}\" schema=\"{d.Schema}\">{DocZip(d.Xml)}</docZip>")) + "</loteDistDFeInt>")
           + "</retDistDFeInt></nfeDistDFeInteresseResult></nfeDistDFeInteresseResponse></soap:Body></soap:Envelope>";

    [Fact]
    public void Monta_a_consulta_por_chave_e_le_o_retorno_compactado()
    {
        var chave = DigitacaoCalculosTests.ChaveValida("3526036169522700019355001000000001100000000");
        var msg = ClienteNfe.MontarConsultaChave("61695227000193", 35, chave, producao: true);
        Assert.Contains("<cUFAutor>35</cUFAutor><CNPJ>61695227000193</CNPJ><consChNFe><chNFe>" + chave, msg);
        Assert.Contains("<tpAmb>1</tpAmb>", msg);

        var r = ClienteNfe.LerDistribuicao(Retorno(138, "Documento localizado", ("procNFe_v4.00.xsd", "<nfeProc/>")));
        Assert.True(r.Localizado);
        Assert.True(r.Documentos.Single().NotaCompleta);
        Assert.Equal("<nfeProc/>", r.Documentos.Single().Xml);
        Assert.False(ClienteNfe.LerDistribuicao(Retorno(137, "Nenhum documento localizado")).Localizado);
    }

    [Fact]
    public void Valida_a_chave_e_assina_o_evento_de_ciencia()
    {
        Assert.Contains("44 dígitos", ServicoImportacaoChave.Validar("123"));
        Assert.Contains("dígito verificador", ServicoImportacaoChave.Validar(new string('1', 44)));
        var chave = DigitacaoCalculosTests.ChaveValida("3526036169522700019355001000000001100000000");
        Assert.Null(ServicoImportacaoChave.Validar(chave[..4] + " " + chave[4..]));   // espaços e pontos são ignorados

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=EMPRESA TESTE:61695227000193", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1));
        var assinado = ClienteNfe.AssinarEvento(ClienteNfe.MontarCiencia("61695227000193", chave, DateTimeOffset.Now, true), cert);
        Assert.Contains($"Id=\"ID210210{chave}01\"", assinado);
        Assert.Contains($"URI=\"#ID210210{chave}01\"", assinado);
        Assert.Contains("<tpEvento>210210</tpEvento>", assinado);
        Assert.Equal(1, CodigosUf.De("pr") is 41 ? 1 : 0);
    }
}

/// <summary>Cliente da SEFAZ simulado (os testes não acessam a internet).</summary>
internal sealed class ClienteNfeFalso(RespostaDistribuicao resposta) : IClienteNfe
{
    public List<(string Cnpj, int Uf, string Chave)> Consultas { get; } = [];
    public Task<RespostaDistribuicao> ConsultarChaveAsync(string cnpj, int codigoUf, string chave, CancellationToken ct = default)
    {
        Consultas.Add((cnpj, codigoUf, chave));
        return Task.FromResult(resposta);
    }
    public Task<RespostaEvento> ManifestarCienciaAsync(string cnpj, string chave, CancellationToken ct = default) => Task.FromResult(new RespostaEvento(135, "Evento registrado"));
}

public class ImportacaoChaveIntegracaoTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    [Fact]
    public async Task Importa_a_nfe_devolvida_pela_sefaz_e_avisa_quando_so_ha_resumo()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync(XmlsDeTeste.CnpjEmpresa);
        var adm = await db.AdminAsync();
        var svc = db.App.Fiscal().ImportacaoChave;
        var chave = DigitacaoCalculosTests.ChaveValida($"352603{XmlsDeTeste.CnpjFornecedor}55001000000777100000000");
        var xml = XmlsDeTeste.Nfe(XmlsDeTeste.CnpjFornecedor, XmlsDeTeste.CnpjEmpresa, 1, 777, new DateOnly(2026, 3, 10), [new ItemNfe("5102", 500m)], chave: chave);

        var falso = new ClienteNfeFalso(ClienteNfe.LerDistribuicao(DistribuicaoDfeTests.Retorno(138, "Documento localizado", ("procNFe_v4.00.xsd", xml))));
        var r = await svc.ImportarAsync(empresa, chave, falso, adm, ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        Assert.Equal(SituacaoChave.Importada, r.Valor.Situacao);
        Assert.Equal((XmlsDeTeste.CnpjEmpresa, 35), (falso.Consultas[0].Cnpj, falso.Consultas[0].Uf));
        Assert.Equal(SituacaoChave.JaImportada, (await svc.ImportarAsync(empresa, chave, falso, adm, ct)).Valor.Situacao);

        var resumo = new ClienteNfeFalso(ClienteNfe.LerDistribuicao(DistribuicaoDfeTests.Retorno(138, "Documento localizado", ("resNFe_v1.01.xsd", "<resNFe/>"))));
        var outra = DigitacaoCalculosTests.ChaveValida($"352603{XmlsDeTeste.CnpjFornecedor}55001000000778100000000");
        Assert.Equal(SituacaoChave.SomenteResumo, (await svc.ImportarAsync(empresa, outra, resumo, adm, ct)).Valor.Situacao);
        Assert.True((await svc.ManifestarCienciaAsync(empresa, outra, resumo, adm, ct)).Sucesso);
    }
}
