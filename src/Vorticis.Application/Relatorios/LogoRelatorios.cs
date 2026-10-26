namespace Vorticis.Application.Relatorios;

/// <summary>
/// Logo da empresa nos relatórios em PDF. <see cref="Atual"/> é o logo da empresa ativa no programa (o casco troca ao mudar de empresa
/// ou ao enviar outro logo); o documento pode trazer o seu (<see cref="DocumentoRelatorio.Logo"/>), que tem preferência.
/// </summary>
public static class LogoRelatorios
{
    public const int TamanhoMaximo = 1024 * 1024;

    public static byte[]? Atual { get; set; }

    /// <summary>PNG ou JPEG pela assinatura do arquivo (não pela extensão).</summary>
    public static bool EhImagem(byte[]? b) =>
        b is { Length: >= 8 } && (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 || b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF);

    /// <summary>Mensagem de erro, ou nulo se o arquivo serve de logo.</summary>
    public static string? Validar(byte[]? b) =>
        b is null || b.Length == 0 ? "Escolha o arquivo do logo."
        : b.Length > TamanhoMaximo ? "O logo deve ter no máximo 1 MB. Reduza a imagem e tente de novo."
        : !EhImagem(b) ? "O logo deve ser uma imagem PNG ou JPG."
        : null;
}
