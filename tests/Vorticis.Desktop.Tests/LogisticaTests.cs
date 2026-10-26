using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Vorticis.Application.Cadastros;
using Vorticis.Desktop.Comum.Ajuda;
using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Domain.Seguranca;
using Vorticis.Logistica.Desktop;

namespace Vorticis.Desktop.Tests;

/// <summary>Vorticis Suite Logística: um programa só, com as telas de Frota, Gerencial, Contábil e Fiscal num menu único (sem a Folha).</summary>
public class LogisticaTests(SessaoHeadless headless, Vorticis.Infra.Tests.BancoDeTeste db) : IClassFixture<Vorticis.Infra.Tests.BancoDeTeste>
{
    private static readonly IReadOnlyList<string> TabelasTmsTitulos = Vorticis.Frota.TabelasTms.Titulos;
    private static IEnumerable<ItemMenu> Folhas(IEnumerable<ItemMenu> itens) => itens.SelectMany(i => i.Filhos is null ? [i] : Folhas(i.Filhos));

    private static async Task<T> ForaDaSessao<T>(Task<T> tarefa)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = tarefa.ContinueWith(t => { if (t.IsFaulted) tcs.SetException(t.Exception!.InnerExceptions); else tcs.SetResult(t.Result); }, TaskScheduler.Default);
        return await tcs.Task;
    }

    [Fact]
    public void Um_menu_so_com_frota_manutencao_emissao_financeiro_gerencial_e_relatorios()
    {
        var vm = new MainViewModel();
        Assert.Equal("Vorticis Suite Logística", vm.Modulo.Nome);
        Assert.Equal(["_Cadastros", "_Operações", "_Emissão", "_Financeiro", "_Contábil", "_Relatórios", "Con_figurações", "_Ajuda"],
                     vm.Menus.Select(m => m.Titulo));
        // toda função habilitada abre uma tela conhecida; o que ainda falta (emissão de CT-e, MDF-e e NFS-e) fica "em breve"
        var itens = Folhas(vm.Menus).Where(i => !i.EhSeparador).ToList();
        Assert.All(itens.Where(i => i.Habilitado && i.Chave is not null && i.Chave != TelasComuns.Sair), i => Assert.True(vm.ConheceTela(i.Chave!), $"{i.Titulo}: {i.Chave}"));
        // Emissão: as funções do antigo submenu Faturamento (pedidos de venda e notas de saída) direto no menu; o Financeiro não tem mais Faturamento
        var emissao = vm.Menus.Single(m => m.Titulo == "_Emissão");
        Assert.Equal(["Emissão de NF-e na SEFAZ (em breve)", "Transmissão de CT-e e MDF-e à SEFAZ (em breve)", "NFS-e – Nota fiscal de serviço (Padrão Nacional) (em breve)"],
                     Folhas(emissao.Filhos!).Where(i => !i.Habilitado && !i.EhSeparador).Select(i => i.Titulo));
        // TMS: cotações, CT-e, manifestos e faturas de frete no Emissão; entregas na Operação; tabelas de frete nos Cadastros; relatórios em Relatórios › Transporte (TMS)
        foreach (var chave in new[] { Frota.Desktop.ViewModels.FrotaTelas.Cotacoes, Frota.Desktop.ViewModels.FrotaTelas.Ctes, Frota.Desktop.ViewModels.FrotaTelas.Manifestos,
                                      Frota.Desktop.ViewModels.FrotaTelas.Faturas })
            Assert.Contains(emissao.Filhos!, i => i.Chave == chave && i.Habilitado);
        Assert.Contains(vm.Menus.Single(m => m.Titulo == "_Operações").Filhos!, i => i.Chave == Frota.Desktop.ViewModels.FrotaTelas.Entregas);
        Assert.Contains(Folhas(vm.Menus[0].Filhos!), i => i.Chave == Frota.Desktop.ViewModels.FrotaTelas.TabelasFrete);
        foreach (var chave in new[] { Erp.Desktop.ViewModels.ErpTelas.PedidosVenda, Fiscal.Desktop.ViewModels.Telas.NotaSaida })
        {
            Assert.Contains(emissao.Filhos!, i => i.Chave == chave && i.Habilitado);
            Assert.Single(Folhas(vm.Menus), i => i.Chave == chave);
        }
        Assert.DoesNotContain(Folhas(vm.Menus.Single(m => m.Titulo == "_Financeiro").Filhos!), i => i.Titulo.Replace("_", "") == "Faturamento");
        Assert.DoesNotContain(vm.Menus.Single(m => m.Titulo == "_Financeiro").Filhos!, i => i.Titulo.Replace("_", "") == "Faturamento");
        // Contábil: os menus da contabilidade direto (sem os relatórios, que vão para Relatórios); a Folha saiu do programa
        var contabil = vm.Menus.Single(m => m.Titulo == "_Contábil");
        Assert.DoesNotContain(contabil.Filhos!, m => m.Titulo.Replace("_", "") is "Contábil" or "Relatórios");
        Assert.Contains(Folhas(contabil.Filhos!), i => i.Chave == ViewModels.Telas.Lancamento);
        Assert.DoesNotContain(Folhas(contabil.Filhos!), i => i.Chave == ViewModels.Telas.Balancete);
        Assert.DoesNotContain(vm.ChavesDeTelas, c => c.StartsWith("folha.") || c.StartsWith("VorticisFolha:"));
        // Operações: a operação da frota e as compras, o estoque e os monitores do Gerencial (que saíram do Financeiro)
        var operacoes = vm.Menus.Single(m => m.Titulo == "_Operações");
        Assert.Equal(["Compras", "Manutenção", "Estoque", "Monitores"], operacoes.Filhos!.Where(i => i.Filhos is not null && i.Titulo is not ("_Coletas" or "_Pedágio")).Select(i => i.Titulo));
        Assert.Equal(["Contas a pagar", "Contas a receber", "Conciliação bancária"], vm.Menus.Single(m => m.Titulo == "_Financeiro").Filhos!.Where(i => !i.EhSeparador).Select(i => i.Titulo));
        // Manutenção sem Notas de compra; Cadastros sem Contadores (ficam na aba do cadastro da transportadora)
        Assert.DoesNotContain(Folhas(vm.Menus), i => i.Chave == Frota.Desktop.ViewModels.FrotaTelas.Notas);
        Assert.DoesNotContain(vm.Menus[0].Filhos!, i => i.Chave == TelasComuns.Contabilidade);
        Assert.Contains(vm.Menus[0].Filhos!, i => i.Titulo == "_Gerencial" && i.Filhos is not null);
        Assert.True(vm.ConheceTela(TelasComuns.Contabilidade));
        // Cadastros: cada grupo (a lista principal, Gerencial e Contábil) tem o seu "Importar de planilha (Excel)", só com os cadastros dele
        const string Importar = "Importar de _planilha (Excel)";
        string[] ImportaveisDe(IReadOnlyList<ItemMenu> grupo) => [.. grupo.Single(i => i.Titulo == Importar).Filhos!.Select(i => i.Titulo)];
        var cadastros = vm.Menus[0].Filhos!;
        Assert.Equal(["Clientes", "Fornecedores", "Veículos", "Motoristas", "Frotas", "Rotas", "Tabelas de frete"], ImportaveisDe(cadastros));
        Assert.Equal(["Produtos", "Naturezas de entrada"], ImportaveisDe(cadastros.Single(i => i.Titulo == "_Gerencial").Filhos!));
        Assert.Equal(["Centros de custo", "Históricos padrão"], ImportaveisDe(cadastros.Single(i => i.Titulo == "_Contábil").Filhos!));
        Assert.All(Folhas(cadastros).Where(i => i.Chave?.StartsWith("importar.") == true), i => Assert.True(vm.ConheceTela(i.Chave!), i.Chave));
        Assert.Equal(11, Folhas(cadastros).Count(i => i.Chave?.StartsWith("importar.") == true));
        // Operações tem Diárias e o submenu Coletas com as ordens de coleta
        var coletas = vm.Menus.Single(m => m.Titulo == "_Operações").Filhos!.Single(m => m.Titulo == "_Coletas");
        Assert.Equal(["Ordens de coleta"], coletas.Filhos!.Select(i => i.Titulo));
        Assert.True(coletas.Filhos![0].Habilitado);
        Assert.Equal(Frota.Desktop.ViewModels.FrotaTelas.Coletas, coletas.Filhos![0].Chave);
        Assert.Contains(vm.Menus.Single(m => m.Titulo == "_Operações").Filhos!, i => i.Titulo == "Diárias");
        // Relatórios por submenus; todos os relatórios saíram dos menus de origem
        var relatorios = vm.Menus.Single(m => m.Titulo == "_Relatórios");
        Assert.Equal(["_Frota e operação", "_Manutenção", "_Transporte (TMS)", "_Gerencial", "_Contábil", "-", "Grá_ficos"], relatorios.Filhos!.Select(m => m.Titulo));
        Assert.Equal(TabelasTmsTitulos, relatorios.Filhos!.Single(m => m.Titulo == "_Transporte (TMS)").Filhos!.Select(i => i.Titulo));
        Assert.DoesNotContain(vm.Menus, m => m.Titulo == "_Emissão" && m.Filhos!.Any(i => i.Titulo.Contains("Relat")));
        foreach (var chave in new[] { Frota.Desktop.ViewModels.FrotaTelas.Painel, Frota.Desktop.ViewModels.FrotaTelas.RelNotas, ViewModels.Telas.Balancete, Fiscal.Desktop.ViewModels.Telas.RelDuplicatasPagar })
            Assert.Single(Folhas(vm.Menus), i => i.Chave == chave);
        Assert.Contains(Folhas(relatorios.Filhos!), i => i.Chave == ViewModels.Telas.Balancete);
        // ausentes: Fiscal, Calendário de obrigações, Painel do empresário e relatórios fiscais
        Assert.DoesNotContain(vm.Menus, m => m.Titulo.Replace("_", "") is "Fiscal" or "Obrigações");
        Assert.DoesNotContain(itens, i => i.Chave is TelasComuns.Obrigacoes or Erp.Desktop.ViewModels.ErpTelas.Painel
            || i.Chave == Fiscal.Desktop.ViewModels.Telas.LivroEntradas || i.Chave == Fiscal.Desktop.ViewModels.Telas.Documentos || i.Chave == Fiscal.Desktop.ViewModels.Telas.RegrasContabilizacao);
        Assert.DoesNotContain(vm.Menus[0].Filhos!, i => i.Titulo == "_Fiscal");
        Assert.DoesNotContain(vm.Atalhos, a => a.Chave == Fiscal.Desktop.ViewModels.Telas.Documentos);
        Assert.False(vm.Modulo.UsaCalendarioObrigacoes);
        // funções de cada área no mesmo programa
        foreach (var chave in new[] { Frota.Desktop.ViewModels.FrotaTelas.Ordens, Frota.Desktop.ViewModels.FrotaTelas.Autorizacao, Fiscal.Desktop.ViewModels.Telas.ContasPagar,
                                      Fiscal.Desktop.ViewModels.Telas.ContasReceber, Fiscal.Desktop.ViewModels.Telas.RelDuplicatasReceber, ViewModels.Telas.Lancamento, ViewModels.Telas.Balancete, Fiscal.Desktop.ViewModels.Telas.ImportacaoXml })
            Assert.Contains(itens, i => i.Chave == chave);
        // Parâmetros é uma janela só, com abas (a de cada módulo mantém a sua tela com chave própria); não há mais o submenu por módulo
        Assert.DoesNotContain(vm.Menus[0].Filhos!, i => i.Titulo.Contains("Parâmetros dos"));
        Assert.Contains(vm.Menus[0].Filhos!, i => i.Titulo == "Parâmetros" && i.Chave == TelasComuns.Parametros);
        Assert.Equal(5, vm.ChavesDeTelas.Count(c => c == TelasComuns.Parametros || c.EndsWith(":" + TelasComuns.Parametros)));
        // atalhos sem tecla repetida, todos para telas conhecidas
        Assert.Equal(vm.Atalhos.Count, vm.Atalhos.Select(a => a.Gesto).Distinct().Count());
        Assert.All(vm.Atalhos.Where(a => a.Chave is not null), a => Assert.True(vm.ConheceTela(a.Chave!), a.Chave));
    }

    [Fact]
    public void Transportadora_e_cadastrada_em_todos_os_modulos_reunidos_e_o_manual_mostra_as_areas_deles()
    {
        var m = new ModuloLogistica();
        Assert.Equal(ModulosEmpresa.Frota, m.ModuloEmpresa);
        Assert.Equal(ModulosEmpresa.Frota | ModulosEmpresa.Contabil | ModulosEmpresa.Fiscal | ModulosEmpresa.Erp,
                     ((IModuloDesktop)m).ModulosAoCadastrar);
        Assert.Equal("Transportadora", ((IModuloDesktop)m).RotuloEmpresaAtiva);
        var areas = ((IModuloDesktop)m).AreasDoManual;
        Assert.Contains("Contábil", areas);
        Assert.DoesNotContain("Folha", areas);
        Assert.Contains(ManualUsuario.Visiveis(areas, false), a => a.Nome == "03-contabil.md");
        Assert.NotNull(ManualUsuario.TopicoDaTela("VorticisContabil:" + TelasComuns.Parametros));   // F1 nos parâmetros de cada módulo
    }

    [Fact]
    public async Task Telas_de_todas_as_areas_abrem_no_programa_unico()
    {
        var ok = await ForaDaSessao(headless.Sessao.Dispatch(() =>
        {
            HospedeiroSuite.IncluirTelas(Avalonia.Application.Current!, ModuloLogistica.Apps());
            var vm = new MainViewModel();
            var janela = new Window { Width = 1400, Height = 860 };
            object[] telas =
            [
                new Frota.Desktop.ViewModels.OrdensViewModel(vm), new Frota.Desktop.ViewModels.VeiculosViewModel(vm),
                new Fiscal.Desktop.ViewModels.ContasPagarViewModel(vm), new ViewModels.BalanceteViewModel(vm),
                vm.ImportacaoCadastros,
                new ViewModels.FiliaisViewModel(vm), new ViewModels.RelatorioGrupoViewModel(vm),
            ];
            foreach (var t in telas)
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
    public async Task Com_a_transportadora_escolhida_todas_as_telas_do_menu_abrem()
    {
        Vorticis.Infra.Tests.BancoDeTeste.ExigirBanco();
        var adm = await db.AdminAsync();
        await db.App.Perfis.ConcluirConfiguracaoInicialAsync(new SessaoUsuario(adm, "admin_teste", "Admin", true, false), TestContext.Current.CancellationToken);
        var empresa = await db.CriarEmpresaAsync("27183946000359");
        await db.EscalarAsync<int>("UPDATE core.empresa SET modulo_frota = TRUE WHERE id = @e RETURNING 1", ("e", empresa));
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
            for (var i = 0; i < 50; i++) { await Task.Delay(20, ct); Dispatcher.UIThread.RunJobs(); }
            var chaves = Folhas(vm.Menus).Where(i => i.Habilitado && i.Chave is not null && i.Chave != TelasComuns.Sair).Select(i => i.Chave!).Distinct().ToList();
            Assert.True(chaves.Count > 100, $"{chaves.Count} telas");
            foreach (var chave in chaves)
            {
                Assert.True(vm.AbrirCommand.CanExecute(chave), chave);
                vm.AbrirCommand.Execute(chave);
                for (var i = 0; i < 5; i++) { await Task.Delay(10, ct); Dispatcher.UIThread.RunJobs(); }
                Assert.NotNull(vm.Conteudo);
                Assert.True(vm.Confirmacao is null, $"{chave}: {vm.Status}");     // nenhum erro inesperado ao abrir
            }
            return true;
        }, ct));
        Assert.True(ok);
    }

    [Fact]
    public void Instalador_proprio_com_um_programa_so()
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "instalador", "VorticisLogistica.iss"))) raiz = raiz.Parent;
        var iss = File.ReadAllText(Path.Combine(raiz!.FullName, "instalador", "VorticisLogistica.iss"));
        Assert.Contains("AppName=Vorticis Suite Logística", iss);
        Assert.Contains("OutputBaseFilename=Setup-VorticisSuiteLogistica-{#Versao}", iss);
        Assert.Contains(@"Source: ""..\publish\VorticisLogistica.exe""; DestDir: ""{app}""; Flags: ignoreversion", iss);
        Assert.Contains(@"Filename: ""{app}\VorticisLogistica.exe""; Description: ""{cm:LaunchProgram,Vorticis Suite Logística}""", iss);
        Assert.DoesNotContain("[Components]", iss);                         // nada para escolher: é um programa só
        Assert.DoesNotContain("VorticisFrota.exe", iss);
        Assert.Contains(@"Name: ""{autoprograms}\Vorticis Logística.lnk""", iss);   // atalho antigo removido ao atualizar
        Assert.Contains("AppId={{8C1F4A6D", iss);                           // produto próprio, não substitui o Vorticis
        foreach (var ico in new[] { "VorticisLogistica.ico", "VorticisLogisticaInstalador.ico" })
            Assert.True(File.Exists(Path.Combine(raiz.FullName, "instalador", "icone", ico)), ico);
        var ci = File.ReadAllText(Path.Combine(raiz.FullName, ".github", "workflows", "ci.yml"));
        Assert.Contains("dotnet publish src/Vorticis.Logistica.Desktop", ci);
        Assert.Contains("instalador/VorticisLogistica.iss", ci);
        Assert.Contains("-Programa VorticisLogistica.exe", ci);
        Assert.Contains("VorticisSuiteLogistica-portatil", ci);
    }
}
