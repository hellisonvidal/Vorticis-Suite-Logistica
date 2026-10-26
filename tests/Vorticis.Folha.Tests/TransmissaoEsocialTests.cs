using System.Net;
using System.Text;
using System.Xml.Linq;
using Vorticis.Application.Cadastros;
using Vorticis.Domain.Comum;
using Vorticis.Folha.Esocial;
using Vorticis.Folha.Servicos;

namespace Vorticis.Folha.Tests;

/// <summary>Servidor HTTP local que imita EnviarLoteEventos/ConsultarLoteEventos (nunca acessa a Receita).</summary>
internal sealed class EsocialSimulado : IDisposable
{
    private readonly HttpListener _http = new();
    private readonly Task _laco;
    public string Url { get; }
    public List<string> LotesRecebidos { get; } = [];
    public List<string> Acoes { get; } = [];
    public int ConsultasAteProcessar { get; set; } = 1;
    private int _consultas;
    /// <summary>IdEvento → rejeitar (código 401 com ocorrência)?</summary>
    public HashSet<string> Rejeitar { get; } = [];
    public int CodigoEnvio { get; set; } = 201;

    public EsocialSimulado()
    {
        var porta = new Random().Next(20000, 40000);
        Url = $"http://127.0.0.1:{porta}/";
        _http.Prefixes.Add(Url);
        _http.Start();
        _laco = Task.Run(async () =>
        {
            while (_http.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _http.GetContextAsync(); } catch { return; }
                var corpo = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
                var acao = ctx.Request.Headers["SOAPAction"] ?? "";
                Acoes.Add(acao);
                var resposta = acao.Contains("EnviarLoteEventos") ? Envio(corpo) : Consulta();
                var bytes = Encoding.UTF8.GetBytes(resposta);
                ctx.Response.ContentType = "text/xml; charset=utf-8";
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        });
    }

    private static string Soap(string conteudo) => $"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body>{conteudo}</s:Body></s:Envelope>";

    private string Envio(string corpo)
    {
        LotesRecebidos.Add(corpo);
        if (CodigoEnvio != 201)
            return Soap("<EnviarLoteEventosResponse xmlns=\"http://www.esocial.gov.br/servicos/empregador/lote/eventos/envio/v1_1_0\"><EnviarLoteEventosResult><eSocial xmlns=\"http://www.esocial.gov.br/schema/lote/eventos/envio/retornoEnvio/v1_1_0\"><retornoEnvioLoteEventos><status><cdResposta>" + CodigoEnvio + "</cdResposta><descResposta>Lote recusado</descResposta><ocorrencias><ocorrencia><tipo>1</tipo><codigo>411</codigo><descricao>Assinante inválido.</descricao></ocorrencia></ocorrencias></status></retornoEnvioLoteEventos></eSocial></EnviarLoteEventosResult></EnviarLoteEventosResponse>");
        return Soap($"<EnviarLoteEventosResponse xmlns=\"http://www.esocial.gov.br/servicos/empregador/lote/eventos/envio/v1_1_0\"><EnviarLoteEventosResult><eSocial xmlns=\"http://www.esocial.gov.br/schema/lote/eventos/envio/retornoEnvio/v1_1_0\"><retornoEnvioLoteEventos><status><cdResposta>201</cdResposta><descResposta>Lote recebido com sucesso.</descResposta></status><dadosRecepcaoLote><dhRecepcao>2026-10-23T10:00:00</dhRecepcao><versaoAplicativoRecepcao>1.0</versaoAplicativoRecepcao><protocoloEnvio>1.2.2026100000000000{LotesRecebidos.Count:000}</protocoloEnvio></dadosRecepcaoLote></retornoEnvioLoteEventos></eSocial></EnviarLoteEventosResult></EnviarLoteEventosResponse>");
    }

