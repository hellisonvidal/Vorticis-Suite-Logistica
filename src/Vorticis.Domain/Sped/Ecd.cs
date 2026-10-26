using Vorticis.Domain.Contabil;

namespace Vorticis.Domain.Sped;

/// <summary>Identificação da pessoa jurídica (ECD 0000, 0007, I030).</summary>
public sealed record EmpresaEcd(
    string Cnpj,
    string Nome,
    string Uf,
    string CodMunicipioIbge,
    string? Municipio,
    string? InscricaoEstadual,
    string? InscricaoMunicipal,
    string? Nire,
    bool GrandePorte,
    DateOnly DataInicioAtividades);

/// <summary>Conta do plano de contas (I050) com o vínculo ao plano referencial do ano (I051), quando houver.</summary>
public sealed record ContaEcd(
    long Id,
    string Codigo,
    string Descricao,
    short Nivel,
    bool Analitica,
    long? ContaPaiId,
    string CodNat,
    IndicadorDC Natureza,
    DateOnly DataInclusao,
    string? CodigoReferencial = null);

/// <summary>Centro de custo (I100).</summary>
public sealed record CentroCustoEcd(long Id, string Codigo, string Descricao, DateOnly DataInclusao);

/// <summary>Histórico padronizado (I075).</summary>
public sealed record HistoricoPadraoEcd(string Codigo, string Descricao);

/// <summary>Saldo de uma conta analítica (e centro de custo; 0 = sem centro) antes do início do período. Débito positivo.</summary>
public sealed record SaldoAberturaEcd(long ContaId, long CentroCustoId, decimal Saldo);

/// <summary>Partida de lançamento (I250). <paramref name="CentroCustoId"/> nulo = sem centro de custo.</summary>
public sealed record PartidaEcd(long ContaId, long? CentroCustoId, IndicadorDC DC, decimal Valor, string Historico, string? NumeroArquivo = null);

/// <summary>Lançamento (I200). Inclui estornados e estornos: ambos constam do Diário.</summary>
/// <param name="SaldoInicial">Lançamento de implantação de saldos (origem SALDO_INICIAL) — usado na classificação das mutações do PL (J215).</param>
public sealed record LancamentoEcd(
    long Id,
    long Numero,
    DateOnly Data,
    IndicadorLancamento Indicador,
    DateOnly? DataExtemporanea,
    IReadOnlyList<PartidaEcd> Partidas,
    bool SaldoInicial = false)
{
    public decimal Total => Partidas.Where(p => p.DC == IndicadorDC.D).Sum(p => p.Valor);
}

/// <summary>Signatário da escrituração (J930). <paramref name="CodigoQualificacao"/>: tabela de qualificação do assinante (900 = contador).</summary>
public sealed record SignatarioEcd(
    string Nome,
    string CpfCnpj,
    string Qualificacao,
    string CodigoQualificacao,
    bool ResponsavelLegal,
    string? Crc = null,
    string? UfCrc = null,
    string? Email = null,
    string? Telefone = null,
    string? NumeroCertidaoCrc = null,
    DateOnly? ValidadeCertidaoCrc = null)
{
    public bool Contador => CodigoQualificacao == QualificacaoAssinante.Contador;
}

/// <summary>Como as mutações do patrimônio líquido vão ao bloco J (J210.IND_TIP).</summary>
public enum TipoMutacoesEcd
{
    /// <summary>DMPL (IND_TIP = 1): um J210 por código de aglutinação do PL, com os fatos contábeis (J215).</summary>
    Dmpl,
    /// <summary>DLPA (IND_TIP = 0): um único J210, o do código de aglutinação de lucros ou prejuízos acumulados.</summary>
    Dlpa,
    /// <summary>Sem J210 (registro facultativo).</summary>
    Nenhuma,
}

/// <summary>Parâmetros do livro gerado (Livro G — Diário Geral).</summary>
/// <param name="NumeroOrdem">Número de ordem do livro (I030/J900.NUM_ORD), sequencial por tipo de livro.</param>
/// <param name="NaturezaLivro">Nome do livro (I030.NAT_LIVR = J900.NAT_LIVRO).</param>
/// <param name="IndicadorSituacaoInicio">0000.IND_SIT_INI_PER: 0 normal, 1 abertura, 2 resultante de cisão/fusão/incorporação, 3 início de obrigatoriedade.</param>
/// <param name="IndicadorSituacaoEspecial">0000.IND_SIT_ESP: 1 cisão, 2 fusão, 3 incorporação, 4 extinção (nulo = não há).</param>
/// <param name="NivelAglutinacao">Nível do plano de contas cujas contas viram linhas de detalhe (D) nas demonstrações; contas
/// analíticas de nível menor são linhas de detalhe próprias. Mínimo 2 (o nível 1 das demonstrações é sempre totalizador).</param>
/// <param name="ResultadoAnterior">Resultado (débito positivo, sem os lançamentos de encerramento) do exercício anterior por conta analítica
/// de resultado, para J150.VL_CTA_INI. Nulo = sem exercício anterior (campos vazios).</param>
public sealed record ParametrosEcd(
    DateOnly Inicio,
    DateOnly Fim,
    int NumeroOrdem = 1,
    string NaturezaLivro = "DIÁRIO GERAL",
    string? CodPlanRef = null,
    char IndicadorSituacaoInicio = '0',
    char? IndicadorSituacaoEspecial = null,
    DateOnly? DataArquivamentoAtos = null,
    DateOnly? DataArquivamentoConversao = null,
    DateOnly? FimExercicioSocial = null,
    short NivelAglutinacao = 3,
    TipoMutacoesEcd Mutacoes = TipoMutacoesEcd.Dmpl,
    bool IncluirHistoricosPadronizados = true,
    bool MudancaPlanoContas = false,
    IReadOnlyDictionary<long, decimal>? ResultadoAnterior = null);

/// <summary>Tudo o que o gerador precisa, já lido do banco: o gerador é uma função pura desses dados.</summary>
public sealed record DadosEcd(
    EmpresaEcd Empresa,
    ParametrosEcd Parametros,
    IReadOnlyList<ContaEcd> Plano,
    IReadOnlyList<CentroCustoEcd> CentrosCusto,
    IReadOnlyList<HistoricoPadraoEcd> Historicos,
    IReadOnlyList<SaldoAberturaEcd> SaldosAbertura,
    IReadOnlyList<LancamentoEcd> Lancamentos,
    IReadOnlyList<SignatarioEcd> Signatarios);

public enum GravidadePendencia { Aviso, Bloqueante }

/// <summary>Pendência encontrada antes de gerar a ECD. Bloqueantes impedem a geração (o PVA rejeitaria o arquivo).</summary>
public sealed record PendenciaEcd(GravidadePendencia Gravidade, string Codigo, string Mensagem)
{
    public bool Bloqueante => Gravidade == GravidadePendencia.Bloqueante;
    public override string ToString() => $"{(Bloqueante ? "Bloqueante" : "Aviso")}: {Mensagem}";
}

/// <summary>Arquivo gerado (sem assinaturas): linhas, contagem por registro e o hash SHA-1 que identifica a escrituração.</summary>
public sealed record EscrituracaoEcd(IReadOnlyList<string> Linhas, IReadOnlyDictionary<string, int> Registros, byte[] Conteudo, string HashSha1)
{
    public int QuantidadeLinhas => Linhas.Count;
}
