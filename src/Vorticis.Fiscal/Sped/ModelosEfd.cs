using Vorticis.Domain.Sped;

namespace Vorticis.Fiscal.Sped;

/// <summary>Empresa declarante (registros 0000, 0005 e 0140 da EFD).</summary>
public sealed record EmpresaEfd(string Cnpj, string Nome, string? Fantasia, string Uf, string CodMunicipio, string? InscricaoEstadual, string? InscricaoMunicipal,
                                string? Cep, string? Logradouro, string? Numero, string? Complemento, string? Bairro, string? Telefone, string? Email,
                                string? Regime, bool RegimeCaixa);

/// <summary>Contabilista (registro 0100). <paramref name="CnpjEscritorio"/> é o CNPJ do escritório contábil, quando o contador pertence a um.</summary>
public sealed record ContadorEfd(string Nome, string Cpf, string Crc, string? CnpjEscritorio, string? Email, string? Telefone);

/// <summary>Participante (registro 0150): cliente ou fornecedor do cadastro, ou montado a partir dos dados da própria nota quando a nota não tem cadastro.</summary>
public sealed record ParticipanteEfd(string Codigo, string Nome, string? Documento, string? InscricaoEstadual, string? CodMunicipio, string? Uf, string? Logradouro,
                                     string? Numero, string? Complemento, string? Bairro, string CodPais, bool DoCadastro)
{
    public bool Estrangeiro => CodPais != "01058";
    public string? Cnpj => Documento is { Length: 14 } ? Documento : null;
    public string? Cpf => Documento is { Length: 11 } ? Documento : null;
}

/// <summary>Item de documento fiscal com o que a EFD usa (CFOP já é o de escrituração; produto vem do cadastro quando há vínculo).</summary>
public sealed record ItemEfd(
    int NumeroItem, long? ProdutoId, int? ProdutoCodigo, string? CodProdutoNota, string Descricao, string? Ncm, string? Cest, string? Gtin, string TipoItem,
    decimal? AliquotaProduto, string Cfop, string Unidade, decimal Quantidade, decimal ValorProduto, decimal Desconto, decimal Frete, decimal Seguro, decimal Outras,
    decimal ValorContabil, string? CstIcms, string? Origem, decimal BcIcms, decimal AliqIcms, decimal Icms, decimal BcIcmsSt, decimal IcmsSt,
    string? CstIpi, decimal BcIpi, decimal AliqIpi, decimal Ipi, string? CstPis, decimal BcPis, decimal AliqPis, decimal Pis,
    string? CstCofins, decimal BcCofins, decimal AliqCofins, decimal Cofins, decimal BcIss, decimal AliqIss, decimal Iss)
{
    /// <summary>COD_ITEM do arquivo: o código sequencial do produto cadastrado; sem vínculo, o código da nota.</summary>
    public string CodigoItem => ProdutoCodigo?.ToString() ?? (string.IsNullOrWhiteSpace(CodProdutoNota) ? "SEMCOD" : CodProdutoNota.Trim());

    /// <summary>CFOP só com dígitos (o cadastro aceita "5.102").</summary>
    public string CfopNumerico => new(Cfop.Where(char.IsAsciiDigit).ToArray());
}

/// <summary>Documento fiscal lido do banco: NF-e (55), NFC-e (65) ou NFS-e.</summary>
public sealed record DocumentoEfd(
    long Id, string Modelo, string? Chave, bool Entrada, string Numero, string Serie, DateOnly Emissao, DateOnly EntradaSaida, string Situacao,
    string? EmitenteDocumento, string? EmitenteNome, string? EmitenteUf, string? DestinatarioDocumento, string? DestinatarioNome, string? DestinatarioUf,
    ParticipanteEfd? Participante, decimal ValorProdutos, decimal Desconto, decimal Frete, decimal Seguro, decimal Outras, decimal Total,
    decimal BcIcms, decimal Icms, decimal BcIcmsSt, decimal IcmsSt, decimal Ipi, decimal Pis, decimal Cofins, decimal Iss, decimal RetPis, decimal RetCofins,
    IReadOnlyList<ItemEfd> Itens)
{
    public bool Autorizada => Situacao == "AUTORIZADA";
    public bool Nfse => Modelo == "NFSE";
    public decimal SomaOperacao => Itens.Sum(i => i.ValorContabil);
}

