using Vorticis.Domain.Contabil;

namespace Vorticis.Domain.Tests;

public class ValidadorLancamentoTests
{
    private static readonly DateOnly Hoje = new(2026, 3, 10);

    private static readonly Dictionary<string, ContaInfo> Contas = new()
    {
        ["1.1.01.001"] = new(1, "1.1.01.001", "Caixa", true, true, new(2026, 1, 1), false, false),
        ["1.1.02.001"] = new(2, "1.1.02.001", "Clientes", true, true, new(2026, 1, 1), false, true),
        ["1.1.01"] = new(3, "1.1.01", "Disponível", false, true, new(2026, 1, 1), false, false),
        ["4.2.02.001"] = new(4, "4.2.02.001", "Aluguéis", true, true, new(2026, 1, 1), true, false),
        ["9.9.99.999"] = new(5, "9.9.99.999", "Inativa", true, false, new(2026, 1, 1), false, false),
        ["1.1.01.009"] = new(6, "1.1.01.009", "Nova", true, true, new(2026, 6, 1), false, false),
    };

    private static ContextoValidacao Ctx(bool periodoAberto = true) => new(
        Contas,
        new Dictionary<string, long> { ["ADM"] = 10 },
        new Dictionary<string, long> { ["CLI001"] = 20 },
        new PeriodoInfo(7, new(2026, 1, 1), new(2026, 12, 31), false, true, periodoAberto));

    private static LancamentoProposto L(params PartidaProposta[] p) => new(1, Hoje, OrigemLancamento.Manual, p);

    private static PartidaProposta D(string conta, decimal v, string? cc = null, string? part = null) => new(conta, IndicadorDC.D, v, "Histórico", cc, part);
    private static PartidaProposta C(string conta, decimal v, string? cc = null, string? part = null) => new(conta, IndicadorDC.C, v, "Histórico", cc, part);

    [Fact]
    public void Aceita_lancamento_balanceado_com_varias_partidas()
    {
        var r = ValidadorLancamento.Validar(L(D("1.1.01.001", 700m), D("4.2.02.001", 300.55m, cc: "ADM"), C("1.1.02.001", 1000.55m, part: "CLI001")), Ctx());

        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        Assert.Equal(7, r.Valor.ExercicioId);
        Assert.Equal(1000.55m, r.Valor.Total);
        Assert.Equal([1, 2, 3], r.Valor.Partidas.Select(p => (int)p.Sequencia));
        Assert.Equal(10, r.Valor.Partidas[1].CentroCustoId);
        Assert.Equal(20, r.Valor.Partidas[2].ParticipanteId);
    }

    [Fact]
    public void Rejeita_debito_diferente_de_credito_informando_a_diferenca()
    {
        var r = ValidadorLancamento.Validar(L(D("1.1.01.001", 100m), C("1.1.02.001", 99.99m, part: "CLI001")), Ctx());
        Assert.Contains(r.Erros, e => e.Contains("Diferença") && e.Contains("0,01"));
    }

    [Fact]
    public void Rejeita_lancamento_sem_credito()
    {
        var r = ValidadorLancamento.Validar(L(D("1.1.01.001", 100m)), Ctx());
        Assert.Contains(r.Erros, e => e.Contains("ao menos uma partida a débito e uma a crédito"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Rejeita_valor_nao_positivo(decimal valor)
    {
        var r = ValidadorLancamento.Validar(L(D("1.1.01.001", valor), C("1.1.01.001", valor)), Ctx());
        Assert.Contains(r.Erros, e => e.Contains("deve ser positivo"));
    }

    [Fact]
    public void Rejeita_valor_com_mais_de_duas_casas()
    {
        var r = ValidadorLancamento.Validar(L(D("1.1.01.001", 10.005m), C("1.1.01.001", 10.005m)), Ctx());
        Assert.Contains(r.Erros, e => e.Contains("mais de 2 casas"));
    }

    [Fact]
    public void Acumula_erros_de_conta_sintetica_inativa_inexistente_e_posterior()
    {
        var r = ValidadorLancamento.Validar(L(D("1.1.01", 10), D("9.9.99.999", 10), D("1.1.01.009", 10), C("0.0", 30)), Ctx());

        Assert.Contains(r.Erros, e => e.Contains("é sintética"));
        Assert.Contains(r.Erros, e => e.Contains("inativa"));
        Assert.Contains(r.Erros, e => e.Contains("após a data do lançamento"));
        Assert.Contains(r.Erros, e => e.Contains("conta 0.0 não encontrada"));
    }

    [Fact]
    public void Exige_centro_de_custo_e_participante_quando_a_conta_exige()
    {
        var r = ValidadorLancamento.Validar(L(D("4.2.02.001", 10), C("1.1.02.001", 10)), Ctx());
        Assert.Contains(r.Erros, e => e.Contains("exige centro de custo"));
        Assert.Contains(r.Erros, e => e.Contains("exige participante"));
    }

    [Fact]
    public void Rejeita_periodo_fechado_e_exercicio_inexistente()
    {
        var l = L(D("1.1.01.001", 10), C("1.1.01.001", 10));
        Assert.Contains(ValidadorLancamento.Validar(l, Ctx(periodoAberto: false)).Erros, e => e.Contains("está fechado"));
        Assert.Contains(ValidadorLancamento.Validar(l, Ctx() with { Periodo = null }).Erros, e => e.Contains("Não há exercício"));
    }

    [Fact]
    public void Encerramento_so_na_data_final_do_exercicio()
    {
        var l = L(D("1.1.01.001", 10), C("1.1.01.001", 10)) with { Indicador = IndicadorLancamento.Encerramento };
        Assert.Contains(ValidadorLancamento.Validar(l, Ctx()).Erros, e => e.Contains("data final do exercício"));
        Assert.True(ValidadorLancamento.Validar(l with { Data = new(2026, 12, 31) }, Ctx()).Sucesso);
    }

    [Fact]
    public void Extemporaneo_exige_data_do_fato()
    {
        var l = L(D("1.1.01.001", 10), C("1.1.01.001", 10)) with { Indicador = IndicadorLancamento.Extemporaneo };
        Assert.Contains(ValidadorLancamento.Validar(l, Ctx()).Erros, e => e.Contains("extemporâneo"));
        Assert.True(ValidadorLancamento.Validar(l with { DataExtemporanea = new(2025, 11, 3) }, Ctx()).Sucesso);
    }

    [Fact]
    public void Historico_obrigatorio()
    {
        var r = ValidadorLancamento.Validar(L(D("1.1.01.001", 10) with { Historico = "  " }, C("1.1.01.001", 10)), Ctx());
        Assert.Contains(r.Erros, e => e.Contains("Linha 1: histórico obrigatório"));
    }
}
