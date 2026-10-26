using Vorticis.Application.Contabil;
using Vorticis.Domain.Contabil;
using Vorticis.Folha.Servicos;

namespace Vorticis.Folha.Tests;

/// <summary>Folha em contabilização em lote: o clique em Contabilizar só registra a pendência; a tela de lote gera o mesmo lançamento.</summary>
public class ContabilizacaoLoteFolhaTests(BancoFolha db) : IClassFixture<BancoFolha>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<long> LancamentosDaFolhaAsync(long empresa) =>
        await db.Escalar<long>("SELECT count(*) FROM contabil.lancamento WHERE empresa_id = @e AND origem::text = 'FOLHA'", ("e", empresa));

    [Fact]
    public async Task Folha_em_lote_registra_pendencia_e_o_lote_gera_o_mesmo_lancamento_e_desfaz()
    {
        BancoFolha.ExigirBanco();
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("11222333000181");
        var folha = db.Folha.Folha;
        var contab = db.Folha.Contabilizacao;
        var pend = db.App.Pendencias;
        var competencia = new DateOnly(2026, 6, 1);

        Assert.True((await db.Folha.Cadastros.GravarFuncionarioAsync(empresa, FolhaIntegracaoTests.Novo("1", "Maria da Silva", "52998224725", 3000m), adm, ct: Ct)).Sucesso);
        Assert.True((await folha.CalcularMensalAsync(empresa, competencia, adm, ct: Ct)).Sucesso);
        Assert.True((await folha.FecharAsync(empresa, competencia, adm, Ct)).Sucesso);
        Assert.True((await contab.GerarRegrasSugeridasAsync(empresa, comProvisoes: true, adm, Ct)).Sucesso);
        var previa = (await contab.PreviaAsync(empresa, competencia, "FOLHA", adm, Ct)).Valor;

        // em lote: Contabilizar não lança (lançamento 0/0) e deixa a pendência
        Assert.True((await pend.GravarModoAsync(empresa, ModoContabilizacao.Lote, adm, Ct)).Sucesso);
        var r = await contab.ContabilizarAsync(empresa, competencia, "FOLHA", adm, Ct);
        Assert.True(r.Sucesso, string.Join(";", r.Erros));
        Assert.Equal(0, r.Valor.Numero);
        Assert.Equal(0L, await LancamentosDaFolhaAsync(empresa));
        Assert.Empty(await contab.ListarAsync(empresa, competencia, adm, Ct));
        var p = (await pend.ListarAsync(empresa, new FiltroPendencias(OrigensPendencia.Folha), adm, Ct)).Valor.Single();
        Assert.Equal((competencia, previa.Total), (p.Periodo, p.Valor));
        Assert.Equal("FOLHA", p.Tipo);
        // repetir o clique não duplica a pendência
        Assert.True((await contab.ContabilizarAsync(empresa, competencia, "FOLHA", adm, Ct)).Sucesso);
        Assert.Single((await pend.ListarAsync(empresa, new FiltroPendencias(), adm, Ct)).Valor);
        Assert.Contains((await pend.PreviaAsync(empresa, [p.Id], adm, Ct)).Valor, l => l.Length > 0);

        // o lote contabiliza com as mesmas regras (a mesma prévia, o mesmo total)
        var lote = (await pend.ContabilizarAsync(empresa, [p.Id], adm, Ct)).Valor;
        Assert.Equal(1, lote.Contabilizadas);
        var registro = Assert.Single(await contab.ListarAsync(empresa, competencia, adm, Ct));
        Assert.Null(registro.DesfeitaEm);
        Assert.Equal(1L, await LancamentosDaFolhaAsync(empresa));
        Assert.Equal(previa.Total, await db.Escalar<decimal>("SELECT COALESCE(SUM(valor), 0) FROM contabil.partida WHERE lancamento_id = @l AND ind_dc = 'D'", ("l", registro.LancamentoId)));
        Assert.Contains("Já contabilizado", (await contab.ContabilizarAsync(empresa, competencia, "FOLHA", adm, Ct, imediato: true)).Erros[0]);

        // desfazer lote: estorna o lançamento da folha e a pendência volta
        Assert.True((await pend.DesfazerLoteAsync(empresa, lote.LoteId!.Value, adm, Ct)).Sucesso);
        Assert.Equal("ESTORNADO", await db.Escalar<string>("SELECT status::text FROM contabil.lancamento WHERE id = @l", ("l", registro.LancamentoId)));
        Assert.DoesNotContain(await contab.ListarAsync(empresa, competencia, adm, Ct), c => c.DesfeitaEm is null);
        Assert.Single((await pend.ListarAsync(empresa, new FiltroPendencias(), adm, Ct)).Valor);

        // no modo on-line segue como sempre: lança na hora
        Assert.True((await pend.GravarModoAsync(empresa, ModoContabilizacao.Online, adm, Ct)).Sucesso);
        var online = await contab.ContabilizarAsync(empresa, competencia, "FOLHA", adm, Ct);
        Assert.True(online.Sucesso);
        Assert.True(online.Valor.Numero > 0);
    }
}
