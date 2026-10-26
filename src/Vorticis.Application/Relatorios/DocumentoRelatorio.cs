namespace Vorticis.Application.Relatorios;

public enum Alinhamento { Esquerda, Direita, Centro }

public sealed record Coluna(string Titulo, double LarguraCm, Alinhamento Alinhamento = Alinhamento.Esquerda);

public sealed record LinhaTabela(IReadOnlyList<string> Celulas, bool Negrito = false, int Recuo = 0);

/// <summary>Bloco do relatório: título opcional, texto livre e/ou tabela.</summary>
public sealed record Bloco(string? Titulo = null, string? Texto = null, IReadOnlyList<Coluna>? Colunas = null,
                           IReadOnlyList<LinhaTabela>? Linhas = null, bool NovaPagina = false);

/// <summary>
/// Termo de abertura ou encerramento de livro (ITG 2000, itens 8 a 12). O texto pode conter <c>{folhas}</c>,
/// substituído pelo número total de folhas do livro (calculado após a montagem).
/// </summary>
public sealed record Termo(string Titulo, string Texto, IReadOnlyList<string> Assinaturas);

/// <summary>Modelo neutro de relatório; a infraestrutura transforma em PDF.</summary>
public sealed record DocumentoRelatorio(
    string Titulo,
    IReadOnlyList<string> Cabecalho,       // linhas de identificação (empresa, CNPJ, período)
    IReadOnlyList<Bloco> Blocos,
    bool Paisagem = false,
    Termo? TermoAbertura = null,
    Termo? TermoEncerramento = null,
    string RotuloPagina = "Página",
    IReadOnlyList<string>? Assinaturas = null,    // bloco de assinaturas ao final do conteúdo (opção comum dos relatórios)
    AssinaturaDigitalPdf? AssinaturaDigital = null,    // assina digitalmente o arquivo PDF (certificado A1/A3)
    byte[]? Logo = null);    // logo no cabeçalho (PNG ou JPEG); sem ele, o da empresa ativa (LogoRelatorios.Atual)

/// <summary>Assinatura digital do PDF com o certificado dado (chave privada presente; no A3, o driver pede o PIN).</summary>
public sealed record AssinaturaDigitalPdf(System.Security.Cryptography.X509Certificates.X509Certificate2 Certificado, string? Motivo = null, string? Local = null);

public sealed record ArquivoGerado(byte[] Conteudo, int Paginas);

public interface IGeradorPdf
{
    ArquivoGerado Gerar(DocumentoRelatorio documento);
}
