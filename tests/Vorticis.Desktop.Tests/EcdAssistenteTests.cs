using Vorticis.Application.Sped;
using Vorticis.Desktop.ViewModels;
using Vorticis.Domain.Seguranca;
using Vorticis.Domain.Sped;
using Vorticis.Infra;
using Vorticis.Infra.Tests;

namespace Vorticis.Desktop.Tests;

/// <summary>Contexto mínimo para exercitar ViewModels sem a janela principal.</summary>
internal sealed class ContextoDeTeste(VorticisApp? app, SessaoUsuario? sessao, long? empresa, bool pode = true) : IContexto
{
    public List<string> Abertas { get; } = [];
    public VorticisApp? App => app;
    public SessaoUsuario? Sessao => sessao;
    public long? EmpresaId => empresa;
    public bool Pode(Permissao permissao) => pode;
    public Task RecarregarEmpresasAsync(long? selecionar = null) => Task.CompletedTask;
    public void AoConcluirConfiguracaoInicial() { }
    public Task<bool> ConfirmarAsync(string titulo, string mensagem, string textoSim = "Sim", string textoNao = "Não") => Task.FromResult(true);
    public Task ReiniciarParaLoginAsync(string mensagem) => Task.CompletedTask;
    public Task ConcluirImplantacaoAsync(string loginAdministrador) => Task.CompletedTask;
    public Task<string?> SolicitarSenhaAsync(string titulo, string mensagem, string textoSim = "Confirmar", string textoNao = "Cancelar") => Task.FromResult<string?>(null);
    public Application.Cadastros.ModulosEmpresa ModuloAtual => Application.Cadastros.ModulosEmpresa.Contabil;
    public Task<Application.Cadastros.ModulosEmpresa?> EscolherModulosAsync(Application.Cadastros.ModulosEmpresa inicial) => Task.FromResult<Application.Cadastros.ModulosEmpresa?>(inicial);
    public string DescricaoEmpresa => "Empresa de teste";
    public void AbrirTela(string tela) => Abertas.Add(tela);
}