    private string Consulta()
    {
        var ultimo = LotesRecebidos[^1];
        var ns = "http://www.esocial.gov.br/servicos/empregador/lote/eventos/envio/consulta/retornoProcessamento/v1_1_0";
        if (++_consultas <= ConsultasAteProcessar)
            return Soap($"<ConsultarLoteEventosResponse xmlns=\"{ns}\"><ConsultarLoteEventosResult><eSocial xmlns=\"x\"><retornoProcessamentoLoteEventos><status><cdResposta>101</cdResposta><descResposta>Lote aguardando processamento.</descResposta></status></retornoProcessamentoLoteEventos></eSocial></ConsultarLoteEventosResult></ConsultarLoteEventosResponse>");
        _consultas = 0;
        var ids = XDocument.Parse(ultimo).Descendants().Where(e => e.Name.LocalName == "evento" && e.Attribute("Id") is not null).Select(e => e.Attribute("Id")!.Value).ToList();
        var sb = new StringBuilder();
        foreach (var id in ids)
            sb.Append(Rejeitar.Contains(id)
                ? $"<evento Id=\"{id}\"><retornoEvento><eSocial xmlns=\"r\"><retornoEvento Id=\"{id}\"><processamento><cdResposta>401</cdResposta><descResposta>Erro</descResposta><ocorrencias><ocorrencia><tipo>1</tipo><codigo>MS0001</codigo><descricao>CNAE inválido.</descricao><localizacao>/eSocial/evtTabEstab</localizacao></ocorrencia></ocorrencias></processamento></retornoEvento></eSocial></retornoEvento></evento>"
                : $"<evento Id=\"{id}\"><retornoEvento><eSocial xmlns=\"r\"><retornoEvento Id=\"{id}\"><processamento><cdResposta>201</cdResposta><descResposta>Sucesso</descResposta></processamento><recibo><nrRecibo>1.2.{id[^10..]}00000000000000000</nrRecibo></recibo></retornoEvento></eSocial></retornoEvento></evento>");
        return Soap($"<ConsultarLoteEventosResponse xmlns=\"{ns}\"><ConsultarLoteEventosResult><eSocial xmlns=\"x\"><retornoProcessamentoLoteEventos><status><cdResposta>201</cdResposta><descResposta>Lote processado com sucesso.</descResposta></status><retornoEventos>{sb}</retornoEventos></retornoProcessamentoLoteEventos></eSocial></ConsultarLoteEventosResult></ConsultarLoteEventosResponse>");
    }

    public void Dispose() { _http.Stop(); _http.Close(); }
}

