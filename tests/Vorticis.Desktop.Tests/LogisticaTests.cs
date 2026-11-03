using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Vorticis.Application.Cadastros;
using Vorticis.Desktop.Comum.Ajuda;
using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Domain.Seguranca;
using Vorticis.Logistica.Desktop;
using FT = Vorticis.Frota.Desktop.ViewModels.FrotaTelas;

namespace Vorticis.Desktop.Tests;

/// <summary>Vorticis Suite Logística: sete módulos na mesma janela (Frotas, Transporte, Faturamento, Compras, Financeiro, Gestão e Contabilidade), sem a Folha.</summary>
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

    private static MainViewModel Modulo<T>() where T : ModuloLogistica, new() => new(new T());
    private static string Sem(ItemMenu m) => m.Titulo.Replace("_", "");
    private static IReadOnlyList<string> Chaves(MainViewModelBase vm) => [.. Folhas(vm.Menus).Where(i => i.Chave is not null).Select(i => i.Chave!)];

    [Fact]
    public void Sete_modulos_com_as_funcoes_reorganizadas()
    {
        var todos = ModuloLogistica.Todos().Select(m => new MainViewModel(m)).ToList();
        Assert.Equal(["Frotas", "Transporte", "Faturamento", "Compras", "Financeiro", "Gestão", "Contabilidade"], todos.Select(m => m.Modulo.NomeCurto));
        foreach (var vm in todos)
        {
            // menus do casco em volta dos do módulo; toda função habilitada abre uma tela conhecida; Folha fora
            Assert.Equal("_Cadastros", vm.Menus[0].Titulo);
            Assert.Equal(["Con_figurações", "_Ajuda"], vm.Menus.TakeLast(2).Select(m => m.Titulo));
            Assert.Equal("_Relatórios", vm.Menus[^3].Titulo);
            var itens = Folhas(vm.Menus).Where(i => !i.EhSeparador).ToList();
            Assert.All(itens.Where(i => i.Habilitado && i.Chave is not null && i.Chave != TelasComuns.Sair), i => Assert.True(vm.ConheceTela(i.Chave!), $"{vm.Modulo.NomeCurto}: {i.Titulo}"));
            Assert.DoesNotContain(vm.ChavesDeTelas, c => c.StartsWith("folha.") || c.StartsWith("VorticisFolha:"));
            Assert.False(vm.Modulo.UsaCalendarioObrigacoes);
            Assert.DoesNotContain(vm.Menus[0].Filhos!, i => i.Chave == TelasComuns.Contabilidade);   // contadores só no cadastro da transportadora
            Assert.Equal(vm.Atalhos.Count, vm.Atalhos.Select(a => a.Gesto).Distinct().Count());
            Assert.All(vm.Atalhos.Where(a => a.Chave is not null), a => Assert.Contains(a.Chave!, Chaves(vm).Append(TelasComuns.ManualContextual)));
        }
        static string[] Menus(MainViewModel vm) => [.. vm.Menus.Skip(1).SkipLast(2).Select(Sem)];
        Assert.Equal(["Operação", "Manutenção", "Monitores", "Contabilidade", "Relatórios"], Menus(todos[0]));
        Assert.Equal(["Operação", "Contabilidade", "Relatórios"], Menus(todos[1]));
        Assert.Equal(["Faturamento", "Contabilidade", "Relatórios"], Menus(todos[2]));
        Assert.Equal(["Compras", "Financeiro", "Contabilidade", "Relatórios"], Menus(todos[3]));
        Assert.Equal(["Contas", "Imobilizado", "Monitores", "Contabilidade", "Relatórios"], Menus(todos[4]));
        Assert.Equal(["Painel", "Monitores", "Relatórios"], Menus(todos[5]));
        Assert.Contains("Lançamentos", Menus(todos[6]));
        // a Manutenção virou menu do Frotas, com as ordens, a autorização e os cadastros de planos e de peças
        Assert.Equal(["Ordens de serviço", "Autorização de serviços", "-", "Planos de manutenção", "Peças e serviços"],
                     todos[0].Menus.Single(m => Sem(m) == "Manutenção").Filhos!.Select(i => i.Titulo));
        Assert.Equal(["Monitor de multas de trânsito"], todos[0].Menus.Single(m => Sem(m) == "Monitores").Filhos!.Select(f => f.Titulo));
        Assert.NotNull(todos[0].ImportacaoDaTela(FT.Multas));                // as multas também vêm de planilha
        // o módulo Faturamento tem todo o faturamento (o antigo menu Faturamento do Transporte)
        Assert.Equal(["Conhecimentos de transporte (CT-e)", "Manifestos de carga (MDF-e)", "-", "Faturas de frete", "-",
                      "Pedidos de venda", "Notas fiscais de saída", "NFS-e – Notas fiscais de serviço", "-",
                      "Transmissão de CT-e e MDF-e à SEFAZ (em breve)", "Emissão de NF-e na SEFAZ (em breve)", "Transmissão da NFS-e ao Emissor Nacional (em breve)"],
                     todos[2].Menus.Single(m => Sem(m) == "Faturamento").Filhos!.Select(i => i.Titulo));
        // Compras: compras, estoque e o financeiro das compras (contas a pagar)
        Assert.Equal(["Pedidos de compra", "Notas fiscais de entrada", "Importação de XML", "Notas de compra da manutenção", "-", "_Estoque"],
                     todos[3].Menus.Single(m => Sem(m) == "Compras").Filhos!.Select(i => i.Titulo));
        Assert.Equal(["Contas a pagar"], todos[3].Menus.Single(m => Sem(m) == "Financeiro").Filhos!.Select(i => i.Titulo));
        Assert.Equal(["Contas a receber", "-", "Consórcios", "Financiamentos", "-", "Conciliação bancária"],
                     todos[4].Menus.Single(m => m.Titulo == "_Contas").Filhos!.Select(f => f.Titulo));
        Assert.Equal(["Bens financiados e consorciados", "-", "Cadastro de bens", "Cálculo e lançamento da depreciação"],
                     todos[4].Menus.Single(m => m.Titulo == "_Imobilizado").Filhos!.Select(f => f.Titulo));
        // todos os módulos (menos a própria Contabilidade e a Gestão, só de consulta) se ligam à Contabilidade
        Assert.All(todos.Take(5), vm => Assert.Contains(Folhas(vm.Menus.Single(m => Sem(m) == "Contabilidade").Filhos!), i => i.Chave == ViewModels.Telas.PlanoContas));
        Assert.All(todos.Take(5), vm => Assert.Contains(Folhas(vm.Menus).Where(i => i.Recurso == MainViewModelBase.RecursoContabilizacaoLote), i => i.Chave == TelasComuns.ContabilizacaoLote));
        Assert.Contains(Folhas(todos[2].Menus), i => i.Chave == Fiscal.Desktop.ViewModels.Telas.RegrasContabilizacao);
        // BI no Financeiro e, por categoria, na Gestão (com os relatórios de todos os módulos)
        var biFinanceiro = todos[4].Menus[^3].Filhos!.Single(m => Sem(m) == "BI – indicadores").Filhos!;
        Assert.Equal(Fiscal.Desktop.GraficosFiscais.Bi.Select(g => g.Titulo), biFinanceiro.Select(i => i.Titulo));
        Assert.Equal(["Frotas", "Manutenção", "Transporte", "Faturamento", "Compras", "Financeiro", "Contabilidade"],
                     todos[5].Menus[^3].Filhos!.TakeWhile(i => !i.EhSeparador).Select(Sem));
        Assert.Contains(todos[5].Menus[^3].Filhos!, i => Sem(i) == "BI – indicadores");
        Assert.Contains(todos[5].Menus[^3].Filhos!, i => i.Titulo == "Gráficos – Frota");
        Assert.Contains(Folhas(todos[5].Menus), i => i.Chave == ViewModels.Telas.Dre);

        // cada função de trabalho num módulo só (a Gestão repete os relatórios; o menu Contabilidade e os cadastros fiscais se repetem de propósito)
        void Em(int modulo, params string[] chaves) => Assert.All(chaves, c => Assert.Contains(c, Chaves(todos[modulo])));
        Em(0, FT.Abastecimentos, FT.Viagens, FT.Diarias, FT.Pedagio, FT.Veiculos, FT.Motoristas, FT.Painel, FT.Planos, FT.Itens, FT.Ordens, FT.Autorizacao,
           FT.RelHistoricoManutencao, FT.Multas);
        Em(1, FT.Cotacoes, FT.Coletas, FT.Entregas, FT.TabelasFrete, FT.Rotas, FT.Ciot, FT.Roteirizador);
        Em(2, FT.Ctes, FT.Manifestos, FT.Faturas, Erp.Desktop.ViewModels.ErpTelas.PedidosVenda, Fiscal.Desktop.ViewModels.Telas.NotaSaida, Fiscal.Desktop.ViewModels.Telas.Nfse);
        Em(3, Erp.Desktop.ViewModels.ErpTelas.PedidosCompra, Fiscal.Desktop.ViewModels.Telas.NotaEntrada, Erp.Desktop.ViewModels.ErpTelas.Estoque,
           Fiscal.Desktop.ViewModels.Telas.ContasPagar, Fiscal.Desktop.ViewModels.Telas.Produtos, Fiscal.Desktop.ViewModels.Telas.RegrasFiscais, FT.Notas);
        Em(4, Fiscal.Desktop.ViewModels.Telas.ContasReceber, Fiscal.Desktop.ViewModels.Telas.ConciliacaoDuplicatas,
           TelasComuns.ContasBancarias, FT.Financiamentos, FT.Consorcios, FT.BensContratos, ViewModels.Telas.Bens);
        Em(5, Erp.Desktop.ViewModels.ErpTelas.Painel, Erp.Desktop.ViewModels.ErpTelas.MonitorNotas);
        Em(6, ViewModels.Telas.Lancamento, ViewModels.Telas.Balancete, ViewModels.Telas.Filiais, ViewModels.Telas.RelatorioGrupo);
        Assert.DoesNotContain(FT.Rotas, Chaves(todos[0]));
        Assert.DoesNotContain(Erp.Desktop.ViewModels.ErpTelas.Estoque, Chaves(todos[5]));
        Assert.DoesNotContain(Erp.Desktop.ViewModels.ErpTelas.PedidosCompra, Chaves(todos[5]));
        static IEnumerable<ItemMenu> Trabalho(MainViewModel vm) => Folhas([.. vm.Modulo.ItensCadastros, .. vm.Modulo.Menus.Where(m => Sem(m) is not ("Contabilidade" or "Relatórios"))]);
        var doModulo = todos.Where(vm => vm.Modulo.NomeCurto is not ("Gestão" or "Contabilidade"))
                            .SelectMany(vm => Trabalho(vm).Where(x => x.Chave is { } c && c is not (TelasComuns.FormasPagamento or Fiscal.Desktop.ViewModels.Telas.Produtos
                                                                                                    or Fiscal.Desktop.ViewModels.Telas.RegrasFiscais))
                                                          .Select(x => (Modulo: vm.Modulo.NomeCurto, Chave: x.Chave!)).Distinct()).ToList();
        Assert.Empty(doModulo.GroupBy(x => x.Chave).Where(g => g.Count() > 1).Select(g => $"{g.Key}: {string.Join(", ", g.Select(x => x.Modulo))}"));
        // a transmissão do CT-e/MDF-e e da NFS-e continuam "em breve"; a NFS-e já tem a sua lista (as notas de serviço, lançadas ou importadas)
        var nfse = new Fiscal.Desktop.ViewModels.NfseViewModel(todos[2]);
        Assert.Equal((Vorticis.Fiscal.Documentos.ModeloDocumento.NFSe, false, "NFS-e – Notas fiscais de serviço"), (nfse.ModeloFixo, nfse.EscolheModelo, nfse.Titulo));
        // importação de planilha dentro de cada cadastro (botão na janela do cadastro); não há mais o submenu em Cadastros
        Assert.All(todos, vm => Assert.DoesNotContain(vm.Menus[0].Filhos!, i => i.Titulo.Contains("planilha")));
        string[] Importaveis(MainViewModel vm) => [.. Folhas(vm.Menus[0].Filhos!).Where(i => vm.ImportacaoDaTela(i.Chave) is not null).Select(i => i.Titulo)];
        Assert.Equal(["Clientes", "Fornecedores", "Veículos", "Motoristas", "Frotas"], Importaveis(todos[0]));
        Assert.Equal(["Clientes", "Fornecedores", "Tabelas de frete", "Rotas"], Importaveis(todos[1]));
        Assert.Equal(["Clientes", "Fornecedores", "Produtos e serviços", "Regras fiscais"], Importaveis(todos[3]));
        Assert.Contains("Centros de custo", Importaveis(todos[6]));
        Assert.All(todos.SelectMany(vm => Importaveis(vm).Select(t => (vm, t))), x => Assert.True(x.vm.ConheceTela(x.vm.ImportacaoDaTela(
            Folhas(x.vm.Menus[0].Filhos!).First(i => i.Titulo == x.t).Chave)!.Tela)));
    }

    [Fact]
    public void Estrutura_dos_menus_sem_separadores_sobrando_submenus_vazios_ou_itens_repetidos()
    {
        foreach (var vm in ModuloLogistica.Todos().Select(m => new MainViewModel(m)))
        {
            void Conferir(IReadOnlyList<ItemMenu> itens, string onde)
            {
                Assert.True(itens.Count > 0, $"{vm.Modulo.NomeCurto}: submenu vazio em {onde}");
                Assert.False(itens[0].EhSeparador, $"{vm.Modulo.NomeCurto}: separador no começo de {onde}");
                Assert.False(itens[^1].EhSeparador, $"{vm.Modulo.NomeCurto}: separador no fim de {onde}");
                for (var i = 1; i < itens.Count; i++)
                    Assert.False(itens[i].EhSeparador && itens[i - 1].EhSeparador, $"{vm.Modulo.NomeCurto}: dois separadores seguidos em {onde}");
                var chaves = itens.Where(i => i.Chave is not null).Select(i => i.Chave!).ToList();
                Assert.True(chaves.Count == chaves.Distinct().Count(), $"{vm.Modulo.NomeCurto}: item repetido em {onde}");
                Assert.True(itens.Where(i => !i.EhSeparador).Select(i => Sem(i)).Distinct().Count() == itens.Count(i => !i.EhSeparador), $"{vm.Modulo.NomeCurto}: título repetido em {onde}");
                foreach (var f in itens.Where(i => i.Filhos is not null)) Conferir(f.Filhos!, onde + " › " + Sem(f));
            }
            Conferir(vm.Menus, "menu");
        }
    }

    [Fact]
    public void Transportadora_e_cadastrada_em_todos_os_modulos_reunidos_e_o_manual_mostra_as_areas_deles()
    {
        foreach (IModuloDesktop m in ModuloLogistica.Todos())
        {
            Assert.Equal(ModulosEmpresa.Frota, m.ModuloEmpresa);
            Assert.Equal(ModulosEmpresa.Frota | ModulosEmpresa.Contabil | ModulosEmpresa.Fiscal | ModulosEmpresa.Erp, m.ModulosAoCadastrar);
            Assert.Equal("Transportadora", m.RotuloEmpresaAtiva);
            Assert.NotNull(m.Icone);
            Assert.StartsWith("VorticisLogistica.", m.NomeExecutavel);
        }
        var areas = ((IModuloDesktop)new ModuloContabilidade()).AreasDoManual;
        Assert.Contains("Contábil", areas);
        Assert.DoesNotContain("Folha", areas);
        Assert.Contains(ManualUsuario.Visiveis(areas, false), a => a.Nome == "03-contabil.md");
        Assert.NotNull(ManualUsuario.TopicoDaTela("VorticisContabil:" + TelasComuns.Parametros));   // F1 nos parâmetros de cada área
    }

    [Fact]
    public async Task Telas_de_todas_as_areas_abrem_no_programa_unico()
    {
        var ok = await ForaDaSessao(headless.Sessao.Dispatch(() =>
        {
            HospedeiroSuite.IncluirTelas(Avalonia.Application.Current!, ModuloLogistica.Apps());
            var vm = Modulo<ModuloFrotaLogistica>();
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
            var total = 0;
            foreach (var modulo in ModuloLogistica.Todos())
            {
            var vm = new MainViewModel(modulo);
            await vm.InicializarAsync(db.ConnectionString);
            var login = Assert.IsType<LoginViewModel>(vm.Conteudo);
            (login.Usuario, login.Senha) = ("admin_teste", "Senha1234");
            await login.EntrarCommand.ExecuteAsync(null);
            for (var i = 0; i < 200 && !vm.Logado; i++) { await Task.Delay(20, ct); Dispatcher.UIThread.RunJobs(); }
            Assert.True(vm.Logado);
            vm.EmpresaSelecionada = vm.EmpresasPermitidas.Single(e => e.Id == empresa);
            for (var i = 0; i < 50; i++) { await Task.Delay(20, ct); Dispatcher.UIThread.RunJobs(); }
            var chaves = Folhas(vm.Menus).Where(i => i.Habilitado && i.Chave is not null && i.Chave != TelasComuns.Sair).Select(i => i.Chave!).Distinct().ToList();
            total += chaves.Count;
            foreach (var chave in chaves)
            {
                Assert.True(vm.AbrirCommand.CanExecute(chave), $"{modulo.NomeCurto}: {chave}");
                vm.AbrirCommand.Execute(chave);
                for (var i = 0; i < 5; i++) { await Task.Delay(10, ct); Dispatcher.UIThread.RunJobs(); }
                Assert.NotNull(vm.Conteudo);
                Assert.True(vm.Confirmacao is null, $"{modulo.NomeCurto} › {chave}: {vm.Status}");     // nenhum erro inesperado ao abrir
            }
            await vm.DisposeAsync();
            }
            Assert.True(total > 100, $"{total} telas");
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
