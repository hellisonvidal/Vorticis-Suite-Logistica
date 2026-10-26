using Vorticis.Domain.Cadastros;

namespace Vorticis.Domain.Tests;

public class NireTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("35.2.0123456-7", "35201234567")]
    public void Normaliza_para_digitos(string? entrada, string? esperado) => Assert.Equal(esperado, Nire.Normalizar(entrada));

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("35201234567", true)]
    [InlineData("35.2.0123456-7", true)]
    [InlineData("3520123456", false)]
    [InlineData("352012345678", false)]
    public void Valida_somente_o_tamanho(string? nire, bool esperado) => Assert.Equal(esperado, Nire.Valido(nire));
}
