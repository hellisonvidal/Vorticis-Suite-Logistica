using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vorticis.Application.Cadastros;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Desktop.Comum.Views;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra;
using F = Vorticis.Frota.Desktop;

namespace Vorticis.Desktop.Tests;

/// <summary>Cadastros em pop-up: a tela mostra só a lista; o formulário abre numa janela por cima (Novo, Abrir, duplo clique, Incluir e Alterar).</summary>
public class CadastrosPopupTests(SessaoHeadless headless)
{
    private static async Task<T> ForaDaSessao<T>(Task<T> tarefa)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = tarefa.ContinueWith(t => { if (t.IsFaulted) tcs.SetException(t.Exception!.InnerExceptions); else tcs.SetResult(t.Result); }, TaskScheduler.Default);
        return await tcs.Task;
    }

    private sealed class Contexto : IContexto
    {
        public VorticisApp? App => null;
        public SessaoUsuario? Sessao { get; } = new(1, "admin", "Admin", true, false);
        public long? EmpresaId => 1;
        public bool Pode(Permissao permissao) => true;
        public Task RecarregarEmpresasAsync(long? selecionar = null) => Task.CompletedTask;
        public void AoConcluirConfiguracaoInicial() { }
        public Task<bool> ConfirmarAsync(string titulo, string mensagem, string textoSim = "Sim", string textoNao = "Não") => Task.FromResult(true);
        public Task ReiniciarParaLoginAsync(string mensagem) => Task.CompletedTask;
        public Task<ModulosEmpresa?> EscolherModulosAsync(ModulosEmpresa inicial) => Task.FromResult<ModulosEmpresa?>(inicial);
        public ModulosEmpresa ModuloAtual => ModulosEmpresa.Frota;
        public Task<string?> SolicitarSenhaAsync(string titulo, string mensagem, string textoSim = "Confirmar", string textoNao = "Cancelar") => Task.FromResult<string?>(null);
        public string DescricaoEmpresa => "Transportadora de teste";
        public void AbrirTela(string tela) { }
        public Task ConcluirImplantacaoAsync(string loginAdministrador) => Task.CompletedTask;
    }

    [Fact]
    public async Task Cadastro_mostra_a_lista_e_abre_o_formulario_em_popup() => Assert.True(await ForaDaSessao(headless.Sessao.Dispatch(() =>
    {
        // cadastro da frota: Novo e Abrir na barra sobre a lista; o formulário sai da tela e volta a ela ao fechar o pop-up
        var vm = new F.ViewModels.VeiculosViewModel(new Contexto());
        var view = new F.Views.VeiculosView { DataContext = vm };
        var janela = new Window { Content = view, Width = 1200, Height = 760 };
        janela.Show();
        Dispatcher.UIThread.RunJobs();
        var formulario = view.GetVisualDescendants().OfType<Control>().Single(c => FormularioPopup.GetTitulo(c) == "Veículo");
        Assert.False(formulario.IsVisible);
        var lista = view.GetVisualDescendants().OfType<DataGrid>().Single();
        Assert.True(lista.Bounds.Width > 1000);                                  // a lista ocupa a largura toda
        var botoes = view.GetVisualDescendants().OfType<Button>().Where(b => b.IsVisible).Select(b => b.Content as string).ToList();
        Assert.Contains("Novo", botoes);
        Assert.Contains("Abrir", botoes);
        view.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Novo").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var popup = FormularioPopup.JanelaDe(view);
        Assert.NotNull(popup);
        Assert.Equal("Veículo", popup.Title);
        Assert.True(formulario.IsVisible);
        Assert.Same(popup, TopLevel.GetTopLevel(formulario));
        if (Environment.GetEnvironmentVariable("VORTICIS_SCREENSHOTS") is { Length: > 0 } pasta)
        {
            Directory.CreateDirectory(pasta);
            janela.CaptureRenderedFrame()!.Save(Path.Combine(pasta, "cadastro-lista.png"));
            popup.CaptureRenderedFrame()!.Save(Path.Combine(pasta, "cadastro-popup.png"));
        }
        popup.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(FormularioPopup.JanelaDe(view));
        Assert.False(formulario.IsVisible);
        Assert.Same(janela, TopLevel.GetTopLevel(formulario));                  // o formulário voltou para a tela, escondido
        janela.Close();

        // cadastro com a barra padrão: Incluir abre o pop-up; Cancelar (ou o X) desiste e fecha
        var contas = new ContasBancariasViewModel(new Contexto());
        var telaContas = new ContasBancariasView { DataContext = contas };
        var j2 = new Window { Content = telaContas, Width = 1200, Height = 760 };
        j2.Show();
        Dispatcher.UIThread.RunJobs();
        contas.IncluirCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var p2 = FormularioPopup.JanelaDe(telaContas);
        Assert.NotNull(p2);
        Assert.Equal("Conta bancária", p2.Title);
        p2.Close();                                                              // o X cancela a inclusão
        Dispatcher.UIThread.RunJobs();
        Assert.True(contas.EmConsulta);
        Assert.Null(FormularioPopup.JanelaDe(telaContas));
        contas.IncluirCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(FormularioPopup.JanelaDe(telaContas));
        contas.CancelarCommand.Execute(null);                                    // Cancelar fecha o pop-up
        Dispatcher.UIThread.RunJobs();
        Assert.Null(FormularioPopup.JanelaDe(telaContas));
        j2.Close();
        return Task.FromResult(true);
    }, TestContext.Current.CancellationToken)));
}
