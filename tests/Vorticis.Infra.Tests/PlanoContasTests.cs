using Vorticis.Application.Contabil;
using Vorticis.Domain.Contabil;

namespace Vorticis.Infra.Tests;

public class PlanoContasTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static NovaConta Conta(string codigo, string nat = "01", IndicadorDC dc = IndicadorDC.D) =>
        new(codigo, $"Conta {codigo}", nat, dc, false, false, new DateOnly(2026, 1, 1));

    [Fact]
    public async Task Inclusao_respeita_hierarquia_natureza_e_movimento()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var svc = db.App.PlanoContas;
        var empresa = await db.CriarEmpresaAsync("11222333000181");
        var adm = await db.AdminAsync();

        var nova = await svc.IncluirAsync(empresa, Conta("1.1.01.004"), adm, ct);
        Assert.True(nova.Sucesso, string.Join("; ", nova.Erros));
        var plano = await svc.ListarAsync(empresa, adm, ct);
        var criada = plano.Single(c => c.Codigo == "1.1.01.004");
        Assert.True(criada.Analitica);
        Assert.Equal(4, criada.Nivel);

        // Subconta de analítica sem movimento: a pai vira sintética
        Assert.True((await svc.IncluirAsync(empresa, Conta("1.1.01.004.01"), adm, ct)).Sucesso);
        Assert.False((await svc.ListarAsync(empresa, adm, ct)).Single(c => c.Codigo == "1.1.01.004").Analitica);

        // Subconta de analítica COM movimento: proibido
        await db.App.Lancamentos.EfetivarAsync(new LancamentoProposto(empresa, new(2026, 2, 1), OrigemLancamento.Manual,
            [new("1.1.01.001", IndicadorDC.D, 50m, "x"), new("2.3.01.001", IndicadorDC.C, 50m, "x")]), adm, ct);
        Assert.Contains("já tem lançamentos", (await svc.IncluirAsync(empresa, Conta("1.1.01.001.01"), adm, ct)).Erros[0]);

        Assert.Contains("não existe", (await svc.IncluirAsync(empresa, Conta("1.9.99.001"), adm, ct)).Erros[0]);
        Assert.Contains("mesma da conta superior", (await svc.IncluirAsync(empresa, Conta("1.1.01.005", nat: "04"), adm, ct)).Erros[0]);
        Assert.Contains("Já existe", (await svc.IncluirAsync(empresa, Conta("1.1.01.001"), adm, ct)).Erros[0]);
        Assert.Contains("Código inválido", (await svc.IncluirAsync(empresa, Conta("1.a.2"), adm, ct)).Erros[0]);
        Assert.True((await svc.IncluirAsync(empresa, Conta("2.3.04", nat: "03", dc: IndicadorDC.C), adm, ct)).Sucesso);   // PL sob Passivo
    }

    [Fact]
    public async Task Inativacao_e_exclusao_protegem_contas_com_saldo_ou_movimento()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var svc = db.App.PlanoContas;
        var empresa = await db.CriarEmpresaAsync("33000167000101");
        var adm = await db.AdminAsync();
        await db.App.Lancamentos.EfetivarAsync(new LancamentoProposto(empresa, new(2026, 2, 1), OrigemLancamento.Manual,
            [new("1.1.01.001", IndicadorDC.D, 50m, "x"), new("2.3.01.001", IndicadorDC.C, 50m, "x")]), adm, ct);
        var plano = await svc.ListarAsync(empresa, adm, ct);
        var caixa = plano.Single(c => c.Codigo == "1.1.01.001");
        Assert.Equal(50m, caixa.Saldo);

        Assert.Contains("tem saldo", (await svc.AlterarAsync(empresa, caixa.Id, "Caixa", IndicadorDC.D, false, false, false, adm, ct)).Erros[0]);
        Assert.Contains("tem saldo", (await svc.AlterarAsync(empresa, plano.Single(c => c.Codigo == "1.1").Id, "AC", IndicadorDC.D, false, false, false, adm, ct)).Erros[0]);
        Assert.Contains("tem lançamentos", (await svc.ExcluirAsync(empresa, caixa.Id, adm, ct)).Erros[0]);
        Assert.Contains("subcontas", (await svc.ExcluirAsync(empresa, plano.Single(c => c.Codigo == "1.1.01").Id, adm, ct)).Erros[0]);

        var semUso = plano.Single(c => c.Codigo == "1.1.01.003");
        Assert.True((await svc.AlterarAsync(empresa, semUso.Id, "Aplicações (renomeada)", IndicadorDC.D, true, false, false, adm, ct)).Sucesso);
        var alterada = (await svc.ListarAsync(empresa, adm, ct)).Single(c => c.Id == semUso.Id);
        Assert.Equal(("Aplicações (renomeada)", true, false), (alterada.Descricao, alterada.ExigeCentroCusto, alterada.Ativa));
        Assert.True((await svc.ExcluirAsync(empresa, semUso.Id, adm, ct)).Sucesso);
    }

    [Fact]
    public async Task Centros_de_custo_hierarquicos_e_uso_em_lancamento()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var svc = db.App.PlanoContas;
        var empresa = await db.CriarEmpresaAsync("60701190000104");
        var adm = await db.AdminAsync();

        Assert.True((await svc.IncluirCentroAsync(empresa, "ADM", "Administrativo", null, adm, new DateOnly(2026, 1, 1), ct)).Sucesso);
        Assert.True((await svc.IncluirCentroAsync(empresa, "ADM.TI", "Tecnologia", "ADM", adm, new DateOnly(2026, 1, 1), ct)).Sucesso);
        Assert.Contains("não encontrado", (await svc.IncluirCentroAsync(empresa, "X", "X", "NAOEXISTE", adm, ct: ct)).Erros[0]);
        Assert.Contains("Já existe", (await svc.IncluirCentroAsync(empresa, "ADM", "Dup", null, adm, ct: ct)).Erros[0]);

        var r = await db.App.Lancamentos.EfetivarAsync(new LancamentoProposto(empresa, new(2026, 3, 1), OrigemLancamento.Manual,
            [new("4.2.02.004", IndicadorDC.D, 99m, "Internet", "ADM.TI"), new("1.1.01.001", IndicadorDC.C, 99m, "Internet")]), adm, ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));

        var centros = await svc.ListarCentrosAsync(empresa, adm, ct);
        var ti = centros.Single(c => c.Codigo == "ADM.TI");
        Assert.True(ti.TemMovimento);
        Assert.Contains("tem lançamentos", (await svc.ExcluirCentroAsync(empresa, ti.Id, adm, ct)).Erros[0]);
        Assert.Contains("subcentros", (await svc.ExcluirCentroAsync(empresa, centros.Single(c => c.Codigo == "ADM").Id, adm, ct)).Erros[0]);
        Assert.True((await svc.AlterarCentroAsync(empresa, ti.Id, "TI", false, adm, ct)).Sucesso);
        var inativo = await db.App.Lancamentos.EfetivarAsync(new LancamentoProposto(empresa, new(2026, 3, 2), OrigemLancamento.Manual,
            [new("4.2.02.004", IndicadorDC.D, 1m, "x", "ADM.TI"), new("1.1.01.001", IndicadorDC.C, 1m, "x")]), adm, ct);
        Assert.Contains(inativo.Erros, e => e.Contains("centro de custo ADM.TI não encontrado"));   // inativo não aceita lançamento
    }
}

