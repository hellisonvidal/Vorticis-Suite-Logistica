using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Vorticis.Application.Relatorios;
using Vorticis.Fiscal.Desktop.ViewModels;
using Vorticis.Fiscal.Desktop.Views;

namespace Vorticis.Desktop.Tests;

/// <summary>As telas do módulo Fiscal carregam (XAML com bindings compilados), recebem o ViewModel e renderizam sem exceção.</summary>
public class FiscalViewsTests(SessaoHeadless headless)
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
    public async Task Todas_as_telas_fiscais_renderizam()
    {
        var concluido = await ForaDaSessao(headless.Sessao.Dispatch(async () =>
        {
            var ctx = new Vorticis.Fiscal.Desktop.MainViewModel();
            var pares = new (object Vm, UserControl View)[]
            {
                (new ImportacaoXmlViewModel(ctx), new ImportacaoXmlView()),
                (new FechamentoFiscalViewModel(ctx), new FechamentoFiscalView()),
                (new AjustesIcmsViewModel(ctx), new AjustesIcmsView()),
                (new ContasConsumoViewModel(ctx), new ContasConsumoView()),
                (new InventarioViewModel(ctx), new InventarioView()),
                (new DocumentosViewModel(ctx), new DocumentosView()),
                (new LivroEntradasViewModel(ctx), new LivroFiscalView()),
                (new LivroSaidasViewModel(ctx), new LivroFiscalView()),
                (new LivroApuracaoIcmsViewModel(ctx), new LivroApuracaoView()),
                (new LivroApuracaoIpiViewModel(ctx), new LivroApuracaoView()),
                (new ResumoCfopViewModel(ctx), new ResumoCfopView()),
                (new NotaEntradaViewModel(ctx), new DigitacaoDocumentoView()),
                (new NotaSaidaViewModel(ctx), new DigitacaoDocumentoView()),
                (new NotaEntradaViewModel(ctx), new DigitacaoNotaView()),
                (new NotaSaidaViewModel(ctx), new DigitacaoNotaView()),
                (new ProdutosViewModel(ctx), new ProdutosView()),
                (new RelatorioProdutosViewModel(ctx), new RelatorioProdutosView()),
                (new RelatorioItensNotasViewModel(ctx), new RelatorioItensNotasView()),
                (new RegrasContabilizacaoViewModel(ctx), new RegrasContabilizacaoView()),
                (new ContabilizarNotasViewModel(ctx), new ContabilizarNotasView()),
                (new ApuracaoSimplesViewModel(ctx), new ApuracaoSimplesView()),
                (new BaseMensalViewModel(ctx), new BaseMensalView()),
                (new ApuracaoPresumidoViewModel(ctx), new ApuracaoPresumidoView()),
                (new ApuracaoRealViewModel(ctx), new ApuracaoRealView()),
                (new ApuracaoPisCofinsViewModel(ctx), new ApuracaoPisCofinsView()),
                (new RetencoesViewModel(ctx), new RetencoesView()),
                (new GuiasViewModel(ctx), new GuiasView()),
                (new ContasTributosViewModel(ctx), new ContasTributosView()),
            };
            foreach (var (vm, view) in pares)
            {
                view.DataContext = vm;
                var janela = new Window { Content = view, Width = 1280, Height = 760 };
                janela.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.NotNull(janela.CaptureRenderedFrame());
                janela.Close();
            }

            // Grade montada em código para as tabelas dos relatórios
            var tabela = new TabelaRelatorio("Teste", [new("Nome"), new("Valor", TipoColuna.Valor)],
                [new LinhaRelatorio(["A", 10m]), new LinhaRelatorio(["TOTAL", 10m], true)], "rodapé");
            var exibida = new TabelaExibidaView { DataContext = new TabelaExibida(tabela) };
            var jt = new Window { Content = exibida, Width = 600, Height = 300 };
            jt.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(jt.CaptureRenderedFrame());
            jt.Close();
            await Task.CompletedTask;
            return true;
        }, TestContext.Current.CancellationToken));
        Assert.True(concluido);
    }
}
