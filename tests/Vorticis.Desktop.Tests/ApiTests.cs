using System.Net.Http.Headers;
using Vorticis.Frota;
using Vorticis.Infra.Api;
using Vorticis.Infra.Tests;
using Vorticis.Logistica.Desktop;

namespace Vorticis.Desktop.Tests;

/// <summary>Integração por API: chaves (gerar, validar, revogar), leitura e gravação pelas funções do programa.</summary>
public class ApiTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    [Fact]
    public async Task Chaves_dao_acesso_as_funcoes_com_as_permissoes_e_o_servidor_responde_em_json()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("11222333000181");
        await db.EscalarAsync<int>("UPDATE core.empresa SET modulo_frota = TRUE WHERE id = @e RETURNING 1", ("e", empresa));
        var frota = ServicosFrota.De(db.App);
        Assert.True((await frota.GravarVeiculoAsync(empresa, new Veiculo(0, 1, "API1A23", null, "CAVALO", null, null, null, null, "DIESEL_S10", null, 0, null), adm, ct)).Sucesso);

        var chaves = db.App.ChavesApi;
        var outro = await db.EscalarAsync<long>("INSERT INTO core.usuario (login, nome, senha_hash, administrador) VALUES ('nao_adm_api', 'X', 'x', FALSE) RETURNING id");
        Assert.False((await chaves.GerarAsync(empresa, "Rastreador", false, outro, ct)).Sucesso);       // só administradores
        Assert.False((await chaves.GerarAsync(empresa, " ", false, adm, ct)).Sucesso);
        var leitura = (await chaves.GerarAsync(empresa, "BI", false, adm, ct)).Valor;
        var escrita = (await chaves.GerarAsync(empresa, "Cartão de combustível", true, adm, ct)).Valor;
        Assert.StartsWith(ServicoChavesApi.Prefixo, leitura);
        var lista = (await chaves.ListarAsync(empresa, adm, ct)).Valor;
        Assert.Equal(2, lista.Count);
        Assert.DoesNotContain(lista, c => leitura.Contains(c.Prefixo + "x"));
        Assert.Equal(leitura[..12], lista.Single(c => c.Nome == "BI").Prefixo);

        var servidor = new ServidorApi(db.App, RotasApi.Todas);
        Task<RespostaApi> Chamar(string metodo, string caminho, string? chave, string corpo = "", Dictionary<string, string>? p = null) =>
            servidor.ResponderAsync(metodo, caminho, p ?? [], chave is null ? null : "Bearer " + chave, corpo, ct);
        Assert.Equal(401, (await Chamar("GET", "/api/v1/veiculos", null)).Status);
        Assert.Equal(401, (await Chamar("GET", "/api/v1/veiculos", "vtx_errada")).Status);
        var veiculos = await Chamar("GET", "/api/v1/veiculos", leitura);
        Assert.Equal(200, veiculos.Status);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<Veiculo>>(veiculos.Dados));
        Assert.Equal(404, (await Chamar("GET", "/api/v1/nao-existe", leitura)).Status);
        Assert.Equal(200, (await Chamar("GET", "/api/v1", leitura)).Status);

        const string abastecimento = """{"placa":"api-1a23","data":"2026-10-05","km":1500,"litros":200,"valor":1200.50}""";
        Assert.Equal(403, (await Chamar("POST", "/api/v1/abastecimentos", leitura, abastecimento)).Status);   // chave só de leitura
        Assert.Equal(400, (await Chamar("POST", "/api/v1/abastecimentos", escrita, "{x")).Status);
        Assert.Equal(404, (await Chamar("POST", "/api/v1/abastecimentos", escrita, abastecimento.Replace("api-1a23", "ZZZ9Z99"))).Status);
        Assert.Equal(201, (await Chamar("POST", "/api/v1/abastecimentos", escrita, abastecimento)).Status);
        var abast = await Chamar("GET", "/api/v1/abastecimentos", leitura, p: new() { ["inicio"] = "2026-10-01", ["fim"] = "2026-10-31" });
        Assert.Equal(1200.50m, Assert.Single(Assert.IsAssignableFrom<IEnumerable<Abastecimento>>(abast.Dados)).ValorTotal);
        Assert.NotNull((await chaves.ListarAsync(empresa, adm, ct)).Valor.Single(c => c.Nome == "BI").UltimoUso);

        // pelo HTTP de verdade (localhost)
        var porta = Random.Shared.Next(41000, 49000);
        servidor.Iniciar(porta);
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", leitura);
            var r = await http.GetAsync($"http://localhost:{porta}/api/v1/veiculos", ct);
            Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
            Assert.Contains("API1A23", await r.Content.ReadAsStringAsync(ct));
            Assert.Equal("application/json", r.Content.Headers.ContentType!.MediaType);
        }
        finally { await servidor.PararAsync(); }

        // revogada: deixa de valer na hora
        Assert.True((await chaves.RevogarAsync(empresa, lista.Single(c => c.Nome == "BI").Id, adm, ct)).Sucesso);
        Assert.Equal(401, (await Chamar("GET", "/api/v1/veiculos", leitura)).Status);
        Assert.False((await chaves.RevogarAsync(empresa, lista.Single(c => c.Nome == "BI").Id, adm, ct)).Sucesso);
    }

    [Fact]
    public void So_a_Suite_tem_o_menu_da_API_e_a_tela_conhece_as_funcoes()
    {
        var suite = new MainViewModel(new ModuloFrotaLogistica());
        var config = suite.Menus.Single(m => m.Titulo == "Con_figurações").Filhos!;
        Assert.Contains(config, i => i.Chave == Comum.ViewModels.TelasComuns.Api);
        Assert.True(suite.ConheceTela(Comum.ViewModels.TelasComuns.Api));
        Assert.DoesNotContain(new Vorticis.Desktop.ViewModels.MainViewModel().Menus.Single(m => m.Titulo == "Con_figurações").Filhos!,
                              i => i.Chave == Comum.ViewModels.TelasComuns.Api);
        Assert.Contains(RotasApi.Todas, r => r is { Metodo: "POST", Grava: true });
        Assert.Equal(RotasApi.Todas.Count, RotasApi.Todas.Select(r => r.Metodo + r.Caminho).Distinct().Count());
    }
}
