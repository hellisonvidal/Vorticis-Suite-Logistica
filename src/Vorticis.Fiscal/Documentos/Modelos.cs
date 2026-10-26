namespace Vorticis.Fiscal.Documentos;

/// <summary>Modelo do documento fiscal eletrônico (coluna fiscal.documento.modelo).</summary>
public enum ModeloDocumento { NFe55, NFCe65, NFSe }

public enum TipoOperacao { Entrada, Saida }

public enum SituacaoDocumento { Autorizada, Cancelada, Denegada }

public static class ModelosExtensions
{
    public static string Codigo(this ModeloDocumento m) => m switch { ModeloDocumento.NFe55 => "55", ModeloDocumento.NFCe65 => "65", _ => "NFSE" };

    public static ModeloDocumento ModeloDe(string codigo) => codigo switch { "55" => ModeloDocumento.NFe55, "65" => ModeloDocumento.NFCe65, _ => ModeloDocumento.NFSe };

    public static string Rotulo(this ModeloDocumento m) => m switch { ModeloDocumento.NFe55 => "NF-e", ModeloDocumento.NFCe65 => "NFC-e", _ => "NFS-e" };

    public static string Codigo(this TipoOperacao t) => t == TipoOperacao.Entrada ? "E" : "S";

    public static TipoOperacao OperacaoDe(string codigo) => codigo == "E" ? TipoOperacao.Entrada : TipoOperacao.Saida;

    public static string Rotulo(this TipoOperacao t) => t == TipoOperacao.Entrada ? "Entrada" : "Saída";

    public static string Codigo(this SituacaoDocumento s) => s switch { SituacaoDocumento.Cancelada => "CANCELADA", SituacaoDocumento.Denegada => "DENEGADA", _ => "AUTORIZADA" };

    public static SituacaoDocumento SituacaoDe(string codigo) => codigo switch
    {
        "CANCELADA" => SituacaoDocumento.Cancelada, "DENEGADA" => SituacaoDocumento.Denegada, _ => SituacaoDocumento.Autorizada,
    };

    public static string Rotulo(this SituacaoDocumento s) => s switch { SituacaoDocumento.Cancelada => "Cancelada", SituacaoDocumento.Denegada => "Denegada", _ => "Autorizada" };
}

/// <summary>Emitente, destinatário (NF-e) ou prestador/tomador (NFS-e).</summary>
public sealed record ParteDocumento(string? Documento, string? Nome, string? Uf, string? CodMunicipioIbge = null, string? Municipio = null,
                                    string? InscricaoEstadual = null);

/// <summary>Item da nota com CFOP, NCM, CST e tributos. NFS-e tem um único item, com o CFOP convencional <see cref="Cfop.Servico"/>.</summary>
public sealed record ItemFiscal(
    int Numero, string? CodProduto, string? Descricao, string? Ncm, string? Cest, string Cfop, string? Unidade,
    decimal Quantidade, decimal ValorUnitario, decimal ValorProduto, decimal Desconto, decimal Frete, decimal Seguro, decimal Outras,
    decimal ValorContabil,
    string? CstIcms, string? Origem, decimal BcIcms, decimal AliqIcms, decimal Icms, decimal BcIcmsSt, decimal IcmsSt,
    string? CstIpi, decimal BcIpi, decimal AliqIpi, decimal Ipi,
    string? CstPis, decimal BcPis, decimal AliqPis, decimal Pis,
    string? CstCofins, decimal BcCofins, decimal AliqCofins, decimal Cofins,
    decimal BcIss, decimal AliqIss, decimal Iss,
    string? Gtin = null, long? ProdutoId = null, long? CentroCustoId = null, string? CentroCustoCodigo = null, string? CfopOriginal = null);

/// <summary>Totais e retenções do documento.</summary>
public sealed record TotaisDocumento(
    decimal Produtos, decimal Desconto, decimal Frete, decimal Seguro, decimal Outras, decimal Total,
    decimal BcIcms, decimal Icms, decimal BcIcmsSt, decimal IcmsSt, decimal Ipi, decimal Pis, decimal Cofins, decimal Iss, bool IssRetido,
    decimal RetPis, decimal RetCofins, decimal RetCsll, decimal Irrf, decimal RetPrev);

/// <summary>Documento como lido do XML, ainda sem a identificação de entrada/saída (que depende da empresa).</summary>
public sealed record DocumentoLido(
    ModeloDocumento Modelo, string Chave, string Numero, string Serie, DateOnly Emissao, DateOnly? EntradaSaida, string? NaturezaOperacao,
    SituacaoDocumento Situacao, int? TipoNf, ParteDocumento Emitente, ParteDocumento? Destinatario, TotaisDocumento Totais,
    IReadOnlyList<ItemFiscal> Itens, byte[] Xml, IReadOnlyList<Vorticis.Fiscal.Duplicatas.ParcelaDuplicata>? Duplicatas = null)
{
    /// <summary>Competência fiscal do livro: data de entrada/saída, ou a emissão se não informada.</summary>
    public DateOnly DataLivro => EntradaSaida ?? Emissao;

    /// <summary>Soma do valor contábil dos itens menos o total da nota (diferença indica valor não lido ou arredondamento).</summary>
    public decimal DiferencaItens => Itens.Sum(i => i.ValorContabil) - Totais.Total;
}

/// <summary>O que um arquivo XML contém.</summary>
public abstract record LeituraXml;

public sealed record LeituraDocumento(DocumentoLido Documento) : LeituraXml;

/// <summary>Evento de cancelamento (NF-e 110111; NFS-e e101101) da chave informada.</summary>
public sealed record LeituraCancelamento(string Chave) : LeituraXml;

public sealed record LeituraInvalida(string Motivo) : LeituraXml;
