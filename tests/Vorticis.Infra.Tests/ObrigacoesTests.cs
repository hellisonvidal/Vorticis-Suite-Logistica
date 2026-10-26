using Vorticis.Application.Obrigacoes;
using Vorticis.Domain.Obrigacoes;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Infra.Tests;

public class ObrigacoesTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    [Fact]
    public async Task Baixas_e_personalizadas_persistem_e_perfis_padrao_recebem_a_permissao()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var admin = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("11222333000181");
        foreach (var perfil in new[] { "Administrador", "Contador", "Assistente" })
            Assert.Equal(1, await db.EscalarAsync<long>("SELECT COUNT(*) FROM core.perfil_permissao pp JOIN core.perfil p ON p.id = pp.perfil_id WHERE p.nome = @n AND pp.permissao = 'OBRIGACOES_MANTER'", ("n", perfil)));
        Assert.Equal(0, await db.EscalarAsync<long>("SELECT COUNT(*) FROM core.perfil_permissao pp JOIN core.perfil p ON p.id = pp.perfil_id WHERE p.nome = 'Consulta' AND pp.permissao = 'OBRIGACOES_MANTER'"));

        var hoje = new DateOnly(2026, 4, 25);
        var lista = await db.App.Obrigacoes.ListarAsync(admin, empresa, new(2026, 4, 1), new(2026, 4, 30), hoje, null, ct);
        Assert.True(lista.Sucesso, string.Join(";", lista.Erros));
        Assert.Contains(lista.Valor, i => i.Ocorrencia.Definicao.Codigo == "PIS_COFINS");            // Lucro Presumido
        Assert.DoesNotContain(lista.Valor, i => i.Ocorrencia.Definicao.Codigo is "DAS" or "FGTS");   // regime e sem empregados

        Assert.True((await db.App.Obrigacoes.MarcarCumpridaAsync(admin, empresa, "PIS_COFINS", "2026-03", new(2026, 4, 24), "ok", ct)).Sucesso);
        var p = await db.App.Obrigacoes.SalvarPersonalizadaAsync(admin, empresa,
            new(0, "ISS", "Prefeitura", "Municipal", Periodicidade.Mensal, TipoRegra.DiaFixo, 10, 1, 1, 0, AjusteNaoUtil.Posterga, "obs"), ct);
        Assert.True(p.Sucesso, string.Join(";", p.Erros));
        Assert.True((await db.App.Obrigacoes.MarcarCumpridaAsync(admin, empresa, $"P{p.Valor}", "2026-03", new(2026, 4, 9), null, ct)).Sucesso);

        var depois = (await db.App.Obrigacoes.ListarAsync(admin, empresa, new(2026, 4, 1), new(2026, 4, 30), hoje, null, ct)).Valor;
        Assert.Equal(StatusObrigacao.Cumprida, depois.Single(i => i.Ocorrencia.Definicao.Codigo == "PIS_COFINS").Status);
        Assert.Equal(StatusObrigacao.Cumprida, depois.Single(i => i.Ocorrencia.Definicao.Personalizada).Status);

        Assert.True((await db.App.Obrigacoes.ExcluirPersonalizadaAsync(admin, empresa, p.Valor, ct)).Sucesso);
        Assert.Equal(0, await db.EscalarAsync<long>("SELECT COUNT(*) FROM core.obrigacao_status WHERE obrigacao ~ '^P[0-9]+$'"));
        Assert.Empty((await db.App.Obrigacoes.ListarPersonalizadasAsync(admin, empresa, ct)).Valor);
    }
}
