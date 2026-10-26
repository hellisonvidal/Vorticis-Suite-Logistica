using Vorticis.Domain.Contabil;

namespace Vorticis.Domain.Tests;

public class PlanoCentrosCustoTests
{
    private static readonly MascaraPlano Mascara = MascaraPlano.Ler("9.99.999").Valor!;

    [Theory]
    [InlineData("1", 1, null)]
    [InlineData("1.01", 2, "1")]
    [InlineData("1.01.001", 3, "1.01")]
    public void Codigo_dentro_da_mascara_calcula_nivel_e_pai(string codigo, int nivel, string? pai)
    {
        var r = PlanoCentrosCusto.Validar(codigo, Mascara);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        Assert.Equal(((short)nivel, pai), (r.Valor!.Nivel, r.Valor.CodigoPai));
    }

    [Theory]
    [InlineData("1.1")]          // largura do nível 2 errada
    [InlineData("11")]           // nível 1 com 2 dígitos
    [InlineData("1.01.001.1")]   // mais níveis que a máscara
    [InlineData("A.01")]         // letras
    [InlineData("1..01")]        // nível vazio
    [InlineData("")]
    public void Codigo_fora_da_mascara_e_recusado(string codigo) => Assert.False(PlanoCentrosCusto.Validar(codigo, Mascara).Sucesso);

    [Fact]
    public void Sem_mascara_o_codigo_e_livre_e_o_nivel_vem_dos_pontos()
    {
        var r = PlanoCentrosCusto.Validar("ADM.TI", null);
        Assert.True(r.Sucesso);
        Assert.Equal(((short)2, "ADM"), (r.Valor!.Nivel, r.Valor.CodigoPai));
    }

    [Fact]
    public void Nivel_1_e_sempre_grupo_e_ultimo_nivel_sempre_analitico()
    {
        Assert.False(PlanoCentrosCusto.DefinirAnalitico(1, sintetico: false, Mascara).Valor);   // raiz vira sintética
        Assert.False(PlanoCentrosCusto.DefinirAnalitico(2, sintetico: true, Mascara).Valor);    // grupo intermediário
        Assert.True(PlanoCentrosCusto.DefinirAnalitico(2, sintetico: false, Mascara).Valor);
        Assert.False(PlanoCentrosCusto.DefinirAnalitico(3, sintetico: true, Mascara).Sucesso);  // último nível
        Assert.True(PlanoCentrosCusto.DefinirAnalitico(1, sintetico: false, null).Valor);        // sem máscara: livre
    }
}
