using Avalonia.Controls;
using Avalonia.Threading;
using Vorticis.Application.Cadastros;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Desktop.ViewModels;
using Vorticis.Fiscal.Desktop.Views;
using Vorticis.Fiscal.Tests;
using Vorticis.Infra;
using Vorticis.Infra.Tests;

namespace Vorticis.Desktop.Tests;

/// <summary>Importar na tela de notas um XML que não é da empresa selecionada: avisa na lista e a janela continua aberta.</summary>
public class ImportacaoXmlTelaTests(BancoDeTeste db, SessaoHeadless headless) : IClassFixture<BancoDeTeste>
{
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
        public ModulosEmpresa ModuloAtual => ModulosEmpresa.Fiscal;
        public Task<string?> SolicitarSenhaAsync(string titulo, string mensagem, string textoSim = "Confirmar", string textoNao = "Cancelar") => Task.FromResult<string?>(null);
        public string DescricaoEmpresa => "Empresa de teste";
        public void AbrirTela(string tela) { }
        public Task ConcluirImplantacaoAsync(string loginAdministrador) => Task.CompletedTask;
    }

    [Fact]
    public async Task Xml_de_outra_empresa_avisa_e_nao_derruba_a_tela()
    {
        BancoDeTeste.ExigirBanco();
        var empresa = await db.CriarEmpresaAsync("27865757000102");
        var ctx = new Contexto(db.App, await db.AdminAsync(), empresa);
        var pasta = Directory.CreateTempSubdirectory("xml_outra").FullName;
        var arquivo = Path.Combine(pasta, "terceiros.xml");
        await File.WriteAllBytesAsync(arquivo, XmlsDeTeste.Bytes(XmlsDeTeste.Nfe("99887766000155", "55443322000199", 1, 5000, new DateOnly(2026, 3, 10),
                                                                                  [new ItemNfe("5102", 10m)])), TestContext.Current.CancellationToken);
        var falhas = new List<Exception>();
        var ok = await Task.Run(async () => await headless.Sessao.Dispatch(async () =>
        {
            Dispatcher.UIThread.UnhandledException += (_, e) => { falhas.Add(e.Exception); e.Handled = true; };
            foreach (var vm in new DigitacaoDocumentoViewModel[] { new NotaEntradaViewModel(ctx), new NotaSaidaViewModel(ctx) })
            {
                var janela = new Window { Content = new DigitacaoDocumentoView { DataContext = vm }, Width = 1300, Height = 800 };
                janela.Show();
                await vm.CarregarAsync();
                Dispatcher.UIThread.RunJobs();
                await vm.Importador.ImportarArquivosAsync([arquivo]);
                for (var i = 0; i < 20; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
                Assert.Equal(1, vm.Importador.Ultimo!.OutrasEmpresas);
                Assert.Equal("Outra empresa", Assert.Single(vm.Importador.Resultados).Situacao);
                Assert.True(janela.IsVisible);
                janela.Hide();
            }
            return true;
        }, TestContext.Current.CancellationToken));
        Assert.True(ok);
        Assert.Empty(falhas.Select(f => f.ToString()));
        Directory.Delete(pasta, true);
    }
}
