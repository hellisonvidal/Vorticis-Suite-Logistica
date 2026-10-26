using System.Net;
using System.Text.Json;
using Vorticis.Application.Cadastros;
using Vorticis.Domain.Comum;

namespace Vorticis.Infra.Cnpj;

/// <summary>
/// Inscrições estaduais de um CNPJ pela API pública e gratuita do CNPJ.ws, que reúne as inscrições dos cadastros
/// estaduais (os mesmos dados do SINTEGRA/CCC de cada UF). Os portais SINTEGRA estaduais exigem captcha e não podem
/// ser consultados automaticamente; o usuário confere e decide se salva o número encontrado.
/// Limite da API pública: 3 consultas por minuto.
/// </summary>
public sealed class ConsultaInscricaoEstadualPublica(HttpClient http, TimeProvider? relogio = null) : IConsultaInscricaoEstadual
{
    public const string Fonte = "CNPJ.ws (cadastros estaduais / SINTEGRA)";

    public static Uri Url(string cnpj) => new($"https://publica.cnpj.ws/cnpj/{cnpj}");

    public async Task<Resultado<IReadOnlyList<InscricaoEstadualEncontrada>>> ConsultarAsync(string cnpj, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync(Url(cnpj), ct);
            if (resp.StatusCode == HttpStatusCode.NotFound) return Resultado<IReadOnlyList<InscricaoEstadualEncontrada>>.Ok([]);
            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
                return Resultado<IReadOnlyList<InscricaoEstadualEncontrada>>.Falha(
                    "A consulta pública de inscrição estadual aceita 3 consultas por minuto. Aguarde um minuto e tente de novo.");
            if (!resp.IsSuccessStatusCode)
                return Resultado<IReadOnlyList<InscricaoEstadualEncontrada>>.Falha($"Consulta de inscrição estadual indisponível (HTTP {(int)resp.StatusCode}).");

            await using var corpo = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(corpo, cancellationToken: ct);
            return Resultado<IReadOnlyList<InscricaoEstadualEncontrada>>.Ok(Interpretar(doc.RootElement, (relogio ?? TimeProvider.System).GetUtcNow()));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            return Resultado<IReadOnlyList<InscricaoEstadualEncontrada>>.Falha(
                $"Não foi possível consultar a inscrição estadual agora ({(ex is TaskCanceledException ? "tempo esgotado" : ex.Message)}). " +
                "Verifique a internet ou informe o número manualmente.");
        }
    }

    /// <summary>Lê estabelecimento.inscricoes_estaduais[] { inscricao_estadual, ativo, estado.sigla }.</summary>
    public static IReadOnlyList<InscricaoEstadualEncontrada> Interpretar(JsonElement raiz, DateTimeOffset consultadoEm)
    {
        if (!raiz.TryGetProperty("estabelecimento", out var est) || !est.TryGetProperty("inscricoes_estaduais", out var lista)
            || lista.ValueKind != JsonValueKind.Array)
            return [];
        var res = new List<InscricaoEstadualEncontrada>();
        foreach (var i in lista.EnumerateArray())
        {
            var numero = i.TryGetProperty("inscricao_estadual", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            var uf = i.TryGetProperty("estado", out var e) && e.TryGetProperty("sigla", out var s) ? s.GetString()?.ToUpperInvariant() : null;
            if (string.IsNullOrWhiteSpace(numero) || uf is null) continue;
            var ativa = !i.TryGetProperty("ativo", out var a) || a.ValueKind != JsonValueKind.False;
            res.Add(new InscricaoEstadualEncontrada(uf, numero.Trim(), ativa, Fonte, consultadoEm));
        }
        return res.OrderByDescending(r => r.Ativa).ToList();
    }
}
