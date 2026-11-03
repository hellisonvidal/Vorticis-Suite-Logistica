using System.Net;
using System.Text;
using System.Text.Json;

namespace Vorticis.Infra.Api;

/// <summary>Chamada recebida: quem chama (pela chave), os parâmetros da URL e o corpo JSON.</summary>
public sealed record PedidoApi(AcessoApi Acesso, IReadOnlyDictionary<string, string> Parametros, string Corpo, VorticisApp App)
{
    public string? Parametro(string nome) => Parametros.TryGetValue(nome, out var v) && v.Length > 0 ? v : null;

    /// <summary>Data do parâmetro (aaaa-mm-dd) ou o padrão.</summary>
    public DateOnly Data(string nome, DateOnly padrao) => DateOnly.TryParse(Parametro(nome), System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : padrao;
}

/// <summary>Resposta: código HTTP e o objeto que vira JSON.</summary>
public sealed record RespostaApi(int Status, object? Dados)
{
    public static RespostaApi Ok(object? dados) => new(200, dados);
    public static RespostaApi Criado(object? dados) => new(201, dados);
    public static RespostaApi Erro(int status, params string[] erros) => new(status, new { erros });
}

/// <summary>Uma função da API: método, caminho (ex.: /api/v1/veiculos), se grava e o que faz.</summary>
public sealed record RotaApi(string Metodo, string Caminho, bool Grava, string Descricao, Func<PedidoApi, CancellationToken, Task<RespostaApi>> Executar);

/// <summary>
/// Servidor da integração por API, dentro do programa (http://localhost:porta/). Toda chamada exige o cabeçalho
/// <c>Authorization: Bearer vtx_...</c> com uma chave ativa (Configurações › Integração por API); as rotas que gravam exigem chave com escrita.
/// As respostas são JSON (UTF-8). <c>GET /api/v1</c> lista as rotas disponíveis.
/// </summary>
public sealed class ServidorApi(VorticisApp app, IReadOnlyList<RotaApi> rotas) : IAsyncDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private HttpListener? _ouvinte;
    private CancellationTokenSource? _parar;
    private Task? _laco;

    public int? Porta { get; private set; }
    public bool Ativo => _ouvinte?.IsListening == true;
    public IReadOnlyList<RotaApi> Rotas => rotas;

    public void Iniciar(int porta)
    {
        if (Ativo) return;
        var ouvinte = new HttpListener();
        ouvinte.Prefixes.Add($"http://localhost:{porta}/");
        ouvinte.Start();
        (_ouvinte, Porta, _parar) = (ouvinte, porta, new CancellationTokenSource());
        var ct = _parar.Token;
        _laco = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested && ouvinte.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await ouvinte.GetContextAsync(); }
                catch (Exception) { break; }
                _ = Task.Run(() => AtenderAsync(ctx, ct), ct);
            }
        }, ct);
    }

    public async Task PararAsync()
    {
        _parar?.Cancel();
        try { _ouvinte?.Stop(); _ouvinte?.Close(); } catch (Exception) { /* já parado */ }
        if (_laco is not null) try { await _laco; } catch (Exception) { /* encerrado */ }
        (_ouvinte, _laco, Porta) = (null, null, null);
    }

    public ValueTask DisposeAsync() => new(PararAsync());

    private async Task AtenderAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        RespostaApi resposta;
        try { resposta = await ResponderAsync(ctx.Request.HttpMethod, ctx.Request.Url!.AbsolutePath, ctx.Request.QueryString.AllKeys
                .Where(k => k is not null).ToDictionary(k => k!, k => ctx.Request.QueryString[k] ?? ""), ctx.Request.Headers["Authorization"],
                ctx.Request.HasEntityBody ? await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync(ct) : "", ct); }
        catch (Exception ex) { resposta = RespostaApi.Erro(500, "Erro interno: " + ex.Message); }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(resposta.Dados, Json);
        ctx.Response.StatusCode = resposta.Status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes, ct);
        ctx.Response.Close();
    }

    /// <summary>Atende uma chamada (separado do HTTP para os testes).</summary>
    public async Task<RespostaApi> ResponderAsync(string metodo, string caminho, IReadOnlyDictionary<string, string> parametros, string? autorizacao, string corpo,
                                                  CancellationToken ct = default)
    {
        var segredo = autorizacao is { } a && a.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? a[7..].Trim() : null;
        if (await app.ChavesApi.ValidarAsync(segredo, ct) is not { } acesso)
            return RespostaApi.Erro(401, "Chave de acesso ausente, inválida ou revogada (cabeçalho Authorization: Bearer vtx_...).");
        var caminhoLimpo = caminho.TrimEnd('/');
        if (metodo == "GET" && caminhoLimpo == "/api/v1")
            return RespostaApi.Ok(new { versao = "v1", rotas = rotas.Select(r => new { metodo = r.Metodo, caminho = r.Caminho, grava = r.Grava, descricao = r.Descricao }) });
        if (rotas.FirstOrDefault(r => r.Metodo == metodo && r.Caminho.Equals(caminhoLimpo, StringComparison.OrdinalIgnoreCase)) is not { } rota)
            return RespostaApi.Erro(404, $"Rota não encontrada: {metodo} {caminho}. Veja GET /api/v1.");
        if (rota.Grava && !acesso.Escrita) return RespostaApi.Erro(403, "Esta chave é só de leitura.");
        return await rota.Executar(new PedidoApi(acesso, parametros, corpo, app), ct);
    }
}
