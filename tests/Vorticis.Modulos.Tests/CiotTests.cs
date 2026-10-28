using Vorticis.Frota;
using Vorticis.Infra.Tests;

namespace Vorticis.Modulos.Tests;

public class RegrasCiotTests
{
    /// <summary>Parâmetros de 2026: base de 20% (INSS 11% até o teto de R$ 8.475,55; SEST/SENAT 2,5%) e IR sobre 10% com a redução da Lei 15.270/2025.</summary>
    internal static readonly ParametrosTac P2026 = new(20, 11, 2.5m, 10, 8475.55m,
        [new(2428.80m, 0, 0), new(2826.65m, 7.5m, 182.16m), new(3751.05m, 15, 394.16m), new(4664.68m, 22.5m, 675.49m), new(null, 27.5m, 908.73m)],
        189.59m, new ReducaoIr(5000m, 312.89m, 7350m, 978.62m, 0.133145m));

    internal static OperacaoCiot Operacao(long contratado = 1, decimal frete = 10000m) =>
        new(0, 0, null, null, "Repom", "PADRAO", contratado, "12345678", "ABC1D23", "João Autônomo", "52998224725", null,
            new ParadaRota("SP", "3509502", "Campinas"), new ParadaRota("RJ", "3304557", "Rio de Janeiro"), new(2026, 3, 2), new(2026, 3, 4),
            "Peças", "84099190", 12000m, frete, 350m, 2000m, 0, RetencoesTac.Nenhuma, "DIGITADO", null, []);

    [Fact]
    public void Retencoes_do_tac_pelas_tabelas_de_2026()
    {
        Assert.Equal(new RetencoesTac(220m, 50m, 0m), RegrasCiot.Calcular(10000m, 0, P2026));
        // base do INSS acima do teto; IR na faixa de 27,5% com a redução (978,62 − 0,133145 × 6.000)
        Assert.Equal(new RetencoesTac(932.31m, 300m, 305.13m), RegrasCiot.Calcular(60000m, 0, P2026));
        // dois fretes no mês somam o mesmo que um só (o teto e a tabela do IR são mensais)
        var primeiro = RegrasCiot.Retencoes("52998224725", 30000m, 0, 0, P2026);
        var segundo = RegrasCiot.Retencoes("52998224725", 30000m, 30000m, 0, P2026);
        Assert.Equal(new RetencoesTac(660m, 150m, 0m), primeiro);
        Assert.Equal(new RetencoesTac(272.31m, 150m, 305.13m), segundo);
        // empresa (ETC equiparada, CNPJ) não tem retenção; dependentes reduzem o IR
        Assert.Equal(RetencoesTac.Nenhuma, RegrasCiot.Retencoes("11222333000181", 60000m, 0, 0, P2026));
        Assert.True(RegrasCiot.Calcular(60000m, 2, P2026).Irrf < 305.13m);
    }

    [Fact]
    public void Validacao_da_operacao()
    {
        var o = Operacao();
        Assert.Empty(RegrasCiot.Validar(o));
        Assert.Contains("RNTRC", RegrasCiot.Validar(o with { Rntrc = "12" })[0]);
        Assert.Contains("Placa", RegrasCiot.Validar(o with { Placa = "XX" })[0]);
        Assert.Contains("CIOT gerado", RegrasCiot.Validar(o with { Situacao = "DECLARADO" })[0]);
        Assert.Contains("12 dígitos", RegrasCiot.Validar(o with { Ciot = "123" })[0]);
        Assert.Contains("adiantamento", RegrasCiot.Validar(o with { Adiantamento = 20000m })[0]);
        Assert.Equal(7650m, (o with { Retencoes = new RetencoesTac(220, 50, 80) }).Saldo);             // 10.000 − 350 retido − 2.000 de adiantamento
    }
}

