using Npgsql;
using Vorticis.Domain.Contabil;

namespace Vorticis.Infra.Tests;

/// <summary>Excluir e alterar lançamentos gravados; configuração "Usar estorno de lançamentos" (migração 049).</summary>
public class ExclusaoLancamentosTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static PartidaProposta D(string c, decimal v) => new(c, IndicadorDC.D, v, "Teste");
    private static PartidaProposta C(string c, decimal v) => new(c, IndicadorDC.C, v, "Teste");
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<LancamentoEfetivado> LancarAsync(long empresa, DateOnly data, decimal valor, OrigemLancamento origem = OrigemLancamento.Manual)
    {
        var r = await db.App.Lancamentos.EfetivarAsync(new LancamentoProposto(empresa, data, origem,
            [D("1.1.01.001", valor), C("3.1.01.001", valor)]), await db.AdminAsync(), Ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        return r.Valor;
    }

    private async Task ExecutarAsync(string sql)
    {
        await using var con = new NpgsqlConnection(db.ConnectionString);
        await con.OpenAsync(Ct);
        await using var cmd = new NpgsqlCommand(sql, con);
        await cmd.ExecuteNonQueryAsync(Ct);
    }

    private Task<decimal> DebitoCaixaAsync(long empresa, int mes) => db.EscalarAsync<decimal>("""
        SELECT COALESCE(SUM(s.total_debito), 0) FROM contabil.saldo_mensal s JOIN contabil.plano_contas c ON c.id = s.conta_id
         WHERE s.empresa_id = @e AND c.codigo = '1.1.01.001' AND s.competencia = make_date(2026, @m, 1)
        """, ("e", empresa), ("m", mes));

    private Task<long> AuditoriaAsync(long empresa, string operacao, long id) => db.EscalarAsync<long>(
        "SELECT COUNT(*) FROM core.auditoria WHERE empresa_id = @e AND operacao = @o AND registro_id = @i", ("e", empresa), ("o", operacao), ("i", id));

    private async Task SemEstornoAsync(long empresa, bool semEstorno)
    {
        var adm = await db.AdminAsync();
        var cfg = await db.App.PlanoContas.ObterConfiguracaoAsync(empresa, adm, Ct);
        var r = await db.App.PlanoContas.SalvarConfiguracaoAsync(empresa, cfg with { UsaEstorno = !semEstorno }, null, adm, Ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
    }

    [Fact]
    public async Task Configuracao_usa_estorno_vem_ligada_e_pode_ser_desligada()
    {
        BancoDeTeste.ExigirBanco();
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("11222333000181");
        Assert.True((await db.App.PlanoContas.ObterConfiguracaoAsync(empresa, adm, Ct)).UsaEstorno);
        Assert.True(await db.App.Lancamentos.UsaEstornoAsync(empresa, adm, Ct));
        await SemEstornoAsync(empresa, true);
        Assert.False((await db.App.PlanoContas.ObterConfiguracaoAsync(empresa, adm, Ct)).UsaEstorno);
        Assert.False(await db.App.Lancamentos.UsaEstornoAsync(empresa, adm, Ct));
    }

    [Fact]
    public async Task Contabilidade_gerencial_vem_desligada_e_e_gravada_sem_mexer_no_bloqueio()
    {
        BancoDeTeste.ExigirBanco();
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("73910456000162");
        var cfg = await db.App.PlanoContas.ObterConfiguracaoAsync(empresa, adm, Ct);
        Assert.False(cfg.UsaGerencial);
        var r = await db.App.PlanoContas.SalvarConfiguracaoAsync(empresa, cfg with { UsaGerencial = true, DataBloqueio = new DateOnly(2026, 1, 31) }, null, adm, Ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        var lido = await db.App.PlanoContas.ObterConfiguracaoAsync(empresa, adm, Ct);
        Assert.Equal((true, new DateOnly(2026, 1, 31)), (lido.UsaGerencial, lido.DataBloqueio));
    }

    [Fact]
    public async Task Excluir_remove_partidas_desfaz_saldo_audita_e_reaproveita_o_numero()
    {
        BancoDeTeste.ExigirBanco();
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("19131243000197");
        await LancarAsync(empresa, new(2026, 3, 5), 100m);
        var l2 = await LancarAsync(empresa, new(2026, 3, 6), 40m);
        Assert.Equal(140m, await DebitoCaixaAsync(empresa, 3));

        // O banco continua recusando exclusão direta, fora da função Excluir.
        await Assert.ThrowsAnyAsync<Exception>(() => ExecutarAsync($"DELETE FROM contabil.lancamento WHERE id = {l2.Id}"));

        var r = await db.App.Lancamentos.ExcluirAsync(empresa, l2.Id, adm, Ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        Assert.Equal(100m, await DebitoCaixaAsync(empresa, 3));
        Assert.Equal(0L, await db.EscalarAsync<long>("SELECT COUNT(*) FROM contabil.partida WHERE lancamento_id = @i", ("i", l2.Id)));
        Assert.Equal(0L, await db.EscalarAsync<long>("SELECT COUNT(*) FROM contabil.lancamento WHERE id = @i", ("i", l2.Id)));
        Assert.Equal(1L, await AuditoriaAsync(empresa, "EXCLUI", l2.Id));
        Assert.Equal(1L, await db.EscalarAsync<long>(
            "SELECT COUNT(*) FROM core.auditoria WHERE operacao = 'EXCLUI' AND registro_id = @i AND dados_depois->'lancamento'->'partidas' @> '[{\"valor\": 40.00}]'", ("i", l2.Id)));
        Assert.Equal(0L, await db.EscalarAsync<long>("SELECT COALESCE(core.fn_verificar_auditoria(), 0)"));

        Assert.Equal(l2.Numero, (await LancarAsync(empresa, new(2026, 3, 7), 5m)).Numero);   // o último número volta a ser usado
        Assert.Contains("não encontrado", (await db.App.Lancamentos.ExcluirAsync(empresa, l2.Id, adm, Ct)).Erros[0]);
    }

    [Fact]
    public async Task Excluir_nao_renumera_os_demais_e_exclui_o_par_de_estorno()
    {
        BancoDeTeste.ExigirBanco();
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("11444777000161");
        var l1 = await LancarAsync(empresa, new(2026, 4, 1), 10m);
        var l2 = await LancarAsync(empresa, new(2026, 4, 2), 20m);
        var l3 = await LancarAsync(empresa, new(2026, 4, 3), 30m);
        Assert.True((await db.App.Lancamentos.ExcluirAsync(empresa, l2.Id, adm, Ct)).Sucesso);
        Assert.Equal([l1.Numero, l3.Numero], (await db.EscalarAsync<long[]>(
            "SELECT array_agg(numero ORDER BY numero) FROM contabil.lancamento WHERE empresa_id = @e", ("e", empresa))));

        var est = await db.App.Lancamentos.EstornarAsync(empresa, l3.Id, new(2026, 4, 4), "teste", adm, Ct);
        Assert.True(est.Sucesso);
        var r = await db.App.Lancamentos.ExcluirAsync(empresa, l3.Id, adm, Ct);   // original + estorno juntos
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        Assert.Equal(2, r.Valor.Count);
        Assert.Equal(1L, await db.EscalarAsync<long>("SELECT COUNT(*) FROM contabil.lancamento WHERE empresa_id = @e", ("e", empresa)));
        Assert.Equal(10m, await DebitoCaixaAsync(empresa, 4));
    }

    [Fact]
    public async Task Excluir_e_bloqueado_em_periodo_fechado_ecd_gerada_e_exercicio_encerrado()
    {
        BancoDeTeste.ExigirBanco();
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("34028316000103");
        var l = await LancarAsync(empresa, new(2026, 1, 10), 50m);

        Assert.True((await db.App.Periodos.FecharAsync(empresa, new(2026, 1, 1), adm, Ct)).Sucesso);
        Assert.Contains("fechado", (await db.App.Lancamentos.ExcluirAsync(empresa, l.Id, adm, Ct)).Erros[0]);
        Assert.True((await db.App.Periodos.ReabrirAsync(empresa, new(2026, 1, 1), adm, Ct)).Sucesso);

        await ExecutarAsync($"""
            INSERT INTO contabil.ecd_gerada (empresa_id, ano, dt_ini, dt_fin, num_ordem, nat_livro, hash_sha1, linhas)
            VALUES ({empresa}, 2026, '2026-01-01', '2026-12-31', 1, 'GERAL', '{new string('A', 40)}', 10)
            """);
        Assert.Contains("ECD", (await db.App.Lancamentos.ExcluirAsync(empresa, l.Id, adm, Ct)).Erros[0]);
        await ExecutarAsync($"DELETE FROM contabil.ecd_gerada WHERE empresa_id = {empresa}");

        await ExecutarAsync($"UPDATE contabil.exercicio SET encerrado = TRUE WHERE empresa_id = {empresa} AND ano = 2026");
        Assert.Contains("encerrado", (await db.App.Lancamentos.ExcluirAsync(empresa, l.Id, adm, Ct)).Erros[0]);
        Assert.Equal(1L, await db.EscalarAsync<long>("SELECT COUNT(*) FROM contabil.lancamento WHERE id = @i", ("i", l.Id)));
    }

    [Fact]
    public async Task Lancamento_de_outro_modulo_so_se_desfaz_no_modulo_de_origem()
    {
        BancoDeTeste.ExigirBanco();
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("60701190000104");
        var folha = await LancarAsync(empresa, new(2026, 6, 30), 70m, OrigemLancamento.Folha);
        var depr = await LancarAsync(empresa, new(2026, 6, 30), 80m, OrigemLancamento.Depreciacao);
        var nfe = await LancarAsync(empresa, new(2026, 6, 30), 90m, OrigemLancamento.NFe);
        Assert.Contains("Folha › Contabilização", (await db.App.Lancamentos.ExcluirAsync(empresa, folha.Id, adm, Ct)).Erros[0]);
        Assert.Contains("Imobilizado", (await db.App.Lancamentos.ExcluirAsync(empresa, depr.Id, adm, Ct)).Erros[0]);
        Assert.Contains("Fiscal", (await db.App.Lancamentos.ExcluirAsync(empresa, nfe.Id, adm, Ct)).Erros[0]);
        Assert.Equal(3L, await db.EscalarAsync<long>("SELECT COUNT(*) FROM contabil.lancamento WHERE empresa_id = @e", ("e", empresa)));
    }

    [Fact]
    public async Task Edicao_sem_estorno_regrava_no_mesmo_numero_com_saldo_e_auditoria()
    {
        BancoDeTeste.ExigirBanco();
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("07526557000100");
        var l = await LancarAsync(empresa, new(2026, 1, 10), 100m);
        await SemEstornoAsync(empresa, true);

        var nova = new LancamentoProposto(empresa, new(2026, 1, 20), OrigemLancamento.Manual, [D("1.1.01.001", 250m), C("3.1.01.001", 250m)]);
        var r = await db.App.Lancamentos.SubstituirAsync(l.Id, nova, adm, Ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        Assert.Equal([new LancamentoEfetivado(l.Id, l.Numero)], r.Valor);
        Assert.Equal(250m, await DebitoCaixaAsync(empresa, 1));
        Assert.Equal("2026-01-20", await db.EscalarAsync<string>("SELECT data_lancamento::text FROM contabil.lancamento WHERE id = @i", ("i", l.Id)));
        Assert.Equal(1L, await db.EscalarAsync<long>("SELECT COUNT(*) FROM contabil.lancamento WHERE empresa_id = @e", ("e", empresa)));
        Assert.Equal(1L, await AuditoriaAsync(empresa, "ALTERA", l.Id));
        Assert.Equal(0L, await db.EscalarAsync<long>("SELECT COALESCE(core.fn_verificar_auditoria(), 0)"));

        // Período fechado impede a alteração sem estorno.
        Assert.True((await db.App.Periodos.FecharAsync(empresa, new(2026, 1, 1), adm, Ct)).Sucesso);
        Assert.Contains("fechado", (await db.App.Lancamentos.SubstituirAsync(l.Id, nova, adm, Ct)).Erros[0]);
        Assert.Equal(250m, await DebitoCaixaAsync(empresa, 1));
    }

    [Fact]
    public async Task Edicao_com_estorno_continua_estornando_e_regravando()
    {
        BancoDeTeste.ExigirBanco();
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("45997418000153");
        var l = await LancarAsync(empresa, new(2026, 9, 10), 100m);
        var nova = new LancamentoProposto(empresa, new(2026, 9, 10), OrigemLancamento.Manual, [D("1.1.01.001", 120m), C("3.1.01.001", 120m)]);
        var r = await db.App.Lancamentos.SubstituirAsync(l.Id, nova, adm, Ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        Assert.Equal(2, r.Valor.Count);
        Assert.Equal(3L, await db.EscalarAsync<long>("SELECT COUNT(*) FROM contabil.lancamento WHERE empresa_id = @e", ("e", empresa)));
    }
}