public class TransmissaoEsocialTests(BancoFolha db) : IClassFixture<BancoFolha>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static void Ok(Resultado r) => Assert.True(r.Sucesso, string.Join("; ", r.Erros));

    private async Task<(long Empresa, long Adm)> PrepararAsync(string cnpj)
    {
        BancoFolha.ExigirBanco();
        var empresa = await db.CriarEmpresaAsync(cnpj);
        var adm = await db.AdminAsync();
        var cad = db.Folha.Cadastros;
        var cargo = (await cad.GravarCargoAsync(empresa, new Vorticis.Folha.Cadastros.Cargo(0, "ANL", "Analista", "252105"), adm, Ct)).Valor;
        Ok(await cad.GravarFuncionarioAsync(empresa, FolhaIntegracaoTests.Novo("1", "Maria da Silva", "52998224725", 3000m, cargo: cargo), adm, ct: Ct));
        return (empresa, adm);
    }

    [Fact]
    public async Task Assina_envia_em_ordem_de_grupos_consulta_e_grava_recibo_e_ocorrencias()
    {
        var (empresa, adm) = await PrepararAsync("11222333000181");
        var competencia = new DateOnly(2026, 6, 1);
        var es = db.Folha.ESocial;
        var tx = db.Folha.Transmissao;
        var gerar = await es.GerarAsync(empresa, competencia, ["S-1000", "S-1005", "S-1020", "S-2200"], adm, Ct);
        Ok(gerar);
        Assert.Empty(gerar.Valor.Avisos);
        var ids = (await es.ListarAsync(empresa, competencia, adm, Ct)).Select(e => e.Id).ToList();

        using var servidor = new EsocialSimulado();
        tx.FabricaCliente = (_, _) => new ClienteEsocial(new HttpClient(), new EnderecosEsocial(new Uri(servidor.Url + "envio"), new Uri(servidor.Url + "consulta")));
        using var certEmpregador = AssinaturaEsocialTests.CertificadoTeste("EMPRESA DE TESTE LTDA:11222333000181");

        // Regras de segurança do envio
        Assert.Contains("PRODUÇÃO", (await tx.AssinarEEnviarAsync(empresa, competencia, ids, AmbienteEsocial.Producao, PapelTransmissor.Empregador, certEmpregador, adm, ct: Ct)).Erros[0]);
        using var certOutro = AssinaturaEsocialTests.CertificadoTeste("OUTRA EMPRESA:99888777000166");
        Assert.Contains("não é do empregador", (await tx.AssinarEEnviarAsync(empresa, competencia, ids, AmbienteEsocial.ProducaoRestrita, PapelTransmissor.Empregador, certOutro, adm, ct: Ct)).Erros[0]);
        Assert.Contains("Não há procurador", (await tx.AssinarEEnviarAsync(empresa, competencia, ids, AmbienteEsocial.ProducaoRestrita, PapelTransmissor.Procurador, certEmpregador, adm, ct: Ct)).Erros[0]);
        Assert.Empty(servidor.Acoes);

        // 1º envio: só o grupo de tabelas; os não periódicos esperam
        var env = await tx.AssinarEEnviarAsync(empresa, competencia, ids, AmbienteEsocial.ProducaoRestrita, PapelTransmissor.Empregador, certEmpregador, adm, ct: Ct);
        Ok(env);
        Assert.Equal((4, 3, 1), (env.Valor.Assinados, env.Valor.Enviados, env.Valor.Lotes));
        Assert.Contains(env.Valor.Avisos, a => a.Contains("Não periódicos"));
        Assert.Contains("EnviarLoteEventos", Assert.Single(servidor.Acoes));
        var lote = XDocument.Parse(servidor.LotesRecebidos[0], LoadOptions.PreserveWhitespace);   // o servidor também preserva o texto exato assinado
        Assert.Empty(ValidadorXsdEsocial.ValidarLote(lote.Descendants().First(e => e.Name.LocalName == "eSocial" && e.Name.NamespaceName.EndsWith("v1_1_1")).ToString()));
        var eventosLote = lote.Descendants().Where(e => e.Name.LocalName == "evento" && e.Attribute("Id") is not null).ToList();
        Assert.Equal(3, eventosLote.Count);
        foreach (var ev in eventosLote)
        {
            var xmlEvento = ev.Elements().Single().ToString(SaveOptions.DisableFormatting);
            Assert.True(AssinadorEsocial.Verificar(xmlEvento, out _), "assinatura do evento dentro do lote deve conferir");
            Assert.Contains("<tpAmb>2</tpAmb>", xmlEvento);
            Assert.Empty(ValidadorXsdEsocial.ValidarEvento(xmlEvento));
        }
        Assert.Equal("1", lote.Descendants().First(e => e.Name.LocalName == "envioLoteEventos").Attribute("grupo")!.Value);
        Assert.Equal("11222333", lote.Descendants().First(e => e.Name.LocalName == "ideEmpregador").Elements().Last().Value);
        Assert.Equal("11222333000181", lote.Descendants().First(e => e.Name.LocalName == "ideTransmissor").Elements().Last().Value);
        var depois = await es.ListarAsync(empresa, competencia, adm, Ct);
        Assert.Equal(3, depois.Count(e => e.Status == "ENVIADO"));
        Assert.Equal("ASSINADO", depois.Single(e => e.Tipo == "S-2200").Status);

        // Enquanto as tabelas não forem processadas, os não periódicos não seguem
        var bloqueado = await tx.AssinarEEnviarAsync(empresa, competencia, ids, AmbienteEsocial.ProducaoRestrita, PapelTransmissor.Empregador, certEmpregador, adm, ct: Ct);
        Assert.Equal(0, bloqueado.Valor.Enviados);
        Assert.Contains(bloqueado.Valor.Avisos, a => a.Contains("aguardando processamento"));

        // Consulta: 1ª vez ainda em processamento, 2ª vez processado com uma rejeição
        var s1005 = depois.Single(e => e.Tipo == "S-1005");
        servidor.Rejeitar.Add(s1005.IdEvento);
        var c1 = await tx.ConsultarRetornoAsync(empresa, certEmpregador, adm, ct: Ct);
        Ok(c1);
        Assert.Equal(1, c1.Valor.Aguardando);
        var c2 = await tx.ConsultarRetornoAsync(empresa, certEmpregador, adm, ct: Ct);
        Ok(c2);
        Assert.Equal((1, 2, 1), (c2.Valor.Processados, c2.Valor.Aceitos, c2.Valor.Rejeitados));
        var apos = await es.ListarAsync(empresa, competencia, adm, Ct);
        var ok1000 = apos.Single(e => e.Tipo == "S-1000");
        Assert.Equal(("PROCESSADO", 201), (ok1000.Status, ok1000.CodigoResposta));
        Assert.StartsWith("1.2.", ok1000.Recibo);
        var rej = apos.Single(e => e.Tipo == "S-1005");
        Assert.Equal("ERRO", rej.Status);
        Assert.Equal("MS0001", Assert.Single(ServicoTransmissaoEsocial.LerOcorrencias(rej.OcorrenciasJson)).Codigo);
        var lotes = await tx.ListarLotesAsync(empresa, adm, ct: Ct);
        Assert.Equal(("PROCESSADO", 3), (lotes.Single().Status, lotes.Single().QtdEventos));
        Assert.StartsWith("1.2.", lotes.Single().Protocolo);

        // Agora os não periódicos podem ir
        var env2 = await tx.AssinarEEnviarAsync(empresa, competencia, ids, AmbienteEsocial.ProducaoRestrita, PapelTransmissor.Empregador, certEmpregador, adm, ct: Ct);
        Ok(env2);
        Assert.Equal((1, 1), (env2.Valor.Enviados, env2.Valor.Lotes));
        Assert.Equal("2", XDocument.Parse(servidor.LotesRecebidos[1]).Descendants().First(e => e.Name.LocalName == "envioLoteEventos").Attribute("grupo")!.Value);
        servidor.Rejeitar.Clear();
        servidor.ConsultasAteProcessar = 0;
        Ok(await tx.ConsultarRetornoAsync(empresa, certEmpregador, adm, ct: Ct));
        Assert.Equal("PROCESSADO", (await es.ListarAsync(empresa, competencia, adm, Ct)).Single(e => e.Tipo == "S-2200").Status);
    }

    private static async Task<long> CadastrarProcuradorAsync(Vorticis.Infra.VorticisApp app, long empresa, long usuario, string nome, string cpf, DateOnly inicio, DateOnly? fim)
    {
        var r = await app.Responsaveis.VincularContadorAsync(empresa, new Contador(0, nome, cpf, "", "", null, null, null, false), inicio, false, usuario, Ct,
            new Procuracao(true, inicio, fim, ["ECD", "ECF", "ESOCIAL"]));
        Ok(r);
        return r.Valor;
    }

    [Fact]
    public async Task Lote_recusado_marca_erro_e_permite_reenviar_e_procurador_exige_procuracao_vigente()
    {
        var (empresa, adm) = await PrepararAsync("19131243000197");
        var competencia = new DateOnly(2026, 6, 1);
        var es = db.Folha.ESocial;
        var tx = db.Folha.Transmissao;
        Ok(await es.GerarAsync(empresa, competencia, ["S-1000"], adm, Ct));
        var ids = (await es.ListarAsync(empresa, competencia, adm, Ct)).Select(e => e.Id).ToList();
        using var servidor = new EsocialSimulado { CodigoEnvio = 401 };
        tx.FabricaCliente = (_, _) => new ClienteEsocial(new HttpClient(), new EnderecosEsocial(new Uri(servidor.Url + "envio"), new Uri(servidor.Url + "consulta")));

        // Procurador: CPF do certificado = CPF do procurador, com procuração vigente
        using var certProc = AssinaturaEsocialTests.CertificadoTeste("JOAO PROCURADOR:52998224725");
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        await CadastrarProcuradorAsync(db.App, empresa, adm, "João Procurador", "52998224725", hoje.AddYears(-2), hoje.AddDays(-5));
        Assert.Contains("não está vigente", (await tx.ResolverTransmissorAsync(empresa, PapelTransmissor.Procurador, certProc, Ct)).Erros[0]);
        var maria = await CadastrarProcuradorAsync(db.App, empresa, adm, "Maria Procuradora", "11144477735", hoje.AddDays(-1), null);
        Assert.Contains("não é do procurador", (await tx.ResolverTransmissorAsync(empresa, PapelTransmissor.Procurador, certProc, Ct)).Erros[0]);
        // Abrangência: a procuração só da ECD não vale para o eSocial
        Ok(await db.App.Responsaveis.DefinirProcuracaoAsync(empresa, maria, new Procuracao(true, hoje.AddDays(-1), null, ["ECD"]), adm, Ct));
        Assert.Contains("não abrange o eSocial", (await tx.ResolverTransmissorAsync(empresa, PapelTransmissor.Procurador, certProc, Ct)).Erros[0]);
        Ok(await db.App.Responsaveis.DefinirProcuracaoAsync(empresa, maria, new Procuracao(true, hoje.AddDays(-1), null, ["ESOCIAL"]), adm, Ct));
        using var certProc2 = AssinaturaEsocialTests.CertificadoTeste("MARIA PROCURADORA:11144477735");
        var t = await tx.ResolverTransmissorAsync(empresa, PapelTransmissor.Procurador, certProc2, Ct);
        Ok(t);
        Assert.Equal(("PROCURADOR", 2, "11144477735"), (t.Valor.Papel, t.Valor.TpInsc, t.Valor.NrInsc));

        // Envio recusado (ex.: 411 sem procuração no eSocial): evento volta a poder ser reenviado
        var env = await tx.AssinarEEnviarAsync(empresa, competencia, ids, AmbienteEsocial.ProducaoRestrita, PapelTransmissor.Procurador, certProc2, adm, ct: Ct);
        Ok(env);
        Assert.Equal(0, env.Valor.Enviados);
        Assert.Contains(env.Valor.Avisos, a => a.Contains("411") || a.Contains("recusado"));
        var ev = (await es.ListarAsync(empresa, competencia, adm, Ct)).Single();
        Assert.Equal("ERRO", ev.Status);
        Assert.Contains("procuração", ServicoTransmissaoEsocial.LerOcorrencias(ev.OcorrenciasJson)[0].Descricao);
        servidor.CodigoEnvio = 201;
        var reenvio = await tx.AssinarEEnviarAsync(empresa, competencia, ids, AmbienteEsocial.ProducaoRestrita, PapelTransmissor.Procurador, certProc2, adm, ct: Ct);
        Assert.Equal(1, reenvio.Valor.Enviados);
        Assert.Equal("PROCURADOR", (await tx.ListarLotesAsync(empresa, adm, ct: Ct))[0].TransmissorPapel);
    }
}
