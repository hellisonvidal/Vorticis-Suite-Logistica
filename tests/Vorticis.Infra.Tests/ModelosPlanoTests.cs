using System.Text.RegularExpressions;
using Vorticis.Application.Cadastros;
using Vorticis.Domain.Contabil;

namespace Vorticis.Infra.Tests;

/// <summary>Consistência dos modelos de plano de contas embutidos (catálogo + um JSON por modelo). Não usa banco.</summary>
public partial class ModelosPlanoTests
{
    private static readonly PlanoModeloEmbutido Fonte = new();

    [GeneratedRegex(@"^\d+(\.\d+)*$")]
    private static partial Regex Formato();

    public static IEnumerable<TheoryDataRow<string>> Modelos() => Fonte.Listar().Select(m => new TheoryDataRow<string>(m.Id));

    [Fact]
    public void Catalogo_tem_ids_unicos_o_modelo_padrao_e_os_modelos_esperados()
    {
        var ids = Fonte.Listar().Select(m => m.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.Contains(Fonte.ModeloPadrao, ids);
        foreach (var id in new[] { "comercio_simples", "comercio_presumido", "comercio_real", "industria_simples", "industria_presumido", "industria_real",
                                   "servicos_simples", "servicos_presumido", "servicos_real", "itg1000_me_epp", "entidade_sem_fins_lucrativos", "completo_geral" })
            Assert.Contains(id, ids);
        Assert.Empty(Fonte.Obter("inexistente"));
    }

    [Theory]
    [MemberData(nameof(Modelos))]
    public void Modelo_e_coerente(string id)
    {
        var modelo = Fonte.Listar().Single(m => m.Id == id);
        var contas = Fonte.Obter(id);
        Assert.False(string.IsNullOrWhiteSpace(modelo.Nome));
        Assert.False(string.IsNullOrWhiteSpace(modelo.Descricao));
        Assert.False(string.IsNullOrWhiteSpace(modelo.Base));
        // lista vazia = serve a qualquer regime (plano completo geral)
        Assert.True(modelo.Regimes.Count > 0 || id == "completo_geral");
        Assert.NotEmpty(contas);
        Assert.Equal(contas.Count, modelo.QuantidadeContas);

        var porCodigo = new Dictionary<string, ContaModelo>();
        foreach (var c in contas)
        {
            Assert.True(Formato().IsMatch(c.Codigo), $"{id}: código inválido '{c.Codigo}'");
            Assert.True(c.Codigo.Length <= 30, $"{id}: código longo {c.Codigo}");
            Assert.False(string.IsNullOrWhiteSpace(c.Descricao), $"{id}: {c.Codigo} sem descrição");
            Assert.True(porCodigo.TryAdd(c.Codigo, c), $"{id}: código repetido {c.Codigo}");
            Assert.Contains(c.CodNat, new[] { "01", "02", "03", "04", "05", "09" });
        }
        var pais = contas.Select(c => c.Codigo.Contains('.') ? c.Codigo[..c.Codigo.LastIndexOf('.')] : null).ToArray();
        for (var i = 0; i < contas.Count; i++)
        {
            var c = contas[i];
            if (pais[i] is not { } codPai) continue;            // raiz
            Assert.True(porCodigo.TryGetValue(codPai, out var pai), $"{id}: {c.Codigo} sem a conta superior {codPai}");
            // mesmo COD_NAT do pai (a ECD agrupa por classe); natureza de saldo só difere nas redutoras "(-)"
            // única exceção: o Patrimônio Líquido (COD_NAT 03) fica sob o grupo 2 (Passivo e PL)
            Assert.True(pai!.CodNat == c.CodNat || (pai.CodNat == "02" && c.CodNat == "03"), $"{id}: {c.Codigo} COD_NAT {c.CodNat} difere do pai {codPai} ({pai.CodNat})");
            if (pai.NaturezaSaldo != c.NaturezaSaldo)
                Assert.True(c.Descricao.TrimStart().StartsWith("(-)") || c.Descricao.TrimStart().StartsWith("(+/-)"),
                            $"{id}: {c.Codigo} '{c.Descricao}' tem natureza diferente do pai e não é redutora");
        }

        // raízes: Ativo, Passivo, PL e resultado, cada uma com a natureza devedora/credora esperada
        var raizes = contas.Where(c => !c.Codigo.Contains('.')).ToDictionary(c => c.Codigo);
        Assert.Equal("01", raizes["1"].CodNat);
        Assert.Equal(IndicadorDC.D, raizes["1"].NaturezaSaldo);
        Assert.Equal("02", raizes["2"].CodNat);
        Assert.Equal(IndicadorDC.C, raizes["2"].NaturezaSaldo);

        // folhas são analíticas (recebem lançamento); quem tem filhas é sintética — e as raízes são sempre sintéticas com filhas
        var comFilhas = pais.Where(p => p is not null).ToHashSet();
        foreach (var r in raizes.Keys) Assert.Contains(r, comFilhas);
        Assert.Contains(contas, c => !comFilhas.Contains(c.Codigo));

        // contas de encerramento do modelo existem e são analíticas
        foreach (var (rotulo, codigo) in new[] { ("apuração", modelo.Contas.Apuracao), ("lucros", modelo.Contas.LucrosAcumulados), ("prejuízos", modelo.Contas.PrejuizosAcumulados) })
        {
            Assert.True(porCodigo.ContainsKey(codigo), $"{id}: conta de {rotulo} {codigo} não existe");
            Assert.DoesNotContain(codigo, comFilhas!);
        }
        Assert.NotEqual(modelo.Contas.LucrosAcumulados, modelo.Contas.PrejuizosAcumulados);
        Assert.Equal("03", porCodigo[modelo.Contas.LucrosAcumulados].CodNat);
        Assert.Equal("03", porCodigo[modelo.Contas.PrejuizosAcumulados].CodNat);
        Assert.Equal("04", porCodigo[modelo.Contas.Apuracao].CodNat);
    }
}
