using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Vorticis.Application.Relatorios;
using Vorticis.Application.Sistema;
using Vorticis.Desktop.ViewModels;
using Vorticis.Domain.Seguranca;
using Vorticis.Domain.Sped;
using Vorticis.Infra.Tests;

namespace Vorticis.Desktop.Tests;

/// <summary>Senha local protegida, certificado padrão no assistente da ECD e no PDF, e a tela de certificados.</summary>
public class CertificadosPadraoDesktopTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static byte[] Pfx(string cn, string senha)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={cn}, O=ICP-Brasil, C=BR", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var c = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddYears(1));
        return c.Export(X509ContentType.Pfx, senha);
    }

    /// <summary>Troca a proteção do Windows por uma reversível de teste e usa um arquivo temporário.</summary>
    private sealed class SenhasDeTeste : IDisposable
    {
        private readonly string _arquivo = Path.Combine(Path.GetTempPath(), "senhas_" + Guid.NewGuid().ToString("N") + ".json");
        private readonly (string, Func<byte[], byte[]>?, Func<byte[], byte[]>?) _original =
            (SenhasCertificados.Arquivo, SenhasCertificados.Proteger, SenhasCertificados.Abrir);

        public SenhasDeTeste()
        {
            SenhasCertificados.Arquivo = _arquivo;
            SenhasCertificados.Proteger = b => b.Select(x => (byte)(x ^ 0x5A)).ToArray();
            SenhasCertificados.Abrir = b => b.Select(x => (byte)(x ^ 0x5A)).ToArray();
        }

        public void Dispose()
        {
            (SenhasCertificados.Arquivo, SenhasCertificados.Proteger, SenhasCertificados.Abrir) = _original;
            if (File.Exists(_arquivo)) File.Delete(_arquivo);
        }
    }

    [Fact]
    public void Senha_do_certificado_fica_protegida_no_arquivo_local_e_pode_ser_esquecida()
    {
        using var _ = new SenhasDeTeste();
        Assert.True(SenhasCertificados.Disponivel);
        Assert.True(SenhasCertificados.Guardar(7, "segredo-123"));
        Assert.DoesNotContain("segredo-123", File.ReadAllText(SenhasCertificados.Arquivo));
        Assert.Equal("segredo-123", SenhasCertificados.Obter(7));
        Assert.Null(SenhasCertificados.Obter(8));
        SenhasCertificados.Esquecer(7);
        Assert.Null(SenhasCertificados.Obter(7));
    }

    [Fact]
    public void Sem_protecao_do_sistema_a_senha_nao_e_guardada()
    {
        using var _ = new SenhasDeTeste();
        SenhasCertificados.Proteger = null;
        Assert.False(SenhasCertificados.Guardar(1, "x"));
    }

    [Fact]
    public async Task Tela_de_certificados_define_padrao_confere_senha_e_guarda_localmente()
    {
        BancoDeTeste.ExigirBanco();
        using var _ = new SenhasDeTeste();
        var ct = TestContext.Current.CancellationToken;
        var admin = new SessaoUsuario(await db.AdminAsync(), "admin_teste", "Admin", true, false);
        var ctx = new ContextoDeTeste(db.App, admin, null);
        var id = (await db.App.Certificados.ImportarA1Async(admin, Pfx("CONTADOR TELA:52998224725", "s3nha"), "s3nha", "Contador da tela", null, ct)).Valor;

        var vm = new CertificadosViewModel(ctx);
        await vm.CarregarAsync();
        vm.Selecionado = vm.Lista.Single(l => l.C.Id == id);
        vm.PadraoContadorCommand.Execute(null);
        await Task.Delay(300, ct);
        Assert.Contains(vm.Erros, e => e.Contains("senha"));                    // A1 exige a senha para ser padrão

        vm.SenhaPadrao = "errada";
        await vm.PadraoContadorCommand.ExecuteAsync(null);
        Assert.NotEmpty(vm.Erros);
        vm.SenhaPadrao = "s3nha";
        await vm.PadraoContadorCommand.ExecuteAsync(null);
        Assert.Empty(vm.Erros);
        Assert.Equal("Contador", vm.Lista.Single(l => l.C.Id == id).Padrao);
        Assert.Equal("s3nha", SenhasCertificados.Obter(id));

        // O certificado padrão abre sem perguntar a senha de novo
        var aberto = await CertificadosPadrao.AbrirAsync(ctx, PapelAssinatura.Contador);
        Assert.True(aberto.Sucesso, string.Join("; ", aberto.Erros));
        Assert.True(aberto.Valor.HasPrivateKey);
        var pdf = await CertificadosPadrao.ParaPdfAsync(ctx, "teste");
        Assert.True(pdf.Sucesso);

        await vm.RetirarPadraoCommand.ExecuteAsync(null);
        Assert.Equal("", vm.Lista.Single(l => l.C.Id == id).Padrao);
        Assert.Null(SenhasCertificados.Obter(id));
        Assert.False((await CertificadosPadrao.ParaPdfAsync(ctx, "teste")).Sucesso);
        await db.App.Certificados.ExcluirAsync(admin, id, ct);
    }

    [Fact]
    public async Task Assistente_da_ecd_ja_escolhe_o_certificado_padrao_de_cada_signatario()
    {
        BancoDeTeste.ExigirBanco();
        using var _ = new SenhasDeTeste();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync("33000167000101", 2025);
        var admin = new SessaoUsuario(await db.AdminAsync(), "admin_teste", "Admin", true, false);
        var cert = (await db.App.Certificados.ImportarA1Async(admin, Pfx("CONTADOR ECD:52998224725", "pw"), "pw", "Contador ECD", null, ct)).Valor;
        Assert.True((await db.App.Certificados.DefinirPadraoAsync(admin, cert, PapelAssinatura.Contador, true, ct)).Sucesso);
        SenhasCertificados.Guardar(cert, "pw");

        var vm = new EcdAssistenteViewModel(new ContextoDeTeste(db.App, admin, empresa));
        var contador = new SignatarioAssinatura(new SignatarioEcd("Maria", "52998224725", "Contador", "900", false));
        var responsavel = new SignatarioAssinatura(new SignatarioEcd("João", "39053344705", "Administrador", "205", true));
        vm.Signatarios.Add(contador);
        vm.Signatarios.Add(responsavel);
        await vm.CarregarCertificadosAsync();

        Assert.Equal((ModoAssinatura.Cadastrado, cert, "pw"), (contador.Modo, contador.Cadastrado?.Id, contador.Senha));
        Assert.Equal(ModoAssinatura.Nenhum, responsavel.Modo);                  // sem padrão do responsável: continua "assinar depois"
        Assert.Empty(vm.Instalados);                                           // A3 só lista A3 (nada fora do Windows)
        await db.App.Certificados.ExcluirAsync(admin, cert, ct);
    }

    [Fact]
    public void Menu_lancamentos_tem_os_submenus_contabeis_conciliacoes_e_fechamentos()
    {
        var lanc = new MainViewModel().Menus.Single(m => m.Titulo == "_Lançamentos").Filhos!;
        Assert.Equal(["_Contábeis", "C_onciliações", "_Fechamentos"], lanc.Select(i => i.Titulo));
        Assert.Contains(lanc[0].Filhos!, i => i.Chave == Telas.Lancamento);
        Assert.Contains(lanc[0].Filhos!, i => i.Chave == Telas.SaldosIniciais);
        Assert.Contains(lanc[1].Filhos!, i => i.Chave == Telas.Conciliacao);
        Assert.Equal([Telas.Periodos, Telas.Encerramento, Telas.ReorganizarSaldos], lanc[2].Filhos!.Where(i => !i.EhSeparador).Select(i => i.Chave));
        Assert.Contains(lanc[0].Filhos!, i => i.Chave == Telas.ReorganizarLancamentos);
    }

    [Fact]
    public void Relatorio_oferece_assinar_digitalmente_o_pdf()
    {
        var vm = new LivrosPdfViewModel(new ContextoDeTeste(null, null, null));
        Assert.False(vm.AssinarPdfDigitalmente);
        vm.AssinarPdfDigitalmente = true;
        Assert.True(vm.AssinarPdfDigitalmente);
    }
}
