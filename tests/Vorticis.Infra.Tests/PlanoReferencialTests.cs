using System.Text;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Infra.Tests;

public class PlanoReferencialTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private SessaoUsuario Admin = new(0, "teste", "Teste", true, false);   // id real atribuído no início de cada teste

    /// <summary>Tabela FICTÍCIA para testes (estrutura semelhante à da RFB, conteúdo inventado).</summary>
    public const string TabelaFicticia = """
        CODIGO|DESCRICAO|TIPO|COD_SUP|NIVEL|COD_NAT
        1|ATIVO|S||1|01
        1.01|ATIVO CIRCULANTE|S|1|2|01
        1.01.01|Caixa e equivalentes de caixa|A|1.01|3|01
        1.01.02|Bancos conta movimento|A|1.01|3|01
        1.01.03|Clientes|A|1.01|3|01
        2|PASSIVO|S||1|02
        2.01|Fornecedores|A|2|2|02
        2.03|PATRIMÔNIO LÍQUIDO|S|2|2|03
        2.03.01|Capital social|A|2.03|3|03
        2.03.02|Lucros acumulados|A|2.03|3|03
        3|RESULTADO|S||1|04
        3.01|Receita de venda de mercadorias|A|3|2|04
        3.02|Despesas com aluguéis|A|3|2|04
        """;

    private static byte[] Bytes(string s) => Encoding.Latin1.GetBytes(s);

    [Fact]
    public async Task Importa_tabela_vincula_contas_e_aponta_pendencias()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var svc = db.App.PlanoReferencial;
        var empresa = await db.CriarEmpresaAsync("11222333000181");
        var adm = await db.AdminAsync();
        Admin = Admin with { Id = adm };

        Assert.Contains("Somente administradores", (await svc.ImportarAsync(Admin with { Administrador = false }, "1", 2026, Bytes(TabelaFicticia), "t.txt", ct)).Erros[0]);
        Assert.Contains("ainda não foi importada", (await svc.DefinirPlanoAsync(empresa, 2026, "1", adm, ct)).Erros[0]);

        var imp = await svc.ImportarAsync(Admin, "1", 2026, Bytes(TabelaFicticia), "ficticia.txt", ct);
        Assert.True(imp.Sucesso, string.Join("; ", imp.Erros));
        Assert.StartsWith("13 contas importadas (8 analíticas)", imp.Valor);
        Assert.Contains(await svc.ListarTabelasAsync(ct), t => t.CodPlanRef == "1" && t.Ano == 2026 && t.Contas == 13);
        Assert.True((await svc.DefinirPlanoAsync(empresa, 2026, "1", adm, ct)).Sucesso);

        // Movimento em caixa, capital e vendas
        await db.App.Lancamentos.EfetivarAsync(new LancamentoProposto(empresa, new(2026, 2, 1), OrigemLancamento.Manual,
            [new("1.1.01.001", IndicadorDC.D, 1000m, "x"), new("2.3.01.001", IndicadorDC.C, 1000m, "x")]), adm, ct);
        await db.App.Lancamentos.EfetivarAsync(new LancamentoProposto(empresa, new(2026, 3, 1), OrigemLancamento.Manual,
            [new("1.1.01.001", IndicadorDC.D, 300m, "x"), new("3.1.01.001", IndicadorDC.C, 300m, "x")]), adm, ct);

        var pend = (await svc.PendenciasAsync(empresa, 2026, adm, ct)).Valor;
        Assert.Equal(["1.1.01.001", "2.3.01.001", "3.1.01.001"], pend.Select(p => p.Conta));

        var carregado = (await svc.CarregarAsync(empresa, 2026, adm, ct)).Valor;
        long Id(string codigo) => carregado.Contas.Single(c => c.Codigo == codigo).ContaId;

        // Validações do vínculo
        Assert.Contains("sintética", (await svc.VincularAsync(empresa, 2026, new Dictionary<long, string?> { [Id("1.1.01.001")] = "1.01" }, adm, ct)).Erros[0]);
        Assert.Contains("incompatível", (await svc.VincularAsync(empresa, 2026, new Dictionary<long, string?> { [Id("1.1.01.001")] = "3.01" }, adm, ct)).Erros[0]);
        Assert.Contains("não existe", (await svc.VincularAsync(empresa, 2026, new Dictionary<long, string?> { [Id("1.1.01.001")] = "9.99" }, adm, ct)).Erros[0]);

        // Sugestões e aceite
        var sugestoes = await svc.SugerirAsync(empresa, 2026, adm, ct);
        Assert.Equal("1.01.01", sugestoes[Id("1.1.01.001")].Conta.Codigo);     // Caixa Geral → Caixa e equivalentes
        Assert.Equal("3.01", sugestoes[Id("3.1.01.001")].Conta.Codigo);        // Venda de Mercadorias → Receita de venda de mercadorias
        Assert.True((await svc.VincularAsync(empresa, 2026, sugestoes.ToDictionary(s => s.Key, s => (string?)s.Value.Conta.Codigo), adm, ct)).Sucesso);
        Assert.True((await svc.VincularAsync(empresa, 2026, new Dictionary<long, string?> { [Id("2.3.01.001")] = "2.03.01" }, adm, ct)).Sucesso);
        Assert.Empty((await svc.PendenciasAsync(empresa, 2026, adm, ct)).Valor);
        Assert.True(await db.EscalarAsync<bool>("SELECT COUNT(*) > 0 FROM core.auditoria WHERE tabela = 'contabil.mapeamento_referencial'"));

        // Remoção de vínculo volta a gerar pendência
        Assert.True((await svc.VincularAsync(empresa, 2026, new Dictionary<long, string?> { [Id("2.3.01.001")] = null }, adm, ct)).Sucesso);
        Assert.Single((await svc.PendenciasAsync(empresa, 2026, adm, ct)).Valor);

        // Reimportação: conta que saiu do arquivo mas está vinculada é mantida e informada
        var nova = TabelaFicticia.Replace("1.01.01|Caixa e equivalentes de caixa|A|1.01|3|01\n", "");
        var re = await svc.ImportarAsync(Admin, "1", 2026, Bytes(nova), "ficticia_v2.txt", ct);
        Assert.True(re.Sucesso, string.Join("; ", re.Erros));
        Assert.Contains("1.01.01", re.Valor);
        Assert.Contains((await svc.ListarContasAsync("1", 2026, ct)), r => r.Codigo == "1.01.01");
    }

    [Fact]
    public async Task Copia_vinculos_do_ano_anterior_e_exige_permissao()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var svc = db.App.PlanoReferencial;
        var empresa = await db.CriarEmpresaAsync("33000167000101", ano: 2025);
        await db.App.RepositorioEmpresas.CriarExercicioAsync(empresa, 2026, ct);
        var adm = await db.AdminAsync();
        Admin = Admin with { Id = adm };
        Assert.True((await svc.ImportarAsync(Admin, "2", 2025, Bytes(TabelaFicticia), "a.txt", ct)).Sucesso);
        Assert.True((await svc.ImportarAsync(Admin, "2", 2026, Bytes(TabelaFicticia), "b.txt", ct)).Sucesso);
        Assert.True((await svc.DefinirPlanoAsync(empresa, 2025, "2", adm, ct)).Sucesso);
        Assert.True((await svc.DefinirPlanoAsync(empresa, 2026, "2", adm, ct)).Sucesso);

        var contas = (await svc.CarregarAsync(empresa, 2025, adm, ct)).Valor.Contas;
        await svc.VincularAsync(empresa, 2025, new Dictionary<long, string?>
        {
            [contas.Single(c => c.Codigo == "1.1.01.001").ContaId] = "1.01.01",
            [contas.Single(c => c.Codigo == "1.1.01.002").ContaId] = "1.01.02",
        }, adm, ct);

        var copia = await svc.CopiarDoAnoAnteriorAsync(empresa, 2026, adm, ct);
        Assert.True(copia.Sucesso, string.Join("; ", copia.Erros));
        Assert.Equal(2, copia.Valor);
        Assert.Equal("1.01.02", (await svc.CarregarAsync(empresa, 2026, adm, ct)).Valor.Contas.Single(c => c.Codigo == "1.1.01.002").CodigoReferencial);

        // Usuário com perfil Consulta vê, mas não vincula
        var consulta = (await db.App.Perfis.ListarAsync(Admin, ct)).Single(p => p.Nome == "Consulta");
        var uid = (await db.App.Usuarios.CriarUsuarioAsync(Admin, "leitor", "Leitor", "Inicial123", consulta.Id, ct)).Valor;
        await db.App.Usuarios.DefinirAcessoAsync(Admin, uid, empresa, consulta.Id, ct);
        Assert.True((await svc.CarregarAsync(empresa, 2026, uid, ct)).Sucesso);
        Assert.Contains("não permite", (await svc.VincularAsync(empresa, 2026, new Dictionary<long, string?>(), uid, ct)).Erros[0]);
    }
}
