using Vorticis.Desktop.Views;

namespace Vorticis.Desktop.Tests;

public class MascaraTests
{
    [Theory]
    [InlineData("Cnpj", "11222333000181", "11.222.333/0001-81")]
    [InlineData("Cnpj", "12abc34501de35", "12.ABC.345/01DE-35")]            // CNPJ alfanumérico
    [InlineData("Cnpj", "112223", "11.222.3")]                             // formata enquanto digita
    [InlineData("Cpf", "39053344705", "390.533.447-05")]
    [InlineData("Cpf", "390.5", "390.5")]
    [InlineData("CpfCnpj", "39053344705", "390.533.447-05")]
    [InlineData("CpfCnpj", "112223330001", "11.222.333/0001")]
    [InlineData("Cep", "01310100", "01310-100")]
    [InlineData("Data", "10032026", "10/03/2026")]
    [InlineData("Data", "1003", "10/03")]
    [InlineData("Data", "10/03/20261", "10/03/2026")]
    public void Formata_no_padrao(string tipo, string digitado, string esperado) => Assert.Equal(esperado, Mascara.Aplicar(tipo, digitado));
}
