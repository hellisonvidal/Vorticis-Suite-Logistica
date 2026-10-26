using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Vorticis.Application.Relatorios;
using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Desktop.Comum.Views;
using Vorticis.Frota;
using Vorticis.Frota.Desktop;
using Vorticis.Infra.Tests;

namespace Vorticis.Desktop.Tests;

/// <summary>Relatórios › Gráficos: menu por área, dados das funções do sistema e o desenho do gráfico com a tabela.</summary>
public class GraficosTelasTests(SessaoHeadless headless, BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static async Task<T> ForaDaSessao<T>(Task<T> tarefa)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = tarefa.ContinueWith(t => { if (t.IsFaulted) tcs.SetException(t.Exception!.InnerExceptions); else tcs.SetResult(t.Result); }, TaskScheduler.Default);
        return await tcs.Task;
    }

    [Fact]
    public void Menu_de_graficos_por_area_com_telas_conhecidas()
    {
        var vm = new Logistica.Desktop.MainViewModel();
        var graficos = vm.Menus.Single(m => m.Titulo == "_Relatórios").Filhos!.Single(m => m.Titulo == "Grá_ficos");
        Assert.Equal(["Frota", "Transporte", "Contábil", "Financeiro"], graficos.Filhos!.Select(a => a.Titulo));
        Assert.Equal(9, graficos.Filhos!.SelectMany(a => a.Filhos!).Count());
        Assert.All(graficos.Filhos!.SelectMany(a => a.Filhos!), i => Assert.True(vm.ConheceTela(i.Chave!), i.Chave));
    }

    [Fact]
    public async Task Custos_da_frota_por_mes_somam_cada_origem()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("11444777000161");
        var svc = ServicosFrota.De(db.App);
        var veiculo = (await svc.GravarVeiculoAsync(empresa, new Veiculo(0, 1, "ABC1D23", null, "CAVALO", null, null, null, null, "DIESEL_S10", null, 0, null), adm, ct)).Valor;
        Assert.True((await svc.GravarAbastecimentoAsync(empresa, new Abastecimento(0, veiculo, null, new DateOnly(2026, 2, 10), 1000, 100, 600, "DIESEL_S10", null), adm, ct)).Sucesso);
        Assert.True((await svc.GravarAbastecimentoAsync(empresa, new Abastecimento(0, veiculo, null, new DateOnly(2026, 3, 10), 1500, 200, 1200, "DIESEL_S10", null), adm, ct)).Sucesso);
        var def = GraficosFrota.Todos.Single(g => g.Chave == "custosfrota");
        var d = await def.Gerar(new ContextoGrafico(db.App, empresa, adm, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)));
        Assert.Equal(["jan/26", "fev/26", "mar/26"], d.Categorias);
        Assert.Equal(["Combustível", "Manutenção", "Despesas de viagem", "Pedágio"], d.Series.Select(s => s.Nome));
        Assert.Equal([0m, 600m, 1200m], d.Series[0].Valores);
        var consumo = await GraficosFrota.Todos.Single(g => g.Chave == "consumoveiculo").Gerar(new ContextoGrafico(db.App, empresa, adm, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)));
        Assert.Equal(["ABC1D23"], consumo.Categorias);
        Assert.Equal(2.5m, consumo.Series[0].Valores[0]);   // 500 km ÷ 200 l
    }

    [Fact]
    public async Task Grafico_desenha_barras_e_linhas_com_a_tabela_dos_valores()
    {
        var pasta = Environment.GetEnvironmentVariable("VORTICIS_FOTOS_GRAFICOS");
        var ok = await ForaDaSessao(headless.Sessao.Dispatch(() =>
        {
            var m = new Logistica.Desktop.MainViewModel();
            var def = new DefinicaoGrafico("teste", "Custos da frota por mês", "Frota", _ => Task.FromResult<DadosGrafico>(null!));
            DadosGrafico[] exemplos =
            [
                new("Custos", "R$", FormaGrafico.Barras, ["jan/26", "fev/26", "mar/26", "abr/26", "mai/26", "jun/26"],
                    [new("Combustível", [52000m, 48500m, 61200m, 58900m, 63000m, 60100m]), new("Manutenção", [12000m, 30500m, 9800m, 15000m, 7200m, 22000m]),
                     new("Despesas de viagem", [8000m, 7600m, 9900m, 8800m, 9100m, 10200m]), new("Pedágio", [6400m, 6100m, 7000m, 6900m, 7300m, 7100m])]),
                new("Resultado", "R$", FormaGrafico.Barras, ["jan/26", "fev/26", "mar/26", "abr/26"], [new("Lucro (+) ou prejuízo (−)", [35000m, -12000m, 18000m, 22000m])]),
                new("Saldo", "R$", FormaGrafico.Linhas, ["jan/26", "fev/26", "mar/26", "abr/26", "mai/26"], [new("A receber", [80000m, 92000m, 76000m, 101000m, 98000m]),
                                                                                                         new("A pagar", [61000m, 70000m, 82000m, 69000m, 72000m])]),
            ];
            var n = 0;
            foreach (var dados in exemplos)
            {
                var vm = new GraficoViewModel(m, def) { Dados = dados };
                foreach (var s in dados.Series) vm.Colunas.Add(s.Nome);
                for (var i = 0; i < dados.Categorias.Count; i++)
                    vm.Tabela.Add(new LinhaGrafico(dados.Categorias[i], [.. dados.Series.Select(s => GraficoControl.Valor(s.Valores[i], dados.Unidade))]));
                var view = new GraficoView { DataContext = vm };
                var janela = new Window { Content = view, Width = 1200, Height = 720 };
                janela.Show();
                Dispatcher.UIThread.RunJobs();
                var quadro = janela.CaptureRenderedFrame();
                Assert.NotNull(quadro);
                Assert.Equal(dados.Series.Count + 1, view.FindControl<DataGrid>("Grade")!.Columns.Count);   // categoria + uma coluna por série
                if (pasta is not null) quadro.Save(Path.Combine(pasta, $"grafico_{++n}.png"));
                janela.Close();
            }
            return Task.FromResult(true);
        }, TestContext.Current.CancellationToken));
        Assert.True(ok);
    }
}
