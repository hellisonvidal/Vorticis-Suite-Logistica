using Vorticis.Application.Cadastros;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Infra.Tests;

public class ModulosEmpresaTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static NovaEmpresa Nova(string cnpj, ModulosEmpresa modulos) =>
        new(cnpj, "Empresa Modular LTDA", null, "2062", "4751201", "SP", "3550308", new DateOnly(2020, 1, 1),
            RegimeTributario.LucroPresumido, 2026, Modulos: modulos);

    [Fact]
    public async Task Cria_com_modulos_filtra_por_modulo_e_ativa_depois()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = new SessaoUsuario(await db.AdminAsync(), "admin_teste", "Admin de Teste", true, false);

        // padrão: todos os módulos
        var todos = await db.CriarEmpresaAsync("11444777000161");
        Assert.Equal(ModulosEmpresa.Todos, (await db.App.Empresas.ObterAsync(todos, ct))!.Modulos);

        // nenhum módulo é recusado
        Assert.False((await db.App.Empresas.CadastrarAsync(Nova("45723174000110", ModulosEmpresa.Nenhum), ct)).Sucesso);

        var r = await db.App.Empresas.CadastrarAsync(Nova("45723174000110", ModulosEmpresa.Contabil), ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        var soContabil = r.Valor;
        Assert.Equal(ModulosEmpresa.Contabil, (await db.App.Empresas.ObterAsync(soContabil, ct))!.Modulos);

        // filtro por módulo (administrador vê todas as ativas no módulo)
        var contabil = await db.App.Usuarios.EmpresasDoModuloAsync(adm, ModulosEmpresa.Contabil, ct);
        var fiscal = await db.App.Usuarios.EmpresasDoModuloAsync(adm, ModulosEmpresa.Fiscal, ct);
        Assert.Contains(contabil, e => e.Id == soContabil);
        Assert.DoesNotContain(fiscal, e => e.Id == soContabil);
        Assert.Contains(fiscal, e => e.Id == todos);
        Assert.Contains(await db.App.Usuarios.EmpresasForaDoModuloAsync(adm, ModulosEmpresa.Folha, ct), e => e.Id == soContabil);

        // ativação posterior no Fiscal, sem mexer nos demais
        Assert.True((await db.App.Empresas.AtivarNoModuloAsync(soContabil, ModulosEmpresa.Fiscal, ct)).Sucesso);
        Assert.Equal(ModulosEmpresa.Contabil | ModulosEmpresa.Fiscal, (await db.App.Empresas.ObterAsync(soContabil, ct))!.Modulos);
        Assert.Contains(await db.App.Usuarios.EmpresasDoModuloAsync(adm, ModulosEmpresa.Fiscal, ct), e => e.Id == soContabil);
        Assert.DoesNotContain(await db.App.Usuarios.EmpresasDoModuloAsync(adm, ModulosEmpresa.Folha, ct), e => e.Id == soContabil);

        // alteração do cadastro com módulos; sem módulos informados, mantém
        var det = (await db.App.Empresas.ObterAsync(soContabil, ct))!;
        var alt = new AlteracaoEmpresa(soContabil, det.RazaoSocial, null, det.NaturezaJuridica, det.CnaePrincipal, det.Uf, det.CodMunicipioIbge,
                                       det.DataInicioAtividades!.Value, null);
        Assert.True((await db.App.Empresas.AlterarAsync(alt, ct)).Sucesso);
        Assert.Equal(ModulosEmpresa.Contabil | ModulosEmpresa.Fiscal, (await db.App.Empresas.ObterAsync(soContabil, ct))!.Modulos);
        Assert.True((await db.App.Empresas.AlterarAsync(alt with { Modulos = ModulosEmpresa.Folha }, ct)).Sucesso);
        Assert.Equal(ModulosEmpresa.Folha, (await db.App.Empresas.ObterAsync(soContabil, ct))!.Modulos);
        Assert.False((await db.App.Empresas.AlterarAsync(alt with { Modulos = ModulosEmpresa.Nenhum }, ct)).Sucesso);
    }
}
