using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Cadastros;
using Vorticis.Application.Contabil;
using Vorticis.Application.Sistema;
using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Desktop.Comum.Views;
using Vorticis.Domain.Seguranca;
using Vorticis.Logistica.Desktop;

namespace Vorticis.Desktop.Tests;

/// <summary>
/// Vorticis Suite Logística: Cadastros › Parâmetros numa janela com abas (uma por módulo + Menus), preferências de menu (ocultar, bloquear,
/// itens essenciais) e a tela de Contabilização em lote.
/// </summary>
public class ParametrosMenusLoteTests(SessaoHeadless headless, Vorticis.Infra.Tests.BancoDeTeste db) : IClassFixture<Vorticis.Infra.Tests.BancoDeTeste>
{
    private static IEnumerable<ItemMenu> Folhas(IEnumerable<ItemMenu> itens) => itens.SelectMany(i => i.Filhos is null ? [i] : Folhas(i.Filhos));
    private static IEnumerable<ItemMenu> Todos(IEnumerable<ItemMenu> itens) => itens.SelectMany(i => (i.Filhos is null ? [] : Todos(i.Filhos)).Prepend(i));
    private static string Sem(ItemMenu m) => m.Titulo.Replace("_", "");

    private static async Task<T> ForaDaSessao<T>(Task<T> tarefa)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = tarefa.ContinueWith(t => { if (t.IsFaulted) tcs.SetException(t.Exception!.InnerExceptions); else tcs.SetResult(t.Result); }, TaskScheduler.Default);
        return await tcs.Task;
    }

    private static async Task Esperar(CancellationToken ct, int ciclos = 25)
    {
        for (var i = 0; i < ciclos; i++) { await Task.Delay(20, ct); Dispatcher.UIThread.RunJobs(); }
    }

    [Fact]
    public void Cadastros_tem_um_Parametros_com_abas_por_modulo_e_nao_tem_mais_o_submenu_por_modulo()
    {
        var vm = new MainViewModel();
        var cadastros = vm.Menus[0].Filhos!;
        Assert.DoesNotContain(cadastros, i => i.Titulo.Contains("Parâmetros dos"));
        Assert.Single(cadastros, i => i.Titulo == "Parâmetros" && i.Chave == TelasComuns.Parametros);
        var abas = Assert.IsType<ParametrosAbasViewModel>(vm.ViewModelDaTela(TelasComuns.Parametros));
        Assert.Equal(["Frota", "Gerencial e financeiro", "Contábil", "Menus"], abas.Abas.Select(a => a.Titulo));
        Assert.IsType<Vorticis.Frota.Desktop.ViewModels.ParametrosFrotaViewModel>(abas.Abas[0].Conteudo);
        Assert.IsType<Vorticis.Fiscal.Desktop.ViewModels.ParametrosFiscalViewModel>(abas.Abas[1].Conteudo);
        Assert.IsType<Vorticis.Desktop.ViewModels.ParametrosContabilViewModel>(abas.Abas[2].Conteudo);
        Assert.IsType<PreferenciasMenuViewModel>(abas.Abas[3].Conteudo);
        // cada módulo mantém a sua tela de parâmetros com chave própria (F1 e ManualTests)
        Assert.Equal(5, vm.ChavesDeTelas.Count(c => c == TelasComuns.Parametros || c.EndsWith(":" + TelasComuns.Parametros)));
        // Contabilização em lote: item do Exportar do Contábil, só com a empresa em lote
        var item = Folhas(vm.Menus).Single(i => i.Chave == TelasComuns.ContabilizacaoLote);
        Assert.Equal(MainViewModelBase.RecursoContabilizacaoLote, item.Recurso);
        Assert.True(vm.ConheceTela(TelasComuns.ContabilizacaoLote));
    }

    [Fact]
    public void Itens_ocultos_somem_do_menu_bloqueiam_a_tela_e_os_essenciais_nao_saem()
    {
        var vm = new MainViewModel();
        var tudo = vm.ArvoreDeMenus();
        Assert.Equal(vm.Menus.Count, tudo.Count);
        var relatorios = tudo.Single(n => n.Titulo == "Relatórios");
        Assert.False(relatorios.Essencial);
        Assert.True(tudo.Single(n => n.Titulo == "Ajuda").Essencial);
        Assert.True(tudo.Single(n => n.Titulo == "Cadastros").Essencial);               // contém o Parâmetros
        Assert.True(tudo.Single(n => n.Titulo == "Configurações").Essencial);           // contém o Sair
        Assert.True(tudo.Single(n => n.Titulo == "Cadastros").Filhos.Single(f => f.Titulo == "Parâmetros").Essencial);
        var chavesRelatorios = Todos(vm.Menus.Single(m => Sem(m) == "Relatórios").Filhos!).Where(i => i.Chave is not null).Select(i => i.Chave!).ToList();
        Assert.NotEmpty(chavesRelatorios);

        // oculta Relatórios inteiro + tenta ocultar itens essenciais (ignorados) + um item solto de Cadastros
        var parametros = "Cadastros › Parâmetros";
        vm.AplicarPreferenciasMenu(new PreferenciasMenu(new HashSet<string> { "Relatórios", parametros, "Ajuda", "Configurações › Sair (trocar de usuário)" }, new HashSet<string>()));
        Assert.DoesNotContain(vm.MenusVisiveis, m => Sem(m) == "Relatórios");
        Assert.Equal(vm.Menus.Count - 1, vm.MenusVisiveis.Count);
        Assert.Contains(vm.MenusVisiveis, m => Sem(m) == "Ajuda");
        Assert.Contains(Folhas(vm.MenusVisiveis), i => i.Chave == TelasComuns.Parametros);
        Assert.Contains(Folhas(vm.MenusVisiveis), i => i.Chave == TelasComuns.Sair);
        // telas só alcançáveis pelo menu escondido ficam bloqueadas (atalho e chamada direta usam a mesma regra); as que têm outro acesso, não
        var visiveis = Folhas(vm.MenusVisiveis).Where(i => i.Chave is not null).Select(i => i.Chave!).ToHashSet();
        var bloqueadas = chavesRelatorios.Where(c => !visiveis.Contains(c)).ToList();
        Assert.NotEmpty(bloqueadas);
        Assert.All(bloqueadas, c => Assert.True(vm.TelaBloqueadaPorMenu(c), c));
        Assert.All(chavesRelatorios.Where(visiveis.Contains), c => Assert.False(vm.TelaBloqueadaPorMenu(c), c));
        Assert.False(vm.TelaBloqueadaPorMenu(TelasComuns.Parametros));
        Assert.False(vm.TelaBloqueadaPorMenu(TelasComuns.Sair));
        Assert.False(vm.TelaBloqueadaPorMenu(TelasComuns.Manual));

        // submenu que ficou sem nenhum item visível some; voltar tudo restaura o menu
        var operacao = vm.Menus.Single(m => Sem(m) == "Operação");
        vm.AplicarPreferenciasMenu(new PreferenciasMenu(operacao.Filhos!.Where(f => !f.EhSeparador).Select(f => "Operação › " + Sem(f)).ToHashSet(), new HashSet<string>()));
        Assert.DoesNotContain(vm.MenusVisiveis, m => Sem(m) == "Operação");
        vm.AplicarPreferenciasMenu(PreferenciasMenu.Vazia);
        Assert.Same(vm.Menus, vm.MenusVisiveis);
        Assert.False(vm.TelaBloqueadaPorMenu(bloqueadas[0]));
    }

    [Fact]
    public async Task Cada_aba_grava_os_parametros_do_seu_modulo_e_a_aba_Menus_oculta_e_bloqueia_com_aviso()
    {
        Vorticis.Infra.Tests.BancoDeTeste.ExigirBanco();
        var adm = await db.AdminAsync();
        await db.App.Perfis.ConcluirConfiguracaoInicialAsync(new SessaoUsuario(adm, "admin_teste", "Admin", true, false), TestContext.Current.CancellationToken);
        var empresa = await db.CriarEmpresaAsync("27183946000359");
        await db.EscalarAsync<int>("UPDATE core.empresa SET modulo_frota = TRUE WHERE id = @e RETURNING 1", ("e", empresa));
        var outro = await db.EscalarAsync<long>("INSERT INTO core.usuario (login, nome, senha_hash, administrador) VALUES ('outro_menu', 'Outro', 'x', FALSE) RETURNING id");
        var ct = TestContext.Current.CancellationToken;
        var ok = await ForaDaSessao(headless.Sessao.Dispatch(async () =>
        {
            HospedeiroSuite.IncluirTelas(Avalonia.Application.Current!, ModuloLogistica.Apps());
            var vm = new MainViewModel();
            await vm.InicializarAsync(db.ConnectionString);
            var login = Assert.IsType<LoginViewModel>(vm.Conteudo);
            (login.Usuario, login.Senha) = ("admin_teste", "Senha1234");
            await login.EntrarCommand.ExecuteAsync(null);
            for (var i = 0; i < 200 && !vm.Logado; i++) { await Task.Delay(20, ct); Dispatcher.UIThread.RunJobs(); }
            Assert.True(vm.Logado);
            vm.EmpresaSelecionada = vm.EmpresasPermitidas.Single(e => e.Id == empresa);
            await Esperar(ct);

            try
            {
                // ---- Parâmetros: uma janela, várias abas
                vm.AbrirCommand.Execute(TelasComuns.Parametros);
                await Esperar(ct);
                var abas = Assert.IsType<ParametrosAbasViewModel>(vm.Conteudo);
                static CampoParametro Campo(ParametrosViewModel p, string chave) => p.Grupos.SelectMany(g => g.Campos).Single(c => c.Chave == chave);
                static Task Salvar(ParametrosViewModel p) => ((IAsyncRelayCommand)p.SalvarCommand).ExecuteAsync(null);
                var frota = (ParametrosViewModel)abas.Abas[0].Conteudo;
                var gerencial = (ParametrosViewModel)abas.Abas[1].Conteudo;
                var contabil = (ParametrosViewModel)abas.Abas[2].Conteudo;
                Assert.All(new[] { frota, gerencial, contabil }, p => Assert.True(p.PodeGravar));
                Assert.False(string.IsNullOrEmpty(Campo(frota, "dias").Texto));   // as abas já vieram carregadas

                Campo(frota, "dias").Texto = "21";
                await Salvar(frota);
                Assert.Empty(frota.Erros);
                Campo(gerencial, "contaFornecedores").Texto = "2.1.01.001";
                await Salvar(gerencial);
                Assert.Empty(gerencial.Erros);
                Assert.Equal("ONLINE", Campo(contabil, "tipoContabilizacao").ValorEscolha);   // padrão: on-line
                Campo(contabil, "tipoContabilizacao").DefinirEscolha("LOTE");
                Assert.True(abas.TemAlteracoesPendentes);
                await Salvar(contabil);
                Assert.Empty(contabil.Erros);
                Assert.False(abas.TemAlteracoesPendentes);

                Assert.Equal(21, (await Vorticis.Frota.ServicosFrota.De(db.App).ConfiguracaoAsync(empresa)).DiasAviso);
                Assert.Equal(ModoContabilizacao.Lote, (await db.App.Pendencias.ModoAsync(empresa, adm, ct)).Valor);
                await abas.CarregarAsync();                                         // recarrega do banco: o que foi gravado volta
                Assert.Equal(("21", "2.1.01.001", "LOTE"), (Campo(frota, "dias").Texto, Campo(gerencial, "contaFornecedores").Texto,
                                                             Campo(contabil, "tipoContabilizacao").ValorEscolha));
                Assert.Contains(MainViewModelBase.RecursoContabilizacaoLote, vm.RecursosAtivos);   // o item Exportar › Contabilização em lote aparece
                Assert.True(vm.AbrirCommand.CanExecute(TelasComuns.ContabilizacaoLote));

                // ---- aba Menus: árvore, essenciais e gravação por usuário
                var menus = Assert.IsType<PreferenciasMenuViewModel>(abas.Abas[3].Conteudo);
                Assert.NotEmpty(menus.Raiz);
                var ajuda = menus.Raiz.Single(n => n.Titulo == "Ajuda");
                Assert.True(ajuda.Essencial && !ajuda.Editavel && ajuda.Marcado);
                var relatorios = menus.Raiz.Single(n => n.Titulo == "Relatórios");
                relatorios.Marcado = false;                                          // desmarcar o menu leva os itens junto
                Assert.All(relatorios.Todos(), n => Assert.False(n.Marcado));
                ajuda.Marcado = false;                                               // essencial: volta/continua marcado ao gravar
                Assert.True(menus.Alterado && menus.PodeParaTodos);
                await ((IAsyncRelayCommand)menus.SalvarCommand).ExecuteAsync(null);
                Assert.Empty(menus.Erros);
                Assert.DoesNotContain(vm.MenusVisiveis, m => m.Titulo.Replace("_", "") == "Relatórios");
                Assert.Contains(vm.MenusVisiveis, m => m.Titulo.Replace("_", "") == "Ajuda");
                var prefs = await db.App.PreferenciasMenu.ObterAsync(vm.Modulo.NomeExecutavel, adm, ct);
                Assert.Contains("Relatórios", prefs.DoUsuario);
                Assert.DoesNotContain("Ajuda", prefs.DoUsuario);
                Assert.Empty((await db.App.PreferenciasMenu.ObterAsync(vm.Modulo.NomeExecutavel, outro, ct)).Ocultos);   // cada usuário tem as suas

                // tela escondida: botão/atalho/chamada direta ficam bloqueados com mensagem
                var chaveEscondida = vm.Menus.Single(m => m.Titulo.Replace("_", "") == "Relatórios").Filhos!
                    .SelectMany(f => f.Filhos ?? [f]).First(i => i.Chave is not null && vm.TelaBloqueadaPorMenu(i.Chave)).Chave!;
                Assert.False(vm.AbrirCommand.CanExecute(chaveEscondida));
                vm.Status = "";
                vm.AbrirPorAtalho(chaveEscondida);
                Assert.Contains("oculta", vm.Status);
                Assert.Contains("Cadastros › Parâmetros › Menus", vm.Status);
                vm.Status = "";
                vm.AbrirTela(chaveEscondida);
                Assert.Contains("oculta", vm.Status);
                Assert.NotEqual(chaveEscondida, vm.ViewModelDaTela(chaveEscondida)?.GetType().Name);

                // a preferência acompanha o usuário no próximo login; Sair limpa o menu para o usuário seguinte
                vm.SairCommand.Execute(null);
                Assert.Same(vm.Menus, vm.MenusVisiveis);
                (login.Usuario, login.Senha) = ("admin_teste", "Senha1234");
                await login.EntrarCommand.ExecuteAsync(null);
                for (var i = 0; i < 200 && !vm.Logado; i++) { await Task.Delay(20, ct); Dispatcher.UIThread.RunJobs(); }
                Assert.DoesNotContain(vm.MenusVisiveis, m => m.Titulo.Replace("_", "") == "Relatórios");

                // administrador aplica para todos; quem não é administrador não consegue
                var essenciais = new HashSet<string>();
                var negado = await db.App.PreferenciasMenu.GravarAsync(new SessaoUsuario(outro, "outro_menu", "Outro", false, false), vm.Modulo.NomeExecutavel, ["Emissão"], essenciais, true);
                Assert.Contains("administradores", negado.Erros[0]);
                Assert.True((await vm.SalvarPreferenciasMenuAsync(["Emissão"], true)).Sucesso);
                var deTodos = await db.App.PreferenciasMenu.ObterAsync(vm.Modulo.NomeExecutavel, outro, ct);
                Assert.Contains("Emissão", deTodos.DeTodos);
                Assert.Contains("Emissão", deTodos.Ocultos);
                Assert.Contains("Relatórios", vm.PreferenciasDeMenu.DoUsuario);       // a escolha pessoal do administrador continua
                Assert.DoesNotContain(vm.MenusVisiveis, m => m.Titulo.Replace("_", "") == "Emissão");
                return true;
            }
            finally
            {
                await db.EscalarAsync<long>("WITH d AS (DELETE FROM core.preferencia_menu RETURNING 1) SELECT count(*) FROM d");
            }
        }, ct));
        Assert.True(ok);
    }

    [Fact]
    public async Task Telas_novas_renderizam()
    {
        var ok = await ForaDaSessao(headless.Sessao.Dispatch(() =>
        {
            HospedeiroSuite.IncluirTelas(Avalonia.Application.Current!, ModuloLogistica.Apps());
            var vm = new MainViewModel();
            var abas = Assert.IsType<ParametrosAbasViewModel>(vm.ViewModelDaTela(TelasComuns.Parametros));
            var menus = (PreferenciasMenuViewModel)abas.Abas[^1].Conteudo;
            menus.Carregar();
            var janela = new Window { Width = 1100, Height = 800 };
            foreach (object t in new object[] { abas, menus, vm.ContabilizacaoLote })
            {
                var view = Avalonia.Application.Current!.DataTemplates.First(d => d.Match(t)).Build(t)!;
                view.DataContext = t;
                janela.Content = view;
                if (!janela.IsVisible) janela.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.NotNull(janela.CaptureRenderedFrame());
            }
            return Task.FromResult(true);
        }, TestContext.Current.CancellationToken));
        Assert.True(ok);
    }

    [Fact]
    public async Task Tela_de_lote_lista_por_origem_contabiliza_e_desfaz_pelo_ViewModel()
    {
        Vorticis.Infra.Tests.BancoDeTeste.ExigirBanco();
        var adm = await db.AdminAsync();
        await db.App.Perfis.ConcluirConfiguracaoInicialAsync(new SessaoUsuario(adm, "admin_teste", "Admin", true, false), TestContext.Current.CancellationToken);
        var empresa = await db.CriarEmpresaAsync("33000167000101");
        var ct = TestContext.Current.CancellationToken;
        // pendência fiscal sem regra: a tela mostra, tenta contabilizar e informa o motivo sem perder a pendência
        Assert.True((await db.App.Pendencias.RegistrarAsync(empresa, new NovaPendencia(OrigensPendencia.Fiscal, "NOTA", "999999", new DateOnly(2026, 3, 10), "NF 999 (saída) - Cliente", 100m), adm, ct)));
        var ok = await ForaDaSessao(headless.Sessao.Dispatch(async () =>
        {
            HospedeiroSuite.IncluirTelas(Avalonia.Application.Current!, ModuloLogistica.Apps());
            var vm = new MainViewModel();
            await vm.InicializarAsync(db.ConnectionString);
            var login = Assert.IsType<LoginViewModel>(vm.Conteudo);
            (login.Usuario, login.Senha) = ("admin_teste", "Senha1234");
            await login.EntrarCommand.ExecuteAsync(null);
            for (var i = 0; i < 200 && !vm.Logado; i++) { await Task.Delay(20, ct); Dispatcher.UIThread.RunJobs(); }
            vm.EmpresaSelecionada = vm.EmpresasPermitidas.Single(e => e.Id == empresa);
            await Esperar(ct);
            var lote = vm.ContabilizacaoLote;
            await lote.CarregarAsync();
            Assert.Contains("On-line", lote.ModoTexto);
            var linha = Assert.Single(lote.Pendencias);
            Assert.Equal("03/2026", linha.Competencia);
            Assert.Single(lote.Resumo);
            Assert.Contains("03/2026", lote.Resumo[0]);
            lote.OrigemSelecionada = ContabilizacaoLoteViewModel.Origens.Single(o => o.Origem == OrigensPendencia.Folha);
            await ((IAsyncRelayCommand)lote.ConsultarCommand).ExecuteAsync(null);
            Assert.Empty(lote.Pendencias);                                          // filtro por origem
            lote.OrigemSelecionada = ContabilizacaoLoteViewModel.Origens[0];
            await ((IAsyncRelayCommand)lote.ConsultarCommand).ExecuteAsync(null);
            lote.MarcarTodasCommand.Execute(null);
            await ((IAsyncRelayCommand)lote.VerPreviaCommand).ExecuteAsync(null);
            Assert.NotEmpty(lote.Previa);
            return true;
        }, ct));
        Assert.True(ok);
    }
}
