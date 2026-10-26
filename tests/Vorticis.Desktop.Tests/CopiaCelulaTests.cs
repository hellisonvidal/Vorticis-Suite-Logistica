using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Vorticis.Desktop.Views;

namespace Vorticis.Desktop.Tests;

public class CopiaCelulaTests(SessaoHeadless headless)
{
    private sealed record Item(string Codigo, string Descricao, string Valor);

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
    public async Task Ctrl_C_copia_so_a_celula_atual_e_nao_a_linha()
    {
        var concluido = await ForaDaSessao(headless.Sessao.Dispatch(async () =>
        {
            var itens = new[] { new Item("1.1", "Caixa", "10,00"), new Item("1.2", "Bancos", "20,00") };
            var grade = new DataGrid { ItemsSource = itens, AutoGenerateColumns = false, IsReadOnly = true };
            grade.Columns.Add(new DataGridTextColumn { Header = "Código", Binding = new Binding("Codigo") });
            grade.Columns.Add(new DataGridTextColumn { Header = "Descrição", Binding = new Binding("Descricao") });
            grade.Columns.Add(new DataGridTextColumn { Header = "Valor", Binding = new Binding("Valor") });
            var janela = new Window { Content = grade, Width = 500, Height = 300 };
            janela.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.True(CopiaCelula.GetAtiva(grade));                       // ligado pelo estilo global de App.axaml
            grade.SelectedItem = itens[1];
            grade.CurrentColumn = grade.Columns[1];
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Bancos", CopiaCelula.TextoDaCelulaAtual(grade));
            grade.CurrentColumn = grade.Columns[2];
            Assert.Equal("20,00", CopiaCelula.TextoDaCelulaAtual(grade));

            grade.Focus();
            janela.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, "c");
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            using var dados = await janela.Clipboard!.TryGetDataAsync();
            Assert.Equal("20,00", await dados!.TryGetTextAsync());           // só o valor da célula
            return true;
        }, TestContext.Current.CancellationToken));
        Assert.True(concluido);
    }

    private sealed class ItemEditavel { public string Descricao { get; set; } = "Caixa"; public string Valor { get; set; } = "10,00"; }

    [Fact]
    public async Task Ctrl_V_cola_na_celula_atual_quando_editavel_e_ignora_somente_leitura()
    {
        var concluido = await ForaDaSessao(headless.Sessao.Dispatch(async () =>
        {
            var item = new ItemEditavel();
            var grade = new DataGrid { ItemsSource = new[] { item }, AutoGenerateColumns = false };
            grade.Columns.Add(new DataGridTextColumn { Header = "Descrição", Binding = new Binding("Descricao"), IsReadOnly = true });
            grade.Columns.Add(new DataGridTextColumn { Header = "Valor", Binding = new Binding("Valor") });
            var janela = new Window { Content = grade, Width = 500, Height = 300 };
            janela.Show();
            Dispatcher.UIThread.RunJobs();
            await janela.Clipboard!.SetTextAsync("99,00");
            grade.SelectedItem = item;
            grade.Focus();
            grade.CurrentColumn = grade.Columns[0];                          // coluna somente leitura: não cola
            janela.KeyPress(Key.V, RawInputModifiers.Control, PhysicalKey.V, "v");
            await Task.Delay(100);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Caixa", item.Descricao);
            grade.CurrentColumn = grade.Columns[1];
            janela.KeyPress(Key.V, RawInputModifiers.Control, PhysicalKey.V, "v");
            await Task.Delay(100);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("99,00", item.Valor);
            return true;
        }, TestContext.Current.CancellationToken));
        Assert.True(concluido);
    }
}
