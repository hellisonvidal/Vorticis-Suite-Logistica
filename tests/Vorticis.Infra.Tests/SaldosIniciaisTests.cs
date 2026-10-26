using Vorticis.Application.Contabil;
using Vorticis.Domain.Contabil;

namespace Vorticis.Infra.Tests;

public class SaldosIniciaisTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    [Fact]
    public async Task Implanta_saldos_na_abertura_do_exercicio_uma_unica_vez()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync("11222333000181");
        var adm = await db.AdminAsync();
        var svc = db.App.Lancamentos;

        Assert.Contains("Diferença", (await svc.LancarSaldosIniciaisAsync(empresa, 2026,
            [new("1.1.01.002", 1000m, IndicadorDC.D), new("2.3.01.001", 900m, IndicadorDC.C)], adm, ct)).Erros.Single(e => e.Contains("Diferença")));
        Assert.Contains("contas patrimoniais", (await svc.LancarSaldosIniciaisAsync(empresa, 2026,
            [new("1.1.01.002", 10m, IndicadorDC.D), new("3.1.01.001", 10m, IndicadorDC.C)], adm, ct)).Erros[0]);

        var ok = await svc.LancarSaldosIniciaisAsync(empresa, 2026,
        [
            new("1.1.01.002", 80_000m, IndicadorDC.D),
            new("1.2.03.003", 45_000m, IndicadorDC.D),
            new("1.2.03.007", 9_000m, IndicadorDC.C),     // depreciação acumulada (redutora)
            new("2.1.01.001", 16_000m, IndicadorDC.C),
            new("2.3.01.001", 100_000m, IndicadorDC.C),
            new("1.1.02.001", 0m, IndicadorDC.D),         // zerada: ignorada
        ], adm, ct);
        Assert.True(ok.Sucesso, string.Join("; ", ok.Erros));

        var diario = (await db.App.Relatorios.DiarioAsync(empresa, new(2026, 1, 1), new(2026, 1, 1), adm, ct)).Valor;
        Assert.Equal(5, diario.Count);
        Assert.All(diario, l => Assert.Equal(new DateOnly(2026, 1, 1), l.Data));

        var bal = (await db.App.Relatorios.BalanceteAsync(empresa, new(2026, 1, 1), new(2026, 1, 31), adm, ct: ct)).Valor;
        Assert.Equal(116_000m, bal.Single(l => l.Codigo == "1").SaldoFinal);

        Assert.Contains("Já existem saldos iniciais", (await svc.LancarSaldosIniciaisAsync(empresa, 2026,
            [new("1.1.01.002", 1m, IndicadorDC.D), new("2.3.01.001", 1m, IndicadorDC.C)], adm, ct)).Erros[0]);
    }
}
