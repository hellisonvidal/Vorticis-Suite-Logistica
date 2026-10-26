using Vorticis.Application.Cadastros;
using Vorticis.Application.Contabil;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;

namespace Vorticis.Infra.Tests;

/// <summary>Códigos sequenciais de cadastros: excluído o do meio, o próximo ocupa a lacuna.</summary>
public class SequenciasLacunasTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    [Theory]
    [InlineData(new[] { 1, 2, 3 }, 4)]
    [InlineData(new[] { 1, 3, 4 }, 2)]
    [InlineData(new[] { 2, 3 }, 1)]
    [InlineData(new int[0], 1)]
    public void Menor_livre_preenche_lacunas(int[] usados, int esperado) => Assert.Equal(esperado, Sequencia.MenorLivre(usados));

    [Fact]
    public async Task Historicos_excluido_do_meio_e_ocupado_pelo_proximo()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync("27865757000102");
        var adm = await db.AdminAsync();
        var h = db.App.Historicos;
        var ids = new List<long>();
        foreach (var d in new[] { "A", "B", "C" }) ids.Add((await h.IncluirAsync(empresa, null, d, adm, ct)).Valor);
        Assert.True((await h.ExcluirAsync(empresa, ids[1], adm, ct)).Sucesso);
        Assert.True((await h.IncluirAsync(empresa, null, "D", adm, ct)).Sucesso);
        Assert.Equal(["1", "2", "3"], (await h.ListarAsync(empresa, adm, ct)).Select(x => x.Codigo));
    }

    [Fact]
    public async Task Participantes_excluido_do_meio_e_ocupado_pelo_proximo()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync("42938862000100");
        var adm = await db.AdminAsync();
        var svc = db.App.Participantes;
        Participante Novo(string nome, string doc) => new(0, "", nome, null, doc, null, "SP", null, null, null, null, true, false, "1.1.02.001", true);
        var a = await svc.GravarAsync(empresa, Novo("A", "11222333000181"), adm, ct);
        var b = await svc.GravarAsync(empresa, Novo("B", "52998224725"), adm, ct);
        var c = await svc.GravarAsync(empresa, Novo("C", "11444777000161"), adm, ct);
        Assert.True(a.Sucesso && b.Sucesso && c.Sucesso);
        Assert.True((await svc.ExcluirAsync(empresa, b.Valor, adm, ct)).Sucesso);
        var d = await svc.GravarAsync(empresa, Novo("D", "34028316000103"), adm, ct);
        Assert.True(d.Sucesso, string.Join("; ", d.Erros));
        Assert.Equal(["1", "2", "3"], (await svc.ListarAsync(empresa, adm, ct)).Select(p => p.Codigo));
    }

    [Fact]
    public async Task Empresa_so_no_fiscal_ocupa_o_codigo_tambem_nos_demais_modulos()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var sessao = new Vorticis.Domain.Seguranca.SessaoUsuario(adm, "admin_teste", "Admin de Teste", true, false);
        var r = await db.App.Empresas.CadastrarAsync(new NovaEmpresa("73712451000125", "Empresa Fiscal LTDA", null, "2062", "4751201", "SP", "3550308",
            new DateOnly(2020, 1, 1), RegimeTributario.LucroPresumido, 2026, Modulos: ModulosEmpresa.Fiscal), ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        var soFiscal = r.Valor;
        Assert.DoesNotContain(await db.App.Usuarios.EmpresasDoModuloAsync(sessao, ModulosEmpresa.Contabil, ct), e => e.Id == soFiscal);

        // o código (id) da empresa é global: a próxima empresa nunca reaproveita o número, em qualquer módulo
        var outra = await db.CriarEmpresaAsync("58421733000180");
        Assert.NotEqual(soFiscal, outra);
        Assert.True(outra > soFiscal);

        // participantes cadastrados "no Fiscal" ocupam o código também para o Contábil (a lacuna considera toda a tabela)
        var svc = db.App.Participantes;
        Participante Novo(string nome, string doc) => new(0, "", nome, null, doc, null, "SP", null, null, null, null, true, false, "1.1.02.001", true);
        Assert.True((await svc.GravarAsync(soFiscal, Novo("A", "11222333000181"), adm, ct)).Sucesso);
        Assert.True((await svc.GravarAsync(soFiscal, Novo("B", "52998224725"), adm, ct)).Sucesso);
        var lista = await svc.ListarAsync(soFiscal, adm, ct);
        Assert.Equal("3", ServicoParticipantes.ProximoCodigo(lista));
        Assert.Equal(["1", "2"], lista.Select(p => p.Codigo));
    }

    [Fact]
    public async Task Reduzido_do_plano_excluido_do_meio_e_ocupado_pela_proxima_conta()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync("11444777000161");
        var adm = await db.AdminAsync();
        var svc = db.App.PlanoContas;
        NovaConta Conta(string cod) => new(cod, "Conta " + cod, "01", IndicadorDC.D, false, false, new DateOnly(2026, 1, 1));
        var ids = new List<long>();
        foreach (var cod in new[] { "1.1.01.011", "1.1.01.012", "1.1.01.013" }) ids.Add((await svc.IncluirAsync(empresa, Conta(cod), adm, ct)).Valor);
        var plano = await svc.ListarAsync(empresa, adm, ct);
        var meio = plano.Single(x => x.Id == ids[1]).CodigoReduzido;
        Assert.True((await svc.ExcluirAsync(empresa, ids[1], adm, ct)).Sucesso);
        var nova = (await svc.IncluirAsync(empresa, Conta("1.1.01.014"), adm, ct)).Valor;
        Assert.Equal(meio, (await svc.ListarAsync(empresa, adm, ct)).Single(x => x.Id == nova).CodigoReduzido);
    }

    [Fact]
    public async Task Produto_e_participante_do_fiscal_usam_menor_codigo_livre()
    {
        BancoDeTeste.ExigirBanco();
        var empresa = await db.CriarEmpresaAsync("34028316000103");
        // funções SQL: com 1 e 3 ocupados, o próximo é 2
        await db.EscalarAsync<int>("INSERT INTO core.participante (empresa_id, codigo, nome) VALUES (@e,'1','A'),(@e,'3','B') RETURNING 1", ("e", empresa));
        Assert.Equal(2L, await db.EscalarAsync<long>("SELECT core.fn_proximo_participante(@e)", ("e", empresa)));
        Assert.Equal(1, await db.EscalarAsync<int>("SELECT fiscal.fn_proximo_produto(@e)", ("e", empresa)));
    }

    [Fact]
    public async Task Lancamento_rejeitado_ou_rascunho_nao_consome_numero_e_efetivados_seguem_sem_lacunas()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync("11222333000181");
        var adm = await db.AdminAsync();
        // O número só é atribuído na efetivação (rascunho não tem número): um lançamento recusado não deixa lacuna.
        var ruim = new LancamentoProposto(empresa, new(2026, 1, 10), OrigemLancamento.Manual,
            [new("1.1.01.001", IndicadorDC.D, 50m, "x"), new("2.3.01.001", IndicadorDC.C, 40m, "x")]);
        Assert.False((await db.App.Lancamentos.EfetivarAsync(ruim, adm, ct)).Sucesso);
        LancamentoProposto Bom() => new(empresa, new(2026, 1, 10), OrigemLancamento.Manual,
            [new("1.1.01.001", IndicadorDC.D, 50m, "x"), new("2.3.01.001", IndicadorDC.C, 50m, "x")]);
        Assert.True((await db.App.Lancamentos.EfetivarAsync(Bom(), adm, ct)).Sucesso);
        Assert.True((await db.App.Lancamentos.EfetivarAsync(Bom(), adm, ct)).Sucesso);
        var nums = await db.EscalarAsync<string>("SELECT string_agg(numero::text, ',' ORDER BY numero) FROM contabil.lancamento WHERE empresa_id = @e", ("e", empresa));
        Assert.Equal("1,2", nums);
    }
}
