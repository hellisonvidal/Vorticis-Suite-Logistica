using Vorticis.Application.Cadastros;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Infra.Tests;

/// <summary>CNPJs válidos para criar várias empresas num mesmo teste.</summary>
internal static class CnpjsDeTeste
{
    public static string Gerar(int n)
    {
        var d = $"{n:00000000}0001".Select(c => c - '0').ToList();
        foreach (var pesos in new[] { new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 }, new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 } })
        {
            var soma = d.Select((v, i) => v * pesos[i]).Sum() % 11;
            d.Add(soma < 2 ? 0 : 11 - soma);
        }
        return string.Concat(d);
    }
}

/// <summary>Empresas liberadas por usuário (cadastro de usuários): só as marcadas aparecem na seleção de empresa.</summary>
public class AcessoPorEmpresaTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static readonly SessaoUsuario Admin = new(0, "teste", "Teste", true, false);

    private async Task<long> CriarEmpresa(int n)
    {
        var r = await db.App.Empresas.CadastrarAsync(new NovaEmpresa(CnpjsDeTeste.Gerar(n), $"Empresa {n} LTDA", null, "2062", "4751201",
            "SP", "3550308", new DateOnly(2020, 1, 1), RegimeTributario.LucroPresumido, 2026), TestContext.Current.CancellationToken);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        return r.Valor;
    }

    [Fact]
    public async Task Usuario_so_acessa_as_empresas_marcadas_e_as_novas_so_chegam_a_quem_tem_todas()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var svc = db.App.Usuarios;
        var consulta = await db.PerfilIdAsync("Consulta");
        var assistente = await db.PerfilIdAsync("Consulta");
        var (e1, e2, e3) = (await CriarEmpresa(101), await CriarEmpresa(102), await CriarEmpresa(103));

        // restrito: só e1 e e3
        var r = await svc.CriarUsuarioAsync(Admin, "restrito", "Restrito", "Inicial123", consulta, ct, empresaIds: [e1, e3]);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        var restrito = new SessaoUsuario(r.Valor, "restrito", "Restrito", false, false);
        Assert.Equal([e1, e3], (await svc.EmpresasPermitidasAsync(restrito, ct)).Select(e => e.Id).Order());
        Assert.False(await db.EscalarAsync<bool>("SELECT todas_empresas FROM core.usuario WHERE id = @u", ("u", r.Valor)));

        // sem lista: todas (como antes)
        var t = await svc.CriarUsuarioAsync(Admin, "todas", "Todas", "Inicial123", assistente, ct);
        var todas = new SessaoUsuario(t.Valor, "todas", "Todas", false, false);
        Assert.Equal(3, (await svc.EmpresasPermitidasAsync(todas, ct)).Count);
        Assert.True(await db.EscalarAsync<bool>("SELECT todas_empresas FROM core.usuario WHERE id = @u", ("u", t.Valor)));

        // o administrador vê todas, independentemente de marcas
        var adm = new SessaoUsuario(await db.AdminAsync(), "admin_teste", "Admin", true, false);
        Assert.True((await svc.EmpresasPermitidasAsync(adm, ct)).Count >= 3);

        // empresa nova: vai a quem tem "todas"; o restrito continua só com as suas
        var e4 = await CriarEmpresa(104);
        Assert.Contains(e4, (await svc.EmpresasPermitidasAsync(todas, ct)).Select(e => e.Id));
        Assert.DoesNotContain(e4, (await svc.EmpresasPermitidasAsync(restrito, ct)).Select(e => e.Id));

        // o administrador libera e retira por empresa (perfil por empresa marcada)
        var comum = new SessaoUsuario(1, "x", "X", false, false);
        Assert.False((await svc.DefinirAcessosAsync(comum, r.Valor, new Dictionary<long, long?> { [e2] = consulta }, ct)).Sucesso);
        Assert.True((await svc.DefinirAcessosAsync(Admin, r.Valor, new Dictionary<long, long?> { [e2] = consulta, [e1] = null }, ct)).Sucesso);
        Assert.Equal([e2, e3], (await svc.EmpresasPermitidasAsync(restrito, ct)).Select(e => e.Id).Order());
        var acessos = await svc.ListarAcessosAsync(r.Valor, ct);
        Assert.All(acessos, a => Assert.Equal(consulta, a.PerfilId));

        // marcando todas as que faltam, volta a receber as empresas novas
        Assert.True((await svc.DefinirAcessosAsync(Admin, r.Valor, new Dictionary<long, long?> { [e1] = consulta, [e4] = consulta }, ct)).Sucesso);
        Assert.True(await db.EscalarAsync<bool>("SELECT todas_empresas FROM core.usuario WHERE id = @u", ("u", r.Valor)));
        var e5 = await CriarEmpresa(105);
        Assert.Contains(e5, (await svc.EmpresasPermitidasAsync(restrito, ct)).Select(e => e.Id));

        // usuário criado sem nenhuma empresa marcada não vê nenhuma (e não recebe as novas)
        var v = await svc.CriarUsuarioAsync(Admin, "semempresa", "Sem", "Inicial123", consulta, ct, empresaIds: []);
        var sem = new SessaoUsuario(v.Valor, "semempresa", "Sem", false, false);
        Assert.Empty(await svc.EmpresasPermitidasAsync(sem, ct));
        await CriarEmpresa(106);
        Assert.Empty(await svc.EmpresasPermitidasAsync(sem, ct));
    }
}

