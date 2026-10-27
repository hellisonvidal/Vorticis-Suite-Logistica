using Vorticis.Frota;
using Vorticis.Infra.Tests;

namespace Vorticis.Modulos.Tests;

public class RegrasMultasTests
{
    private static readonly DateOnly Hoje = new(2026, 5, 10);

    private static MultaTransito Multa(string situacao = "NOTIFICADA", DateOnly? indicacao = null, DateOnly? recurso = null, DateOnly? vencimento = null) =>
        new(1, 1, 7, "E1", null, Hoje.AddDays(-20), null, "745-50", null, "MEDIA", 4, 130.16m, 20, vencimento, indicacao, recurso, Situacao: situacao);

    [Fact]
    public void Proximo_prazo_segue_a_situacao_da_multa()
    {
        var m = Multa(indicacao: Hoje.AddDays(5), recurso: Hoje.AddDays(5), vencimento: Hoje.AddDays(40));
        Assert.Equal(new PrazoMulta("Indicação do condutor", Hoje.AddDays(5), GravidadeAviso.AVencer), RegrasMultas.ProximoPrazo(m, Hoje, 10));
        // condutor indicado: vale o recurso; em recurso: só o pagamento (fora da antecedência, em dia)
        Assert.Equal("Defesa/recurso", RegrasMultas.ProximoPrazo(m with { Situacao = "CONDUTOR_INDICADO" }, Hoje, 10)!.Assunto);
        Assert.Equal(new PrazoMulta("Pagamento", Hoje.AddDays(40), null), RegrasMultas.ProximoPrazo(m with { Situacao = "EM_RECURSO" }, Hoje, 10));
        // todos vencidos: mostra o último que venceu, como vencido
        var vencida = Multa(indicacao: Hoje.AddDays(-15), vencimento: Hoje.AddDays(-2));
        Assert.Equal(new PrazoMulta("Pagamento", Hoje.AddDays(-2), GravidadeAviso.Vencido), RegrasMultas.ProximoPrazo(vencida, Hoje, 10));
        // paga ou cancelada: sem prazo; sem datas: sem prazo
        Assert.Null(RegrasMultas.ProximoPrazo(vencida with { Situacao = "PAGA", DataPagamento = Hoje }, Hoje, 10));
        Assert.Null(RegrasMultas.ProximoPrazo(Multa(), Hoje, 10));
    }

    [Fact]
    public void Validacao_desconto_e_pontos_dos_motoristas()
    {
        Assert.Empty(RegrasMultas.Validar(Multa()));
        Assert.Contains("Multa paga: informe a data do pagamento.", RegrasMultas.Validar(Multa("PAGA")));
        Assert.Contains("Condutor indicado: escolha o motorista.", RegrasMultas.Validar(Multa("CONDUTOR_INDICADO") with { MotoristaId = null }));
        Assert.Contains("Multa descontada do motorista: escolha o motorista.", RegrasMultas.Validar(Multa() with { MotoristaId = null, Responsavel = "MOTORISTA" }));
        Assert.Contains("Escolha a gravidade da infração.", RegrasMultas.Validar(Multa() with { Gravidade = "X" }));
        Assert.Equal(104.13m, Multa().ValorComDesconto);                                     // 130,16 com 20%

        var multas = new[]
        {
            Multa() with { MotoristaNome = "Ana" },
            Multa() with { Id = 2, Pontos = 7, MotoristaNome = "Ana" },
            Multa() with { Id = 3, Pontos = 7, MotoristaNome = "Ana", Situacao = "CANCELADA" },                   // cancelada não conta
            Multa() with { Id = 4, Pontos = 5, MotoristaNome = "Ana", DataInfracao = Hoje.AddMonths(-13) },       // fora dos 12 meses
            Multa() with { Id = 5, Pontos = 5, MotoristaId = 8, MotoristaNome = "Bruno" },
            Multa() with { Id = 6, MotoristaId = null },
        };
        Assert.Equal([new PontosMotorista(7, "Ana", 11, 2), new PontosMotorista(8, "Bruno", 5, 1)], RegrasMultas.PontosDosMotoristas(multas, Hoje));
    }
}

public class MultasIntegracaoTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    [Fact]
    public async Task Lanca_multas_avisa_prazos_e_sugere_pela_tabela_do_CTB()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("61529347000110");
        var svc = ServicosFrota.De(db.App);
        var hoje = DateOnly.FromDateTime(DateTime.Today);

        var veiculo = (await svc.GravarVeiculoAsync(empresa, new Veiculo(0, 1, "QWE1R23", null, "CAVALO", null, null, null, null, "DIESEL_S10", null, 0, null), adm, ct)).Valor;
        var motorista = (await svc.GravarMotoristaAsync(empresa, new Motorista(0, 1, "Ana Souza", "529.982.247-25", null, "E", null, null), adm, ct)).Valor;

        // tabela do CTB em core.parametro_legal (migração 081)
        var tabela = await svc.TabelaInfracoesAsync(hoje, ct);
        Assert.NotNull(tabela);
        Assert.Equal((7, 293.47m), tabela.PorGravidade["GRAVISSIMA"]);
        Assert.Equal((40m, 20m), (tabela.DescontoSne, tabela.DescontoVencimento));

        var multa = new MultaTransito(0, veiculo, motorista, " e123 ", "DETRAN-SP", hoje.AddDays(-30), "Rod. Anhanguera", "745-50", "Velocidade", "MEDIA", 4, 130.16m, 20,
                                      hoje.AddDays(20), hoje.AddDays(3), hoje.AddDays(3));
        var r = await svc.GravarMultaAsync(empresa, multa, adm, ct);
        Assert.True(r.Sucesso, string.Join(" ", r.Erros));
        Assert.Equal("O auto de infração E123 já está lançado.", (await svc.GravarMultaAsync(empresa, multa, adm, ct)).Erros[0]);
        Assert.False((await svc.GravarMultaAsync(empresa, multa with { AutoInfracao = "E9", Situacao = "PAGA" }, adm, ct)).Sucesso);

        var gravada = Assert.Single(await svc.MultasAsync(empresa, adm, ct));
        Assert.Equal(("E123", "QWE1R23", "Ana Souza"), (gravada.AutoInfracao, gravada.Placa, gravada.MotoristaNome));

        // o prazo de indicação do condutor aparece no painel da frota
        var aviso = Assert.Single(await svc.AvisosAsync(empresa, hoje, adm, ct), a => a.Assunto.StartsWith("Multa", StringComparison.Ordinal));
        Assert.Equal(GravidadeAviso.AVencer, aviso.Gravidade);
        Assert.StartsWith("Indicação do condutor", aviso.Descricao);

        // paga: sai dos avisos
        Assert.True((await svc.GravarMultaAsync(empresa, gravada with { Situacao = "PAGA", DataPagamento = hoje, ValorPago = 104.13m }, adm, ct)).Sucesso);
        Assert.DoesNotContain(await svc.AvisosAsync(empresa, hoje, adm, ct), a => a.Assunto.StartsWith("Multa", StringComparison.Ordinal));

        Assert.True((await svc.ExcluirMultaAsync(empresa, gravada.Id, adm, ct)).Sucesso);
        Assert.Empty(await svc.MultasAsync(empresa, adm, ct));
    }
}
