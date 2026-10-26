using Vorticis.Domain.Seguranca;

namespace Vorticis.Fiscal.Tests;

public class EstruturaTests
{
    [Fact]
    public void Biblioteca_do_modulo_identifica_o_modulo() => Assert.Equal("Vorticis Fiscal", ModuloFiscalInfo.Nome);

    [Fact]
    public void Permissoes_fiscais_estao_no_catalogo_com_codigos_unicos_e_grupo_Fiscal()
    {
        var fiscais = Permissoes.Catalogo.Where(d => d.Grupo == "Fiscal").ToList();
        Assert.Equal(10, fiscais.Count);
        Assert.All(fiscais, d => Assert.StartsWith("FISCAL_", d.Codigo, StringComparison.Ordinal));
        Assert.Equal(Permissoes.Catalogo.Count, Permissoes.Catalogo.Select(d => d.Codigo).Distinct().Count());
        Assert.Equal(Permissao.FiscalApurar, Permissoes.DeCodigos(["FISCAL_APURAR"]).Single());
    }
}
