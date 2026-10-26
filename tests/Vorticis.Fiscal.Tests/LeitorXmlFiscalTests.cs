using System.Text;
using Vorticis.Fiscal.Documentos;

namespace Vorticis.Fiscal.Tests;

public class LeitorXmlFiscalTests
{
    private static readonly DateOnly Data = new(2026, 3, 10);

    private static DocumentoLido Documento(string xml) =>
        Assert.IsType<LeituraDocumento>(LeitorXmlFiscal.Ler(XmlsDeTeste.Bytes(xml))).Documento;

    [Fact]
    public void Nfe_modelo_55_le_cabecalho_partes_itens_e_tributos()
    {
        var itens = new[]
        {
            new ItemNfe("5102", 1000m, Icms: 180m, Pis: 16.5m, Cofins: 76m, Produto: "Notebook"),
            new ItemNfe("5405", 500m, Ipi: 25m, IcmsSt: 40m, CstPis: "04", Desconto: 10m, Frete: 20m),
        };
        var d = Documento(XmlsDeTeste.Nfe(XmlsDeTeste.CnpjEmpresa, XmlsDeTeste.CnpjCliente, 1, 321, Data, itens));

        Assert.Equal(ModeloDocumento.NFe55, d.Modelo);
        Assert.Equal(44, d.Chave.Length);
        Assert.Equal(("321", "1", Data), (d.Numero, d.Serie, d.Emissao));
        Assert.Equal(1, d.TipoNf);
        Assert.Equal(SituacaoDocumento.Autorizada, d.Situacao);
        Assert.Equal(XmlsDeTeste.CnpjEmpresa, d.Emitente.Documento);
        Assert.Equal("SP", d.Emitente.Uf);
        Assert.Equal(XmlsDeTeste.CnpjCliente, d.Destinatario!.Documento);
        Assert.Equal(("RJ", "3304557"), (d.Destinatario.Uf, d.Destinatario.CodMunicipioIbge));

        Assert.Equal(2, d.Itens.Count);
        var i1 = d.Itens[0];
        Assert.Equal(("5102", "84714900", "Notebook"), (i1.Cfop, i1.Ncm, i1.Descricao));
        Assert.Equal((1000m, 180m, 18m, 1000m), (i1.ValorProduto, i1.Icms, i1.AliqIcms, i1.BcIcms));
        Assert.Equal(("00", "0", "01", "50"), (i1.CstIcms, i1.Origem, i1.CstPis, i1.CstIpi));
        Assert.Equal((16.5m, 76m), (i1.Pis, i1.Cofins));
        // Valor contábil = produto - desconto + frete + IPI + ICMS-ST: 500 - 10 + 20 + 25 + 40
        Assert.Equal(575m, d.Itens[1].ValorContabil);
        Assert.Equal("04", d.Itens[1].CstPis);
        Assert.Equal(1575m, d.Totais.Total);
        Assert.Equal(0m, d.DiferencaItens);
        Assert.Equal((180m, 25m, 40m), (d.Totais.Icms, d.Totais.Ipi, d.Totais.IcmsSt));
    }

    [Fact]
    public void Nfce_modelo_65_sem_destinatario_e_nfe_cancelada_pelo_protocolo()
    {
        var nfce = Documento(XmlsDeTeste.Nfe(XmlsDeTeste.CnpjEmpresa, null, 1, 7, Data, [new ItemNfe("5102", 59.9m, 10.78m)], modelo: "65"));
        Assert.Equal(ModeloDocumento.NFCe65, nfce.Modelo);
        Assert.Null(nfce.Destinatario);

        var cancelada = Documento(XmlsDeTeste.Nfe(XmlsDeTeste.CnpjEmpresa, XmlsDeTeste.CnpjCliente, 1, 8, Data, [new ItemNfe("5102", 10m)], cStat: "101"));
        Assert.Equal(SituacaoDocumento.Cancelada, cancelada.Situacao);
        var denegada = Documento(XmlsDeTeste.Nfe(XmlsDeTeste.CnpjEmpresa, XmlsDeTeste.CnpjCliente, 1, 9, Data, [new ItemNfe("5102", 10m)], cStat: "301"));
        Assert.Equal(SituacaoDocumento.Denegada, denegada.Situacao);
    }

    [Fact]
    public void Nfe_le_retencoes_do_grupo_retTrib()
    {
        var d = Documento(XmlsDeTeste.Nfe(XmlsDeTeste.CnpjFornecedor, XmlsDeTeste.CnpjEmpresa, 1, 50, Data, [new ItemNfe("5933", 10_000m)],
            retPis: 65m, retCofins: 300m, retCsll: 100m, irrf: 150m, retPrev: 1_100m));
        Assert.Equal((65m, 300m, 100m, 150m, 1_100m), (d.Totais.RetPis, d.Totais.RetCofins, d.Totais.RetCsll, d.Totais.Irrf, d.Totais.RetPrev));
    }

