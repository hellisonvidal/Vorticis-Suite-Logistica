using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;

namespace Vorticis.Folha.Esocial;

/// <summary>Ambiente de destino da transmissão. O valor é o do campo tpAmb dos eventos.</summary>
public enum AmbienteEsocial { Producao = 1, ProducaoRestrita = 2 }

/// <summary>Endereços dos web services (Manual de Orientação do Desenvolvedor / portal do eSocial, ambiente de produção restrita e de produção).</summary>
public sealed record EnderecosEsocial(Uri Envio, Uri Consulta)
{
    public static readonly EnderecosEsocial ProducaoRestrita = new(
        new("https://webservices.producaorestrita.esocial.gov.br/servicos/empregador/enviarloteeventos/WsEnviarLoteEventos.svc"),
        new("https://webservices.producaorestrita.esocial.gov.br/servicos/empregador/consultarloteeventos/WsConsultarLoteEventos.svc"));

    public static readonly EnderecosEsocial Producao = new(
        new("https://webservices.envio.esocial.gov.br/servicos/empregador/enviarloteeventos/WsEnviarLoteEventos.svc"),
        new("https://webservices.consulta.esocial.gov.br/servicos/empregador/consultarloteeventos/WsConsultarLoteEventos.svc"));

    public static EnderecosEsocial Do(AmbienteEsocial a) => a == AmbienteEsocial.Producao ? Producao : ProducaoRestrita;
}

public sealed record OcorrenciaEsocial(int Tipo, string Codigo, string Descricao, string? Localizacao)
{
    public string TipoTexto => Tipo switch { 1 => "Erro", 2 => "Advertência", _ => $"Tipo {Tipo}" };
}

/// <summary>Resposta ao envio do lote (status 201/202 = recebido; 203 = já recebido; 301 e 4xx = erro).</summary>
public sealed record RespostaEnvioLote(int Codigo, string Descricao, string? Protocolo, IReadOnlyList<OcorrenciaEsocial> Ocorrencias, string XmlBruto)
{
    public bool Recebido => Codigo is 201 or 202 or 203;
}

/// <summary>Resultado de um evento dentro do lote processado: 201 sucesso, 202 sucesso com advertências, demais = rejeitado.</summary>
public sealed record ResultadoEventoEsocial(string IdEvento, int Codigo, string Descricao, string? Recibo, IReadOnlyList<OcorrenciaEsocial> Ocorrencias)
{
    public bool Aceito => Codigo is 201 or 202;
}

/// <summary>Resposta à consulta do lote: 101 = aguardando processamento; 201/202 = processado; 301/4xx = erro.</summary>
public sealed record RespostaConsultaLote(int Codigo, string Descricao, IReadOnlyList<OcorrenciaEsocial> Ocorrencias,
                                          IReadOnlyList<ResultadoEventoEsocial> Eventos, string XmlBruto)
{
    public bool EmProcessamento => Codigo == 101;
    public bool Processado => Codigo is 201 or 202;
}

public sealed class ErroComunicacaoEsocial(string mensagem, Exception? interna = null) : Exception(mensagem, interna);

public interface IClienteEsocial
{
    Task<RespostaEnvioLote> EnviarLoteAsync(string xmlLote, CancellationToken ct = default);
    Task<RespostaConsultaLote> ConsultarLoteAsync(string protocolo, CancellationToken ct = default);
}

/// <summary>
/// Cliente SOAP 1.1 dos serviços EnviarLoteEventos (v1_1_0) e ConsultarLoteEventos (v1_1_0) do eSocial. A autenticação é TLS mútuo:
/// o certificado ICP-Brasil do transmissor vai no <see cref="HttpClientHandler"/>; o CPF/CNPJ dele precisa coincidir com o
/// ideTransmissor do lote. Use <see cref="Criar"/> em produção; os testes usam o construtor com um servidor HTTP simulado.
/// </summary>
public sealed class ClienteEsocial(HttpClient http, EnderecosEsocial enderecos) : IClienteEsocial
{
    public const string NsEnvio = "http://www.esocial.gov.br/servicos/empregador/lote/eventos/envio/v1_1_0";
    public const string NsConsulta = "http://www.esocial.gov.br/servicos/empregador/lote/eventos/envio/consulta/retornoProcessamento/v1_1_0";
    public const string NsConsultaXsd = "http://www.esocial.gov.br/schema/lote/eventos/envio/consulta/retornoProcessamento/v1_0_0";
    private const string AcaoEnvio = NsEnvio + "/ServicoEnviarLoteEventos/EnviarLoteEventos";
    private const string AcaoConsulta = NsConsulta + "/ServicoConsultarLoteEventos/ConsultarLoteEventos";

