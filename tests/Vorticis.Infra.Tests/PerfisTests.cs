using Vorticis.Application.Contabil;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Infra.Tests;

public class PerfisTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static readonly SessaoUsuario Admin = new(0, "teste", "Teste", true, false);

    [Fact]
    public async Task Configuracao_inicial_dos_perfis_e_obrigatoria_e_concluida_so_pelo_administrador()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var perfis = db.App.Perfis;
        // banco novo (sem empresas e sem outros usuários): pendente
        var adm = await db.AdminAsync();
        Assert.True(await perfis.ConfiguracaoInicialPendenteAsync(ct));
        Assert.False((await perfis.ConcluirConfiguracaoInicialAsync(new SessaoUsuario(adm, "x", "X", false, false), ct)).Sucesso);
        Assert.True(await perfis.ConfiguracaoInicialPendenteAsync(ct));

        var r = await perfis.ConcluirConfiguracaoInicialAsync(new SessaoUsuario(adm, "admin_teste", "Admin", true, false), ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        Assert.False(await perfis.ConfiguracaoInicialPendenteAsync(ct));
        Assert.Equal(adm, await db.EscalarAsync<long>("SELECT alterado_por FROM core.configuracao_sistema WHERE chave = 'perfis_configurados'"));
    }

    [Fact]
    public async Task Perfil_personalizado_libera_exatamente_as_funcoes_marcadas_e_os_servicos_respeitam()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var app = db.App;
        var empresa = await db.CriarEmpresaAsync("11222333000181");
        var adm = await db.AdminAsync();
        Assert.True((await app.Lancamentos.EfetivarAsync(new LancamentoProposto(empresa, new(2026, 5, 2), OrigemLancamento.Manual,
            [new("1.1.01.001", IndicadorDC.D, 10m, "x"), new("3.1.01.001", IndicadorDC.C, 10m, "x")]), adm, ct)).Sucesso);

        // Perfil "Auditor": só o Razão
        var perfil = await app.Perfis.SalvarAsync(Admin, null, "Auditor externo", "Somente Razão", [Permissao.RelatorioRazao], ct);
        Assert.True(perfil.Sucesso, string.Join("; ", perfil.Erros));
        var usuario = (await app.Usuarios.CriarUsuarioAsync(Admin, "auditor", "Auditor", "Inicial123", perfil.Valor, ct)).Valor;
        Assert.True((await app.Usuarios.DefinirAcessoAsync(Admin, usuario, empresa, perfil.Valor, ct)).Sucesso);

        var razao = await app.Relatorios.RazaoAsync(empresa, "1.1.01.001", new(2026, 1, 1), new(2026, 12, 31), usuario, ct);
        Assert.True(razao.Sucesso);
        var balancete = await app.Relatorios.BalanceteAsync(empresa, new(2026, 1, 1), new(2026, 12, 31), usuario, ct: ct);
        Assert.Equal(["Seu perfil (Auditor externo) não permite: Balancete de verificação."], balancete.Erros);
        var lanc = await app.Lancamentos.EfetivarAsync(new LancamentoProposto(empresa, new(2026, 5, 3), OrigemLancamento.Manual,
            [new("1.1.01.001", IndicadorDC.D, 1m, "x"), new("3.1.01.001", IndicadorDC.C, 1m, "x")]), usuario, ct);
        Assert.Contains("não permite: Incluir lançamentos", lanc.Erros[0]);
        Assert.Contains("não permite", (await app.Periodos.FecharAsync(empresa, new(2026, 1, 1), usuario, ct)).Erros[0]);

        // Administrador edita o perfil: passa a incluir lançamentos
        Assert.True((await app.Perfis.SalvarAsync(Admin, perfil.Valor, "Auditor externo", null,
            [Permissao.RelatorioRazao, Permissao.LancamentoIncluir], ct)).Sucesso);
        lanc = await app.Lancamentos.EfetivarAsync(new LancamentoProposto(empresa, new(2026, 5, 3), OrigemLancamento.Manual,
            [new("1.1.01.001", IndicadorDC.D, 1m, "x"), new("3.1.01.001", IndicadorDC.C, 1m, "x")]), usuario, ct);
        Assert.True(lanc.Sucesso, string.Join("; ", lanc.Erros));
        // ...mas estornar continua proibido
        Assert.Contains("Estornar", (await app.Lancamentos.EstornarAsync(empresa, lanc.Valor.Id, new(2026, 5, 4), "teste", usuario, ct)).Erros[0]);

        // Empresa nova: o usuário recebe automaticamente o seu perfil; o administrador pode retirar o acesso
        var outra = await db.CriarEmpresaAsync("06990590000123");
        Assert.Contains((await app.Usuarios.ListarAcessosAsync(usuario, ct)), a => a.EmpresaId == outra && a.PerfilId == perfil.Valor);
        Assert.True((await app.Usuarios.DefinirAcessoAsync(Admin, usuario, outra, null, ct)).Sucesso);
        Assert.Equal(["Você não tem acesso a esta empresa."],
            (await app.Relatorios.RazaoAsync(outra, "1.1.01.001", new(2026, 1, 1), new(2026, 12, 31), usuario, ct)).Erros);

        // Usuário inativo perde tudo, mesmo com perfil
        Assert.True((await app.Usuarios.AlterarUsuarioAsync(Admin, usuario, "Auditor", perfil.Valor, false, ct)).Sucesso);
        Assert.False((await app.Relatorios.RazaoAsync(empresa, "1.1.01.001", new(2026, 1, 1), new(2026, 12, 31), usuario, ct)).Sucesso);

        // Exclusão: perfil em uso e perfil padrão são protegidos
        Assert.Contains("atribuído a usuários", (await app.Perfis.ExcluirAsync(Admin, perfil.Valor, ct)).Erros[0]);
        var contador = (await app.Perfis.ListarAsync(Admin, ct)).Single(p => p.Nome == "Contador");
        Assert.Contains("padrão do sistema", (await app.Perfis.ExcluirAsync(Admin, contador.Id, ct)).Erros[0]);
        await app.Usuarios.DefinirAcessoAsync(Admin, usuario, empresa, null, ct);
        Assert.Contains("atribuído a usuários", (await app.Perfis.ExcluirAsync(Admin, perfil.Valor, ct)).Erros[0]);   // ainda é o perfil do usuário
        Assert.True((await app.Usuarios.AlterarUsuarioAsync(Admin, usuario, "Auditor", await db.PerfilIdAsync("Consulta"), false, ct)).Sucesso);
        Assert.True((await app.Perfis.ExcluirAsync(Admin, perfil.Valor, ct)).Sucesso);

        // Perfil Administrador: todas as funções, sempre (mesmo que se tente desmarcar) e não pode ser excluído
        var adminPerfil = (await app.Perfis.ListarAsync(Admin, ct)).Single(p => p.Administrador);
        Assert.Equal("Administrador", adminPerfil.Nome);
        Assert.Equal(Permissoes.Catalogo.Count, adminPerfil.Permissoes.Count);
        Assert.True((await app.Perfis.SalvarAsync(Admin, adminPerfil.Id, "Administrador", null, [Permissao.RelatorioRazao], ct)).Sucesso);
        Assert.Equal(Permissoes.Catalogo.Count, (await app.Perfis.ListarAsync(Admin, ct)).Single(p => p.Administrador).Permissoes.Count);
        Assert.Contains("padrão do sistema", (await app.Perfis.ExcluirAsync(Admin, adminPerfil.Id, ct)).Erros[0]);

        // Usuário com perfil Administrador é administrador do sistema
        var chefe = (await app.Usuarios.CriarUsuarioAsync(Admin, "chefe2", "Chefe", "Inicial123", adminPerfil.Id, ct)).Valor;
        Assert.True((await app.Usuarios.ListarAsync(Admin, ct)).Single(u => u.Id == chefe).Administrador);
    }

    [Fact]
    public async Task Validacoes_e_restricao_ao_administrador()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var comum = new SessaoUsuario(1, "x", "X", false, false);
        Assert.Contains("Somente administradores", (await db.App.Perfis.SalvarAsync(comum, null, "Hack", null, [Permissao.LancamentoIncluir], ct)).Erros[0]);
        Assert.Empty(await db.App.Perfis.ListarAsync(comum, ct));
        Assert.Contains((await db.App.Perfis.SalvarAsync(Admin, null, "Vazio", null, [], ct)).Erros, e => e.Contains("ao menos uma função"));
        Assert.Contains((await db.App.Perfis.SalvarAsync(Admin, null, "CONSULTA", null, [Permissao.RelatorioDiario], ct)).Erros, e => e.Contains("Já existe"));
        Assert.Equal(Permissoes.Catalogo.Count, (await db.App.Perfis.ListarAsync(Admin, ct)).Single(p => p.Nome == "Contador").Permissoes.Count);
    }
}
