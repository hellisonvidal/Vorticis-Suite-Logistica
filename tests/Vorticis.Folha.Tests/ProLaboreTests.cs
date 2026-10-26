using Vorticis.Application.Cadastros;
using Vorticis.Folha.Regras;

namespace Vorticis.Folha.Tests;

public class ProLaboreRegrasTests
{
    private static readonly ParametrosProLabore P = ParametrosProLabore.Em(new DateOnly(2026, 6, 1), TabelasLegais.Embutidas());

    [Fact]
    public void Inss_de_11_por_cento_irrf_e_cpp_patronal()
    {
        var r = CalculoProLabore.Calcular(5000m, 0, 0, CalculosTests.T2026, P, RegimeEncargos.Geral);
        Assert.Equal(550m, r.Inss);
        Assert.Equal(1000m, r.InssPatronal);
        Assert.Equal(5000m - 550m - r.Irrf, r.Liquido);
        Assert.Equal(CalculoIrrf.Calcular(5000m, 550m, 0, 0, CalculosTests.T2026.Irrf).Imposto, r.Irrf);
    }

    [Fact]
    public void Inss_limitado_ao_teto_considerando_outras_fontes()
    {
        var teto = Math.Round(CalculosTests.T2026.Inss.Teto * 0.11m, 2);
        Assert.Equal(teto, CalculoProLabore.Calcular(20000m, 0, 0, CalculosTests.T2026, P, RegimeEncargos.Geral).Inss);
        Assert.Equal(teto - 500m, CalculoProLabore.Calcular(20000m, 500m, 0, CalculosTests.T2026, P, RegimeEncargos.Geral).Inss);
        Assert.Equal(0m, CalculoProLabore.Calcular(3000m, teto, 0, CalculosTests.T2026, P, RegimeEncargos.Geral).Inss);
    }

    [Fact]
    public void Simples_sem_cpp_nao_tem_patronal()
        => Assert.Equal(0m, CalculoProLabore.Calcular(3000m, 0, 0, CalculosTests.T2026, P, RegimeEncargos.SimplesSemCpp).InssPatronal);
}

public class ProLaboreIntegracaoTests(BancoFolha db) : IClassFixture<BancoFolha>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Calcula_so_quem_retira_e_bloqueia_com_a_competencia_fechada()
    {
        BancoFolha.ExigirBanco();
        var empresa = await db.CriarEmpresaAsync("45723174000110", RegimeTributario.LucroPresumido);
        var adm = await db.AdminAsync();
        var s1 = await db.Escalar<long>("INSERT INTO core.socio (empresa_id, nome, cpf_cnpj, qualificacao) VALUES (@e, 'Ana Sócia', '52998224725', 'Sócio-Administrador') RETURNING id", ("e", empresa));
        await db.Escalar<long>("INSERT INTO core.socio (empresa_id, nome, qualificacao) VALUES (@e, 'Bruno Cotista', 'Sócio') RETURNING id", ("e", empresa));
        var pl = db.Folha.ProLabore;
        var socios = await pl.ListarAsync(empresa, adm, Ct);
        Assert.Equal(2, socios.Count);

        var ana = socios.Single(s => s.SocioId == s1);
        Assert.Contains("valor mensal", (await pl.GravarAsync(empresa, ana with { Retira = true, Valor = 0, Inicio = new DateOnly(2026, 1, 1) }, adm, Ct)).Erros[0]);
        Assert.True((await pl.GravarAsync(empresa, ana with { Retira = true, Valor = 4000m, Inicio = new DateOnly(2026, 1, 1) }, adm, Ct)).Sucesso);

        var comp = new DateOnly(2026, 5, 1);
        var calc = await pl.CalcularAsync(empresa, comp, adm, Ct);
        Assert.True(calc.Sucesso, string.Join("; ", calc.Erros));
        Assert.Equal((440m, 800m), (calc.Valor.Single().Inss, calc.Valor.Single().InssPatronal));

        Assert.True((await db.Folha.Folha.FecharAsync(empresa, comp, adm, Ct)).Sucesso);
        Assert.Contains("fechada", (await pl.CalcularAsync(empresa, comp, adm, Ct)).Erros[0]);
        Assert.Contains("fechada", (await pl.ExcluirCalculosAsync(empresa, comp, adm, Ct)).Erros[0]);
        Assert.Single(await pl.ListarCalculosAsync(empresa, comp, adm, Ct));
    }
}
