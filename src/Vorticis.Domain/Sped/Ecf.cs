namespace Vorticis.Domain.Sped;

/// <summary>Forma de tributação da ECF (0010.FORMA_TRIB): a primeira versão cobre Lucro Real e Lucro Presumido, ambos trimestrais.</summary>
public enum FormaTributacaoEcf { Real, Presumido }

/// <summary>Identificação da pessoa jurídica (ECF 0000, 0030).</summary>
public sealed record EmpresaEcf(
    string Cnpj,
    string Nome,
    string Uf,
    string CodMunicipioIbge,
    string NaturezaJuridica,
    string Cnae,
    string? Logradouro,
    string? Numero,
    string? Complemento,
    string? Bairro,
    string? Cep,
    string? Telefone,
    string? Email,
    DateOnly DataInicioAtividades);

/// <summary>Conta do plano de contas referencial da RFB (tabelas dinâmicas L100/L300/P100/P150), com a hierarquia.</summary>
public sealed record ReferencialEcf(string Codigo, string Descricao, bool Analitica, short Nivel, string CodNat, string? CodigoPai);

/// <summary>Receita do trimestre sujeita a um percentual de presunção (P200/P400): extraída da memória de cálculo da apuração.</summary>
public sealed record ReceitaPresumidaEcf(decimal PercentualIrpj, decimal PercentualCsll, decimal Valor);

/// <summary>Apuração trimestral do IRPJ e da CSLL (fiscal.apuracao_lucro) com os valores que a ECF informa.</summary>
public sealed record ApuracaoEcf(
    FormaTributacaoEcf Regime, int Trimestre, DateOnly Inicio, DateOnly Fim, decimal ReceitaBruta, decimal? LucroContabil,
    decimal AdicoesIrpj, decimal ExclusoesIrpj, decimal AdicoesCsll, decimal ExclusoesCsll, decimal CompensacaoIrpj, decimal CompensacaoCsll,
    decimal PrejuizoIrpj, decimal PrejuizoCsll, decimal BaseIrpj, decimal BaseCsll, decimal IrpjNormal, decimal IrpjAdicional, decimal IrpjDeducoes,
    decimal IrpjAPagar, decimal CsllDevida, decimal CsllDeducoes, decimal CsllAPagar,
    IReadOnlyList<ReceitaPresumidaEcf> Receitas, decimal OutrasReceitas);

/// <summary>Lançamento da Parte A do e-Lalur/e-Lacs (fiscal.lalur_lancamento). <paramref name="Tributo"/>: IRPJ, CSLL ou AMBOS.</summary>
public sealed record LancamentoLalurEcf(int Trimestre, string Tributo, bool Adicao, string Descricao, decimal Valor, string? CodigoEcf, long? ContaId)
{
    public bool Irpj => Tributo is "IRPJ" or "AMBOS";
    public bool Csll => Tributo is "CSLL" or "AMBOS";
}

/// <summary>Movimento da Parte B (fiscal.lalur_parte_b): tipo SALDO_INICIAL, PREJUIZO ou COMPENSACAO; valor com sinal (compensação negativa).</summary>
public sealed record MovimentoParteBEcf(string Tributo, string Tipo, int? Ano, int? Trimestre, decimal Valor);

/// <summary>Sócio, titular ou dirigente (Y600).</summary>
public sealed record SocioEcf(string Nome, char TipoPessoa, string? Documento, decimal? Participacao, DateOnly? Entrada, DateOnly? Saida, string Qualificacao);

/// <summary>ECD gerada pelo Vorticis para o mesmo ano (C040: hash da escrituração recuperada).</summary>
public sealed record EcdDaEcf(string HashSha1, DateOnly Inicio, DateOnly Fim, int NumeroOrdem, string NaturezaLivro, string? Nire);

/// <summary>Parâmetros da escrituração (bloco 0).</summary>
/// <param name="Fim">31/12 do ano-calendário, ou o fim das atividades.</param>
/// <param name="IndicadorSituacaoInicio">0000.IND_SIT_INI_PER: 0 regular, 1 abertura (início das atividades no ano).</param>
/// <param name="IndicadorReceita">0010.IND_REC_RECEITA (Presumido): 1 caixa, 2 competência.</param>
public sealed record ParametrosEcf(
    int Ano,
    DateOnly Inicio,
    DateOnly Fim,
    FormaTributacaoEcf Forma,
    string? CodPlanRef,
    char IndicadorSituacaoInicio = '0',
    bool Retificadora = false,
    string? NumeroRecibo = null,
    bool InformarEcd = false,
    char IndicadorReceita = '2');

/// <summary>Tudo o que o gerador precisa, já lido do banco: o gerador é uma função pura desses dados.</summary>
public sealed record DadosEcf(
    EmpresaEcf Empresa,
    ParametrosEcf Parametros,
    IReadOnlyList<ContaEcd> Plano,
    IReadOnlyList<SaldoAberturaEcd> SaldosAbertura,
    IReadOnlyList<LancamentoEcd> Lancamentos,
    IReadOnlyList<ReferencialEcf> Referencial,
    IReadOnlyList<ApuracaoEcf> Apuracoes,
    IReadOnlyList<LancamentoLalurEcf> Lalur,
    IReadOnlyList<MovimentoParteBEcf> ParteB,
    IReadOnlyList<SocioEcf> Socios,
    IReadOnlyList<SignatarioEcd> Signatarios,
    EcdDaEcf? Ecd);

/// <summary>Arquivo gerado (sem assinaturas): linhas, contagem por registro e o hash SHA-1.</summary>
public sealed record EscrituracaoEcf(IReadOnlyList<string> Linhas, IReadOnlyDictionary<string, int> Registros, byte[] Conteudo, string HashSha1)
{
    public int QuantidadeLinhas => Linhas.Count;
}
