using Vorticis.Application.Sped;
using Vorticis.Desktop.ViewModels;
using Vorticis.Domain.Seguranca;
using Vorticis.Domain.Sped;
using Vorticis.Infra.Tests;

namespace Vorticis.Desktop.Tests;

/// <summary>ViewModel do assistente da ECF (passos, pré-requisitos, assinaturas e salvamento).</summary>
public class EcfAssistenteTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private async Task<(EcfAssistenteViewModel Vm, ContextoDeTeste Ctx)> CriarAsync(string cnpj, bool pode = true)
    {
        BancoDeTeste.ExigirBanco();
        var empresa = await db.CriarEmpresaAsync(cnpj, 2025);
        var adm = await db.AdminAsync();
        var ctx = new ContextoDeTeste(db.App, new SessaoUsuario(adm, "admin_teste", "Admin de Teste", true, false), empresa, pode);
        var vm = new EcfAssistenteViewModel(ctx) { Ano = 2025 };
        await vm.CarregarAsync();
        return (vm, ctx);
    }

    [Fact]
    public async Task Passos_navegam_ate_a_validacao_e_nao_avancam_com_pendencias_bloqueantes()
    {
        var (vm, ctx) = await CriarAsync("34028316000103");
        Assert.Equal(1, vm.Passo);
        Assert.False(vm.PodeVoltar);
        Assert.Contains("Passo 1 de 5", vm.TituloPasso);
        Assert.Equal("Empresa de Teste LTDA", vm.EmpresaNome);

        await vm.AvancarCommand.ExecuteAsync(null);
        Assert.Empty(vm.Erros);
        Assert.Equal(2, vm.Passo);
        var apuracoes = vm.PreRequisitos.First();
        Assert.False(apuracoes.Ok);                                          // sem apurações do módulo Fiscal
        Assert.False(apuracoes.TemAtalho);                                   // o Fiscal é outro aplicativo: sem atalho
        var contador = vm.PreRequisitos.Single(p => p.Titulo.StartsWith("Contador"));
        Assert.False(contador.Ok);
        Assert.Equal(Telas.Contabilidade, contador.Tela);
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
    public async Task Simples_nacional_mostra_a_mensagem_de_que_nao_entrega_ecf()
    {
        var (vm, ctx) = await CriarAsync("00360305000104");
        await db.EscalarAsync<int>("UPDATE core.empresa_regime SET regime = 'SIMPLES_NACIONAL' WHERE empresa_id = @e RETURNING 1", ("e", ctx.EmpresaId!.Value));
        await vm.AvancarCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.Passo);
        Assert.Contains(vm.Erros, e => e.Contains("Simples Nacional não entregam a ECF"));
    }

    [Fact]
    public async Task Sem_permissao_o_assistente_nao_avanca()
    {
        var (vm, _) = await CriarAsync("90400888000142", pode: false);
        await vm.AvancarCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.Passo);
        Assert.NotEmpty(vm.Erros);
    }

    [Fact]
    public async Task Retificadora_exige_o_recibo_e_opcoes_refletem_o_passo_1()
    {
        var (vm, _) = await CriarAsync("60746948000112");
        vm.Retificadora = true;
        await vm.AvancarCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.Passo);
        Assert.Contains(vm.Erros, e => e.Contains("recibo"));
        vm.NumeroRecibo = " 0123456789 ";
        vm.InformarEcd = true;
        vm.ResponsavelPessoaJuridica = true;
        var o = vm.Opcoes();
        Assert.Equal((true, "0123456789", true, true), (o.Retificadora, o.NumeroRecibo, o.InformarEcd, o.ResponsavelPessoaJuridica));
    }

    [Fact]
    public void Assinar_como_procurador_vai_nas_opcoes_e_avisa_do_ecac()
    {
        var vm = new EcfAssistenteViewModel(new ContextoDeTeste(null, null, null));
        Assert.False(vm.TemAvisoProcuracao);
        Assert.Equal("", vm.AvisoProcuracao);
        Assert.False(vm.Opcoes().AssinarComoProcurador);
        vm.AssinarComoProcurador = true;
        Assert.True(vm.Opcoes().AssinarComoProcurador);
        Assert.True(vm.TemAvisoProcuracao);
        Assert.Contains("e-CAC", vm.AvisoProcuracao);
    }

    [Fact]
    public async Task Contador_procurador_cadastra_pelo_viewmodel_e_o_assistente_ecd_tambem_avisa()
    {
        BancoDeTeste.ExigirBanco();
        var empresa = await db.CriarEmpresaAsync("56007569000133", 2025);
        var adm = await db.AdminAsync();
        var ctx = new ContextoDeTeste(db.App, new SessaoUsuario(adm, "admin_teste", "Admin de Teste", true, false), empresa);
        var vm = new Vorticis.Desktop.Comum.ViewModels.ContabilidadeEmpresaViewModel(ctx);
        await vm.CarregarAsync();
        Assert.Empty(vm.Vinculos);
        vm.IncluirCommand.Execute(null);
        (vm.ContadorNome, vm.ContadorCpf, vm.ContadorEmail, vm.ContadorResponsavel, vm.ContadorAssinaEcdEcf) =
            ("Pedro Procurador", "11144477735", "pedro@exemplo.com.br", false, false);
        (vm.ProcuracaoNova.Procurador, vm.ProcuracaoNova.Inicio, vm.ProcuracaoNova.Efd) = (true, new DateTime(2025, 1, 1), true);
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.Empty(vm.Erros);
        var linha = Assert.Single(vm.Vinculos);
        Assert.Equal("111.444.777-35", linha.Documento);
        Assert.Equal("✓ procurador (ECD, ECF, EFD, eSocial) 01/01/2025 a sem fim", linha.Procuracao);

        var ecd = new EcdAssistenteViewModel(ctx);
        Assert.Equal("", ecd.AvisoProcuracao);
        ecd.AssinarComoProcurador = true;
        Assert.True(ecd.Opcoes().AssinarComoProcurador);
        Assert.Contains("ECD", ecd.AvisoProcuracao);
    }

    [Fact]
    public async Task A1_exige_arquivo_no_passo_de_assinaturas()
    {
        var (vm, _) = await CriarAsync("33000167000101");
        vm.Signatarios.Add(new SignatarioAssinatura(new SignatarioEcd("Maria", "52998224725", "Contador", "900", false)) { Modo = ModoAssinatura.ArquivoA1 });
        vm.Passo = 4;
        await vm.AvancarCommand.ExecuteAsync(null);
        Assert.Equal(4, vm.Passo);
        Assert.Contains(vm.Erros, e => e.Contains("arquivo .pfx"));
    }

    [Fact]
    public void Salvar_grava_o_arquivo_na_pasta_escolhida()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "vorticis_ecf_" + Guid.NewGuid().ToString("N"));
        try
        {
            var vm = new EcfAssistenteViewModel(new ContextoDeTeste(null, null, null)) { Pasta = pasta };
            vm.SalvarCommand.Execute(null);
            Assert.NotEmpty(vm.Erros);
            vm.Erros.Clear();
            vm.Arquivo = new ArquivoEcf("ECF_teste.txt", [1, 2, 3], "ABC", 3, new Dictionary<string, int>(), [], [], FormaTributacaoEcf.Real);
            vm.SalvarCommand.Execute(null);
            Assert.Empty(vm.Erros);
            Assert.Equal([1, 2, 3], File.ReadAllBytes(vm.CaminhoSalvo));
            Assert.True(vm.Salvo);
        }
        finally { if (Directory.Exists(pasta)) Directory.Delete(pasta, true); }
    }
}
