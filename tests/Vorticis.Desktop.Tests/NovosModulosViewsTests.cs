using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using R = Vorticis.Erp.Desktop;

namespace Vorticis.Desktop.Tests;

/// <summary>As telas dos módulos Escritório, ERP e Condomínio carregam (XAML com bindings compilados) e renderizam sem exceção.</summary>
public class NovosModulosViewsTests(SessaoHeadless headless)
{
    private static async Task<T> ForaDaSessao<T>(Task<T> tarefa)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = tarefa.ContinueWith(t => { if (t.IsFaulted) tcs.SetException(t.Exception!.InnerExceptions); else tcs.SetResult(t.Result); }, TaskScheduler.Default);
        return await tcs.Task;
    }

    internal static IEnumerable<(object Vm, UserControl View)> Telas()
    {
        var erp = new R.MainViewModel();
        yield return (new R.ViewModels.PainelViewModel(erp), new R.Views.PainelView());
        yield return (new R.ViewModels.PedidosVendaViewModel(erp), new R.Views.PedidosView());
        yield return (new R.ViewModels.PedidosCompraViewModel(erp), new R.Views.PedidosView());
        yield return (new R.ViewModels.EstoqueViewModel(erp), new R.Views.EstoqueView());
        yield return (new R.ViewModels.VeiculosViewModel(erp), new R.Views.VeiculosView());
        yield return (new R.ViewModels.TransporteViewModel(erp), new R.Views.TransporteView());
        // telas novas do lote: cadastros financeiros (casco), resumos e baixas (Fiscal) e depreciação (Contábil)
        var contabil = new Vorticis.Desktop.ViewModels.MainViewModel();
        var fiscal = new Vorticis.Fiscal.Desktop.MainViewModel();
        yield return (new Vorticis.Desktop.Comum.ViewModels.ContasBancariasViewModel(contabil), new Vorticis.Desktop.Comum.Views.ContasBancariasView());
        yield return (new Vorticis.Desktop.Comum.ViewModels.FormasPagamentoViewModel(fiscal), new Vorticis.Desktop.Comum.Views.FormasPagamentoView());
        yield return (new Vorticis.Fiscal.Desktop.ViewModels.ResumoVendasViewModel(fiscal), new Vorticis.Fiscal.Desktop.Views.ResumoNotasView());
        yield return (new Vorticis.Fiscal.Desktop.ViewModels.RelRecebimentosViewModel(fiscal), new Vorticis.Fiscal.Desktop.Views.RelatorioBaixasView());
        yield return (new Vorticis.Desktop.ViewModels.ValorDepreciadoViewModel(contabil), new Vorticis.Desktop.Views.RelatorioDepreciacaoView());
        yield return (new Vorticis.Fiscal.Desktop.ViewModels.BensDaNotaViewModel(fiscal), new Vorticis.Fiscal.Desktop.Views.BensDaNotaView());
        yield return (new R.ViewModels.MonitorNotasViewModel(erp), new R.Views.MonitorNotasView());
        yield return (new R.ViewModels.MonitorCobrancasViewModel(erp), new R.Views.MonitorCobrancasView());
    }

    [Fact]
    public async Task Telas_dos_novos_modulos_renderizam()
    {
        var ok = await ForaDaSessao(headless.Sessao.Dispatch(async () =>
        {
            foreach (var (vm, view) in Telas())
            {
                view.DataContext = vm;
                var janela = new Window { Content = view, Width = 1280, Height = 760 };
                janela.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.NotNull(janela.CaptureRenderedFrame());
                janela.Close();
            }
            await Task.CompletedTask;
            return true;
        }, TestContext.Current.CancellationToken));
        Assert.True(ok);
    }
}
