using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Vorticis.Application.Cadastros;
using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Domain.Seguranca;
using Vorticis.Frota;
using Vorticis.Infra;
using Vorticis.Infra.Tests;
using F = Vorticis.Frota.Desktop;

namespace Vorticis.Desktop.Tests;

/// <summary>Módulo Frota (piloto): menus, telas que renderizam e o fluxo veículo → abastecimentos → relatório pelas telas.</summary>
public class FrotaTelasTests(SessaoHeadless headless, BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static IEnumerable<ItemMenu> Folhas(IEnumerable<ItemMenu> itens) => itens.SelectMany(i => i.Filhos is null ? [i] : Folhas(i.Filhos));

    private static async Task<T> ForaDaSessao<T>(Task<T> tarefa)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = tarefa.ContinueWith(t => { if (t.IsFaulted) tcs.SetException(t.Exception!.InnerExceptions); else tcs.SetResult(t.Result); }, TaskScheduler.Default);
        return await tcs.Task;
    }

    private sealed class Contexto(VorticisApp app, long usuario, long empresa) : IContexto
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
        public ModulosEmpresa ModuloAtual => ModulosEmpresa.Frota;
        public Task<string?> SolicitarSenhaAsync(string titulo, string mensagem, string textoSim = "Confirmar", string textoNao = "Cancelar") => Task.FromResult<string?>(null);
        public string DescricaoEmpresa => "Transportadora de teste";
        public void AbrirTela(string tela) { }
        public Task ConcluirImplantacaoAsync(string loginAdministrador) => Task.CompletedTask;
    }

    [Fact]
    public void Menus_da_frota_tem_cadastros_operacao_e_relatorios_registrados()
    {
        var vm = new F.MainViewModel();
        Assert.Equal("Vorticis Frota", vm.Modulo.Nome);
        Assert.Equal(ModulosEmpresa.Frota, vm.Modulo.ModuloEmpresa);
        Assert.Equal("Transportadora", vm.Modulo.RotuloEmpresaAtiva);
        var cadastros = Folhas(vm.Menus[0].Filhos!).Select(i => i.Titulo).ToList();
        Assert.Contains("Veículos", cadastros);
        Assert.Contains("Motoristas", cadastros);
        Assert.Contains("Frotas", cadastros);
        Assert.Contains("Rotas", cadastros);
        Assert.Contains("Tabelas de frete", cadastros);
        Assert.DoesNotContain("Planos de manutenção", cadastros);
        Assert.Contains("Documentos e vencimentos", cadastros);
        var operacao = Assert.Single(vm.Menus, m => m.Titulo == "_Operação");
        Assert.Equal(["Abastecimentos", "Viagens", "Diárias", "_Coletas", "Entregas e ocorrências", "_Pedágio"], operacao.Filhos!.Where(i => !i.EhSeparador).Select(i => i.Titulo));
        Assert.Equal(["Ordens de coleta"], operacao.Filhos!.Single(i => i.Titulo == "_Coletas").Filhos!.Select(i => i.Titulo));
        var manutencao = Assert.Single(vm.Menus, m => m.Titulo == "_Manutenção");
        Assert.Equal(["Planos de manutenção", "Peças e serviços", "Ordens de serviço", "Autorização de serviços"], manutencao.Filhos!.Select(i => i.Titulo));
        // TMS: cotações, CT-e, manifestos e faturas de frete no menu Emissão
        var emissao = Assert.Single(vm.Menus, m => m.Titulo == "_Emissão");
        Assert.Equal(["Cotações de frete", "Conhecimentos de transporte (CT-e)", "Manifestos de carga (MDF-e)", "Faturas de frete"],
                     emissao.Filhos!.Where(i => !i.EhSeparador).Select(i => i.Titulo));
        var rel = Assert.Single(vm.Menus, m => m.Titulo == "_Relatórios");
        Assert.Equal(["Painel da frota", "Monitor de multas de trânsito", TabelasFrota.TituloCustos, TabelasFrota.TituloConsumo, TabelasFrota.TituloViagens, TabelasFrota.TituloVencimentos,
                      TabelasFrota.TituloCustosFrota, TabelasFrota.TituloDiariasColaborador, TabelasFrota.TituloDiariasFrota, TabelasFrota.TituloDiariasAcerto, TabelasFrota.TituloConsumoVeiculo,
                      TabelasFrota.TituloConsumoMotorista, TabelasFrota.TituloViagensMotorista, TabelasFrota.TituloViagensCliente, TabelasFrota.TituloViagensRota,
                      TabelasFrota.TituloColetasCliente, TabelasFrota.TituloColetasRota, TabelasFrota.TituloColetasMotorista, TabelasFrota.TituloResumo,
                      "Acompanhamento das ordens de serviço", "Aprovações de serviços", "Notas fiscais de compra", TabelasFrota.TituloPreventivas, TabelasFrota.TituloHistoricoManutencao,
                      .. TabelasTms.Titulos],
                     rel.Filhos!.Where(i => !i.EhSeparador).Select(i => i.Titulo));
        Assert.All(Folhas(vm.Menus).Where(i => i.Chave is not null && i.Chave != TelasComuns.Sair), i => Assert.True(vm.ConheceTela(i.Chave!), i.Chave));
        Assert.All(vm.Atalhos.Where(a => a.Chave is not null), a => Assert.True(vm.ConheceTela(a.Chave!), a.Chave));
    }

    [Fact]
    public async Task Cte_anexa_xml_das_notas_e_mostra_o_painel()
    {
        var ok = await ForaDaSessao(headless.Sessao.Dispatch(() =>
        {
            static string Chave(int numero)
            {
                var c43 = $"352603{"11222333000181"}55001{numero:000000000}1{12345678}";
                return c43 + RegrasTms.DigitoChave(c43);
            }
            static byte[] Xml(int numero, decimal valor, int vol, decimal peso) => System.Text.Encoding.UTF8.GetBytes($"""
                <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe"><NFe><infNFe Id="NFe{Chave(numero)}">
                <ide><serie>1</serie><nNF>{numero}</nNF><dhEmi>2026-03-02T10:00:00-03:00</dhEmi></ide>
                <emit><CNPJ>11222333000181</CNPJ><xNome>Indústria Alfa</xNome><enderEmit><cMun>3509502</cMun><xMun>Campinas</xMun><UF>SP</UF></enderEmit></emit>
                <dest><CNPJ>22333444000181</CNPJ><xNome>Comércio Beta</xNome><enderDest><cMun>3304557</cMun><xMun>Rio de Janeiro</xMun><UF>RJ</UF></enderDest></dest>
                <det><prod><xProd>Peças</xProd><vProd>{valor}</vProd></prod></det>
                <total><ICMSTot><vNF>{valor}</vNF></ICMSTot></total><transp><vol><qVol>{vol}</qVol><pesoB>{peso}</pesoB></vol></transp>
                </infNFe></NFe></nfeProc>
                """);
            var vm = new F.ViewModels.CtesViewModel(new F.MainViewModel());
            vm.AnexarXml([("1.xml", Xml(1, 6000, 3, 100)), ("2.xml", Xml(2, 4000, 2, 50)), ("1-de-novo.xml", Xml(1, 6000, 3, 100)), ("lixo.xml", "<x/>"u8.ToArray())]);
            Assert.Equal(2, vm.Notas.Count);
            Assert.Equal(2, vm.Erros.Count);                                           // a repetida e o arquivo que não é NF-e
            Assert.Equal(("5", "150", "10000", "Peças"), (vm.Volumes, vm.Peso, vm.ValorMercadoria, vm.Produto));
            Assert.Equal([Chave(1), Chave(2)], RegrasTms.ChavesNfe(vm.ChavesNfe));
            Assert.Equal(("2", "5", "10.000,00"), (vm.PainelQuantidade, vm.PainelVolumes, vm.PainelValor));
            Assert.Equal(("RJ", "3304557"), (vm.Destino.Uf, vm.Destino.Municipio?.Ibge));    // trecho pelas notas
            vm.NotaSelecionada = vm.Notas[0];
            vm.RetirarNotaCommand.Execute(null);
            Assert.Equal(("2", "50", "1"), (vm.Volumes, vm.Peso, vm.PainelQuantidade));
            Assert.Equal([Chave(2)], RegrasTms.ChavesNfe(vm.ChavesNfe));

            var view = new F.Views.CtesView { DataContext = vm };
            var janela = new Window { Content = view, Width = 1400, Height = 820 };
            janela.Show();
            view.FindControl<TabControl>("AbasCte")!.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            var quadro = janela.CaptureRenderedFrame();
            Assert.NotNull(quadro);
            if (Environment.GetEnvironmentVariable("VORTICIS_SCREENSHOTS") is { Length: > 0 } pasta)
                quadro.Save(Path.Combine(Directory.CreateDirectory(pasta).FullName, "cte-notas.png"));
            janela.Close();
            return Task.FromResult(true);
        }, TestContext.Current.CancellationToken));
        Assert.True(ok);
    }

    [Fact]
    public async Task Telas_da_frota_renderizam_e_o_relatorio_monta_as_colunas_da_tabela()
    {
        var ok = await ForaDaSessao(headless.Sessao.Dispatch(() =>
        {
            var m = new F.MainViewModel();
            var relatorio = new F.ViewModels.RelatorioFrotaViewModel(m, TabelasFrota.TituloCustos);
            (object Vm, UserControl View)[] telas =
            [
                (new F.ViewModels.PainelFrotaViewModel(m), new F.Views.PainelFrotaView()),
                (new F.ViewModels.VeiculosViewModel(m), new F.Views.VeiculosView()),
                (new F.ViewModels.MotoristasViewModel(m), new F.Views.MotoristasView()),
                (new F.ViewModels.PlanosViewModel(m), new F.Views.PlanosView()),
                (new F.ViewModels.DocumentosViewModel(m), new F.Views.DocumentosView()),
                (new F.ViewModels.AbastecimentosViewModel(m), new F.Views.AbastecimentosView()),
                (new F.ViewModels.OrdensViewModel(m), new F.Views.OrdensView()),
                (new F.ViewModels.ViagensViewModel(m), new F.Views.ViagensView()),
                (new F.ViewModels.FrotasViewModel(m), new F.Views.FrotasView()),
                (new F.ViewModels.RotasViewModel(m), new F.Views.RotasView()),
                (new F.ViewModels.DiariasViewModel(m), new F.Views.DiariasView()),
                (new F.ViewModels.ColetasViewModel(m), new F.Views.ColetasView()),
                (new F.ViewModels.RelatorioFrotaViewModel(m, TabelasFrota.TituloDiariasColaborador), new F.Views.RelatorioFrotaView()),
                (new F.ViewModels.ItensManutencaoViewModel(m), new F.Views.ItensManutencaoView()),
                (new F.ViewModels.AutorizacaoViewModel(m), new F.Views.AutorizacaoView()),
                (new F.ViewModels.NotasCompraViewModel(m), new F.Views.NotasCompraView()),
                (new F.ViewModels.TabelasFreteViewModel(m), new F.Views.TabelasFreteView()),
                (new F.ViewModels.PedagioViewModel(m), new F.Views.PedagioView()),
                (new F.ViewModels.LayoutsPedagioViewModel(m), new F.Views.LayoutsPedagioView()),
                (new F.ViewModels.CotacoesViewModel(m), new F.Views.CotacoesView()),
                (new F.ViewModels.CtesViewModel(m), new F.Views.CtesView()),
                (new F.ViewModels.ManifestosViewModel(m), new F.Views.ManifestosView()),
                (new F.ViewModels.EntregasViewModel(m), new F.Views.EntregasView()),
                (new F.ViewModels.FaturasFreteViewModel(m), new F.Views.FaturasFreteView()),
                (new F.ViewModels.RelatorioFrotaViewModel(m, TabelasTms.TituloPrazoEntrega), new F.Views.RelatorioFrotaView()),
                (new F.ViewModels.RelatorioFrotaViewModel(m, TabelasFrota.TituloAcompanhamento), new F.Views.RelatorioFrotaView()),
                (new F.ViewModels.RelatorioFrotaViewModel(m, TabelasFrota.TituloViagensCliente), new F.Views.RelatorioFrotaView()),
                (relatorio, new F.Views.RelatorioFrotaView()),
                (new F.ViewModels.MultasViewModel(m), new F.Views.MultasView()),
                (new F.ViewModels.ContratosViewModel(m, RegrasContratos.Financiamento), new F.Views.ContratosView()),
                (new F.ViewModels.ContratosViewModel(m, RegrasContratos.Consorcio), new F.Views.ContratosView()),
                (new F.ViewModels.BensContratosViewModel(m), new F.Views.BensContratosView()),
            ];
            foreach (var (vm, view) in telas)
            {
                view.DataContext = vm;
                var janela = new Window { Content = view, Width = 1280, Height = 760 };
                janela.Show();
                Dispatcher.UIThread.RunJobs();
                var quadro = janela.CaptureRenderedFrame();
                Assert.NotNull(quadro);
                if (Environment.GetEnvironmentVariable("VORTICIS_SCREENSHOTS") is { Length: > 0 } pasta && vm is F.ViewModels.MultasViewModel or F.ViewModels.ContratosViewModel)
                    quadro.Save(Path.Combine(Directory.CreateDirectory(pasta).FullName, $"frota-{view.GetType().Name}-{(vm as F.ViewModels.ContratosViewModel)?.Tipo}.png"));
                janela.Close();
            }
            var rv = new F.Views.RelatorioFrotaView { DataContext = relatorio };
            relatorio.Colunas = TabelasFrota.Custos([], [], [], []).Colunas;
            var grade = rv.FindControl<DataGrid>("Grade")!;
            Assert.Equal(9, grade.Columns.Count);
            Assert.Equal("Custo por km", grade.Columns[^1].Header);
            Assert.False(new F.ViewModels.RelatorioFrotaViewModel(m, TabelasFrota.TituloVencimentos).UsaPeriodo);
            return Task.FromResult(true);
        }, TestContext.Current.CancellationToken));
        Assert.True(ok);
    }

    [Fact]
    public void Toda_tela_da_frota_tem_DataTemplate_no_App_axaml()
    {
        // sem o DataTemplate a tela abre em branco no programa (aconteceu com Frotas, Rotas, Diárias e Coletas)
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "Vorticis.slnx"))) raiz = raiz.Parent;
        var app = File.ReadAllText(Path.Combine(raiz!.FullName, "src", "Vorticis.Frota.Desktop", "App.axaml"));
        var telas = typeof(F.ModuloFrota).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(ModuloViewModel)) && !t.IsSubclassOf(typeof(ParametrosViewModel)) && t.Name.EndsWith("ViewModel")).ToList();
        Assert.True(telas.Count >= 20, $"{telas.Count} telas");
        Assert.All(telas, t => Assert.Contains($"DataType=\"vm:{t.Name}\"", app));
    }

    [Fact]
    public async Task Rota_pela_tela_estima_a_distancia_e_a_edicao_manual_trava_o_recalculo()
    {
        BancoDeTeste.ExigirBanco();
        var ctx = new Contexto(db.App, await db.AdminAsync(), await db.CriarEmpresaAsync("11222333000181"));
        var rotas = new F.ViewModels.RotasViewModel(ctx);
        await rotas.CarregarAsync();
        Assert.Equal("1", rotas.Codigo);
        rotas.Descricao = "SP - RJ";
        foreach (var (uf, ibge) in new[] { ("SP", "3550308"), ("RJ", "3304557") })
        {
            rotas.Uf = uf;
            rotas.Municipio = rotas.MunicipiosUf.Single(m => m.Ibge == ibge);
            rotas.AdicionarParadaCommand.Execute(null);
        }
        Assert.Equal(["1. Origem: São Paulo/SP", "2. Destino: Rio de Janeiro/RJ"], rotas.Paradas.Select(p => p.Texto));
        var estimada = Vorticis.Domain.Comum.Formato.LerValor(rotas.DistanciaKm)!.Value;
        Assert.InRange(estimada, 440m, 460m);                 // ~360 km em linha reta x 1,25
        Assert.Equal("ESTIMADA", rotas.OrigemDistancia);
        rotas.DistanciaKm = "505";                            // edição manual
        Assert.Equal("MANUAL", rotas.OrigemDistancia);
        rotas.SubirParadaCommand.Execute(null);
        Assert.Equal("505", rotas.DistanciaKm);               // mudar as paradas não recalcula uma distância manual
        await rotas.GravarCommand.ExecuteAsync(null);
        Assert.Equal("Rota gravada.", rotas.Mensagem);
        Assert.Equal("505,0 km", rotas.Lista.Single().Distancia);
        rotas.RecalcularEstimativaCommand.Execute(null);
        Assert.Equal(estimada, Vorticis.Domain.Comum.Formato.LerValor(rotas.DistanciaKm));
    }

    [Fact]
    public async Task Veiculo_abastecimentos_com_media_e_relatorio_pelas_telas()
    {
        BancoDeTeste.ExigirBanco();
        var ctx = new Contexto(db.App, await db.AdminAsync(), await db.CriarEmpresaAsync("48731625000230"));

        var veiculos = new F.ViewModels.VeiculosViewModel(ctx);
        await veiculos.CarregarAsync();
        Assert.Equal("1", veiculos.Codigo);                            // código sugerido
        (veiculos.Placa, veiculos.Marca, veiculos.Modelo, veiculos.KmAtual, veiculos.MediaEsperada) = ("QWE4R56", "Mercedes", "Actros", "50000", "2,6");
        await veiculos.GravarCommand.ExecuteAsync(null);
        Assert.Equal("Veículo gravado.", veiculos.Mensagem);
        Assert.Equal("QWE4R56", Assert.Single(veiculos.Lista).Placa);
        veiculos.Tipo = F.ViewModels.VeiculosViewModel.Tipos.First(t => t.Valor == "REBOQUE");
        Assert.Equal("NENHUM", veiculos.Combustivel!.Valor);           // reboque não abastece

        var abast = new F.ViewModels.AbastecimentosViewModel(ctx) { Inicio = DateTime.Today.AddDays(-10), Fim = DateTime.Today };
        await abast.CarregarAsync();
        abast.Veiculo = abast.VeiculosOpcoes.Single(o => o.Id is not null);
        Assert.Equal("DIESEL_S10", abast.Combustivel!.Valor);          // combustível do veículo
        foreach (var (dias, km, litros) in new[] { (5, "50500", "300"), (2, "51500", "400") })
        {
            (abast.Data, abast.Km, abast.Litros, abast.Valor) = (DateTime.Today.AddDays(-dias), km, litros, "2000");
            abast.Veiculo = abast.VeiculosOpcoes.Single(o => o.Id is not null);
            await abast.GravarCommand.ExecuteAsync(null);
            Assert.Empty(abast.Erros);
        }
        Assert.Contains("2,50 km/l", abast.Mensagem);                  // 1.000 km / 400 l, abaixo de 2,6 mas dentro da tolerância de 15%
        Assert.Equal(2, abast.Lista.Count);
        Assert.Equal("2,50 km/l", abast.Lista[0].Media);

        var rel = new F.ViewModels.RelatorioFrotaViewModel(ctx, TabelasFrota.TituloConsumo) { Inicio = DateTime.Today.AddDays(-10), Fim = DateTime.Today };
        await rel.GerarCommand.ExecuteAsync(null);
        Assert.True(rel.TemDados);
        Assert.Equal("2,50", Assert.Single(rel.Linhas).V4);

        var painel = new F.ViewModels.PainelFrotaViewModel(ctx);
        await painel.CarregarAsync();
        Assert.Contains("Nenhuma pendência", painel.Resumo);
        Assert.Contains("1 veículo(s) ativo(s)", painel.Numeros);
    }

    [Fact]
    public async Task Ordem_de_servico_com_itens_autorizada_concluida_e_nota_de_compra_pelas_telas()
    {
        BancoDeTeste.ExigirBanco();
        var ctx = new Contexto(db.App, await db.AdminAsync(), await db.CriarEmpresaAsync("27183946000278"));
        var hoje = DateTime.Today;
        var veiculos = new F.ViewModels.VeiculosViewModel(ctx);
        await veiculos.CarregarAsync();
        (veiculos.Placa, veiculos.KmAtual) = ("MAN3C45", "20000");
        await veiculos.GravarCommand.ExecuteAsync(null);

        var itens = new F.ViewModels.ItensManutencaoViewModel(ctx);
        await itens.CarregarAsync();
        (itens.Descricao, itens.Valor) = ("Pastilha de freio", "150");
        await itens.GravarCommand.ExecuteAsync(null);
        Assert.Equal("Item gravado.", itens.Mensagem);

        var ordens = new F.ViewModels.OrdensViewModel(ctx);
        await ordens.CarregarAsync();
        ordens.Veiculo = ordens.VeiculosOpcoes.Single(o => o.Id is not null);
        ordens.Descricao = "Revisão dos freios";
        ordens.ItemCatalogo = ordens.CatalogoOpcoes.Single(o => o.Id is not null);
        Assert.Equal(("Pastilha de freio", "150"), (ordens.ItemDescricao, ordens.ItemValor));   // vem do catálogo
        ordens.ItemQuantidade = "4";
        ordens.AdicionarItemCommand.Execute(null);
        (ordens.ItemTipo, ordens.ItemDescricao, ordens.ItemValor) = (F.ViewModels.OrdensViewModel.TiposItem[1], "Mão de obra", "200");
        ordens.AdicionarItemCommand.Execute(null);
        Assert.Contains("= 800,00", ordens.TotalOrdem);
        await ordens.GravarCommand.ExecuteAsync(null);
        Assert.Empty(ordens.Erros);
        Assert.Equal(1, ordens.Selecionada!.Numero);
        await ordens.PedirAutorizacaoCommand.ExecuteAsync(null);
        Assert.Equal("Aguardando autorização", ordens.Selecionada!.Situacao);
        Assert.False(ordens.Editavel);

        var aut = new F.ViewModels.AutorizacaoViewModel(ctx);
        await aut.CarregarAsync();
        Assert.Equal(1, Assert.Single(aut.Pendentes).Numero);
        await aut.ReprovarCommand.ExecuteAsync(null);
        Assert.Contains("motivo", Assert.Single(aut.Erros));
        await aut.AprovarCommand.ExecuteAsync(null);
        Assert.Empty(aut.Pendentes);
        Assert.Equal("Autorizada", Assert.Single(aut.Historico).Decisao);

        await ordens.CarregarAsync();
        ordens.Selecionada = ordens.Lista.Single();
        await ordens.ConcluirCommand.ExecuteAsync(null);
        Assert.Equal("Concluída", ordens.Lista.Single().Situacao);

        var notas = new F.ViewModels.NotasCompraViewModel(ctx);
        await notas.CarregarAsync();
        notas.Ordem = notas.OrdensOpcoes.Single(o => o.Id is not null);
        Assert.Contains("total 800,00", notas.Conferencia);
        (notas.Fornecedor, notas.Numero, notas.ValorPecas, notas.ValorServicos) = ("Freios Já", "321", "600", "300");
        await notas.GravarCommand.ExecuteAsync(null);
        Assert.Contains("passa o da OS nº 1", notas.Mensagem);
        Assert.Equal("1 (MAN3C45)", Assert.Single(notas.Lista).Ordem);

        var rel = new F.ViewModels.RelatorioFrotaViewModel(ctx, TabelasFrota.TituloAcompanhamento) { Inicio = hoje, Fim = hoje };
        await rel.GerarCommand.ExecuteAsync(null);
        Assert.Equal(("Concluída", "800,00", "900,00", "100,00"), (rel.Linhas[0].V4, rel.Linhas[0].V7, rel.Linhas[0].V8, rel.Linhas[0].V9));
    }
}