/// <summary>Regras da aba Contabilidade da empresa com efeito real: documento obrigatório, bloqueio de data e máscara dos centros de custo.</summary>
public class RegrasContabeisDaEmpresaTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static LancamentoProposto Lcto(long empresa, DateOnly data, string? documento, OrigemLancamento origem = OrigemLancamento.Manual) =>
        new(empresa, data, origem, [new("1.1.01.001", IndicadorDC.D, 50m, "x", NumeroArquivo: documento), new("2.3.01.001", IndicadorDC.C, 50m, "x", NumeroArquivo: documento)]);

    [Fact]
    public async Task Exigir_documento_e_bloquear_data_recusam_lancamentos_e_a_configuracao_se_mantem()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("11444777000161");
        var cfg = await db.App.PlanoContas.ObterConfiguracaoAsync(empresa, adm, ct);
        Assert.False(cfg.ExigeDocumento);
        Assert.Null(cfg.DataBloqueio);
        var inicio = new DateOnly(2020, 1, 1);

        // sem regras: lança sem documento
        Assert.True((await db.App.Lancamentos.EfetivarAsync(Lcto(empresa, new(2026, 1, 10), null), adm, ct)).Sucesso);

        // exigir documento
        var s = await db.App.PlanoContas.SalvarConfiguracaoAsync(empresa, cfg with { ExigeDocumento = true, DataBloqueio = new DateOnly(2026, 1, 31) }, inicio, adm, ct);
        Assert.True(s.Sucesso, string.Join("; ", s.Erros));
        var salva = await db.App.PlanoContas.ObterConfiguracaoAsync(empresa, adm, ct);
        Assert.Equal((true, new DateOnly(2026, 1, 31)), (salva.ExigeDocumento, salva.DataBloqueio));

        var semDoc = await db.App.Lancamentos.EfetivarAsync(Lcto(empresa, new(2026, 2, 10), null), adm, ct);
        Assert.Contains(semDoc.Erros, e => e.Contains("nº do documento"));
        Assert.True((await db.App.Lancamentos.EfetivarAsync(Lcto(empresa, new(2026, 2, 10), "NF 123"), adm, ct)).Sucesso);

        // bloqueio: até 31/01 recusa (inclusive a data limite); depois dela, vale
        var bloqueado = await db.App.Lancamentos.EfetivarAsync(Lcto(empresa, new(2026, 1, 31), "NF 1"), adm, ct);
        Assert.Contains(bloqueado.Erros, e => e.Contains("bloqueados"));
        Assert.True((await db.App.Lancamentos.EfetivarAsync(Lcto(empresa, new(2026, 2, 1), "NF 2"), adm, ct)).Sucesso);

        // data de bloqueio anterior ao início das atividades é recusada
        Assert.Contains("anterior ao início", string.Join(" ", (await db.App.PlanoContas.SalvarConfiguracaoAsync(
            empresa, salva with { DataBloqueio = new DateOnly(2019, 12, 31) }, inicio, adm, ct)).Erros));

        // desligar libera de novo
        Assert.True((await db.App.PlanoContas.SalvarConfiguracaoAsync(empresa, salva with { ExigeDocumento = false, DataBloqueio = null }, inicio, adm, ct)).Sucesso);
        Assert.True((await db.App.Lancamentos.EfetivarAsync(Lcto(empresa, new(2026, 1, 20), null), adm, ct)).Sucesso);
    }

    [Fact]
    public async Task Mascara_dos_centros_de_custo_e_definida_pela_configuracao_da_empresa()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("60606060000184");
        var inicio = new DateOnly(2020, 1, 1);
        var cfg = await db.App.PlanoContas.ObterConfiguracaoAsync(empresa, adm, ct);

        Assert.Contains("Máscara", string.Join(" ", (await db.App.PlanoContas.SalvarConfiguracaoAsync(
            empresa, cfg with { MascaraCentroCusto = "..." }, inicio, adm, ct)).Erros));
        var ok = await db.App.PlanoContas.SalvarConfiguracaoAsync(empresa, cfg with { UsaCentroCusto = true, MascaraCentroCusto = "9.99.999" }, inicio, adm, ct);
        Assert.True(ok.Sucesso, string.Join("; ", ok.Erros));
        Assert.Equal("9.99.999", await db.App.PlanoContas.ObterMascaraCentrosAsync(empresa, adm, ct));
        Assert.Equal("9.99.999", (await db.App.PlanoContas.ObterConfiguracaoAsync(empresa, adm, ct)).MascaraCentroCusto);

        // com centros cadastrados, trocar a máscara exige que eles caibam (ou a conversão)
        Assert.True((await db.App.PlanoContas.IncluirCentroAsync(empresa, "1", "Administrativo", "", adm, sintetico: true, ct: ct)).Sucesso);
        Assert.True((await db.App.PlanoContas.IncluirCentroAsync(empresa, "1.01", "Diretoria", "1", adm, sintetico: true, ct: ct)).Sucesso);
        var atual = await db.App.PlanoContas.ObterConfiguracaoAsync(empresa, adm, ct);
        var recusa = await db.App.PlanoContas.SalvarConfiguracaoAsync(empresa, atual with { MascaraCentroCusto = "9.9.99.999" }, inicio, adm, ct);
        Assert.False(recusa.Sucesso);
        Assert.Equal("9.99.999", await db.App.PlanoContas.ObterMascaraCentrosAsync(empresa, adm, ct));   // nada mudou

        // empresa nova já nasce com a máscara escolhida no cadastro
        var nova = await db.App.Empresas.CadastrarAsync(new NovaEmpresa(CnpjsDeTeste.Gerar(201), "Nova LTDA", null, "2062", "4751201", "SP", "3550308",
            new DateOnly(2020, 1, 1), RegimeTributario.LucroPresumido, 2026,
            Contabil: new ConfiguracaoContabil(UsaCentroCusto: true, MascaraCentroCusto: "9.99.999", ExigeDocumento: true)), ct);
        Assert.True(nova.Sucesso, string.Join("; ", nova.Erros));
        Assert.Equal("9.99.999", await db.App.PlanoContas.ObterMascaraCentrosAsync(nova.Valor, adm, ct));
        Assert.True((await db.App.PlanoContas.ObterConfiguracaoAsync(nova.Valor, adm, ct)).ExigeDocumento);
    }
}