public class CiotIntegracaoTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    [Fact]
    public async Task Operacao_calcula_retencoes_no_mes_declara_e_encerra()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("81527463000108");
        var svc = ServicosFrota.De(db.App);
        var tac = await db.EscalarAsync<long>("INSERT INTO core.participante (empresa_id, codigo, nome, cpf_cnpj) VALUES (@e, 'T1', 'João Autônomo', '52998224725') RETURNING id", ("e", empresa));
        var etc = await db.EscalarAsync<long>("INSERT INTO core.participante (empresa_id, codigo, nome, cpf_cnpj) VALUES (@e, 'T2', 'Transportes Beta', '11222333000181') RETURNING id", ("e", empresa));

        Assert.Equal(RegrasCiotTests.P2026, await svc.ParametrosTacAsync(new DateOnly(2026, 3, 2), ct) is { } p ? p with { FaixasIr = RegrasCiotTests.P2026.FaixasIr } : null);
        Assert.Equal(RegrasCiotTests.P2026.FaixasIr, (await svc.ParametrosTacAsync(new DateOnly(2026, 3, 2), ct))!.FaixasIr);

        var r1 = await svc.GravarCiotAsync(empresa, RegrasCiotTests.Operacao(tac, 30000m), adm, ct);
        Assert.True(r1.Sucesso, string.Join(" ", r1.Erros));
        Assert.Equal((1, new RetencoesTac(660m, 150m, 0m)), (r1.Valor.Numero, r1.Valor.Retencoes));
        var r2 = await svc.GravarCiotAsync(empresa, RegrasCiotTests.Operacao(tac, 30000m) with { Inicio = new(2026, 3, 20), Fim = new(2026, 3, 22) }, adm, ct);
        Assert.Equal((2, new RetencoesTac(272.31m, 150m, 305.13m)), (r2.Valor.Numero, r2.Valor.Retencoes));   // o segundo frete do mês
        var r3 = await svc.GravarCiotAsync(empresa, RegrasCiotTests.Operacao(etc, 30000m), adm, ct);
        Assert.Equal(RetencoesTac.Nenhuma, r3.Valor.Retencoes);                                        // ETC: sem retenção

        // CT-e numa operação só
        var alfa = await db.EscalarAsync<long>("INSERT INTO core.participante (empresa_id, codigo, nome) VALUES (@e, 'C1', 'Indústria Alfa') RETURNING id", ("e", empresa));
        var cte = (await svc.GravarCteAsync(empresa, RegrasTmsTests.NovoCte(alfa, alfa, alfa), adm, ct)).Valor;
        Assert.True((await svc.GravarCiotAsync(empresa, r1.Valor with { Ctes = [cte.Id] }, adm, ct)).Sucesso);
        Assert.Contains("já está na operação nº 1", (await svc.GravarCiotAsync(empresa, r3.Valor with { Ctes = [cte.Id] }, adm, ct)).Erros[0]);

        // declarar (CIOT da IPEF), encerrar; declarada não muda; excluir só a digitada
        Assert.False((await svc.DeclararCiotAsync(empresa, r1.Valor.Id, "123", null, null, adm, ct)).Sucesso);
        Assert.True((await svc.DeclararCiotAsync(empresa, r1.Valor.Id, "1234.5678.9012", "4321", "Pamcard", adm, ct)).Sucesso);
        var declarada = (await svc.CiotsAsync(empresa, new(2026, 3, 1), new(2026, 3, 31), adm, ct)).Single(o => o.Id == r1.Valor.Id);
        Assert.Equal(("123456789012", "DECLARADO", "Pamcard"), (declarada.Ciot, declarada.Situacao, declarada.Ipef));
        Assert.Equal([cte.Id], declarada.Ctes);
        Assert.False((await svc.GravarCiotAsync(empresa, declarada with { ValorFrete = 1 }, adm, ct)).Sucesso);
        Assert.False((await svc.ExcluirCiotAsync(empresa, declarada.Id, adm, ct)).Sucesso);
        Assert.False((await svc.MudarSituacaoCiotAsync(empresa, r3.Valor.Id, "ENCERRADO", adm, ct)).Sucesso);   // digitada não encerra
        Assert.True((await svc.MudarSituacaoCiotAsync(empresa, declarada.Id, "ENCERRADO", adm, ct)).Sucesso);
        Assert.True((await svc.ExcluirCiotAsync(empresa, r3.Valor.Id, adm, ct)).Sucesso);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(ServicoFrota.ContratoFretePdf(declarada, "Transportadora", "Ana"), 0, 4));
    }
}
