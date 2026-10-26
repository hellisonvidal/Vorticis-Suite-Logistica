using System.Net;
using System.Text;
using Vorticis.Application.Cadastros;
using Vorticis.Infra.Cnpj;

namespace Vorticis.Infra.Tests;

/// <summary>Simula as APIs externas (sem acesso à internet nos testes).</summary>
public sealed class HttpSimulado(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<Uri> Chamadas { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Chamadas.Add(request.RequestUri!);
        return Task.FromResult(responder(request));
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

public class ConsultaCnpjTests
{
    // Formato da API pública do CNPJ.ws (campos relevantes): inscrições estaduais por UF.
    public const string RespostaInscricoesExemplo = """
        {"razao_social":"EXEMPLO COMERCIO DE INFORMATICA LTDA",
         "estabelecimento":{"cnpj":"11222333000181",
           "inscricoes_estaduais":[
             {"inscricao_estadual":"999888777666","ativo":false,"estado":{"id":19,"nome":"Rio de Janeiro","sigla":"RJ","ibge_id":33}},
             {"inscricao_estadual":"110042490114","ativo":true,"atualizado_em":"2026-09-01T00:00:00.000Z",
              "estado":{"id":26,"nome":"São Paulo","sigla":"SP","ibge_id":35}}]}}
        """;

    /// <summary>Responde CNPJ (BrasilAPI) e inscrições estaduais (CNPJ.ws) conforme o endereço consultado.</summary>
    public static HttpSimulado HttpCnpjEInscricoes() => new(req => HttpSimulado.Json(
        req.RequestUri!.Host == "publica.cnpj.ws" ? RespostaInscricoesExemplo : RespostaExemplo));

    [Fact]
    public async Task Inscricao_estadual_e_filtrada_pela_UF_e_ativa_vem_primeiro()
    {
        var ct = TestContext.Current.CancellationToken;
        var consulta = new ConsultaInscricaoEstadualPublica(new HttpClient(HttpCnpjEInscricoes()));
        var todas = await consulta.ConsultarAsync("11222333000181", ct);
        Assert.True(todas.Sucesso);
        Assert.Equal([("SP", "110042490114", true), ("RJ", "999888777666", false)], todas.Valor.Select(i => (i.Uf, i.Inscricao, i.Ativa)));

        var servico = new ServicoEmpresa(null!, null!, null, consulta);
        // UF deduzida do código IBGE quando a UF não é informada (35 = SP)
        var sp = await servico.ConsultarInscricaoEstadualAsync("11.222.333/0001-81", null, "3550308", ct);
        Assert.Equal("110042490114", Assert.Single(sp.Valor).Inscricao);
        Assert.Empty((await servico.ConsultarInscricaoEstadualAsync("11222333000181", "MG", null, ct)).Valor);

        var limite = new ConsultaInscricaoEstadualPublica(new HttpClient(new HttpSimulado(_ => HttpSimulado.Json("{}", HttpStatusCode.TooManyRequests))));
        Assert.Contains("3 consultas por minuto", (await limite.ConsultarAsync("11222333000181", ct)).Erros[0]);
    }

    [Theory]
    [InlineData("3550308", "SP")]
    [InlineData("5300108", "DF")]
    [InlineData("4314902", "RS")]
    [InlineData("99", null)]
    public void Uf_pelo_codigo_ibge(string codigo, string? uf) => Assert.Equal(uf, Domain.Cadastros.Uf.DeCodigoIbge(codigo));

    [Theory]
    [InlineData("110.042.490.114", "110042490114", true)]
    [InlineData("isento", "ISENTO", true)]
    [InlineData("1", "1", false)]
    [InlineData("", null, true)]
    public void Inscricao_estadual_normalizada(string entrada, string? esperado, bool valida)
    {
        Assert.Equal(esperado, Domain.Cadastros.InscricaoEstadual.Normalizar(entrada));
        Assert.Equal(valida, Domain.Cadastros.InscricaoEstadual.Valida(entrada));
    }
    // Formato real das respostas da BrasilAPI / Minha Receita (campos relevantes).
    public const string RespostaExemplo = """
        {"uf":"SP","cep":"01310100","cnpj":"11222333000181","bairro":"BELA VISTA","numero":"1000","municipio":"SAO PAULO",
         "logradouro":"PAULISTA","cnae_fiscal":4751201,"complemento":"CONJ 101","razao_social":"EXEMPLO COMERCIO DE INFORMATICA LTDA",
         "nome_fantasia":"EXEMPLO INFO","ddd_telefone_1":"1133334444","email":"CONTATO@EXEMPLO.COM.BR","opcao_pelo_mei":false,
         "opcao_pelo_simples":true,"codigo_municipio_ibge":3550308,"data_inicio_atividade":"2015-03-02",
         "codigo_natureza_juridica":2062,"descricao_situacao_cadastral":"ATIVA","descricao_tipo_de_logradouro":"AVENIDA",
         "qsa":[{"nome_socio":"FULANO DE TAL","cnpj_cpf_do_socio":"***123456**","qualificacao_socio":"Sócio-Administrador",
                 "codigo_qualificacao_socio":49,"data_entrada_sociedade":"2015-03-02","identificador_de_socio":2},
                {"nome_socio":"BELTRANA DA SILVA","cnpj_cpf_do_socio":"***654321**","qualificacao_socio":"Sócio",
                 "codigo_qualificacao_socio":22,"data_entrada_sociedade":"2018-07-10","identificador_de_socio":2}]}
        """;

    private static ConsultaCnpjPublica Consulta(HttpSimulado http) => new(new HttpClient(http));

    [Fact]
    public async Task Interpreta_os_dados_cadastrais()
    {
        var http = new HttpSimulado(_ => HttpSimulado.Json(RespostaExemplo));
        var r = await Consulta(http).ConsultarAsync("11222333000181", TestContext.Current.CancellationToken);

        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        var d = r.Valor;
        Assert.Equal("EXEMPLO COMERCIO DE INFORMATICA LTDA", d.RazaoSocial);
        Assert.Equal(("2062", "4751201", "3550308", "SP"), (d.NaturezaJuridica, d.CnaePrincipal, d.CodMunicipioIbge, d.Uf));
        Assert.Equal(new DateOnly(2015, 3, 2), d.DataInicioAtividades);
        Assert.Equal("AVENIDA PAULISTA", d.Logradouro);
        Assert.Equal("contato@exemplo.com.br", d.Email);
        Assert.Equal(RegimeTributario.SimplesNacional, d.RegimeSugerido);
        Assert.True(d.Ativa);
        Assert.StartsWith("BrasilAPI", d.Fonte);
        Assert.Equal("https://brasilapi.com.br/api/cnpj/v1/11222333000181", Assert.Single(http.Chamadas).ToString());
        Assert.Equal(2, d.QuadroSocietario.Count);
        var fulano = d.QuadroSocietario[0];
        Assert.Equal(("FULANO DE TAL", "***123456**", "Sócio-Administrador", 49, new DateOnly(2015, 3, 2), 'F'),
                     (fulano.Nome, fulano.DocumentoMascarado, fulano.Qualificacao, fulano.QualificacaoCodigo, fulano.DataEntrada, fulano.TipoPessoa));
    }

    [Fact]
    public async Task Usa_a_fonte_reserva_quando_a_primeira_falha()
    {
        var http = new HttpSimulado(req => req.RequestUri!.Host == "brasilapi.com.br"
            ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            : HttpSimulado.Json(RespostaExemplo.Replace("\"cnae_fiscal\":4751201", "\"cnae_fiscal\":\"0111301\"")));
        var r = await Consulta(http).ConsultarAsync("11222333000181", TestContext.Current.CancellationToken);

        Assert.True(r.Sucesso);
        Assert.StartsWith("Minha Receita", r.Valor.Fonte);
        Assert.Equal("0111301", r.Valor.CnaePrincipal);   // aceita número como texto e preserva zero à esquerda
        Assert.Equal(2, http.Chamadas.Count);
    }

    [Fact]
    public async Task Nao_encontrado_e_falha_total_geram_mensagens_claras()
    {
        var ct = TestContext.Current.CancellationToken;
        var naoEncontrado = await Consulta(new HttpSimulado(_ => new HttpResponseMessage(HttpStatusCode.NotFound))).ConsultarAsync("11222333000181", ct);
        Assert.Contains("não encontrado", naoEncontrado.Erros[0]);

        var semRede = await Consulta(new HttpSimulado(_ => throw new HttpRequestException("sem rota"))).ConsultarAsync("11222333000181", ct);
        Assert.Contains("preencha manualmente", semRede.Erros[0]);
        Assert.Contains("sem rota", semRede.Erros[0]);
    }

    [Fact]
    public async Task Cnpj_invalido_nao_chega_a_consultar()
    {
        var http = new HttpSimulado(_ => HttpSimulado.Json(RespostaExemplo));
        var servico = new ServicoEmpresa(null!, null!, Consulta(http));
        var r = await servico.ConsultarCnpjAsync("11.222.333/0001-00", TestContext.Current.CancellationToken);
        Assert.Contains("CNPJ inválido", r.Erros[0]);
        Assert.Empty(http.Chamadas);
    }
}