/// <summary>Escolhas da EFD ICMS/IPI. Os nulos são deduzidos (versão do leiaute pelo ano) ou ficam vazios.</summary>
/// <param name="Perfil">0000.IND_PERFIL: A (mais detalhado), B ou C. No A, o C170 sai em todas as notas; no B e no C, só nas entradas de terceiros.</param>
/// <param name="IndicadorAtividade">0000.IND_ATIV: 0 industrial ou equiparado a industrial (obrigado ao IPI), 1 outros.</param>
/// <param name="CodigoVersao">0000.COD_VER (tabela 4.1.1); nulo = 020 para 2026 em diante, 019 para 2025.</param>
/// <param name="CodigoReceitaIcms">E116.COD_REC, código de receita do ICMS próprio na UF; obrigatório quando há ICMS a recolher.</param>
/// <param name="CodigoReceitaIcmsSt">E250.COD_REC do ICMS-ST; obrigatório quando há ICMS-ST a recolher.</param>
/// <param name="SaldoCredorAnterior">E110.VL_SLD_CREDOR_ANT: saldo credor transportado do período anterior.</param>
public sealed record OpcoesEfdIcms(
    char Perfil = 'A',
    char IndicadorAtividade = '1',
    bool Retificadora = false,
    string? CodigoVersao = null,
    string? CodigoReceitaIcms = null,
    string? CodigoReceitaIcmsSt = null,
    int DiaVencimento = 10,
    decimal SaldoCredorAnterior = 0,
    bool InventarioZerado = false);

/// <summary>Escolhas da EFD-Contribuições.</summary>
/// <param name="IndicadorNaturezaPj">0000.IND_NAT_PJ: 00 geral, 01 sociedade cooperativa, 02 sujeita ao PIS/Pasep exclusivamente com base na folha.</param>
/// <param name="IndicadorAtividade">0000.IND_ATIV: 0 industrial, 1 prestador de serviços, 2 comércio, 3 financeira, 4 imobiliária, 9 outros.</param>
public sealed record OpcoesEfdContribuicoes(
    bool Retificadora = false,
    string? NumeroReciboAnterior = null,
    string IndicadorNaturezaPj = "00",
    char IndicadorAtividade = '2',
    string? CodigoVersao = null);

/// <summary>Configuração persistida por empresa (fiscal.efd_configuracao).</summary>
public sealed record ConfiguracaoEfd(char IcmsPerfil = 'A', char IcmsIndicadorAtividade = '1', string? IcmsCodigoReceita = null, string? IcmsCodigoReceitaSt = null,
                                     int IcmsDiaVencimento = 10, string ContribNaturezaPj = "00", char ContribIndicadorAtividade = '2');

/// <summary>Apuração PIS/COFINS salva (fiscal.apuracao_pis_cofins) com a memória de cálculo, para o bloco M.</summary>
public sealed record ApuracaoPisCofinsEfd(DateOnly Competencia, string Regime, decimal ReceitaBase, decimal AliquotaPis, decimal AliquotaCofins,
                                          decimal PisDebito, decimal CofinsDebito, decimal BaseCredito, decimal PisCredito, decimal CofinsCredito,
                                          decimal PisRetido, decimal CofinsRetido, decimal PisAPagar, decimal CofinsAPagar)
{
    public bool Cumulativo => Regime == "CUMULATIVO";
}

/// <summary>Tudo o que os geradores da EFD ICMS/IPI precisam.</summary>
/// <param name="Ajustes">Ajustes da apuração do ICMS (E111) da competência.</param>
/// <param name="Consumo">Contas de energia, gás, água e comunicação recebidas no período (C500/D500).</param>
/// <param name="Inventario">Inventário (bloco H) a informar nesta EFD.</param>
public sealed record DadosEfdIcms(EmpresaEfd Empresa, ContadorEfd? Contador, DateOnly Inicio, DateOnly Fim, IReadOnlyList<DocumentoEfd> Documentos, OpcoesEfdIcms Opcoes,
                                  IReadOnlyList<AjusteIcmsEfd>? Ajustes = null, IReadOnlyList<ContaConsumoEfd>? Consumo = null, InventarioEfd? Inventario = null);

/// <summary>Tudo o que os geradores da EFD-Contribuições precisam.</summary>
public sealed record DadosEfdContribuicoes(EmpresaEfd Empresa, ContadorEfd? Contador, DateOnly Inicio, DateOnly Fim, IReadOnlyList<DocumentoEfd> Documentos,
                                           ApuracaoPisCofinsEfd? Apuracao, OpcoesEfdContribuicoes Opcoes);

/// <summary>Arquivo gerado (sem assinaturas): linhas, contagem por registro, conteúdo ISO-8859-1 e hash SHA-1.</summary>
public sealed record ArquivoGeradoEfd(IReadOnlyList<string> Linhas, IReadOnlyDictionary<string, int> Registros, byte[] Conteudo, string HashSha1)
{
    public int QuantidadeLinhas => Linhas.Count;
}