/// <summary>ViewModel do assistente da ECD (passos, pré-requisitos, assinaturas e salvamento).</summary>
public class EcdAssistenteTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private async Task<(EcdAssistenteViewModel Vm, ContextoDeTeste Ctx)> CriarAsync(string cnpj, bool pode = true)
    {
        BancoDeTeste.ExigirBanco();
        var empresa = await db.CriarEmpresaAsync(cnpj, 2025);
        var adm = await db.AdminAsync();
        var ctx = new ContextoDeTeste(db.App, new SessaoUsuario(adm, "admin_teste", "Admin de Teste", true, false), empresa, pode);
        var vm = new EcdAssistenteViewModel(ctx) { Ano = 2025 };
        await vm.CarregarAsync();
        return (vm, ctx);
    }

    [Fact]
    public async Task Passos_navegam_ate_a_validacao_e_nao_avancam_com_pendencias_bloqueantes()
    {
        var (vm, ctx) = await CriarAsync("11222333000181");
        Assert.Equal(1, vm.Passo);
        Assert.False(vm.PodeVoltar);
        Assert.Equal("1", vm.NumeroLivroSugerido);
        Assert.Contains("Passo 1 de 5", vm.TituloPasso);
        Assert.Contains("[1. Empresa e exercício]", vm.IndicadorPasso);

        await vm.AvancarCommand.ExecuteAsync(null);
        Assert.Empty(vm.Erros);
        Assert.Equal(2, vm.Passo);
        Assert.True(vm.PodeVoltar);
        var contador = vm.PreRequisitos.Single(p => p.Titulo.StartsWith("Contador"));
        Assert.False(contador.Ok);                                          // empresa de teste sem contador
        Assert.Equal(Telas.Contabilidade, contador.Tela);
        Assert.False(vm.PreRequisitos.Single(p => p.Titulo.StartsWith("Exercício encerrado")).Ok);
        vm.AbrirAtalhoCommand.Execute(contador.Tela);
        Assert.Equal([Telas.Contabilidade], ctx.Abertas);

        await vm.AvancarCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.Passo);
        Assert.Contains(vm.Pendencias, p => p.Bloqueante);
        Assert.False(vm.PodeAvancarValidacao);

        await vm.AvancarCommand.ExecuteAsync(null);                          // bloqueado
        Assert.Equal(3, vm.Passo);
        Assert.Contains(vm.Erros, e => e.Contains("bloqueantes"));

        vm.VoltarCommand.Execute(null);
        Assert.Equal(2, vm.Passo);
    }

    [Fact]
    public async Task Sem_permissao_o_assistente_nao_avanca()
    {
        var (vm, _) = await CriarAsync("33000167000101", pode: false);
        await vm.AvancarCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.Passo);
        Assert.NotEmpty(vm.Erros);
    }

    [Fact]
    public void Opcoes_refletem_as_escolhas_do_passo_1()
    {
        var vm = new EcdAssistenteViewModel(new ContextoDeTeste(null, null, null))
        {
            NumeroLivro = 3, NivelAglutinacao = 4, Mutacoes = EcdAssistenteViewModel.OpcaoDlpa, ResponsavelPessoaJuridica = true, CertidaoCrc = " sp/2026/123 ",
        };
        var o = vm.Opcoes();
        Assert.Equal((3, (short)4, TipoMutacoesEcd.Dlpa, true, "sp/2026/123"),
                     (o.NumeroOrdem, o.NivelAglutinacao, o.Mutacoes, o.ResponsavelPessoaJuridica, o.NumeroCertidaoCrc));
    }

    [Fact]
    public void Signatario_valida_documento_e_modo_de_assinatura()
    {
        var ok = new SignatarioAssinatura(new SignatarioEcd("Maria", "52998224725", "Contador", "900", false));
        var ruim = new SignatarioAssinatura(new SignatarioEcd("João", "11111111111", "Administrador", "205", true));
        Assert.True(ok.DocumentoValido);
        Assert.False(ruim.DocumentoValido);
        Assert.Equal("529.982.247-25", ok.Documento);
        Assert.Equal(ModoAssinatura.Nenhum, ok.Modo);
        ok.ModoIndice = 1;
        Assert.True(ok.PedeArquivo && ok.PedeSenha && !ok.PedeInstalado);
        ok.ModoIndice = 2;
        Assert.True(ok.PedeInstalado && !ok.PedeSenha);
        ok.ModoIndice = 3;
        Assert.True(ok.PedeCadastrado && ok.PedeSenha);
    }

    [Fact]
    public async Task Repositorio_windows_so_no_windows_e_a1_exige_arquivo()
    {
        var (vm, _) = await CriarAsync("06990590000123");
        Assert.Equal(OperatingSystem.IsWindows(), vm.RepositorioWindowsDisponivel);
        if (!OperatingSystem.IsWindows()) Assert.Contains("só estão disponíveis no Windows", vm.AvisoRepositorio);
        vm.Signatarios.Add(new SignatarioAssinatura(new SignatarioEcd("Maria", "52998224725", "Contador", "900", false)) { Modo = ModoAssinatura.ArquivoA1 });
        vm.Passo = 4;
        await vm.AvancarCommand.ExecuteAsync(null);
        Assert.Equal(4, vm.Passo);
        Assert.Contains(vm.Erros, e => e.Contains("arquivo .pfx"));
    }

    [Fact]
    public void Salvar_grava_o_arquivo_na_pasta_escolhida()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "vorticis_ecd_" + Guid.NewGuid().ToString("N"));
        try
        {
            var vm = new EcdAssistenteViewModel(new ContextoDeTeste(null, null, null)) { Pasta = pasta };
            vm.SalvarCommand.Execute(null);
            Assert.NotEmpty(vm.Erros);                                       // nada gerado ainda
            vm.Erros.Clear();
            vm.Arquivo = new ArquivoEcd("ECD_teste.txt", [1, 2, 3], "ABC", 3, new Dictionary<string, int>(), [], []);
            vm.SalvarCommand.Execute(null);
            Assert.Empty(vm.Erros);
            Assert.Equal(Path.Combine(pasta, "ECD_teste.txt"), vm.CaminhoSalvo);
            Assert.Equal([1, 2, 3], File.ReadAllBytes(vm.CaminhoSalvo));
            Assert.True(vm.Salvo);
        }
        finally { if (Directory.Exists(pasta)) Directory.Delete(pasta, true); }
    }

    [Fact]
    public void Menu_exportar_tem_ecd_e_ecf_e_nao_ha_menu_auditoria()
    {
        var menus = new MainViewModel().Menus;
        Assert.DoesNotContain(menus, m => m.Titulo == "_Auditoria");
        var exportar = menus.Single(m => m.Titulo == "_Integrações").Filhos!.Single(i => i.Titulo == "_Exportar");
        Assert.Contains(exportar.Filhos!, i => i.Chave == Telas.EcdAssistente && i.Titulo == "Gerar ECD (SPED Contábil)" && i.Habilitado);
        Assert.Contains(exportar.Filhos!, i => i.Chave == Telas.EcfAssistente && i.Titulo == "Gerar ECF (SPED)" && i.Habilitado);
    }
}
