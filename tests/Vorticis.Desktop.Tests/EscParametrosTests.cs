using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Vorticis.Application.Cadastros;
using Vorticis.Desktop.Views;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra;
using Vorticis.Infra.Tests;
using Vorticis.Desktop.ViewModels;

namespace Vorticis.Desktop.Tests;

/// <summary>Tecla Esc fechando função e pop-up, e a tela Cadastros › Parâmetros de cada módulo (salvar e recarregar).</summary>
public class EscParametrosTests(BancoDeTeste db, SessaoHeadless headless) : IClassFixture<BancoDeTeste>
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

    private sealed class TelaComPendencia : IAlteracoesPendentes
    {
        public bool TemAlteracoesPendentes { get; set; }
    }

    private static void Esc(Window w)
    {
        w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public async Task Esc_fecha_a_funcao_do_topo_e_pergunta_antes_se_ha_alteracao_nao_salva()
    {
        var ok = await ForaDaSessao(headless.Sessao.Dispatch(async () =>
        {
            var vm = new MainViewModel();
            var janela = new MainWindow { DataContext = vm, Width = 1000, Height = 700 };
            janela.Show();
            var a = new TelaComPendencia();
            var b = new TelaComPendencia { TemAlteracoesPendentes = true };
            var fa = new FuncaoAberta(a, "a", "Cadastros › A");
            var fb = new FuncaoAberta(b, "b", "Cadastros › B");
            vm.JanelasAbertas.Add(fa);
            vm.JanelasAbertas.Add(fb);
            vm.AtivarFuncao(fb);
            Dispatcher.UIThread.RunJobs();
            var wb = Assert.IsType<Window>(janela.JanelaDa(b));

            // B tem digitação não salva: Esc pergunta; "Continuar editando" mantém a tela
            Esc(wb);
            Assert.NotNull(vm.Confirmacao);
            vm.ResponderConfirmacaoCommand.Execute(false);
            await Task.Delay(50);
            Assert.Contains(fb, vm.JanelasAbertas);

            // confirmando, fecha só a do topo (B); A continua
            Esc(wb);
            vm.ResponderConfirmacaoCommand.Execute(true);
            for (var i = 0; i < 100 && vm.JanelasAbertas.Contains(fb); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.DoesNotContain(fb, vm.JanelasAbertas);
            Assert.Contains(fa, vm.JanelasAbertas);

            // A não tem pendência: Esc fecha direto, sem perguntar
            vm.AtivarFuncao(fa);
            Esc(Assert.IsType<Window>(janela.JanelaDa(a)));
            for (var i = 0; i < 100 && vm.JanelasAbertas.Count > 0; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.Empty(vm.JanelasAbertas);
            Assert.Null(vm.Confirmacao);
            janela.Close();
            return true;
        }, TestContext.Current.CancellationToken));
        Assert.True(ok);
    }

    [Fact]
    public async Task Esc_dentro_do_popup_do_lancamento_fecha_so_o_popup()
    {
        var ok = await ForaDaSessao(headless.Sessao.Dispatch(async () =>
        {
            var vm = new LancamentoViewModel(new MainViewModel());
            var dono = new Window { Content = new LancamentoView { DataContext = vm }, Width = 1200, Height = 700 };
            dono.Show();
            vm.DigitacaoAberta = true;
            Dispatcher.UIThread.RunJobs();
            var popup = Assert.Single(dono.OwnedWindows);
            Esc(popup);
            Assert.False(vm.DigitacaoAberta);
            Assert.Empty(dono.OwnedWindows);
            Assert.True(dono.IsVisible);
            dono.Close();
            await Task.Yield();
            return true;
        }, TestContext.Current.CancellationToken));
        Assert.True(ok);
    }

    // ------------------------------------------------------------------ Cadastros › Parâmetros

    private sealed class ContextoDeTeste(VorticisApp app, long usuario, long empresa) : IContexto
    {
        public VorticisApp? App => app;
        public SessaoUsuario? Sessao { get; } = new(usuario, "admin", "Admin", true, false);
        public long? EmpresaId => empresa;
        public bool Pode(Permissao permissao) => true;
        public Task RecarregarEmpresasAsync(long? selecionar = null) => Task.CompletedTask;
        public void AoConcluirConfiguracaoInicial() { }
        public Task<bool> ConfirmarAsync(string titulo, string mensagem, string textoSim = "Sim", string textoNao = "Não") => Task.FromResult(true);
        public Task ReiniciarParaLoginAsync(string mensagem) => Task.CompletedTask;
        public Task<ModulosEmpresa?> EscolherModulosAsync(ModulosEmpresa inicial) => Task.FromResult<ModulosEmpresa?>(inicial);
        public ModulosEmpresa ModuloAtual => ModulosEmpresa.Todos;
        public Task<string?> SolicitarSenhaAsync(string titulo, string mensagem, string textoSim = "Confirmar", string textoNao = "Cancelar") => Task.FromResult<string?>(null);
        public string DescricaoEmpresa => "Empresa de teste";
        public List<string> Abertas { get; } = [];
        public void AbrirTela(string tela) => Abertas.Add(tela);
        public Task ConcluirImplantacaoAsync(string loginAdministrador) => Task.CompletedTask;
    }

    private static CampoParametro Campo(ParametrosViewModel vm, string chave) => vm.Grupos.SelectMany(g => g.Campos).First(c => c.Chave == chave);

    private async Task<(ContextoDeTeste Contexto, long Empresa)> NovoContextoAsync(string cnpj)
    {
        var admin = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync(cnpj);
        return (new ContextoDeTeste(db.App, admin, empresa), empresa);
    }

    [Fact]
    public async Task Parametros_do_contabil_gravam_e_recarregam()
    {
        BancoDeTeste.ExigirBanco();
        var (ctx, empresa) = await NovoContextoAsync("11222333000181");
        var vm = new ParametrosContabilViewModel(ctx);
        await vm.CarregarAsync();
        Assert.True(Campo(vm, "usaEstorno").Marcado);                 // padrão
        Assert.False(vm.Alterado);

        Campo(vm, "usaEstorno").Marcado = false;
        Campo(vm, "exigeDocumento").Marcado = true;
        Campo(vm, "usaCentroCusto").Marcado = true;
        Campo(vm, "usaGerencial").Marcado = true;
        Assert.True(vm.Alterado);
        Assert.True(vm.TemAlteracoesPendentes);
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.Empty(vm.Erros);
        Assert.False(vm.Alterado);

        var outro = new ParametrosContabilViewModel(ctx);
        await outro.CarregarAsync();
        Assert.False(Campo(outro, "usaEstorno").Marcado);
        Assert.True(Campo(outro, "exigeDocumento").Marcado);
        Assert.True(Campo(outro, "usaCentroCusto").Marcado);
        Assert.True(Campo(outro, "usaGerencial").Marcado);
        Assert.False(await db.App.Lancamentos.UsaEstornoAsync(empresa, ctx.Sessao!.Id, TestContext.Current.CancellationToken));   // mesmo armazenamento da configuração da empresa
        var cfg = await db.App.PlanoContas.ObterConfiguracaoAsync(empresa, ctx.Sessao.Id, TestContext.Current.CancellationToken);
        Assert.True(cfg.ExigeDocumento);
        Assert.True(cfg.UsaGerencial);

        // o bloqueio por data saiu dos parâmetros: fica em Cadastros › Bloqueio de lançamentos, gravando na mesma configuração
        var bloqueio = new BloqueioLancamentosViewModel(ctx);
        await bloqueio.CarregarAsync();
        bloqueio.Grupos[0].Campos[0].Data = new DateTime(2026, 1, 31);
        await bloqueio.SalvarCommand.ExecuteAsync(null);
        Assert.Empty(bloqueio.Erros);
        cfg = await db.App.PlanoContas.ObterConfiguracaoAsync(empresa, ctx.Sessao.Id, TestContext.Current.CancellationToken);
        Assert.Equal((new DateOnly(2026, 1, 31), true, true), (cfg.DataBloqueio!.Value, cfg.ExigeDocumento, cfg.UsaGerencial));
    }

    [Fact]
    public async Task Parametros_do_fiscal_gravam_e_recarregam()
    {
        BancoDeTeste.ExigirBanco();
        var (ctx, _) = await NovoContextoAsync("45723174000110");
        var vm = new Vorticis.Fiscal.Desktop.ViewModels.ParametrosFiscalViewModel(ctx);
        await vm.CarregarAsync();
        Assert.Equal("10", Campo(vm, "icmsDia").Texto);
        Campo(vm, "icmsPerfil").DefinirEscolha("B");
        Campo(vm, "icmsDia").Texto = "15";
        Campo(vm, "icmsReceita").Texto = "SP100";
        Campo(vm, "contribAtividade").DefinirEscolha("1");
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.Empty(vm.Erros);

        var outro = new Vorticis.Fiscal.Desktop.ViewModels.ParametrosFiscalViewModel(ctx);
        await outro.CarregarAsync();
        Assert.Equal("B", Campo(outro, "icmsPerfil").ValorEscolha);
        Assert.Equal("15", Campo(outro, "icmsDia").Texto);
        Assert.Equal("SP100", Campo(outro, "icmsReceita").Texto);
        Assert.Equal("1", Campo(outro, "contribAtividade").ValorEscolha);

        Campo(outro, "icmsDia").Texto = "31";
        await outro.SalvarCommand.ExecuteAsync(null);
        Assert.NotEmpty(outro.Erros);                                  // regra do serviço (1 a 28)
    }

    [Fact]
    public async Task Parametros_da_folha_gravam_e_recarregam()
    {
        BancoDeTeste.ExigirBanco();
        var (ctx, _) = await NovoContextoAsync("07526557000100");
        var vm = new Vorticis.Folha.Desktop.ViewModels.ParametrosModuloFolhaViewModel(ctx);
        await vm.CarregarAsync();
        Assert.Equal("5", Campo(vm, "diaPagamento").Texto);
        Assert.Equal("2", Campo(vm, "ambiente").ValorEscolha);          // produção restrita por padrão
        Campo(vm, "rat").DefinirEscolha("3");
        Campo(vm, "fap").Texto = "1,2000";
        Campo(vm, "diaPagamento").Texto = "10";
        Campo(vm, "ambiente").DefinirEscolha("1");
        Campo(vm, "transmissor").DefinirEscolha("CONTADOR");
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.Empty(vm.Erros);

        var outro = new Vorticis.Folha.Desktop.ViewModels.ParametrosModuloFolhaViewModel(ctx);
        await outro.CarregarAsync();
        Assert.Equal("3", Campo(outro, "rat").ValorEscolha);
        Assert.Equal("1,2000", Campo(outro, "fap").Texto);
        Assert.Equal("10", Campo(outro, "diaPagamento").Texto);
        Assert.Equal("1", Campo(outro, "ambiente").ValorEscolha);
        Assert.Equal("CONTADOR", Campo(outro, "transmissor").ValorEscolha);

        Campo(outro, "diaPagamento").Texto = "40";
        await outro.SalvarCommand.ExecuteAsync(null);
        Assert.NotEmpty(outro.Erros);
        // atalho abre a tela própria
        outro.Grupos.SelectMany(g => g.Atalhos).First(a => a.Texto.Contains("Contabilização")).Abrir.Execute(null);
        Assert.Contains("folha.contabilizacao", ctx.Abertas);
    }

    [Fact]
    public async Task Tela_de_parametros_renderiza_nos_tres_modulos()
    {
        var total = await ForaDaSessao(headless.Sessao.Dispatch(async () =>
        {
            var ctx = new ContextoDeTeste(null!, 1, 1);
            ParametrosViewModel[] vms =
            [
                new ParametrosContabilViewModel(ctx), new Vorticis.Fiscal.Desktop.ViewModels.ParametrosFiscalViewModel(ctx),
                new Vorticis.Folha.Desktop.ViewModels.ParametrosModuloFolhaViewModel(ctx),
            ];
            foreach (var vm in vms)
            {
                var janela = new Window { Content = new ParametrosView { DataContext = vm }, Width = 1000, Height = 800 };
                janela.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.NotNull(janela.CaptureRenderedFrame());
                Assert.All(vm.Grupos, g => Assert.False(string.IsNullOrWhiteSpace(g.Ajuda)));
                janela.Close();
            }
            await Task.Yield();
            return vms.Length;
        }, TestContext.Current.CancellationToken));
        Assert.Equal(3, total);
    }
}