    /// <summary>Cliente com TLS mútuo para o ambiente escolhido.</summary>
    public static ClienteEsocial Criar(X509Certificate2 certificado, AmbienteEsocial ambiente, TimeSpan? timeout = null)
    {
        var handler = new SocketsHttpHandler();
        handler.SslOptions.ClientCertificates = [certificado];
        var http = new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(90) };
        return new ClienteEsocial(http, EnderecosEsocial.Do(ambiente));
    }

    public async Task<RespostaEnvioLote> EnviarLoteAsync(string xmlLote, CancellationToken ct = default)
    {
        var lote = XElement.Parse(xmlLote, LoadOptions.PreserveWhitespace).ToString(SaveOptions.DisableFormatting);   // sem a declaração XML
        XNamespace v = NsEnvio;
        var corpo = new XElement(v + "EnviarLoteEventos", new XElement(v + "loteEventos", XElement.Parse(lote, LoadOptions.PreserveWhitespace)));
        var resposta = await PostarAsync(enderecos.Envio, AcaoEnvio, corpo, ct);
        var ret = resposta.Descendants().FirstOrDefault(e => e.Name.LocalName == "retornoEnvioLoteEventos")
                  ?? throw new ErroComunicacaoEsocial("Resposta do eSocial sem retornoEnvioLoteEventos.");
        var status = Filho(ret, "status");
        return new RespostaEnvioLote(Inteiro(status, "cdResposta"), Texto(status, "descResposta") ?? "", Texto(ret, "protocoloEnvio"),
                                     Ocorrencias(status), resposta.ToString(SaveOptions.DisableFormatting));
    }

    public async Task<RespostaConsultaLote> ConsultarLoteAsync(string protocolo, CancellationToken ct = default)
    {
        XNamespace v = NsConsulta, x = NsConsultaXsd;
        var corpo = new XElement(v + "ConsultarLoteEventos", new XElement(v + "consulta",
            new XElement(x + "eSocial", new XElement(x + "consultaLoteEventos", new XElement(x + "protocoloEnvio", protocolo)))));
        var resposta = await PostarAsync(enderecos.Consulta, AcaoConsulta, corpo, ct);
        var ret = resposta.Descendants().FirstOrDefault(e => e.Name.LocalName == "retornoProcessamentoLoteEventos")
                  ?? throw new ErroComunicacaoEsocial("Resposta do eSocial sem retornoProcessamentoLoteEventos.");
        var status = Filho(ret, "status");
        var eventos = new List<ResultadoEventoEsocial>();
        foreach (var ev in ret.Descendants().Where(e => e.Name.LocalName == "evento" && e.Attribute("Id") is not null))
        {
            var proc = ev.Descendants().FirstOrDefault(e => e.Name.LocalName == "processamento");
            if (proc is null) continue;
            eventos.Add(new ResultadoEventoEsocial(ev.Attribute("Id")!.Value, Inteiro(proc, "cdResposta"), Texto(proc, "descResposta") ?? "",
                                                  ev.Descendants().FirstOrDefault(e => e.Name.LocalName == "nrRecibo")?.Value.Trim(), Ocorrencias(proc)));
        }
        return new RespostaConsultaLote(Inteiro(status, "cdResposta"), Texto(status, "descResposta") ?? "", Ocorrencias(status), eventos,
                                        resposta.ToString(SaveOptions.DisableFormatting));
    }

    private async Task<XElement> PostarAsync(Uri destino, string acao, XElement corpo, CancellationToken ct)
    {
        XNamespace s = "http://schemas.xmlsoap.org/soap/envelope/";
        var envelope = new XDocument(new XElement(s + "Envelope", new XAttribute(XNamespace.Xmlns + "soapenv", s), new XElement(s + "Header"), new XElement(s + "Body", corpo)));
        using var req = new HttpRequestMessage(HttpMethod.Post, destino)
        {
            Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), new UTF8Encoding(false), "text/xml"),
        };
        req.Headers.TryAddWithoutValidation("SOAPAction", $"\"{acao}\"");
        HttpResponseMessage res;
        try { res = await http.SendAsync(req, ct); }
        catch (HttpRequestException ex) { throw new ErroComunicacaoEsocial($"Falha de conexão com o eSocial ({destino.Host}): {ex.InnerException?.Message ?? ex.Message}", ex); }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw new ErroComunicacaoEsocial("O eSocial não respondeu no prazo.", ex); }
        using (res)
        {
            var texto = await res.Content.ReadAsStringAsync(ct);
            XElement? xml = null;
            try { xml = XElement.Parse(texto); } catch (System.Xml.XmlException) { }
            if (xml?.Descendants().FirstOrDefault(e => e.Name.LocalName == "faultstring") is { } falha)
                throw new ErroComunicacaoEsocial($"O eSocial devolveu uma falha SOAP: {falha.Value}");
            if (res.StatusCode != HttpStatusCode.OK || xml is null)
                throw new ErroComunicacaoEsocial($"Resposta inesperada do eSocial (HTTP {(int)res.StatusCode}).");
            return xml;
        }
    }

    private static XElement Filho(XElement pai, string nome)
        => pai.Elements().FirstOrDefault(e => e.Name.LocalName == nome) ?? throw new ErroComunicacaoEsocial($"Resposta do eSocial sem o elemento {nome}.");

    private static string? Texto(XElement pai, string nome) => pai.Descendants().FirstOrDefault(e => e.Name.LocalName == nome)?.Value.Trim();

    private static int Inteiro(XElement pai, string nome) => int.TryParse(Texto(pai, nome), out var n) ? n : 0;

    private static IReadOnlyList<OcorrenciaEsocial> Ocorrencias(XElement pai)
        => pai.Descendants().Where(e => e.Name.LocalName == "ocorrencia")
              .Select(o => new OcorrenciaEsocial(Inteiro(o, "tipo"), Texto(o, "codigo") ?? "", Texto(o, "descricao") ?? "", Texto(o, "localizacao"))).ToList();
}
