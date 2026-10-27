using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Desktop.Comum.Views;
using Vorticis.Infra.Cnpj;
using Vorticis.Infra.Tests;
using Vorticis.Logistica.Desktop;

namespace Vorticis.Desktop.Tests;

/// <summary>
/// Vorticis Suite Logística: sete módulos na mesma janela (login único), cinco layouts de navegação e o seletor de layouts.
/// Com VORTICIS_SCREENSHOTS, salva uma foto de cada layout.
/// </summary>
public class SuiteLogisticaTests(BancoDeTeste db, SessaoHeadless headless) : IClassFixture<BancoDeTeste>
{
    private static readonly string? Pasta = Environment.GetEnvironmentVariable("VORTICIS_SCREENSHOTS");

    private static void Foto(TopLevel janela, string nome)
    {
        Dispatcher.UIThread.RunJobs();
        janela.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        var quadro = janela.CaptureRenderedFrame();
        Assert.NotNull(quadro);
        if (Pasta is not null) quadro.Save(Path.Combine(Pasta, nome + ".png"));
    }

    private static async Task<T> ForaDaSessao<T>(Task<T> tarefa)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = tarefa.ContinueWith(t =>
        {
            if (t.IsFaulted) tcs.SetException(t.Exception!.InnerExceptions);
            else if (t.IsCanceled) tcs.SetCanceled();
            else tcs.SetResult(t.Result);
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        return await tcs.Task;
    }

    private static SuiteViewModel NovaSuite(LayoutSuite layout) =>
        new([.. ModuloLogistica.Todos().Select(m => new MainViewModel(m))], layout, ModuloLogistica.NomeSuite, "VorticisLogistica.Frota", ordemAlfabetica: false);

    [Fact]
    public async Task Sete_modulos_na_ordem_do_trabalho_com_icone_e_cor() => Assert.True(await ForaDaSessao(headless.Sessao.Dispatch(() =>
    {
        var suite = NovaSuite(LayoutSuite.Lateral);
        Assert.Equal(["Frota", "Transporte", "Manutenção", "Faturamento", "Financeiro", "Gerencial", "Contabilidade"], suite.Modulos.Select(m => m.Nome));
        Assert.All(suite.Modulos, m => Assert.NotNull(m.Icone));
        Assert.Equal(7, suite.Modulos.Select(m => m.Vm.Modulo.CorDestaque).Distinct().Count());
        Assert.Equal("Frota", suite.Principal.Modulo.NomeCurto);   // o login é feito no módulo Frota e vale para todos
        Assert.Equal(5, suite.OpcoesLayout.Count);
        Assert.Single(suite.OpcoesLayout, o => o.Selecionado);
        return Task.FromResult(true);
    }, TestContext.Current.CancellationToken)));

    [Fact]
    public async Task Icones_sao_caminhos_validos() => Assert.True(await ForaDaSessao(headless.Sessao.Dispatch(() =>
    {
        foreach (var d in new[] { IconesModulos.Frota, IconesModulos.Transporte, IconesModulos.Manutencao, IconesModulos.Faturamento, IconesModulos.Financeiro,
                                  IconesModulos.Gerencial, IconesModulos.Contabilidade, IconesModulos.Marca, IconesModulos.Layout, IconesModulos.Fixar,
                                  IconesModulos.Inicio, IconesModulos.Busca })
        {
            var g = Avalonia.Media.Geometry.Parse(d);
            Assert.InRange(g.Bounds.Left, 0, 24);
            Assert.InRange(g.Bounds.Right, 0, 24.01);
            Assert.InRange(g.Bounds.Bottom, 0, 24.01);
        }
        return Task.FromResult(true);
    }, TestContext.Current.CancellationToken)));

    [Fact]
    public void Navegacao_recolhe_e_fixa_no_topo_e_a_esquerda()
    {
        var anterior = SuiteViewModel.ArquivoLayout;
        SuiteViewModel.ArquivoLayout = Path.Combine(Path.GetTempPath(), $"suite-{Guid.NewGuid():N}.txt");
        try
        {
            var s = NovaSuite(LayoutSuite.Faixa);
            Assert.True(s.EhTopo);
            Assert.True(s.MostraTopoRecolhido);                  // solta, a faixa de comando fica recolhida
            s.TopoAberto = true;                                 // mouse no alto da janela
            Assert.True(s.MostraFaixaComando);
            s.TopoAberto = false;
            s.AlternarNavegacaoFixaCommand.Execute(null);        // fixar
            Assert.True(s.TopoFixo && s.MostraFaixaComando && s.NavegacaoFixa);
            s.EscolherLayoutCommand.Execute(LayoutSuite.Lateral);
            Assert.True(s.MostraFaixaLateral);                   // a lateral tem o seu próprio "fixar"
            Assert.False(s.NavegacaoFixa);
            Assert.True(NovaSuite(LayoutSuite.Abas).TopoFixo);   // a preferência fica gravada
            Assert.Equal(LayoutSuite.Lateral, SuiteViewModel.LerLayout());
            Assert.Single(s.OpcoesLayout, o => o.Selecionado && o.Layout == LayoutSuite.Lateral);
        }
        finally { SuiteViewModel.ArquivoLayout = anterior; }
    }

    [Fact]
    public async Task Fotos_dos_cinco_layouts_com_login_unico()
    {
        BancoDeTeste.ExigirBanco();
        var adm = await db.AdminAsync();
        await db.App.Perfis.ConcluirConfiguracaoInicialAsync(new Vorticis.Domain.Seguranca.SessaoUsuario(adm, "admin_teste", "Admin", true, false), TestContext.Current.CancellationToken);
        var empresa = await db.CriarEmpresaAsync("11444777000161");
        await db.EscalarAsync<int>("UPDATE core.empresa SET modulo_frota = TRUE, modulo_erp = TRUE, modulo_fiscal = TRUE, modulo_contabil = TRUE WHERE id = @e RETURNING 1", ("e", empresa));
        var anterior = SuiteViewModel.ArquivoLayout;
        SuiteViewModel.ArquivoLayout = Path.Combine(Path.GetTempPath(), $"suite-{Guid.NewGuid():N}.txt");
        try
        {
            var r = await ForaDaSessao(headless.Sessao.Dispatch(async () =>
            {
                var suite = NovaSuite(LayoutSuite.Painel);
                var janela = new MainWindow { Width = 1500, Height = 900 };
                janela.UsarSuite(suite);
                janela.Show();
                var http = ConsultaCnpjTests.HttpCnpjEInscricoes();
                var principal = suite.Principal;
                await principal.InicializarAsync(db.ConnectionString, new ConsultaCnpjPublica(new HttpClient(http)), new ConsultaInscricaoEstadualPublica(new HttpClient(http)));
                (principal.Login.Usuario, principal.Login.Senha) = ("admin_teste", "Senha1234");
                await principal.Login.EntrarCommand.ExecuteAsync(null);
                var relogio = System.Diagnostics.Stopwatch.StartNew();
                for (var i = 0; i < 3000 && suite.Modulos.Any(m => !m.Vm.Logado); i++) { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); }
                Assert.All(suite.Modulos, m => Assert.True(m.Vm.Logado, $"{m.Nome}: {m.Vm.Status} ({relogio.ElapsedMilliseconds} ms)"));   // um login vale para os sete
                suite.TopoFixo = true;
                Foto(janela, "suite_D_painel");
                suite.Trocar(suite.Modulos[0]);
                Foto(janela, "suite_D_modulo");
                foreach (var (layout, nome, modulo) in new[] { (LayoutSuite.Abas, "suite_A_abas", 1), (LayoutSuite.Lateral, "suite_B_lateral", 2),
                                                              (LayoutSuite.Arvore, "suite_C_arvore", 6), (LayoutSuite.Faixa, "suite_E_faixa", 4) })
                {
                    suite.Layout = layout;
                    (suite.TopoFixo, suite.LateralFixa) = (true, true);
                    suite.Trocar(suite.Modulos[modulo]);
                    Assert.Same(suite.Modulos[modulo].Vm, janela.DataContext);
                    Foto(janela, nome);
                }
                (suite.TopoFixo, suite.LateralFixa) = (false, false);
                suite.Layout = LayoutSuite.Faixa;
                Foto(janela, "suite_E_recolhida");
                suite.Layout = LayoutSuite.Lateral;
                Foto(janela, "suite_B_recolhida");
                suite.Busca = "viag";
                Assert.Contains(suite.Resultados, n => n.Titulo == "Viagens" && n.Modulo.Nome == "Frota");
                suite.Busca = "";

                // seletor de layouts: os cinco cartões com prévia
                var seletor = janela.GetVisualDescendants().OfType<SuiteEscolhaLayoutView>().First();
                var conteudo = (Control)((Flyout)seletor.FindControl<Button>("BotaoLayout")!.Flyout!).Content!;
                var vitrine = new Window { Width = 900, Height = 420, Content = conteudo, DataContext = suite, Background = Avalonia.Media.Brushes.LightGray };
                vitrine.Show();
                Foto(vitrine, "suite_seletor_layouts");
                vitrine.Close();
                janela.Close();
                return true;
            }, TestContext.Current.CancellationToken));
            Assert.True(r);
        }
        finally { SuiteViewModel.ArquivoLayout = anterior; }
    }
}
