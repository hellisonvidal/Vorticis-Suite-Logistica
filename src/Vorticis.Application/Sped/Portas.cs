using System.Security.Cryptography.X509Certificates;
using Vorticis.Domain.Sped;

namespace Vorticis.Application.Sped;

/// <summary>Dados da empresa e do exercício lidos para a ECD (sem os signatários, que vêm do cadastro de responsáveis).</summary>
/// <param name="FimExercicio">Data final do exercício social (I030.DT_EX_SOCIAL).</param>
/// <param name="PeriodosAbertos">Competências do exercício ainda abertas (aviso: convém fechar antes de entregar).</param>
/// <param name="NumeroOrdemSugerido">Número do livro sugerido: o da última ECD gerada no mesmo ano ou o seguinte ao do ano anterior (1 na primeira).</param>
public sealed record DadosContabeisEcd(
    EmpresaEcd Empresa,
    long ExercicioId,
    DateOnly InicioExercicio,
    DateOnly FimExercicio,
    string? CodPlanRef,
    IReadOnlyList<ContaEcd> Plano,
    IReadOnlyList<CentroCustoEcd> CentrosCusto,
    IReadOnlyList<HistoricoPadraoEcd> Historicos,
    IReadOnlyList<SaldoAberturaEcd> SaldosAbertura,
    IReadOnlyList<LancamentoEcd> Lancamentos,
    IReadOnlyDictionary<long, decimal>? ResultadoAnterior,
    IReadOnlyList<DateOnly> PeriodosAbertos,
    int NumeroOrdemSugerido);

/// <summary>ECD gerada, para o histórico (contabil.ecd_gerada).</summary>
public sealed record EcdRegistrada(long Id, int Ano, DateOnly Inicio, DateOnly Fim, int NumeroOrdem, string NaturezaLivro, string HashSha1,
                                   int Linhas, int Assinaturas, DateTimeOffset GeradoEm);

/// <summary>Consultas de leitura para a ECD (Infra, SQL direto).</summary>
public interface IConsultasEcd
{
    /// <summary>Carrega o exercício <paramref name="ano"/> da empresa; null se o exercício não existe.</summary>
    Task<DadosContabeisEcd?> CarregarAsync(long empresaId, int ano, CancellationToken ct);
    Task<long> RegistrarAsync(long empresaId, int ano, DateOnly inicio, DateOnly fim, int numeroOrdem, string naturezaLivro, string hashSha1,
                              int linhas, int assinaturas, long? usuarioId, CancellationToken ct);
    Task<IReadOnlyList<EcdRegistrada>> ListarAsync(long empresaId, CancellationToken ct);
}

/// <summary>Certificado de um signatário e o código de qualificação com que ele assina (J930.COD_ASSIN: 900 contador, 205 administrador...).</summary>
/// <param name="Certificado">ICP-Brasil A1 (aberto do .pfx com a senha) ou A3 (do repositório do Windows, com a chave no token/cartão).</param>
public sealed record CertificadoAssinante(X509Certificate2 Certificado, string CodigoQualificacao);

/// <summary>Assinatura encontrada no fim de um arquivo do SPED.</summary>
/// <param name="Valida">A assinatura PKCS#7 confere (criptograficamente) com o hash SHA-1 do arquivo sem assinaturas.
/// A cadeia do certificado (ICP-Brasil) não é conferida aqui: o PVA confere ao importar.</param>
public sealed record AssinaturaSped(int Ordem, string Etiqueta, string CodigoQualificacao, string Titular, string? Documento, string Impressao,
                                    DateTimeOffset? AssinadoEm, bool Valida, string? Erro = null);

/// <summary>
/// Assinatura digital de arquivos do SPED no formato do PVA: cada assinatura é um PKCS#7/CMS (SignedData) anexado ao fim do arquivo,
/// depois do registro 9999, precedido da etiqueta "SBRCAAEPDR" + ordem (2 dígitos) + código de qualificação (3 dígitos).
/// O Desktop fornece os certificados (A1 ou A3); a implementação fica no Infra.
/// </summary>
public interface IAssinadorSped
{
    /// <summary>Anexa as assinaturas, na ordem dada, depois das que o arquivo já tiver. Todas assinam o hash do arquivo sem assinaturas.</summary>
    byte[] Assinar(byte[] arquivo, IReadOnlyList<CertificadoAssinante> assinantes);

    /// <summary>Assinaturas já anexadas ao arquivo, conferidas contra o conteúdo.</summary>
    IReadOnlyList<AssinaturaSped> LerAssinaturas(byte[] arquivo);

    /// <summary>Tamanho do arquivo sem as assinaturas (posição da primeira etiqueta, ou o tamanho todo).</summary>
    int TamanhoSemAssinaturas(byte[] arquivo);
}
