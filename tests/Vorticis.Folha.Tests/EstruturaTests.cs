namespace Vorticis.Folha.Tests;

public class EstruturaTests
{
    [Fact]
    public void Biblioteca_do_modulo_identifica_o_modulo() => Assert.Equal("Vorticis Folha de Pagamento", ModuloFolhaInfo.Nome);
}
