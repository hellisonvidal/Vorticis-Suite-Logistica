using System.Globalization;
using System.Net;
using System.Text.Json;
using Vorticis.Application.Cadastros;
using Vorticis.Domain.Comum;

namespace Vorticis.Infra.Cnpj;

/// <summary>
/// Consulta CNPJ na base pública da Receita Federal (dados abertos do CNPJ, atualização mensal), servida por
/// APIs gratuitas de código aberto. Tenta as fontes em ordem e passa à seguinte em falha de rede, limite de
/// requisições (429) ou erro do servidor.
/// <para>
/// O portal "Comprovante de Inscrição" da RFB exige captcha e não pode ser automatizado; a API oficial
/// (Serpro "Consulta CNPJ") é paga e pode ser adicionada como outra <see cref="IConsultaCnpj"/>.
/// </para>
/// </summary>
public sealed class ConsultaCnpjPublica(HttpClient http, TimeProvider? relogio = null) : IConsultaCnpj
{
    public sealed record Fonte(string Nome, Func<string, Uri> Url);

    // Ambas expõem o mesmo esquema JSON (a BrasilAPI usa o projeto Minha Receita como base).
    public static readonly IReadOnlyList<Fonte> FontesPadrao =
    [
        new("BrasilAPI (dados abertos RFB)", c => new Uri($"https://brasilapi.com.br/api/cnpj/v1/{c}")),
        new("Minha Receita (dados abertos RFB)", c => new Uri($"https://minhareceita.org/{c}")),
    ];

    public IReadOnlyList<Fonte> Fontes { get; init; } = FontesPadrao;

    public static HttpClient CriarHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Vorticis/0.1 (+escrituracao contabil)");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return http;
    }

    public async Task<Resultado<DadosCnpj>> ConsultarAsync(string cnpj, CancellationToken ct)
    {
        var falhas = new List<string>();
        foreach (var fonte in Fontes)
        {
            try
            {
                using var resp = await http.GetAsync(fonte.Url(cnpj), ct);
                if (resp.StatusCode == HttpStatusCode.NotFound)
                    return Resultado<DadosCnpj>.Falha(
                        "CNPJ não encontrado na base pública da Receita Federal. Empresas abertas há poucas semanas podem ainda não constar " +
                        "(a base é atualizada mensalmente); nesse caso, preencha os dados manualmente.");
                if (resp.StatusCode == HttpStatusCode.BadRequest)
                    return Resultado<DadosCnpj>.Falha("A fonte de dados recusou o CNPJ informado (CNPJs alfanuméricos podem ainda não ser aceitos).");
                if (!resp.IsSuccessStatusCode)
                {
                    falhas.Add($"{fonte.Nome}: HTTP {(int)resp.StatusCode}");
                    continue;
                }

                await using var corpo = await resp.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(corpo, cancellationToken: ct);
                var dados = Interpretar(doc.RootElement, cnpj, fonte.Nome, (relogio ?? TimeProvider.System).GetUtcNow());
                if (dados is null) { falhas.Add($"{fonte.Nome}: resposta sem razão social"); continue; }
                return Resultado<DadosCnpj>.Ok(dados);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
            {
                falhas.Add($"{fonte.Nome}: {(ex is TaskCanceledException ? "tempo esgotado" : ex.Message)}");
            }
        }
        return Resultado<DadosCnpj>.Falha(
            $"Não foi possível consultar o CNPJ agora ({string.Join("; ", falhas)}). Verifique a internet ou preencha manualmente.");
    }

    /// <summary>Converte o JSON (esquema BrasilAPI/Minha Receita) em <see cref="DadosCnpj"/>. Tolera números como texto e campos ausentes.</summary>
    public static DadosCnpj? Interpretar(JsonElement j, string cnpjConsultado, string fonte, DateTimeOffset consultadoEm)
    {
        var razao = Texto(j, "razao_social");
        if (razao is null) return null;

        var tipoLogradouro = Texto(j, "descricao_tipo_de_logradouro");
        var logradouro = Texto(j, "logradouro");
        if (tipoLogradouro is not null && logradouro is not null && !logradouro.StartsWith(tipoLogradouro, StringComparison.OrdinalIgnoreCase))
            logradouro = $"{tipoLogradouro} {logradouro}";

        return new DadosCnpj(
            Cnpj: Texto(j, "cnpj") ?? cnpjConsultado,
            RazaoSocial: razao,
            NomeFantasia: Texto(j, "nome_fantasia"),
            NaturezaJuridica: Digitos(j, "codigo_natureza_juridica", 4),
            CnaePrincipal: Digitos(j, "cnae_fiscal", 7),
            Uf: Texto(j, "uf")?.ToUpperInvariant(),
            CodMunicipioIbge: Digitos(j, "codigo_municipio_ibge", 7),
            Municipio: Texto(j, "municipio"),
            DataInicioAtividades: DateOnly.TryParseExact(Texto(j, "data_inicio_atividade"), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                                         DateTimeStyles.None, out var d) ? d : null,
            SituacaoCadastral: Texto(j, "descricao_situacao_cadastral"),
            OptanteSimples: Booleano(j, "opcao_pelo_simples"),
            OptanteMei: Booleano(j, "opcao_pelo_mei"),
            Cep: Digitos(j, "cep", 8),
            Logradouro: logradouro,
            Numero: Texto(j, "numero"),
            Complemento: Texto(j, "complemento"),
            Bairro: Texto(j, "bairro"),
            Email: Texto(j, "email")?.ToLowerInvariant(),
            Telefone: Texto(j, "ddd_telefone_1"),
            Fonte: fonte,
            ConsultadoEm: consultadoEm,
            Socios: LerQsa(j));
    }

    /// <summary>QSA: identificador_de_socio 1 = PJ, 2 = PF, 3 = estrangeiro.</summary>
    private static IReadOnlyList<SocioCnpj> LerQsa(JsonElement j)
    {
        if (!j.TryGetProperty("qsa", out var qsa) || qsa.ValueKind != JsonValueKind.Array) return [];
        var res = new List<SocioCnpj>();
        foreach (var s in qsa.EnumerateArray())
        {
            if (Texto(s, "nome_socio") is not { } nome) continue;
            var tipo = Texto(s, "identificador_de_socio") switch { "1" => 'J', "3" => 'E', _ => 'F' };
            res.Add(new SocioCnpj(nome, Texto(s, "cnpj_cpf_do_socio"), Texto(s, "qualificacao_socio"),
                int.TryParse(Texto(s, "codigo_qualificacao_socio"), out var cod) ? cod : null,
                DateOnly.TryParseExact(Texto(s, "data_entrada_sociedade"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null,
                tipo));
        }
        return res;
    }

    private static string? Texto(JsonElement j, string nome)
    {
        if (!j.TryGetProperty(nome, out var v)) return null;
        var s = v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static string? Digitos(JsonElement j, string nome, int tamanho)
    {
        var s = Texto(j, nome);
        if (s is null) return null;
        var d = new string(s.Where(char.IsAsciiDigit).ToArray());
        return d.Length is 0 || d.Length > tamanho ? null : d.PadLeft(tamanho, '0');
    }

    private static bool? Booleano(JsonElement j, string nome) =>
        j.TryGetProperty(nome, out var v) ? v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => v.GetString()?.Trim().ToUpperInvariant() switch { "S" or "SIM" or "TRUE" => true, "N" or "NAO" or "NÃO" or "FALSE" => false, _ => null },
            _ => null,
        } : null;
}
