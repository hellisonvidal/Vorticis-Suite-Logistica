using Avalonia.Headless;
using Avalonia.Threading;
using Vorticis.Desktop.ViewModels;
using Vorticis.Desktop.Views;

namespace Vorticis.Desktop.Tests;

/// <summary>
/// Primeira execução sem nenhuma configuração: o programa cria e inicia o banco local sozinho e vai direto ao
/// primeiro acesso, sem pedir dados de conexão. Só no Linux (pastas do perfil redirecionadas por XDG_*) e fora do root.
/// </summary>
public class PrimeiraExecucaoTests(SessaoHeadless headless)
{
    private static string? Binarios() =>
        Directory.Exists("/usr/lib/postgresql")
            ? Directory.GetDirectories("/usr/lib/postgresql").OrderByDescending(d => d)
                .Select(d => Path.Combine(d, "bin")).FirstOrDefault(d => File.Exists(Path.Combine(d, "initdb")))
            : null;

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
    public async Task Abre_direto_no_primeiro_acesso_com_o_banco_local()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && Environment.UserName != "root" && Binarios() is not null,
            "Requer Linux, usuário comum e PostgreSQL em /usr/lib/postgresql.");
        Assert.SkipWhen(Environment.GetEnvironmentVariable("VORTICIS_DB") is not null, "VORTICIS_DB definida.");

        var perfil = Path.Combine(Path.GetTempPath(), $"vorticis-perfil-{Guid.NewGuid():N}");
        var anteriores = new[] { "XDG_CONFIG_HOME", "XDG_DATA_HOME", "VORTICIS_PG_EMBUTIDO_BIN" }
            .ToDictionary(v => v, Environment.GetEnvironmentVariable);
        // GetFolderPath devolve "" para pastas inexistentes
        Directory.CreateDirectory(Path.Combine(perfil, "config"));
        Directory.CreateDirectory(Path.Combine(perfil, "dados"));
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(perfil, "config"));
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(perfil, "dados"));
        Environment.SetEnvironmentVariable("VORTICIS_PG_EMBUTIDO_BIN", Binarios());
        var pastaFotos = Environment.GetEnvironmentVariable("VORTICIS_SCREENSHOTS");
        try
        {
            Assert.StartsWith(perfil, Configuracao.ArquivoConfiguracao);
            Assert.Equal(ModoBanco.Local, Configuracao.Carregar().Modo);

            var sessao = headless.Sessao;
            var ok = await ForaDaSessao(sessao.Dispatch(async () =>
            {
                // 1. Sem configuração: cria o banco local e abre o primeiro acesso
                var vm = new MainViewModel();
                await vm.IniciarAsync();
                var login = Assert.IsType<LoginViewModel>(vm.Conteudo);
                Assert.True(login.ImplantacaoAtiva);
                Assert.Contains("banco local do Vorticis", vm.Status);
                var cfg = Configuracao.Carregar();
                Assert.Equal(ModoBanco.Local, cfg.Modo);
                Assert.NotNull(cfg.Senha);
                Assert.True(File.Exists(Path.Combine(Configuracao.PastaBancoLocal, "PG_VERSION")));
                await vm.DisposeAsync();

                // 2. Configuração apontando para um servidor inexistente: tela de conexão oferece o banco local
                Configuracao.Salvar("Host=localhost;Port=1;Database=vorticis;Username=x;Password=y;Timeout=2");
                vm = new MainViewModel();
                await vm.IniciarAsync();
                var conexao = Assert.IsType<ConexaoViewModel>(vm.Conteudo);
                Assert.True(conexao.BancoLocalDisponivel);
                Assert.False(conexao.UsandoBancoLocal);
                var janela = new MainWindow { DataContext = vm, Width = 1280, Height = 780 };
                janela.Show();
                Dispatcher.UIThread.RunJobs();
                var quadro = janela.CaptureRenderedFrame();
                if (pastaFotos is not null) quadro?.Save(Path.Combine(pastaFotos, "00_conexao_banco_local.png"));

                // 3. "Usar o banco local": volta ao mesmo banco (mesma senha), sem recriar
                await conexao.UsarBancoLocalCommand.ExecuteAsync(null);
                Assert.IsType<LoginViewModel>(vm.Conteudo);
                Assert.Equal(cfg.Senha, Configuracao.Carregar().Senha);

                // 4. Configurações › Conexão: edita o banco local (troca a senha) e reconecta
                conexao.CarregarDe(vm.ConnectionString, null);
                Assert.True(conexao.UsandoBancoLocal);
                Assert.True(conexao.UsarBancoLocal);
                Assert.Equal(BancoDoAplicativo.PortaLocal, (int?)conexao.PortaLocal);
                (conexao.NovaSenhaLocal, conexao.ConfirmacaoSenhaLocal) = ("NovaSenhaLocal1", "NovaSenhaLocal1");
                await conexao.SalvarConfiguracaoCommand.ExecuteAsync(null);
                Assert.Empty(conexao.Erros);
                Assert.Equal("NovaSenhaLocal1", Configuracao.Carregar().Senha);
                Assert.IsType<LoginViewModel>(vm.Conteudo);                    // reconectou com a senha nova
                janela.Close();
                await vm.DisposeAsync();
                return true;
            }, TestContext.Current.CancellationToken));
            Assert.True(ok);
        }
        finally
        {
            BancoDoAplicativo.Reiniciar();
            foreach (var (v, valor) in anteriores) Environment.SetEnvironmentVariable(v, valor);
            try { Directory.Delete(perfil, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>Configuração automática real (initdb, start, migrações) sobre os binários de /usr/lib/postgresql.</summary>
    [Fact]
    public async Task Configuracao_automatica_cria_o_banco_e_reaproveita_na_segunda_vez()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && Environment.UserName != "root" && Binarios() is not null,
            "Requer Linux, usuário comum (initdb recusa root) e PostgreSQL em /usr/lib/postgresql.");
        Assert.SkipWhen(Environment.GetEnvironmentVariable("VORTICIS_DB") is not null, "VORTICIS_DB definida.");

        var perfil = Path.Combine(Path.GetTempPath(), $"vorticis-auto-{Guid.NewGuid():N}");
        var anteriores = new[] { "XDG_CONFIG_HOME", "XDG_DATA_HOME", "VORTICIS_PG_EMBUTIDO_BIN" }
            .ToDictionary(v => v, Environment.GetEnvironmentVariable);
        Directory.CreateDirectory(Path.Combine(perfil, "config"));
        Directory.CreateDirectory(Path.Combine(perfil, "dados"));
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(perfil, "config"));
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(perfil, "dados"));
        Environment.SetEnvironmentVariable("VORTICIS_PG_EMBUTIDO_BIN", Binarios());
        BancoDoAplicativo.Reiniciar();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var r = await new ConfiguradorBanco().ExecutarAsync(ct: ct);
            Assert.True(r.Sucesso, r.Resumo);
            Assert.True(File.Exists(Path.Combine(Configuracao.PastaBancoLocal, "PG_VERSION")));
            Assert.True(File.Exists(Configuracao.LogConfiguracaoBanco));
            Assert.NotNull(Configuracao.Carregar().Senha);

            // órfão deixado por queda + segunda execução: reaproveita o cluster e a senha
            BancoDoAplicativo.PararLocal();
            File.WriteAllText(Path.Combine(Configuracao.PastaBancoLocal, "postmaster.pid"), "2147483000\n");
            var senha = Configuracao.Carregar().Senha;
            r = await new ConfiguradorBanco().ExecutarAsync(ct: ct);
            Assert.True(r.Sucesso, r.Resumo);
            Assert.Equal(senha, Configuracao.Carregar().Senha);
        }
        finally
        {
            BancoDoAplicativo.Reiniciar();
            foreach (var (v, valor) in anteriores) Environment.SetEnvironmentVariable(v, valor);
            try { Directory.Delete(perfil, recursive: true); } catch (IOException) { }
        }
    }
}
