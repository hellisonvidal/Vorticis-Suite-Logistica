using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Vorticis.Application.Cadastros;
using Vorticis.Application.Sistema;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Infra.Tests;

/// <summary>Uso dos certificados cadastrados para assinar a ECD/ECF por quem não é administrador (perfil Contador) e certificado padrão do procurador.</summary>
public class CertificadosAssinaturaTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static byte[] Pfx(string cn)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={cn}, O=ICP-Brasil, C=BR", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return cert.Export(X509ContentType.Pfx, "senha1");
    }

    private async Task<SessaoUsuario> UsuarioAsync(string login, string perfil, params long[] empresas)
    {
        var id = await db.EscalarAsync<long>(
            "INSERT INTO core.usuario (login, nome, senha_hash, perfil_id) VALUES (@l, @l, 'x', (SELECT id FROM core.perfil WHERE nome = @p)) RETURNING id", ("l", login), ("p", perfil));
        foreach (var e in empresas)
            await db.EscalarAsync<int>(
                "INSERT INTO core.usuario_empresa (usuario_id, empresa_id, perfil_id) VALUES (@u, @e, (SELECT id FROM core.perfil WHERE nome = @p)) ON CONFLICT (usuario_id, empresa_id) DO UPDATE SET perfil_id = EXCLUDED.perfil_id RETURNING 1",
                ("u", id), ("e", e), ("p", perfil));
        return new SessaoUsuario(id, login, login, false, false);
    }

    [Fact]
    public async Task Contador_usa_os_certificados_da_empresa_para_assinar_mas_nao_administra_o_repositorio()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var svc = db.App.Certificados;
        var admin = new SessaoUsuario(await db.AdminAsync(), "admin_teste", "Admin", true, false);
        var empresa = await db.CriarEmpresaAsync("49123456000138", 2025);
        var outra = await db.CriarEmpresaAsync("17159229000176", 2025);
        long Id(string cn, long? emp) => svc.ImportarA1Async(admin, Pfx(cn), "senha1", cn, emp, ct).GetAwaiter().GetResult().Valor;
        var daEmpresa = Id("CONTADOR DA EMPRESA:52998224725", empresa);
        var geral = Id("PROCURADOR GERAL:11144477735", null);
        var deOutra = Id("OUTRA EMPRESA:39053344705", outra);
        Assert.True((await svc.DefinirPadraoAsync(admin, daEmpresa, PapelAssinatura.Contador, true, ct)).Sucesso);
        Assert.True((await svc.DefinirPadraoAsync(admin, geral, PapelAssinatura.Procurador, true, ct)).Sucesso);

        var contador = await UsuarioAsync("contador_cert", "Contador", empresa);
        var consulta = await UsuarioAsync("consulta_cert", "Consulta", empresa, outra);

        // O que continua exclusivo do administrador
        Assert.False((await svc.ListarAsync(contador, ct)).Sucesso);
        Assert.False((await svc.ImportarA1Async(contador, Pfx("X:52998224725"), "senha1", null, empresa, ct)).Sucesso);
        Assert.False((await svc.ExcluirAsync(contador, daEmpresa, ct)).Sucesso);
        Assert.False((await svc.DefinirPadraoAsync(contador, daEmpresa, PapelAssinatura.Contador, true, ct)).Sucesso);
        Assert.False((await svc.ObterPadraoAsync(contador, PapelAssinatura.Contador, empresa, ct)).Sucesso);
        Assert.False((await svc.AbrirA1Async(contador, daEmpresa, "senha1", ct)).Sucesso);

        // O que o Contador pode para assinar a ECD/ECF: listar (da empresa e gerais), usar o padrão e abrir o A1 com a senha
        var lista = await svc.ListarParaAssinaturaAsync(contador, empresa, ct);
        Assert.True(lista.Sucesso, string.Join("; ", lista.Erros));
        Assert.Equal([daEmpresa, geral], lista.Valor.Select(c => c.Id).Order());
        Assert.DoesNotContain(lista.Valor, c => c.Id == deOutra);
        Assert.Equal(daEmpresa, (await svc.ObterPadraoParaAssinaturaAsync(contador, PapelAssinatura.Contador, empresa, ct)).Valor!.Id);
        Assert.Equal(geral, (await svc.ObterPadraoParaAssinaturaAsync(contador, PapelAssinatura.Procurador, empresa, ct)).Valor!.Id);
        Assert.Null((await svc.ObterPadraoParaAssinaturaAsync(contador, PapelAssinatura.Responsavel, empresa, ct)).Valor);
        using (var aberto = (await svc.AbrirA1ParaAssinaturaAsync(contador, empresa, daEmpresa, "senha1", ct)).Valor) Assert.True(aberto.HasPrivateKey);
        Assert.False((await svc.AbrirA1ParaAssinaturaAsync(contador, empresa, daEmpresa, "errada", ct)).Sucesso);
        Assert.False((await svc.AbrirA1ParaAssinaturaAsync(contador, empresa, deOutra, "senha1", ct)).Sucesso);          // certificado de outra empresa

        // Perfil sem a função de gerar ECD/ECF não usa os certificados
        Assert.False((await svc.ListarParaAssinaturaAsync(consulta, empresa, ct)).Sucesso);
        Assert.False((await svc.AbrirA1ParaAssinaturaAsync(consulta, empresa, daEmpresa, "senha1", ct)).Sucesso);
        Assert.False((await svc.ObterPadraoParaAssinaturaAsync(consulta, PapelAssinatura.Contador, empresa, ct)).Sucesso);

        // O administrador também usa pelos mesmos métodos; um só padrão do procurador por escopo
        Assert.True((await svc.ListarParaAssinaturaAsync(admin, empresa, ct)).Sucesso);
        var outroGeral = Id("PROCURADOR NOVO:11144477735", null);
        Assert.True((await svc.DefinirPadraoAsync(admin, outroGeral, PapelAssinatura.Procurador, true, ct)).Sucesso);
        Assert.Equal(outroGeral, (await svc.ObterPadraoParaAssinaturaAsync(admin, PapelAssinatura.Procurador, empresa, ct)).Valor!.Id);
        Assert.False((await svc.ListarAsync(admin, ct)).Valor.Single(c => c.Id == geral).PadraoProcurador);
    }

    [Fact]
    public async Task Procuracao_no_contador_so_e_mantida_com_a_funcao_de_responsaveis_e_vigencia_e_abrangencia_definem_quem_vale()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("34238864000168", 2025);
        var consulta = await UsuarioAsync("consulta_proc", "Consulta", empresa);
        var resp = db.App.Responsaveis;
        var contador = new Contador(0, "  Pedro Procurador ", "111.444.777-35", "", "", " PEDRO@Exemplo.com.br ", "(11) 98888-7777", null, false);
        var proc = new Procuracao(true, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), ["ECD", "esocial"], "Procuração e-CAC 123");

        Assert.False((await resp.VincularContadorAsync(empresa, contador, new DateOnly(2025, 1, 1), false, consulta.Id, ct, proc)).Sucesso);
        // procurador sem CRC só se não for responsável técnico nem assinar como contador
        Assert.False((await resp.VincularContadorAsync(empresa, contador, new DateOnly(2025, 1, 1), true, adm, ct, proc)).Sucesso);
        Assert.False((await resp.VincularContadorAsync(empresa, contador, new DateOnly(2025, 1, 1), false, adm, ct)).Sucesso);   // sem procuração, o CRC é obrigatório
        Assert.False((await resp.VincularContadorAsync(empresa, contador, new DateOnly(2025, 1, 1), false, adm, ct, proc with { Fim = new DateOnly(2024, 1, 1) })).Sucesso);
        Assert.False((await resp.VincularContadorAsync(empresa, contador, new DateOnly(2025, 1, 1), false, adm, ct, proc with { Abrangencia = [] })).Sucesso);
        Assert.False((await resp.VincularContadorAsync(empresa, contador, new DateOnly(2025, 1, 1), false, adm, ct, proc with { Abrangencia = ["XYZ"] })).Sucesso);
        var id = (await resp.VincularContadorAsync(empresa, contador, new DateOnly(2025, 1, 1), false, adm, ct, proc)).Valor;

        var salvo = (await resp.ListarProcuradoresAsync(empresa, adm, ct)).Valor.Single();
        Assert.Equal(("Pedro Procurador", "11144477735", "pedro@exemplo.com.br", "Procuração e-CAC 123"), (salvo.Nome, salvo.Cpf, salvo.Email, salvo.Observacao));
        Assert.Equal(["ECD", "ESOCIAL"], salvo.Abrangencia.Order());
        Assert.Equal(id, (await resp.ProcuradorVigenteAsync(empresa, new DateOnly(2025, 6, 1), "ECD", ct))!.Id);
        Assert.Null(await resp.ProcuradorVigenteAsync(empresa, new DateOnly(2025, 6, 1), "ECF", ct));      // fora da abrangência
        Assert.Null(await resp.ProcuradorVigenteAsync(empresa, new DateOnly(2025, 6, 1), "EFD", ct));
        Assert.Null(await resp.ProcuradorVigenteAsync(empresa, new DateOnly(2026, 1, 1), "ECD", ct));      // vencida
        Assert.Null(await resp.ProcuradorVigenteAsync(empresa, new DateOnly(2024, 12, 31), "ECD", ct));    // ainda não vigente
        Assert.Equal(id, (await resp.UltimoProcuradorAsync(empresa, ct))!.Id);
        // o vínculo mostra a procuração e o contador sem CRC não assina como contador
        Assert.True((await resp.ListarVinculosAsync(empresa, adm, ct)).Valor.Single().EhProcurador);
        Assert.False((await resp.DefinirResponsavelAsync(empresa, id, adm, ct)).Sucesso);
        Assert.False((await resp.DefinirAssinaEcdEcfAsync(empresa, id, true, adm, ct)).Sucesso);

        // Nova procuração (prazo indeterminado) a partir de 2026 para outra pessoa: a vigente passa a ser a mais recente
        var novo = await db.App.CadastrarProcuradorAsync(empresa, adm, "Paula Procuradora", "39053344705", new DateOnly(2026, 1, 1), null, abrangencia: ["ECD"]);
        Assert.Equal(novo, (await resp.ProcuradorVigenteAsync(empresa, new DateOnly(2026, 6, 1), "ECD", ct))!.Id);

        // Desmarcar o procurador limpa a procuração (só o administrador/quem mantém responsáveis)
        Assert.False((await resp.DefinirProcuracaoAsync(empresa, id, Procuracao.Nenhuma, consulta.Id, ct)).Sucesso);
        Assert.True((await resp.DefinirProcuracaoAsync(empresa, id, Procuracao.Nenhuma, adm, ct)).Sucesso);
        Assert.Single((await resp.ListarProcuradoresAsync(empresa, adm, ct)).Valor);
        Assert.Null((await resp.ListarVinculosAsync(empresa, adm, ct)).Valor.Single(v => v.Id == id).Procuracao);
    }
}
