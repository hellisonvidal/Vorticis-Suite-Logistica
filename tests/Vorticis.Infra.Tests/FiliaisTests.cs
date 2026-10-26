using Vorticis.Application.Contabil;
using Vorticis.Domain.Contabil;
using Vorticis.Infra.Cadastros;

namespace Vorticis.Infra.Tests;

public class FiliaisTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task LancarAsync(long empresa, DateOnly data, decimal valor, string receita = "3.1.01.001")
    {
        var r = await db.App.Lancamentos.EfetivarAsync(new LancamentoProposto(empresa, data, OrigemLancamento.Manual,
            [new PartidaProposta("1.1.01.001", IndicadorDC.D, valor, "Teste"), new PartidaProposta(receita, IndicadorDC.C, valor, "Teste")]), await db.AdminAsync(), Ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
    }

    [Fact]
    public async Task Filial_se_liga_a_matriz_de_mesma_raiz_e_os_relatorios_saem_individuais_ou_agrupados()
    {
        BancoDeTeste.ExigirBanco();
        var adm = await db.AdminAsync();
        var matriz = await db.CriarEmpresaAsync("11222333000181");
        var filial = await db.CriarEmpresaAsync("11222333000262");
        var outra = await db.CriarEmpresaAsync("98765432000198");
        var filiais = new ServicoFiliais(db.App.DataSource, db.App.Autorizador);

        Assert.Equal([matriz], (await filiais.MatrizesPossiveisAsync(filial, adm, Ct)).Select(e => e.Id));   // só a mesma raiz
        Assert.Contains("raiz de CNPJ", (await filiais.DefinirMatrizAsync(outra, matriz, adm, Ct)).Erros[0]);
        Assert.True((await filiais.DefinirMatrizAsync(filial, matriz, adm, Ct)).Sucesso);
        Assert.Contains("já é filial", (await filiais.DefinirMatrizAsync(matriz, filial, adm, Ct)).Erros[0]);
        var grupo = await filiais.GrupoAsync(filial, adm, Ct);
        Assert.Equal([(matriz, true), (filial, false)], grupo.Select(g => (g.Id, g.EhMatriz)));
        Assert.Equal(grupo.Select(g => g.Id), (await filiais.GrupoAsync(matriz, adm, Ct)).Select(g => g.Id));
        Assert.Single(await filiais.GrupoAsync(outra, adm, Ct));

        // receita de 1.000 na matriz e 400 na filial
        await LancarAsync(matriz, new DateOnly(2026, 3, 5), 1000m);
        await LancarAsync(filial, new DateOnly(2026, 3, 6), 400m);
        var svc = new ServicoConsolidacao(db.App.Relatorios, db.App.Demonstracoes);
        EmpresaRelatorio[] ambas = [new(matriz, "Matriz"), new(filial, "Filial")];
        var (ini, fim) = (new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        var agrupado = (await svc.BalanceteAsync(ambas, ini, fim, FormaConsolidacao.Agrupado, adm, Ct)).Valor;
        var caixa = agrupado.Linhas.Single(l => (string)l.Valores[0]! == "1.1.01.001");
        Assert.Equal(("1.400,00 D", 1400m, 0m), ((string)caixa.Valores[5]!, (decimal)caixa.Valores[3]!, (decimal)caixa.Valores[4]!));
        Assert.Contains("agrupado (2 empresas)", agrupado.Titulo);

        var lado = (await svc.BalanceteAsync(ambas, ini, fim, FormaConsolidacao.LadoALado, adm, Ct)).Valor;
        Assert.Equal(["Conta", "Descrição", "Matriz", "Filial", "Total"], lado.Colunas.Select(c => c.Titulo));
        Assert.Equal(["1.000,00 D", "400,00 D", "1.400,00 D"], lado.Linhas.Single(l => (string)l.Valores[0]! == "1.1.01.001").Valores.Skip(2).Cast<string>());

        var individual = (await svc.BalanceteAsync([new(filial, "Filial")], ini, fim, FormaConsolidacao.LadoALado, adm, Ct)).Valor;
        Assert.Equal("400,00 D", individual.Linhas.Single(l => (string)l.Valores[0]! == "1.1.01.001").Valores[5]);   // uma empresa: individual

        var dre = (await svc.DreAsync(ambas, ini, fim, 3, FormaConsolidacao.LadoALado, adm, Ct)).Valor;
        Assert.Equal([1000m, 400m, 1400m], dre.Linhas[^1].Valores.Skip(2).Cast<decimal>());          // resultado de cada uma e o total
        var balanco = (await svc.BalancoAsync(ambas, fim, 3, FormaConsolidacao.Agrupado, adm, Ct)).Valor;
        Assert.Equal(2, balanco.Count);
        Assert.Contains(balanco[0].Linhas, l => l.Valores.Contains(1400m));

        Assert.False((await svc.BalanceteAsync([], ini, fim, FormaConsolidacao.Agrupado, adm, Ct)).Sucesso);
        Assert.True((await filiais.DefinirMatrizAsync(filial, null, adm, Ct)).Sucesso);
        Assert.Single(await filiais.GrupoAsync(filial, adm, Ct));
    }
}
