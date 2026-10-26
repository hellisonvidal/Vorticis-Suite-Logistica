using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Documentos;
using Vorticis.Fiscal.Simples;

namespace Vorticis.Fiscal.Tests;

public class ApuracaoSimplesIntegracaoTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    [Fact]
    public async Task Prepara_calcula_salva_e_usa_a_receita_salva_no_rbt12_seguinte()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        const string cnpj = "60746948000112";
        var empresa = await db.CriarEmpresaAsync(cnpj);   // início de atividade em 2020
        var adm = await db.AdminAsync();
        var fiscal = db.App.Fiscal();
        var svc = fiscal.Simples;

        // histórico: dez/2024 a dez/2025 com R$ 25.000 de receita e R$ 5.000 de folha por mês
        for (var m = 1; m <= 12; m++)
            Assert.True((await svc.GravarBaseMensalAsync(empresa, new BaseMensal(new DateOnly(2025, m, 1), 25_000m, 5_000m), adm, ct)).Sucesso);
        Assert.True((await svc.GravarBaseMensalAsync(empresa, new BaseMensal(new DateOnly(2024, 12, 1), 25_000m, 5_000m), adm, ct)).Sucesso);

        await fiscal.Documentos.ImportarAsync(empresa,
        [
            new ArquivoXml("v.xml", XmlsDeTeste.Bytes(XmlsDeTeste.Nfe(cnpj, XmlsDeTeste.CnpjCliente, 1, 10, new DateOnly(2026, 1, 15), [new ItemNfe("5102", 30_000m)]))),
            new ArquivoXml("s.xml", XmlsDeTeste.Bytes(XmlsDeTeste.Nfse(cnpj, XmlsDeTeste.CnpjCliente, 3, new DateOnly(2026, 1, 20), 20_000m, iss: 1_000m))),
        ], adm, ct: ct);

        var jan = new DateOnly(2026, 1, 1);
        var preparo = (await svc.PrepararAsync(empresa, jan, AtividadeSimples.ServicosFatorR, adm, ct)).Valor;
        Assert.Equal(300_000m, preparo.Rbt12);
        Assert.Equal(60_000m, preparo.Folha12);
        Assert.Equal([AtividadeSimples.ComercioRevenda, AtividadeSimples.ServicosFatorR], preparo.Sugestao.Select(s => s.Atividade));

        var calc = (await svc.CalcularAsync(empresa, jan, preparo.Sugestao, null, null, adm, ct)).Valor;
        // Comércio: 5,32% × 30.000 = 1.596,00 · Serviços (Fator R 20% → Anexo V, 2ª faixa): (300.000 × 18% − 4.500) ÷ 300.000 = 16,5% × 20.000 = 3.300,00
        Assert.Equal(AnexoSimples.V, calc.Segmentos.Single(s => s.Atividade == AtividadeSimples.ServicosFatorR).Anexo);
        Assert.Equal(4_896m, calc.ValorDas);

        var salva = (await svc.SalvarAsync(empresa, jan, preparo.Sugestao, null, null, adm, ct)).Valor;
        Assert.Equal((4_896m, new DateOnly(2026, 2, 20)), (salva.ValorDas, salva.Vencimento));
        Assert.Equal(calc.Segmentos.Count, salva.Resultado.Segmentos.Count);
        Assert.Equal(calc.Memoria, salva.Resultado.Memoria);
        // recalcular substitui, não duplica
        Assert.True((await svc.SalvarAsync(empresa, jan, preparo.Sugestao, null, null, adm, ct)).Sucesso);
        Assert.Single((await svc.ListarAsync(empresa, 2026, adm, ct)).Valor);

        // a receita de janeiro (50.000) entra no RBT12 de fevereiro: fev/25..jan/26 = 11 × 25.000 + 50.000
        var fev = (await svc.PrepararAsync(empresa, new DateOnly(2026, 2, 1), AtividadeSimples.ServicosFatorR, adm, ct)).Valor;
        Assert.Equal(325_000m, fev.Rbt12);

        // parâmetro legal semeado em core.parametro_legal
        Assert.Equal(1L, await db.EscalarAsync<long>("SELECT count(*) FROM core.parametro_legal WHERE chave = 'SIMPLES_NACIONAL'"));

        // sem receita; sem permissão
        Assert.False((await svc.CalcularAsync(empresa, jan, [], null, null, adm, ct)).Sucesso);
        var sessaoAdm = new SessaoUsuario(adm, "a", "A", true, false);
        var consulta = await db.PerfilIdAsync("Consulta");
        var uid = (await db.App.Usuarios.CriarUsuarioAsync(sessaoAdm, "simples_leitor", "L", "Inicial123", consulta, ct)).Valor;
        await db.App.Usuarios.DefinirAcessoAsync(sessaoAdm, uid, empresa, consulta, ct);
        Assert.Contains("não permite", (await svc.CalcularAsync(empresa, jan, preparo.Sugestao, null, null, uid, ct)).Erros[0]);
        Assert.Contains("não permite", (await svc.GravarBaseMensalAsync(empresa, new BaseMensal(jan, 1, 1), uid, ct)).Erros[0]);
        Assert.True((await svc.ExcluirAsync(empresa, (await svc.ListarAsync(empresa, 2026, adm, ct)).Valor.Single().Id, adm, ct)).Sucesso);
        Assert.Empty((await svc.ListarAsync(empresa, 2026, adm, ct)).Valor);
    }
}
