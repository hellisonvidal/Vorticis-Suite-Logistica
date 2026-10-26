using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vorticis.Folha.Desktop;
using Vorticis.Folha.Desktop.Views;

namespace Vorticis.Desktop.Tests;

/// <summary>Todas as telas do módulo Folha carregam o XAML, ligam os bindings compilados ao ViewModel e renderizam (sem banco).</summary>
public class FolhaTelasTests(SessaoHeadless headless)
{
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

    [Fact]
    public async Task Cada_tela_da_folha_renderiza_com_o_seu_viewmodel()
    {
        var total = await ForaDaSessao(headless.Sessao.Dispatch(async () =>
        {
            var vm = new MainViewModel();
            (UserControl View, object Contexto)[] telas =
            [
                (new FuncionariosView(), vm.Funcionarios), (new CargosView(), vm.Cargos), (new DepartamentosView(), vm.Departamentos),
                (new RubricasView(), vm.Rubricas), (new ParametrosFolhaView(), vm.Parametros), (new EventosVariaveisView(), vm.Eventos),
                (new FolhaMensalView(), vm.FolhaMensal), (new FeriasView(), vm.Ferias), (new DecimoTerceiroView(), vm.Decimo),
                (new RescisoesView(), vm.Rescisoes), (new ContabilizacaoFolhaView(), vm.Contabilizacao), (new ESocialView(), vm.ESocial),
                (new ResumoFolhaView(), vm.Resumo), (new EncargosFolhaView(), vm.Encargos), (new ProvisoesFolhaView(), vm.Provisoes),
                (new AssistenteEsocialView(), vm.AssistenteEsocial),
            ];
            foreach (var (view, contexto) in telas)
            {
                view.DataContext = contexto;
                var janela = new Window { Content = view, Width = 1200, Height = 800 };
                janela.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.NotNull(janela.CaptureRenderedFrame());
                janela.Close();
            }
            await Task.Yield();
            return telas.Length;
        }, TestContext.Current.CancellationToken));
        Assert.Equal(16, total);
    }

    [Fact]
    public async Task Relatorios_e_telas_da_folha_escolhem_a_competencia_por_mes_e_ano_com_ano_de_4_digitos()
    {
        var ok = await ForaDaSessao(headless.Sessao.Dispatch(async () =>
        {
            var vm = new MainViewModel();
            (UserControl View, object Contexto)[] telas =
            [
                (new FolhaMensalView(), vm.FolhaMensal), (new ResumoFolhaView(), vm.Resumo), (new EncargosFolhaView(), vm.Encargos),
                (new ProvisoesFolhaView(), vm.Provisoes), (new EventosVariaveisView(), vm.Eventos), (new ContabilizacaoFolhaView(), vm.Contabilizacao),
                (new ESocialView(), vm.ESocial), (new AssistenteEsocialView(), vm.AssistenteEsocial),
            ];
            foreach (var (view, contexto) in telas)
            {
                view.DataContext = contexto;
                var janela = new Window { Content = view, Width = 1200, Height = 800 };
                janela.Show();
                Dispatcher.UIThread.RunJobs();
                var seletor = Assert.Single(view.GetVisualDescendants().OfType<Vorticis.Desktop.Comum.Views.SeletorCompetencia>());
                var ano = seletor.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "AnoCompetencia");
                var mes = seletor.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "MesCompetencia");
                ano.SelectedItem = 2025;
                mes.SelectedIndex = 6;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(new DateTime(2025, 7, 1), (DateTime?)contexto.GetType().GetProperty("Competencia")!.GetValue(contexto));
                // o ano aparece inteiro (4 dígitos) na caixa
                var texto = ano.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "2025");
                Assert.True(texto.Bounds.Width + 1 >= texto.DesiredSize.Width && ano.Bounds.Width >= 80, $"{texto.Bounds.Width} / {ano.Bounds.Width}");
                janela.Close();
            }
            await Task.Yield();
            return true;
        }, TestContext.Current.CancellationToken));
        Assert.True(ok);
    }

    [Fact]
    public async Task Linha_de_menus_da_folha_cabe_na_tela_de_1366_pixels()
    {
        var sobra = await ForaDaSessao(headless.Sessao.Dispatch(async () =>
        {
            var vm = new MainViewModel { Logado = true, Sessao = new Vorticis.Domain.Seguranca.SessaoUsuario(1, "maria", "Maria Aparecida dos Santos", true, false) };
            var janela = new Vorticis.Desktop.Comum.Views.MainWindow { DataContext = vm, Width = 1366, Height = 740 };
            janela.Show();
            Dispatcher.UIThread.RunJobs();
            var menu = janela.FindControl<Menu>("MenuPrincipal")!;
            var barra = (Control)menu.Parent!;
            // fim do último menu x início dos controles da direita (empresa e usuário), na mesma linha
            var fimMenu = menu.Bounds.Left + menu.DesiredSize.Width;
            var inicioDireita = barra.GetVisualChildren().OfType<Control>().Where(c => c != menu && Grid.GetColumn(c) >= 3).Min(c => c.Bounds.Left);
            Assert.True(vm.MostraFaixaPeriodo);                       // a competência fica numa faixa própria, abaixo
            // ... na mesma linha das funções abertas (faixa fixa logo abaixo dos menus)
            var faixa = janela.FindControl<Border>("BarraFuncoes")!;
            Assert.True(faixa.IsVisible);
            Assert.True(janela.FindControl<StackPanel>("FaixaPeriodo")!.IsVisible);
            Assert.Same(faixa, janela.FindControl<StackPanel>("FaixaPeriodo")!.FindAncestorOfType<Border>());
            janela.Hide();                                           // Close pediria confirmação ("Deseja realmente fechar?")
            await Task.CompletedTask;
            return inicioDireita - fimMenu;
        }, TestContext.Current.CancellationToken));
        // com a linha cheia o Grid espreme a coluna do meio a zero (e corta os menus): exige uma folga de verdade
        Assert.True(sobra > 40, $"A linha de menus não cabe: sobram só {sobra:N0} px entre os menus e a empresa.");
    }
}
