using Npgsql;
using Vorticis.Application.Sistema;
using Vorticis.Domain.Contabil;

namespace Vorticis.Infra.Tests;

/// <summary>Lançamentos › Utilitários: reorganizar a numeração dos lançamentos e recalcular os saldos mensais.</summary>
public class ReorganizacaoTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private const string Senha = ServicoConfiguracoesGerais.SenhaAdministradorPadrao;
    private static PartidaProposta D(string c, decimal v) => new(c, IndicadorDC.D, v, "Teste");
    private static PartidaProposta C(string c, decimal v) => new(c, IndicadorDC.C, v, "Teste");

    private async Task LancarAsync(long empresa, DateOnly data, decimal valor)
    {
        var r = await db.App.Lancamentos.EfetivarAsync(new LancamentoProposto(empresa, data, OrigemLancamento.Manual,
            [D("1.1.01.001", valor), C("3.1.01.001", valor)]), await db.AdminAsync(), TestContext.Current.CancellationToken);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
    }

    private async Task<Dictionary<DateOnly, long>> NumerosAsync(long empresa)
    {
        await using var con = new NpgsqlConnection(db.ConnectionString);
        await con.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT data_lancamento, numero FROM contabil.lancamento WHERE empresa_id = @e", con);
        cmd.Parameters.AddWithValue("e", empresa);
        var d = new Dictionary<DateOnly, long>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) d[DateOnly.FromDateTime(r.GetDateTime(0))] = r.GetInt64(1);
        return d;
    }

    private async Task ExecutarAsync(string sql)
    {
        await using var con = new NpgsqlConnection(db.ConnectionString);
        await con.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, con);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Lacunas_e_ordem_de_data_sao_renumeradas_com_auditoria()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("11222333000181");
        await LancarAsync(empresa, new(2026, 3, 5), 10m);    // nº 1
        await LancarAsync(empresa, new(2026, 1, 10), 20m);   // nº 2
        await LancarAsync(empresa, new(2026, 2, 20), 30m);   // nº 3
        // Simula lacuna e desordem (só possíveis por falha/importação): o trigger só admite a troca com a reorganização declarada.
        await Assert.ThrowsAnyAsync<Exception>(() => ExecutarAsync($"UPDATE contabil.lancamento SET numero = numero + 10 WHERE empresa_id = {empresa}"));
        await using (var con = new NpgsqlConnection(db.ConnectionString))
        {
            await con.OpenAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await using var cmd = new NpgsqlCommand($"SELECT set_config('vorticis.reorganizando_lancamentos', '{empresa}', true); UPDATE contabil.lancamento SET numero = numero * 4 WHERE empresa_id = {empresa}", con, tx);
            await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
        }
        Assert.Equal([4L, 8L, 12L], (await NumerosAsync(empresa)).Values.Order());

        Assert.Contains((await db.App.Reorganizacao.ReorganizarLancamentosAsync(empresa, 2026, "errada", adm, ct)).Erros, e => e.Contains("Senha"));
        var r = await db.App.Reorganizacao.ReorganizarLancamentosAsync(empresa, 2026, Senha, adm, ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        Assert.Equal((3, 3), (r.Valor.Total, r.Valor.Renumerados));
        var n = await NumerosAsync(empresa);
        Assert.Equal((1L, 2L, 3L), (n[new(2026, 1, 10)], n[new(2026, 2, 20)], n[new(2026, 3, 5)]));
        Assert.True(await db.EscalarAsync<bool>("SELECT core.fn_verificar_auditoria() IS NULL"));
        Assert.True(await db.EscalarAsync<bool>("SELECT COUNT(*) = 1 FROM core.auditoria WHERE empresa_id = @e AND operacao = 'REORGANIZA' AND dados_depois->>'acao' = 'reorganizar_lancamentos'", ("e", empresa)));
        // Já organizado: nada renumerado; o próximo lançamento segue a sequência.
        Assert.Equal(0, (await db.App.Reorganizacao.ReorganizarLancamentosAsync(empresa, 2026, Senha, adm, ct)).Valor.Renumerados);
        await LancarAsync(empresa, new(2026, 4, 1), 5m);
        Assert.Equal(4L, (await NumerosAsync(empresa))[new(2026, 4, 1)]);
    }

    [Fact]
    public async Task Reorganizar_atualiza_a_copia_do_numero_guardada_pela_folha()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("60701190000104");
        await LancarAsync(empresa, new(2026, 3, 5), 10m);    // nº 1
        await LancarAsync(empresa, new(2026, 1, 10), 20m);   // nº 2 (a data anterior passa a ser o nº 1)
        var lanc = await db.EscalarAsync<long>("SELECT id FROM contabil.lancamento WHERE empresa_id = @e AND numero = 1", ("e", empresa));
        await db.EscalarAsync<long>("INSERT INTO folha.contabilizacao (empresa_id, competencia, tipo, lancamento_id, numero_lancamento, valor_total) VALUES (@e, '2026-03-01', 'FOLHA', @l, 1, 10) RETURNING id", ("e", empresa), ("l", lanc));
        Assert.True((await db.App.Reorganizacao.ReorganizarLancamentosAsync(empresa, 2026, Senha, adm, ct)).Sucesso);
        Assert.Equal(2L, await db.EscalarAsync<long>("SELECT numero_lancamento FROM folha.contabilizacao WHERE lancamento_id = @l", ("l", lanc)));
        Assert.Equal(2L, await db.EscalarAsync<long>("SELECT numero FROM contabil.lancamento WHERE id = @l", ("l", lanc)));
    }

    [Fact]
    public async Task Periodo_fechado_ou_ecd_gerada_bloqueiam_a_reorganizacao()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("19131243000197");
        await LancarAsync(empresa, new(2026, 1, 10), 10m);
        Assert.True((await db.App.Periodos.FecharAsync(empresa, new(2026, 1, 1), adm, ct)).Sucesso);
        var fechado = await db.App.Reorganizacao.ReorganizarLancamentosAsync(empresa, 2026, Senha, adm, ct);
        Assert.Contains(fechado.Erros, e => e.Contains("01/2026"));
        Assert.True((await db.App.Periodos.ReabrirAsync(empresa, new(2026, 1, 1), adm, ct)).Sucesso);

        await ExecutarAsync($"""
            INSERT INTO contabil.ecd_gerada (empresa_id, ano, dt_ini, dt_fin, num_ordem, nat_livro, hash_sha1, linhas)
            VALUES ({empresa}, 2026, '2026-01-01', '2026-12-31', 1, 'GERAL', '{new string('A', 40)}', 10)
            """);
        Assert.Contains((await db.App.Reorganizacao.ReorganizarLancamentosAsync(empresa, 2026, Senha, adm, ct)).Erros, e => e.Contains("ECD"));
        Assert.Contains((await db.App.Reorganizacao.ReorganizarLancamentosAsync(empresa, 2020, Senha, adm, ct)).Erros, e => e.Contains("não cadastrado"));
    }

    [Fact]
    public async Task Saldos_corrompidos_sao_recalculados_com_relatorio_antes_e_depois()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("33000167000101");
        await LancarAsync(empresa, new(2026, 5, 4), 100m);
        await LancarAsync(empresa, new(2026, 5, 9), 50m);
        Assert.Empty((await db.App.Reorganizacao.ReorganizarSaldosAsync(empresa, 2026, adm, ct)).Valor);   // íntegro: nada a corrigir

        await ExecutarAsync($"""
            UPDATE contabil.saldo_mensal SET total_debito = 999
             WHERE empresa_id = {empresa} AND conta_id = (SELECT id FROM contabil.plano_contas WHERE empresa_id = {empresa} AND codigo = '1.1.01.001');
            DELETE FROM contabil.saldo_mensal
             WHERE empresa_id = {empresa} AND conta_id = (SELECT id FROM contabil.plano_contas WHERE empresa_id = {empresa} AND codigo = '3.1.01.001')
            """);
        var r = await db.App.Reorganizacao.ReorganizarSaldosAsync(empresa, 2026, adm, ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        Assert.Equal(2, r.Valor.Count);
        var caixa = r.Valor.Single(a => a.CodigoConta == "1.1.01.001");
        Assert.Equal((999m, 150m, new DateOnly(2026, 5, 1)), (caixa.DebitoAntes, caixa.DebitoDepois, caixa.Competencia));
        var venda = r.Valor.Single(a => a.CodigoConta == "3.1.01.001");
        Assert.Equal((0m, 150m), (venda.CreditoAntes, venda.CreditoDepois));
        Assert.Empty((await db.App.Reorganizacao.ReorganizarSaldosAsync(empresa, 2026, adm, ct)).Valor);
        var bal = (await db.App.Relatorios.BalanceteAsync(empresa, new(2026, 5, 1), new(2026, 5, 31), adm, ct: ct)).Valor;
        Assert.Equal(150m, bal.Single(l => l.Codigo == "1.1.01.001").SaldoFinal);
        Assert.True(await db.EscalarAsync<bool>("SELECT COUNT(*) = 3 FROM core.auditoria WHERE empresa_id = @e AND dados_depois->>'acao' = 'reorganizar_saldos'", ("e", empresa)));
    }
}
