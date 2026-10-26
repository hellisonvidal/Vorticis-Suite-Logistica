using Vorticis.Application.Contabil;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;

namespace Vorticis.Infra.Tests;

public class ImportacaoTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    [Fact]
    public async Task Layout_salvo_e_planilha_importada_tudo_ou_nada()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var svc = db.App.Importacao;
        var empresa = await db.CriarEmpresaAsync("60701190000104");
        var adm = await db.AdminAsync();

        var layout = new LayoutImportacao { Nome = "Vendas do PDV " + Guid.NewGuid().ToString("N")[..6], ColunaData = "Data", ColunaContaDebito = "Debito",
                                            ContaCreditoPadrao = "3.1.01.001", ColunaValor = "Valor", ColunaHistorico = "Historico" };
        var id = await svc.SalvarLayoutAsync(empresa, null, layout, adm, ct);
        Assert.True(id.Sucesso, string.Join("; ", id.Erros));
        Assert.Contains("Já existe", (await svc.SalvarLayoutAsync(empresa, null, layout, adm, ct)).Erros[0]);
        Assert.Contains("coluna da conta a débito", (await svc.SalvarLayoutAsync(empresa, null, layout with { Nome = "x", ColunaContaDebito = null }, adm, ct)).Erros[0]);
        var salvo = (await svc.ListarLayoutsAsync(ct)).Single(l => l.Id == id.Valor);
        Assert.Equal(layout, salvo.Layout);

        var tabela = TabelaImportada.DeTexto("Data;Debito;Valor;Historico\n10/03/2026;1.1.01.001;100,00;Venda 1\n11/03/2026;1.1.01.999;50,00;Venda 2\n");
        var lidos = ImportadorLancamentos.Ler(tabela, salvo.Layout);
        Assert.Empty(lidos.Erros);
        var preparo = await svc.PrepararAsync(empresa, lidos.Lancamentos, OrigemLancamento.Planilha, adm, ct);
        Assert.True(preparo.Valor[0].Valido);
        Assert.Contains("1.1.01.999", string.Join(" ", preparo.Valor[1].Erros));
        var falha = await svc.ImportarAsync(empresa, lidos.Lancamentos, OrigemLancamento.Planilha, adm, ct);
        Assert.Contains("Linha 3", falha.Erros[0]);
        Assert.Equal(0L, await db.EscalarAsync<long>("SELECT count(*) FROM contabil.lancamento WHERE empresa_id = @e", ("e", empresa)));

        var ok = await svc.ImportarAsync(empresa, lidos.Lancamentos.Take(1).ToList(), OrigemLancamento.Planilha, adm, ct);
        Assert.True(ok.Sucesso, string.Join("; ", ok.Erros));
        Assert.Equal(1, ok.Valor[0].Numero);
        Assert.True((await svc.ExcluirLayoutAsync(empresa, id.Valor, adm, ct)).Sucesso);
        Assert.DoesNotContain(await svc.ListarLayoutsAsync(ct), l => l.Id == id.Valor);
    }

    [Fact]
    public async Task Extrato_ofx_nao_e_importado_duas_vezes()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var svc = db.App.Importacao;
        var empresa = await db.CriarEmpresaAsync("33683111000107");
        var adm = await db.AdminAsync();
        var extrato = LeitorOfx.Ler("""
            <OFX><BANKACCTFROM><BANKID>341<BRANCHID>0001<ACCTID>12345-6</BANKACCTFROM><BANKTRANLIST>
            <STMTTRN><TRNTYPE>CREDIT<DTPOSTED>20260305<TRNAMT>1500.00<FITID>X1<MEMO>Recebimento cliente
            <STMTTRN><TRNTYPE>DEBIT<DTPOSTED>20260306<TRNAMT>-89.90<FITID>X2<MEMO>Tarifa
            </BANKTRANLIST></OFX>
            """).Valor;
        var lidos = ServicoImportacao.LancamentosDoExtrato(extrato, extrato.Transacoes, "1.1.01.002", ["1.1.02.001", "4.2.02.001"], "Extrato:");
        Assert.Equal(("1.1.01.002", IndicadorDC.D), (lidos[0].Partidas[0].CodigoConta, lidos[0].Partidas[0].DC));
        Assert.Equal(("4.2.02.001", "1.1.01.002"), (lidos[1].Partidas[0].CodigoConta, lidos[1].Partidas[1].CodigoConta));
        Assert.Equal("Extrato: Tarifa", lidos[1].Partidas[0].Historico);

        var r = await svc.ImportarAsync(empresa, lidos, OrigemLancamento.Extrato, adm, ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        Assert.Equal(2, r.Valor.Count);
        var preparo = await svc.PrepararAsync(empresa, lidos, OrigemLancamento.Extrato, adm, ct);
        Assert.All(preparo.Valor, i => Assert.True(i.JaImportado));
        Assert.Contains("já foram importados", (await svc.ImportarAsync(empresa, lidos, OrigemLancamento.Extrato, adm, ct)).Erros[0]);

        // Estornado o lançamento, a transação pode ser importada de novo
        Assert.True((await db.App.Lancamentos.EstornarAsync(empresa, r.Valor[1].Id, new DateOnly(2026, 3, 6), "classificação errada", adm, ct)).Sucesso);
        var de_novo = await svc.ImportarAsync(empresa, lidos, OrigemLancamento.Extrato, adm, ct);
        Assert.True(de_novo.Sucesso, string.Join("; ", de_novo.Erros));
        Assert.Single(de_novo.Valor);
    }

    [Fact]
    public void Extrato_em_pdf_vira_linhas_com_colunas_e_o_layout_sugerido_reconhece_os_movimentos()
    {
        var tabela = new Vorticis.Application.Relatorios.Bloco(Titulo: "Movimentação",
            Colunas: [new("Data", 2.5), new("Histórico", 7), new("Documento", 3), new("Valor", 3), new("Saldo", 3)],
            Linhas:
            [
                new(["02/03/2026", "PIX RECEBIDO CLIENTE ABC", "123456", "1.500,00", "2.500,00"]),
                new(["05/03/2026", "TARIFA PACOTE", "000001", "-45,90", "2.454,10"]),
                new(["06/03/2026", "SALDO DO DIA", "", "", "2.454,10"]),
            ]);
        var pdf = new Vorticis.Infra.Relatorios.GeradorPdf().Gerar(new Vorticis.Application.Relatorios.DocumentoRelatorio("Extrato de conta corrente", ["Banco Exemplo"], [tabela]));
        var linhas = Vorticis.Infra.Importacao.LeitorDocumentoExtrato.LerPdf(pdf.Conteudo);
        Assert.Contains(linhas, l => l.Contains("PIX RECEBIDO CLIENTE ABC"));
        var layout = LeitorExtratoTexto.Sugerir(linhas, 2026);
        Assert.Equal("dd/MM/yyyy", layout.FormatoData);
        var ts = LeitorExtratoTexto.Extrato(LeitorExtratoTexto.Aplicar(linhas, layout, 2026), "EXTRATO").Transacoes;
        Assert.Equal([1500m, -45.90m], ts.Select(t => t.Valor));
        Assert.StartsWith("PIX RECEBIDO CLIENTE ABC", ts[0].Descricao);
    }

    [Fact]
    public async Task Layout_de_extrato_e_salvo_separado_dos_layouts_de_planilha()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var svc = db.App.Importacao;
        var empresa = await db.CriarEmpresaAsync("19131243000197");
        var adm = await db.AdminAsync();
        var nome = $"Banco PDF {Guid.NewGuid():N}"[..20];
        Assert.False((await svc.SalvarLayoutExtratoAsync(empresa, null, new LayoutExtrato(""), adm, ct)).Sucesso);
        Assert.False((await svc.SalvarLayoutExtratoAsync(empresa, null, new LayoutExtrato(nome, ColunaData: 0), adm, ct)).Sucesso);
        var r = await svc.SalvarLayoutExtratoAsync(empresa, null, new LayoutExtrato(nome, SeparadorExtrato.PontoVirgula, 2, 3, 4, 0, "dd/MM", "SALDO, TOTAL", true), adm, ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        var salvo = Assert.Single(await svc.ListarLayoutsExtratoAsync(ct), l => l.Id == r.Valor);
        Assert.Equal((SeparadorExtrato.PontoVirgula, 2, 4, "dd/MM", true), (salvo.Layout.Separador, salvo.Layout.ColunaData, salvo.Layout.ColunaValor, salvo.Layout.FormatoData, salvo.Layout.InverterSinal));
        Assert.DoesNotContain(await svc.ListarLayoutsAsync(ct), l => l.Id == r.Valor);
        Assert.Contains("Já existe", (await svc.SalvarLayoutExtratoAsync(empresa, null, new LayoutExtrato(nome.ToUpperInvariant()), adm, ct)).Erros[0]);
        Assert.True((await svc.ExcluirLayoutAsync(empresa, r.Valor, adm, ct)).Sucesso);
        Assert.DoesNotContain(await svc.ListarLayoutsExtratoAsync(ct), l => l.Id == r.Valor);
    }
}
