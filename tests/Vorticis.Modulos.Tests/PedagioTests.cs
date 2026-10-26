using Vorticis.Domain.Comum;
using Vorticis.Fiscal;
using Vorticis.Fiscal.Documentos;
using Vorticis.Frota;
using Vorticis.Infra.Tests;

namespace Vorticis.Modulos.Tests;

public class RegrasPedagioTests
{
    internal static readonly LayoutPedagio SemParar = new(1, "Sem Parar", 10, "Data", "Hora", "Placa", "Praça", "Valor (R$)", Operadora: "Sem Parar SA");

    internal static TabelaImportada Arquivo(params string[] linhas) =>
        TabelaImportada.DeTexto(string.Join("\n", ["Data;Hora;Placa;Praça;Valor (R$)", .. linhas]));

    [Fact]
    public void Le_as_passagens_pelo_layout_e_recusa_linhas_ruins_com_o_numero()
    {
        var (passagens, erros) = RegrasPedagio.Ler(Arquivo(
            "02/03/2026;08:15;abc-1d23;Praça Jundiaí;12,40",
            "02/03/2026;17:40;ABC1D23;Praça Campinas;9,80",
            "03/03/2026;;BRA2E19;Praça Itu;7,10",
            "31/02/2026;10:00;BRA2E19;X;5,00",
            "04/03/2026;10:00;XX;Y;5,00",
            ";;;Total;29,30"), SemParar);
        Assert.Equal(3, passagens.Count);
        Assert.Equal((new DateTime(2026, 3, 2, 8, 15, 0), "ABC1D23", "Praça Jundiaí", 12.40m), (passagens[0].DataHora, passagens[0].Placa, passagens[0].Praca, passagens[0].Valor));
        Assert.Equal(new DateTime(2026, 3, 3), passagens[2].DataHora);              // sem hora: meia-noite
        Assert.Equal(["Linha 5: data inválida.", "Linha 6: placa inválida (XX)."], erros);
        // linha de total (sem placa) é ignorada sem erro; o arquivo de outra operadora é recusado inteiro
        var (_, outro) = RegrasPedagio.Ler(TabelaImportada.DeTexto("Dt;Veic;Vl\n02/03/2026;ABC1D23;1,00"), SemParar);
        Assert.StartsWith("O arquivo não tem a(s) coluna(s) do layout: Data, Placa, Valor (R$), Hora, Praça", outro.Single());
    }

    [Fact]
    public void Data_com_hora_na_mesma_coluna_e_itens_da_nota_por_placa()
    {
        Assert.Equal(new DateTime(2026, 3, 2, 8, 15, 30), RegrasPedagio.LerDataHora("02/03/2026 08:15:30", null));
        Assert.Equal(new DateTime(2026, 3, 2, 8, 15, 0), RegrasPedagio.LerDataHora("2026-03-02 08:15", ""));
        Assert.Null(RegrasPedagio.LerDataHora("ontem", null));
        var itens = RegrasPedagio.ItensNota([
            new PassagemPedagio(1, new DateTime(2026, 3, 2), "BRA2E19", null, "A", 7.10m),
            new PassagemPedagio(2, new DateTime(2026, 3, 2), "ABC1D23", null, "B", 12.40m),
            new PassagemPedagio(3, new DateTime(2026, 3, 5), "ABC1D23", null, "C", 9.80m)]);
        Assert.Equal([("ABC1D23", 2, 22.20m), ("BRA2E19", 1, 7.10m)], itens.Select(i => (i.Placa, i.Passagens, i.Valor)));
        Assert.Equal("Pedágio – placa ABC1D23 – 2 passagem(ns) de 02/03/2026 a 05/03/2026", itens[0].Descricao);
        Assert.Contains(RegrasPedagio.Validar(SemParar with { Cfop = "5949" }), e => e.StartsWith("CFOP"));
        Assert.Empty(RegrasPedagio.Validar(SemParar));
    }
}

