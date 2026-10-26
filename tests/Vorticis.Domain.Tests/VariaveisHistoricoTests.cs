using Vorticis.Domain.Contabil;

namespace Vorticis.Domain.Tests;

public class VariaveisHistoricoTests
{
    private static readonly ContextoHistorico Completo = new("NF 123", "Fornecedor X", new DateOnly(2026, 3, 10), "Bancos", 1500m, "parcela 2");

    [Fact]
    public void Substitui_todas_as_variaveis_resolvidas()
    {
        var r = VariaveisHistorico.Aplicar(
            "Pgto {DOCUMENTO} de {PARTICIPANTE} em {DATA} ({MES}/{ANO}) comp. {COMPETENCIA}, ant. {MES_ANTERIOR}, {CONTA}, R$ {VALOR} {COMPLEMENTO}", Completo);
        Assert.Equal("Pgto NF 123 de Fornecedor X em 10/03/2026 (03/2026) comp. 03/2026, ant. 02/2026, Bancos, R$ 1.500,00 parcela 2", r.Texto);
        Assert.True(r.Completo);
    }

    [Fact]
    public void Mes_anterior_de_janeiro_e_dezembro_do_ano_anterior()
    {
        var r = VariaveisHistorico.Aplicar("{MES_ANTERIOR}", new ContextoHistorico(Data: new DateOnly(2026, 1, 15)));
        Assert.Equal("12/2025", r.Texto);
    }

    [Fact]
    public void Variaveis_sem_dado_ficam_no_texto_e_sao_listadas_como_pendentes()
    {
        var r = VariaveisHistorico.Aplicar("NF {DOCUMENTO} - {PARTICIPANTE} {DOCUMENTO}", new ContextoHistorico(Participante: "Cliente Y"));
        Assert.Equal("NF {DOCUMENTO} - Cliente Y {DOCUMENTO}", r.Texto);
        Assert.Equal(["DOCUMENTO"], r.Pendentes);
        Assert.False(r.Completo);
    }

    [Fact]
    public void Nao_diferencia_maiusculas_e_minusculas_e_ignora_texto_sem_variavel()
    {
        Assert.Equal("Venda 2026", VariaveisHistorico.Aplicar("Venda {ano}", Completo).Texto);
        var r = VariaveisHistorico.Aplicar("Venda à vista", new ContextoHistorico());
        Assert.Equal(("Venda à vista", true), (r.Texto, r.Completo));
    }

    [Fact]
    public void Valor_zero_ou_ausente_fica_pendente()
    {
        Assert.Equal(["VALOR"], VariaveisHistorico.Aplicar("{VALOR}", new ContextoHistorico(Valor: 0)).Pendentes);
        Assert.Equal(["COMPLEMENTO"], VariaveisHistorico.Aplicar("{COMPLEMENTO}", new ContextoHistorico(Complemento: "  ")).Pendentes);
    }

    [Fact]
    public void Marcador_desconhecido_permanece_e_nao_e_pendencia_mas_e_detectado()
    {
        var r = VariaveisHistorico.Aplicar("{XYZ} {ANO}", Completo);
        Assert.Equal("{XYZ} 2026", r.Texto);
        Assert.Empty(r.Pendentes);
        Assert.Equal(["XYZ"], VariaveisHistorico.Desconhecidas("{xyz} {ANO} {DATA}"));
        Assert.Empty(VariaveisHistorico.Desconhecidas("sem variáveis"));
    }

    [Fact]
    public void Usadas_lista_sem_repeticao_na_ordem()
    {
        Assert.Equal(["DATA", "VALOR"], VariaveisHistorico.Usadas("{data} {VALOR} {Data}"));
        Assert.True(VariaveisHistorico.TemVariaveis("x {MES}"));
        Assert.False(VariaveisHistorico.TemVariaveis("x {QUALQUER}"));
    }

    [Fact]
    public void Catalogo_tem_as_variaveis_pedidas()
    {
        var nomes = VariaveisHistorico.Todas.Select(v => v.Nome).ToArray();
        foreach (var n in new[] { "DOCUMENTO", "PARTICIPANTE", "DATA", "MES", "ANO", "MES_ANTERIOR", "COMPETENCIA", "CONTA", "VALOR", "COMPLEMENTO" })
            Assert.Contains(n, nomes);
        Assert.Equal("{MES}", VariaveisHistorico.Todas.Single(v => v.Nome == "MES").Marcador);
    }

    [Fact]
    public void Primeiro_pendente_ignora_marcadores_desconhecidos()
    {
        Assert.Equal((8, 11), VariaveisHistorico.PrimeiroPendente("{XYZ} - {DOCUMENTO}"));
        Assert.Null(VariaveisHistorico.PrimeiroPendente("{XYZ} sem pendência"));
        Assert.Null(VariaveisHistorico.PrimeiroPendente(null));
    }
}