public class PlanoCentrosCustoTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    [Fact]
    public async Task Plano_de_centros_com_mascara_hierarquia_e_so_analitico_recebe_lancamento()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var svc = db.App.PlanoContas;
        var empresa = await db.CriarEmpresaAsync("33000167000101");
        var adm = await db.AdminAsync();
        var data = new DateOnly(2026, 1, 1);

        // Cadastro livre (sem máscara): o pai que recebe filho vira grupo sintético.
        Assert.True((await svc.IncluirCentroAsync(empresa, "1", "Administrativo", null, adm, data, ct)).Sucesso);
        Assert.True((await svc.IncluirCentroAsync(empresa, "1.1", "TI", "1", adm, data, ct)).Sucesso);
        Assert.False((await svc.ListarCentrosAsync(empresa, adm, ct)).Single(c => c.Codigo == "1").Analitico);

        // Máscara: sem converter, os códigos antigos não cabem; convertendo, 1.1 → 1.01.
        var recusa = await svc.DefinirMascaraCentrosAsync(empresa, "9.99.999", adm, false, ct);
        Assert.Contains("não cabe", recusa.Erros[0]);
        Assert.True((await svc.DefinirMascaraCentrosAsync(empresa, "9.99.999", adm, true, ct)).Sucesso);
        Assert.Equal("9.99.999", await svc.ObterMascaraCentrosAsync(empresa, adm, ct));
        var ti = (await svc.ListarCentrosAsync(empresa, adm, ct)).Single(c => c.Descricao == "TI");
        Assert.Equal(("1.01", (short)2), (ti.Codigo, ti.Nivel));

        // Validação pela máscara e pai obrigatório.
        Assert.Contains("fora da máscara", (await svc.IncluirCentroAsync(empresa, "1.1.1", "x", null, adm, data, ct)).Erros[0]);
        Assert.Contains("não encontrado", (await svc.IncluirCentroAsync(empresa, "2.01", "Sem pai", null, adm, data, ct)).Erros[0]);
        Assert.Contains("deve ser 1.01", (await svc.IncluirCentroAsync(empresa, "1.01.001", "x", "1", adm, data, ct)).Erros[0]);
        Assert.Contains("último nível", (await svc.IncluirCentroAsync(empresa, "1.01.002", "x", null, adm, data, ct, sintetico: true)).Erros[0]);
        Assert.True((await svc.IncluirCentroAsync(empresa, "1.01.001", "Suporte", null, adm, data, ct)).Sucesso);   // pai deduzido do código
        var centros = await svc.ListarCentrosAsync(empresa, adm, ct);
        var suporte = centros.Single(c => c.Codigo == "1.01.001");
        Assert.Equal((true, (short)3), (suporte.Analitico, suporte.Nivel));
        Assert.False(centros.Single(c => c.Codigo == "1.01").Analitico);

        // Só analítico aceita lançamento.
        LancamentoProposto Lanc(string cc) => new(empresa, new(2026, 3, 1), OrigemLancamento.Manual,
            [new("4.2.02.004", IndicadorDC.D, 10m, "x", cc), new("1.1.01.001", IndicadorDC.C, 10m, "x")]);
        var sintetico = await db.App.Lancamentos.EfetivarAsync(Lanc("1.01"), adm, ct);
        Assert.Contains(sintetico.Erros, e => e.Contains("centro de custo 1.01 não encontrado"));
        var ok = await db.App.Lancamentos.EfetivarAsync(Lanc("1.01.001"), adm, ct);
        Assert.True(ok.Sucesso, string.Join("; ", ok.Erros));

        // Com lançamentos o centro analítico não vira sintético nem recebe subcentros; com subcentros o grupo não vira analítico.
        Assert.Contains("tem lançamentos", (await svc.AlterarCentroAsync(empresa, suporte.Id, "Suporte", true, adm, ct, analitico: false)).Erros[0]);
        Assert.Contains("4 níveis", (await svc.IncluirCentroAsync(empresa, "1.01.001.1", "x", null, adm, data, ct)).Erros[0]);
        Assert.Contains("subcentros", (await svc.AlterarCentroAsync(empresa, ti.Id, "TI", true, adm, ct, analitico: true)).Erros[0]);

        // Remover a máscara volta ao cadastro livre.
        Assert.True((await svc.DefinirMascaraCentrosAsync(empresa, "", adm, false, ct)).Sucesso);
        Assert.Null(await svc.ObterMascaraCentrosAsync(empresa, adm, ct));
        Assert.Contains("já tem lançamentos", (await svc.IncluirCentroAsync(empresa, "1.01.001.1", "x", null, adm, data, ct)).Erros[0]);   // analítico com movimento
        Assert.True((await db.App.Empresas.ExcluirAsync(empresa, ct)).Sucesso);
    }
}

public class ImportacaoPlanoContasTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    [Fact]
    public async Task Importa_planilha_xlsx_substituindo_o_plano_e_converte_mascara_com_movimento()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var svc = db.App.PlanoContas;
        var empresa = await db.CriarEmpresaAsync("19131243000197");
        var adm = await db.AdminAsync();

        // Planilha .xlsx (gerada aqui com o próprio exportador), códigos sem máscara
        var linhas = new (string Cod, string Desc)[]
        {
            ("1", "ATIVO"), ("11", "CIRCULANTE"), ("111", "DISPONÍVEL"), ("1111", "Caixa"), ("1112", "Bancos"),
            ("2", "PASSIVO"), ("21", "CIRCULANTE"), ("211", "FORNECEDORES"), ("2111", "Fornecedores nacionais"),
            ("3", "RESULTADO"), ("31", "RECEITAS"), ("311", "Vendas"), ("3111", "Venda de mercadorias"),
        };
        var xlsx = Relatorios.ExportadorExcel.Gerar([new Application.Relatorios.TabelaRelatorio("Plano",
            [new("Código"), new("Descrição")], linhas.Select(l => new Application.Relatorios.LinhaRelatorio([l.Cod, l.Desc])).ToList())]);
        var tabela = Importacao.LeitorPlanilha.Ler(xlsx, ".xlsx");
        Assert.True(tabela.Sucesso, string.Join("; ", tabela.Erros));
        var plano = LeitorPlanoContas.Ler(tabela.Valor, ColunasPlano.Detectar(tabela.Valor), MascaraPlano.Ler("9.9.9.99").Valor);
        Assert.True(plano.Valido, string.Join("; ", plano.Erros));

        var r = await svc.ImportarAsync(empresa, plano, adm, ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        var atual = await svc.ListarAsync(empresa, adm, ct);
        Assert.Equal(13, atual.Count);
        var caixa = atual.Single(c => c.Codigo == "1.1.1.01");
        Assert.True(caixa.Analitica);
        Assert.Equal("1.1.1", atual.Single(c => c.Id == caixa.ContaPaiId).Codigo);
        Assert.Equal(("04", IndicadorDC.C), (atual.Single(c => c.Codigo == "3.1.1.01").CodNat, atual.Single(c => c.Codigo == "3.1.1.01").NaturezaSaldo));

        // Com lançamento, importar de novo é recusado...
        var lanc = await db.App.Lancamentos.EfetivarAsync(new LancamentoProposto(empresa, new(2026, 2, 1), OrigemLancamento.Manual,
            [new("1.1.1.01", IndicadorDC.D, 80m, "venda"), new("3.1.1.01", IndicadorDC.C, 80m, "venda")]), adm, ct);
        Assert.True(lanc.Sucesso, string.Join("; ", lanc.Erros));
        Assert.Contains("já tem lançamentos", (await svc.ImportarAsync(empresa, plano, adm, ct)).Erros[0]);

        // ...mas a conversão de máscara troca só os códigos, mantendo lançamentos e saldos
        Assert.Contains("níveis", (await svc.AplicarMascaraAsync(empresa, MascaraPlano.Ler("9.9.9").Valor, adm, ct)).Erros[0]);
        var conv = await svc.AplicarMascaraAsync(empresa, MascaraPlano.Ler("9.9.99.999").Valor, adm, ct);
        Assert.True(conv.Sucesso, string.Join("; ", conv.Erros));
        Assert.Equal(7, conv.Valor);                                          // raízes e 2º nível não mudam
        var depois = await svc.ListarAsync(empresa, adm, ct);
        var caixaDepois = depois.Single(c => c.Id == caixa.Id);
        Assert.Equal(("1.1.01.001", 80m), (caixaDepois.Codigo, caixaDepois.Saldo));
        Assert.Equal(0, (await svc.AplicarMascaraAsync(empresa, MascaraPlano.Ler("9.9.99.999").Valor, adm, ct)).Valor);
    }
}
