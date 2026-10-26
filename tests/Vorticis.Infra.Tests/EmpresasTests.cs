using Vorticis.Application.Cadastros;
using Vorticis.Application.Contabil;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Infra.Tests;

public class EmpresasTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static readonly SessaoUsuario Admin = new(0, "teste", "Teste", true, false);

    [Fact]
    public async Task Altera_cadastro_com_inscricao_estadual_e_exclui_tudo_do_banco()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var svc = db.App.Empresas;
        var id = await db.CriarEmpresaAsync("11444777000161");

        var atual = (await svc.ObterAsync(id, ct))!;
        Assert.Equal(RegimeTributario.LucroPresumido, atual.Regime);
        Assert.Null(atual.InscricaoEstadual);

        // município de outra UF é recusado
        var errada = new AlteracaoEmpresa(id, "Empresa Alterada LTDA", "Fantasia", "2062", "4751201", "SP", "3304557",
                                          new DateOnly(2020, 1, 1), "110.042.490.114");
        Assert.Contains("é de RJ", (await svc.AlterarAsync(errada, ct)).Erros[0]);
        Assert.Contains("2 a 14 dígitos", (await svc.AlterarAsync(errada with { CodMunicipioIbge = "3550308", InscricaoEstadual = "1" }, ct)).Erros[0]);

        var r = await svc.AlterarAsync(errada with { CodMunicipioIbge = "3550308", Complemento = new ComplementoEmpresa(Telefone: "1144445555") }, ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        var depois = (await svc.ObterAsync(id, ct))!;
        Assert.Equal(("Empresa Alterada LTDA", "110042490114", "1144445555"), (depois.RazaoSocial, depois.InscricaoEstadual, depois.Telefone));
        Assert.Equal("110042490114", await db.EscalarAsync<string>(
            "SELECT inscricao_estadual FROM core.estabelecimento WHERE empresa_id = @id AND matriz", ("id", id)));

        Assert.True((await svc.SalvarInscricaoEstadualAsync(id, "isento", ct)).Sucesso);
        Assert.Equal("ISENTO", (await svc.ObterAsync(id, ct))!.InscricaoEstadual);

        // Excluir: apaga a empresa e tudo dela (inclusive lançamentos efetivados); o CNPJ fica livre para novo cadastro
        var adm = await db.AdminAsync();
        var lanc = await db.App.Lancamentos.EfetivarAsync(new LancamentoProposto(id, new(2026, 5, 2), OrigemLancamento.Manual,
            [new("1.1.01.001", IndicadorDC.D, 10m, "x"), new("3.1.01.001", IndicadorDC.C, 10m, "x")]), adm, ct);
        Assert.True(lanc.Sucesso, string.Join("; ", lanc.Erros));
        // fora da exclusão da empresa, lançamento efetivado continua imutável
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            db.EscalarAsync<int>("WITH d AS (DELETE FROM contabil.lancamento WHERE empresa_id = @id RETURNING 1) SELECT count(*)::int FROM d", ("id", id)));

        var exclusao = await svc.ExcluirAsync(id, ct);
        Assert.True(exclusao.Sucesso, string.Join("; ", exclusao.Erros));
        Assert.Equal(1, exclusao.Valor);
        Assert.Null(await svc.ObterAsync(id, ct));
        Assert.DoesNotContain(await db.App.Usuarios.EmpresasPermitidasAsync(Admin, ct), e => e.Id == id);
        foreach (var tabela in new[] { "core.empresa WHERE id", "contabil.plano_contas WHERE empresa_id", "contabil.lancamento WHERE empresa_id",
                                       "contabil.saldo_mensal WHERE empresa_id", "contabil.exercicio WHERE empresa_id" })
            Assert.Equal(0L, await db.EscalarAsync<long>($"SELECT count(*) FROM {tabela} = @id", ("id", id)));
        var novamente = await db.CriarEmpresaAsync("11444777000161");                 // mesmo CNPJ, sem conflito
        Assert.NotEqual(id, novamente);
    }

    [Fact]
    public async Task Usuario_excluido_sai_do_banco_e_o_login_fica_livre()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var usuarios = db.App.Usuarios;
        var consulta = await db.PerfilIdAsync("Consulta");
        var antigo = (await usuarios.CriarUsuarioAsync(Admin, "ja_entrou", "Já Entrou", "Inicial123", consulta, ct)).Valor;
        Assert.True((await usuarios.AutenticarAsync("ja_entrou", "Inicial123", ct)).Sucesso);   // gera registro de acesso

        Assert.True((await usuarios.ExcluirUsuarioAsync(Admin, antigo, ct)).Sucesso);
        Assert.DoesNotContain(await usuarios.ListarAsync(Admin, ct), u => u.Id == antigo);
        // o registro de acesso continua (com o login), sem o vínculo ao usuário apagado
        Assert.True(await db.EscalarAsync<long>("SELECT count(*) FROM core.registro_acesso WHERE login = 'ja_entrou' AND usuario_id IS NULL") > 0);
        var novo = await usuarios.CriarUsuarioAsync(Admin, "ja_entrou", "Mesmo Login", "Inicial123", consulta, ct);
        Assert.True(novo.Sucesso, string.Join("; ", novo.Erros));

        var self = new SessaoUsuario(novo.Valor, "ja_entrou", "Mesmo Login", true, false);
        Assert.Contains("próprio usuário", (await usuarios.ExcluirUsuarioAsync(self, novo.Valor, ct)).Erros[0]);
    }
}