    [Fact]
    public void Nfse_nacional_le_prestador_tomador_iss_e_retencoes_federais()
    {
        var d = Documento(XmlsDeTeste.Nfse(XmlsDeTeste.CnpjEmpresa, XmlsDeTeste.CnpjCliente, 15, Data, 10_000m, aliqIss: 5m, iss: 500m, issRetido: true,
            retIrrf: 150m, retCsll: 100m, retCp: 1_100m, pis: 65m, cofins: 300m, pisCofinsRetido: true));
        Assert.Equal(ModeloDocumento.NFSe, d.Modelo);
        Assert.Equal(50, d.Chave.Length);
        Assert.Equal(("15", Data), (d.Numero, d.Emissao));
        Assert.Equal(XmlsDeTeste.CnpjEmpresa, d.Emitente.Documento);
        Assert.Equal(XmlsDeTeste.CnpjCliente, d.Destinatario!.Documento);
        Assert.Equal("SP", d.Destinatario.Uf);   // derivada do código IBGE 3550308

        var item = Assert.Single(d.Itens);
        Assert.Equal(Cfop.Servico, item.Cfop);
        Assert.Equal((10_000m, 10_000m, 5m, 500m), (item.ValorProduto, item.BcIss, item.AliqIss, item.Iss));
        Assert.Equal(10_000m, d.Totais.Total);
        Assert.True(d.Totais.IssRetido);
        Assert.Equal((500m, 65m, 300m), (d.Totais.Iss, d.Totais.RetPis, d.Totais.RetCofins));
        Assert.Equal((100m, 150m, 1_100m), (d.Totais.RetCsll, d.Totais.Irrf, d.Totais.RetPrev));
    }

    [Fact]
    public void Nfse_sem_retencao_de_pis_cofins_nao_gera_retencao()
    {
        var d = Documento(XmlsDeTeste.Nfse(XmlsDeTeste.CnpjEmpresa, XmlsDeTeste.CnpjCliente, 16, Data, 1_000m, iss: 50m, pis: 6.5m, cofins: 30m));
        Assert.False(d.Totais.IssRetido);
        Assert.Equal((0m, 0m), (d.Totais.RetPis, d.Totais.RetCofins));
        Assert.Equal((6.5m, 30m), (d.Totais.Pis, d.Totais.Cofins));
    }

    [Fact]
    public void Eventos_de_cancelamento_de_nfe_e_nfse_sao_reconhecidos()
    {
        var chave = XmlsDeTeste.Chave(55);
        Assert.Equal(chave, Assert.IsType<LeituraCancelamento>(LeitorXmlFiscal.Ler(XmlsDeTeste.Bytes(XmlsDeTeste.CancelamentoNfe(chave)))).Chave);
        var chaveNfse = new string('1', 50);
        Assert.Equal(chaveNfse, Assert.IsType<LeituraCancelamento>(LeitorXmlFiscal.Ler(XmlsDeTeste.Bytes(XmlsDeTeste.CancelamentoNfse(chaveNfse)))).Chave);
    }

    [Theory]
    [InlineData("isto não é xml")]
    [InlineData("<outraCoisa><x>1</x></outraCoisa>")]
    [InlineData("<nfeProc><NFe><infNFe Id=\"NFe123\"></infNFe></NFe></nfeProc>")]
    public void Arquivos_invalidos_viram_leitura_invalida(string conteudo)
    {
        Assert.IsType<LeituraInvalida>(LeitorXmlFiscal.Ler(Encoding.UTF8.GetBytes(conteudo)));
    }

    [Fact]
    public void Xml_com_dtd_e_recusado_sem_resolver_entidades_externas()
    {
        const string xxe = "<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY e SYSTEM \"file:///etc/passwd\">]><nfeProc>&e;</nfeProc>";
        var r = Assert.IsType<LeituraInvalida>(LeitorXmlFiscal.Ler(Encoding.UTF8.GetBytes(xxe)));
        Assert.Contains("XML inválido", r.Motivo);
    }

    [Fact]
    public void Modelo_nao_suportado_e_recusado()
    {
        var xml = XmlsDeTeste.Nfe(XmlsDeTeste.CnpjEmpresa, XmlsDeTeste.CnpjCliente, 1, 90, Data, [new ItemNfe("5102", 10m)], modelo: "57");
        Assert.Contains("57", Assert.IsType<LeituraInvalida>(LeitorXmlFiscal.Ler(XmlsDeTeste.Bytes(xml))).Motivo);
    }
}
