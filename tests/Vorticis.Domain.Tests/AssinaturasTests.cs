using Vorticis.Application.Cadastros;
using Vorticis.Application.Relatorios;

namespace Vorticis.Domain.Tests;

public class AssinaturasTests
{
    [Fact]
    public void Assinatura_do_socio_traz_nome_qualificacao_e_cpf()
    {
        var s = new Socio(1, "Fulana de Tal", 'F', "39053344705", null, "Sócia-Administradora", 49, 60m, null, null, true, null, null);
        Assert.Equal("Fulana de Tal\nSócia-Administradora (representante legal) — CPF 390.533.447-05", ServicoAssinaturas.Formatar(s));
        // sem qualificação cadastrada, usa um cargo genérico; sócio pessoa jurídica mostra o CNPJ
        var pj = s with { Nome = "Holding SA", TipoPessoa = 'J', CpfCnpj = "11222333000181", Qualificacao = "", RepresentanteLegal = false };
        Assert.Equal("Holding SA\nSócio/Responsável — CNPJ 11.222.333/0001-81", ServicoAssinaturas.Formatar(pj));
    }

    [Fact]
    public void Assinatura_do_contador_traz_o_crc_completo()
    {
        var c = new Contador(1, "Maria Contadora", "52998224725", "123456/O-7", "SP", null, null, null);
        Assert.Equal("Maria Contadora\nContador — CRC SP-123456/O-7 — CPF 529.982.247-25", ServicoAssinaturas.Formatar(c));
        Assert.Contains("CRC 1SP123456/O-7", ServicoAssinaturas.Formatar(c with { Crc = "1SP123456/O-7" }));   // UF já faz parte do número
    }

    [Fact]
    public void Opcoes_do_livro_descrevem_so_o_que_foi_marcado()
    {
        Assert.Equal("Diário, Razão, Balancete de Verificação, Balanço Patrimonial, DRE, DMPL e DLPA", new OpcoesLivro().Descricao());
        var parcial = new OpcoesLivro(Diario: false, Razao: false, Balancete: false, Balanco: false, Dre: false, Dlpa: false);
        Assert.Equal("DMPL", parcial.Descricao());
        Assert.True(parcial.TemConteudo && parcial.TemTermos);
        var vazio = new OpcoesLivro(false, false, false, false, false, false, false, false, false);
        Assert.False(vazio.TemConteudo || vazio.TemTermos);
        Assert.False(OpcoesAssinatura.Nenhuma.Alguma);
        Assert.True(new OpcoesAssinatura(Contador: true).Alguma);
    }
}
