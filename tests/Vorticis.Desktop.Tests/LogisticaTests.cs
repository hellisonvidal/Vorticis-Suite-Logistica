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

/// <summary>Vorticis Suite Logística: seis módulos na mesma janela (Frotas, Transporte com o faturamento, Manutenção, Financeiro, Gerencial e Contabilidade), sem a Folha.</summary>
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
    public void Seis_modulos_com_as_funcoes_reorganizadas()
    {
        var todos = ModuloLogistica.Todos().Select(m => new MainViewModel(m)).ToList();
        Assert.Equal(["Frotas", "Transporte", "Manutenção", "Financeiro", "Gerencial", "Contabilidade"], todos.Select(m => m.Modulo.NomeCurto));
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
        Assert.Equal(["Operação", "Relatórios"], Menus(todos[0]));
        Assert.Equal(["Operação", "Faturamento", "Relatórios"], Menus(todos[1]));
        Assert.Equal(["Serviços", "Relatórios"], Menus(todos[2]));
        // o Transporte tem todo o faturamento, com o CT-e e o MDF-e (não há mais o menu Documentos nem o módulo Faturamento), com os relatórios de faturas, vendas e recebimentos
        Assert.Equal(["Conhecimentos de transporte (CT-e)", "Manifestos de carga (MDF-e)", "Transmissão de CT-e e MDF-e à SEFAZ (em breve)", "-",
                      "Faturas de frete", "-", "Pedidos de venda", "Notas fiscais de saída", "NFS-e – Notas fiscais de serviço", "-",
                      "Emissão de NF-e na SEFAZ (em breve)", "Transmissão da NFS-e ao Emissor Nacional (em breve)"],
                     todos[1].Menus.Single(m => Sem(m) == "Faturamento").Filhos!.Select(i => i.Titulo));
        Assert.Contains(Folhas(todos[1].Menus), i => i.Chave == Fiscal.Desktop.ViewModels.Telas.ResumoVendas);
        // Cadastros › Transportadoras só no Frotas (nos demais, a transportadora é escolhida no alto da tela)
        Assert.Equal("Transportadoras", todos[0].Menus[0].Filhos![0].Titulo);
        Assert.All(todos.Skip(1), vm => Assert.DoesNotContain(Folhas(vm.Menus), i => i.Chave == TelasComuns.Empresas));
        Assert.All(todos, vm => Assert.True(vm.ConheceTela(TelasComuns.Empresas)));
        Assert.All(todos.Skip(1), vm => Assert.Equal("Clientes", vm.Menus[0].Filhos![0].Titulo));
        Assert.Equal(["Contas", "Imobilizado", "Monitores", "Relatórios"], Menus(todos[3]));
        Assert.Equal(["Contas a pagar", "Contas a receber", "-", "Consórcios", "Financiamentos", "-", "Conciliação bancária"],
                     todos[3].Menus.Single(m => m.Titulo == "_Contas").Filhos!.Select(f => f.Titulo));
        Assert.Equal(["Bens financiados e consorciados", "-", "Cadastro de bens", "Cálculo e lançamento da depreciação"],
                     todos[3].Menus.Single(m => m.Titulo == "_Imobilizado").Filhos!.Select(f => f.Titulo));
        Assert.Equal(["Compras", "Estoque", "Monitores", "Relatórios"], Menus(todos[4]));
        Assert.Equal(["Monitor de notas fiscais", "Monitor de multas de trânsito"], todos[4].Menus.Single(m => m.Titulo == "_Monitores").Filhos!.Select(f => f.Titulo));
        Assert.NotNull(todos[4].ImportacaoDaTela(FT.Multas));                // as multas também vêm de planilha
        Assert.Contains("Lançamentos", Menus(todos[5]));

        // cada função num módulo só (sem repetir), com as funções espalhadas pelos módulos certos
        void Em(int modulo, params string[] chaves) => Assert.All(chaves, c => Assert.Contains(c, Chaves(todos[modulo])));
        Em(0, FT.Abastecimentos, FT.Viagens, FT.Diarias, FT.Pedagio, FT.Veiculos, FT.Motoristas, FT.Painel);
        Em(1, FT.Cotacoes, FT.Coletas, FT.Entregas, FT.Ctes, FT.Manifestos, FT.TabelasFrete, FT.Ciot, FT.Roteirizador);
        Em(2, FT.Planos, FT.Itens, FT.Ordens, FT.Autorizacao, FT.RelHistoricoManutencao);
        Em(1, FT.Faturas, Erp.Desktop.ViewModels.ErpTelas.PedidosVenda, Fiscal.Desktop.ViewModels.Telas.NotaSaida);
        Em(3, Fiscal.Desktop.ViewModels.Telas.ContasPagar, Fiscal.Desktop.ViewModels.Telas.ContasReceber, Fiscal.Desktop.ViewModels.Telas.ConciliacaoDuplicatas,
           TelasComuns.ContasBancarias, FT.Financiamentos, FT.Consorcios, FT.BensContratos, ViewModels.Telas.Bens);
        Em(4, Erp.Desktop.ViewModels.ErpTelas.PedidosCompra, Fiscal.Desktop.ViewModels.Telas.NotaEntrada, Erp.Desktop.ViewModels.ErpTelas.Estoque,
           Erp.Desktop.ViewModels.ErpTelas.Painel, Fiscal.Desktop.ViewModels.Telas.Produtos, Fiscal.Desktop.ViewModels.Telas.RegrasFiscais, FT.Multas);
        Em(5, ViewModels.Telas.Lancamento, ViewModels.Telas.Balancete, ViewModels.Telas.Filiais, ViewModels.Telas.RelatorioGrupo);
        var doModulo = todos.SelectMany(vm => Folhas([.. vm.Modulo.ItensCadastros, .. vm.Modulo.Menus]).Where(x => x.Chave is { } c && !c.StartsWith("grafico.")
                                                                                                         && c is not (TelasComuns.FormasPagamento or TelasComuns.ContasBancarias
                                                                                                                      // o imobilizado também fica no Financeiro, junto dos contratos
                                                                                                                      or ViewModels.Telas.Bens or ViewModels.Telas.Depreciacao))
                                                  .Select(x => (Modulo: vm.Modulo.NomeCurto, Chave: x.Chave!)).Distinct()).ToList();
        Assert.Empty(doModulo.GroupBy(x => x.Chave).Where(g => g.Count() > 1).Select(g => $"{g.Key}: {string.Join(", ", g.Select(x => x.Modulo))}"));
        // a transmissão do CT-e/MDF-e e da NFS-e continuam "em breve"; a NFS-e já tem a sua lista (as notas de serviço, lançadas ou importadas)
        Assert.Contains(Folhas(todos[1].Menus), i => i.Habilitado && i.Chave == Fiscal.Desktop.ViewModels.Telas.Nfse);
        var nfse = new Fiscal.Desktop.ViewModels.NfseViewModel(todos[1]);
        Assert.Equal((Vorticis.Fiscal.Documentos.ModeloDocumento.NFSe, false, "NFS-e – Notas fiscais de serviço"), (nfse.ModeloFixo, nfse.EscolheModelo, nfse.Titulo));
        Assert.Contains(Folhas(todos[1].Menus), i => !i.Habilitado && i.Titulo.StartsWith("Transmissão de CT-e"));
        Assert.Contains(Folhas(todos[1].Menus), i => !i.Habilitado && i.Titulo.StartsWith("Transmissão da NFS-e"));
        // importação de planilha dentro de cada cadastro (botão na janela do cadastro); não há mais o submenu em Cadastros
        Assert.All(todos, vm => Assert.DoesNotContain(vm.Menus[0].Filhos!, i => i.Titulo.Contains("planilha")));
        string[] Importaveis(MainViewModel vm) => [.. Folhas(vm.Menus[0].Filhos!).Where(i => vm.ImportacaoDaTela(i.Chave) is not null).Select(i => i.Titulo)];
        Assert.Equal(["Clientes", "Fornecedores", "Veículos", "Motoristas", "Frotas", "Rotas"], Importaveis(todos[0]));
        Assert.Contains("Tabelas de frete", Importaveis(todos[1]));
        Assert.Equal(["Clientes", "Fornecedores", "Produtos", "Regras fiscais"], Importaveis(todos[4]));
        Assert.Contains("Centros de custo", Importaveis(todos[5]));
        Assert.All(todos.SelectMany(vm => Importaveis(vm).Select(t => (vm, t))), x => Assert.True(x.vm.ConheceTela(x.vm.ImportacaoDaTela(
            Folhas(x.vm.Menus[0].Filhos!).First(i => i.Titulo == x.t).Chave)!.Tela)));
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
