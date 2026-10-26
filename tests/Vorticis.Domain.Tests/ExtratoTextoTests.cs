using Vorticis.Domain.Contabil;

namespace Vorticis.Domain.Tests;

public class ExtratoTextoTests
{
    private static readonly string[] Pdf =
    [
        "BANCO EXEMPLO S.A.        Extrato de conta corrente",
        "Agência 0001   Conta 12345-6",
        "Data        Histórico                  Documento      Valor (R$)       Saldo (R$)",
        "01/03/2026  SALDO ANTERIOR                                                1.000,00",
        "02/03/2026  PIX RECEBIDO CLIENTE ABC   123456         1.500,00 C       2.500,00",
        "05/03/2026  TARIFA PACOTE              000001            45,90 D       2.454,10",
        "05/03/2026  TARIFA PACOTE              000001            45,90 D       2.408,20",
        "10/03/2026 PAGAMENTO BOLETO  -300,00",
        "Total de lançamentos: 4",
    ];

    [Fact]
    public void Valores_em_varios_formatos_viram_entrada_ou_saida()
    {
        Assert.Equal(1234.56m, LeitorExtratoTexto.LerValor("1.234,56"));
        Assert.Equal(-1234.56m, LeitorExtratoTexto.LerValor("-1.234,56"));
        Assert.Equal(-10m, LeitorExtratoTexto.LerValor("10,00-"));
        Assert.Equal(-45.9m, LeitorExtratoTexto.LerValor("45,90 D"));
        Assert.Equal(10m, LeitorExtratoTexto.LerValor("R$ 10,00 C"));
        Assert.Equal(-12.5m, LeitorExtratoTexto.LerValor("-12.50"));
        Assert.Null(LeitorExtratoTexto.LerValor("123456"));          // número de documento não é valor
        Assert.Null(LeitorExtratoTexto.LerValor("PIX"));
    }

    [Fact]
    public void Layout_sugerido_pelo_documento_reconhece_os_movimentos_e_ignora_saldo_e_cabecalho()
    {
        var layout = LeitorExtratoTexto.Sugerir(Pdf, 2026);
        Assert.Equal((SeparadorExtrato.Espacos, 1, "dd/MM/yyyy"), (layout.Separador, layout.ColunaData, layout.FormatoData));
        var linhas = LeitorExtratoTexto.Aplicar(Pdf, layout, 2026);
        var ts = linhas.Where(l => l.Transacao is not null).Select(l => l.Transacao!).ToList();
        // valor automático: com valor e saldo na linha, fica o penúltimo (o valor do movimento)
        Assert.Equal([1500m, -45.9m, -45.9m, -300m], ts.Select(t => t.Valor));
        Assert.Equal("PIX RECEBIDO CLIENTE ABC 123456", ts[0].Descricao);
        var comColuna = LeitorExtratoTexto.Aplicar(Pdf, layout with { ColunaValor = 4, ColunaDocumento = 3 }, 2026)
                                          .Where(l => l.Transacao is not null).Select(l => l.Transacao!).ToList();
        Assert.Equal([1500m, -45.9m, -45.9m], comColuna.Select(t => t.Valor));
        Assert.Equal("PIX RECEBIDO CLIENTE ABC", comColuna[0].Descricao);
        Assert.Equal("123456", comColuna[0].Documento);
        Assert.NotEqual(comColuna[1].Id, comColuna[2].Id);            // dois movimentos iguais no mesmo dia continuam distintos
        // a mesma leitura gera os mesmos identificadores (reimportar não duplica)
        Assert.Equal(comColuna.Select(t => t.Id), LeitorExtratoTexto.Aplicar(Pdf, layout with { ColunaValor = 4, ColunaDocumento = 3 }, 2026)
                                                         .Where(l => l.Transacao is not null).Select(l => l.Transacao!.Id));
        // linha com a data colada no histórico (um espaço só): a descrição vem do resto da coluna
        var boleto = Assert.Single(ts, t => t.Data == new DateOnly(2026, 3, 10));
        Assert.Equal((-300m, "PAGAMENTO BOLETO"), (boleto.Valor, boleto.Descricao));
    }

    [Fact]
    public void Valor_automatico_pula_o_saldo_e_a_coluna_informada_vale_mesmo_assim()
    {
        string[] linhas = ["02/03  PIX RECEBIDO  1.500,00", "03/03  COMPRA CARTAO  89,90-  1.410,10"];
        var r = LeitorExtratoTexto.Aplicar(linhas, new LayoutExtrato(FormatoData: "dd/MM"), 2025).Select(l => l.Transacao!).ToList();
        Assert.Equal((new DateOnly(2025, 3, 2), 1500m, "PIX RECEBIDO"), (r[0].Data, r[0].Valor, r[0].Descricao));
        Assert.Equal((-89.90m, "COMPRA CARTAO"), (r[1].Valor, r[1].Descricao));
        var saldo = LeitorExtratoTexto.Aplicar(linhas, new LayoutExtrato(FormatoData: "dd/MM", ColunaValor: 4), 2025);
        Assert.Null(saldo[0].Transacao);                             // a linha não tem a 4ª coluna
        Assert.Equal(1410.10m, saldo[1].Transacao!.Valor);
        var invertido = LeitorExtratoTexto.Aplicar(linhas, new LayoutExtrato(FormatoData: "dd/MM", ColunaValor: 3, InverterSinal: true), 2025);
        Assert.Equal(-1500m, invertido[0].Transacao!.Valor);
    }

    [Fact]
    public void Csv_com_ponto_e_virgula_e_sugerido_e_lido()
    {
        string[] csv = ["Data;Lançamento;Valor", "15/04/2026;\"Depósito\";250,00", "16/04/2026;Tarifa;-12,00"];
        var layout = LeitorExtratoTexto.Sugerir(csv, 2026);
        Assert.Equal(SeparadorExtrato.PontoVirgula, layout.Separador);
        var ts = LeitorExtratoTexto.Extrato(LeitorExtratoTexto.Aplicar(csv, layout, 2026), "EXTRATO");
        Assert.Equal([250m, -12m], ts.Transacoes.Select(t => t.Valor));
        Assert.Equal("Depósito", ts.Transacoes[0].Descricao);
        Assert.Equal((new DateOnly(2026, 4, 15), new DateOnly(2026, 4, 16)), (ts.Inicio, ts.Fim));
    }
}