public class PedagioIntegracaoTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    [Fact]
    public async Task Importa_sem_duplicar_liga_o_veiculo_e_gera_a_nota_de_entrada()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("19131243000197");
        var svc = ServicosFrota.De(db.App);
        var operadora = await db.EscalarAsync<long>("INSERT INTO core.participante (empresa_id, codigo, nome, cpf_cnpj) VALUES (@e, 'F1', 'Sem Parar SA', '45997418000153') RETURNING id", ("e", empresa));
        var veiculo = (await svc.GravarVeiculoAsync(empresa, new Veiculo(0, 1, "ABC1D23", null, "CAVALO", null, null, null, null, "DIESEL_S10", null, 0, null), adm, ct)).Valor;
        Assert.False((await svc.GravarLayoutPedagioAsync(empresa, RegrasPedagioTests.SemParar with { Id = 0, OperadoraId = 0 }, adm, ct)).Sucesso);
        var layout = (await svc.GravarLayoutPedagioAsync(empresa, RegrasPedagioTests.SemParar with { Id = 0, OperadoraId = operadora }, adm, ct)).Valor;

        var arquivo = RegrasPedagioTests.Arquivo("02/03/2026;08:15;ABC1D23;Praça Jundiaí;12,40", "05/03/2026;17:40;ABC-1D23;Praça Campinas;9,80",
                                                 "03/03/2026;10:00;BRA2E19;Praça Itu;7,10");
        var r = await svc.ImportarPedagioAsync(empresa, layout, arquivo, "marco.csv", adm, ct);
        Assert.True(r.Sucesso, string.Join(" ", r.Erros));
        Assert.Equal((3, 0, 1, 29.30m), (r.Valor.Importadas, r.Valor.Duplicadas, r.Valor.SemVeiculo, r.Valor.Valor));
        Assert.Equal((0, 3), ((await svc.ImportarPedagioAsync(empresa, layout, arquivo, "marco.csv", adm, ct)).Valor.Importadas,
                              (await svc.ImportarPedagioAsync(empresa, layout, arquivo, "marco.csv", adm, ct)).Valor.Duplicadas));   // o mesmo arquivo não duplica
        var (de, ate) = (new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
        var passagens = await svc.PassagensPedagioAsync(empresa, de, ate, layout, true, adm, ct);
        Assert.Equal(2, passagens.Count(p => p.VeiculoId == veiculo));

        Assert.False((await svc.GerarNotaPedagioAsync(empresa, layout, de, ate, "", "1", new DateOnly(2026, 3, 31), null, adm, ct)).Sucesso);   // sem número
        var nota = await svc.GerarNotaPedagioAsync(empresa, layout, de, ate, "778899", "1", new DateOnly(2026, 3, 31), null, adm, ct);
        Assert.True(nota.Sucesso, string.Join(" ", nota.Erros));
        Assert.Equal((3, 29.30m), (nota.Valor.Passagens, nota.Valor.Valor));
        var doc = (await db.App.Fiscal().Digitacao.ObterAsync(empresa, nota.Valor.DocumentoId, adm, ct)).Valor;
        Assert.Equal((TipoOperacao.Entrada, ModeloDocumento.NFe55, "778899", 29.30m), (doc.Operacao, doc.Modelo, doc.Numero, doc.Totais.Total));
        Assert.Equal([("1949", 22.20m), ("1949", 7.10m)], doc.Itens.Select(i => (i.Cfop, i.ValorContabil)));
        Assert.Empty(await svc.PassagensPedagioAsync(empresa, de, ate, layout, true, adm, ct));                 // todas com nota
        Assert.False((await svc.GerarNotaPedagioAsync(empresa, layout, de, ate, "778900", "1", new DateOnly(2026, 3, 31), null, adm, ct)).Sucesso);
        Assert.Equal(0, (await svc.ExcluirPassagensAsync(empresa, passagens.Select(p => p.Id).ToList(), adm, ct)).Valor);   // com nota não exclui
        // excluída a nota, as passagens voltam a ficar sem nota
        await db.EscalarAsync<int>("DELETE FROM fiscal.documento WHERE id = @d RETURNING 1", ("d", nota.Valor.DocumentoId));
        Assert.Equal(3, (await svc.PassagensPedagioAsync(empresa, de, ate, layout, true, adm, ct)).Count);
    }
}
