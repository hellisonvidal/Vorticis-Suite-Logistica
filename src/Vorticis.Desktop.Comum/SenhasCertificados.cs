using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vorticis.Desktop.Comum;

/// <summary>
/// Senhas dos certificados A1 usados como padrão, guardadas só neste computador e protegidas pelo Windows (DPAPI, escopo do
/// usuário atual): quem copiar o arquivo para outra máquina ou usuário não consegue abri-las. O banco continua sem senha alguma.
/// Fora do Windows não há proteção equivalente: a senha não é guardada e o programa a pede no uso.
/// </summary>
public static class SenhasCertificados
{
    /// <summary>Arquivo das senhas protegidas (mude nos testes).</summary>
    public static string Arquivo { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vorticis", "senhas_certificados.json");

    /// <summary>Proteção/abertura dos bytes; padrão: DPAPI do usuário no Windows. Substituível nos testes.</summary>
    public static Func<byte[], byte[]>? Proteger { get; set; } = OperatingSystem.IsWindows() ? ProtegerDpapi : null;
    public static Func<byte[], byte[]>? Abrir { get; set; } = OperatingSystem.IsWindows() ? AbrirDpapi : null;

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static byte[] ProtegerDpapi(byte[] b) => ProtectedData.Protect(b, null, DataProtectionScope.CurrentUser);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static byte[] AbrirDpapi(byte[] b) => ProtectedData.Unprotect(b, null, DataProtectionScope.CurrentUser);

    public static bool Disponivel => Proteger is not null && Abrir is not null;

    private static Dictionary<string, string> Ler()
    {
        try { return File.Exists(Arquivo) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Arquivo)) ?? [] : []; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>Guarda a senha do certificado; false se não há proteção disponível neste sistema.</summary>
    public static bool Guardar(long certificadoId, string senha)
    {
        if (Proteger is null) return false;
        var dados = Ler();
        dados[certificadoId.ToString()] = Convert.ToBase64String(Proteger(Encoding.UTF8.GetBytes(senha)));
        Directory.CreateDirectory(Path.GetDirectoryName(Arquivo)!);
        File.WriteAllText(Arquivo, JsonSerializer.Serialize(dados));
        return true;
    }

    public static string? Obter(long certificadoId)
    {
        if (Abrir is null || !Ler().TryGetValue(certificadoId.ToString(), out var protegida)) return null;
        try { return Encoding.UTF8.GetString(Abrir(Convert.FromBase64String(protegida))); }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return null; }
    }

    public static void Esquecer(long certificadoId)
    {
        var dados = Ler();
        if (dados.Remove(certificadoId.ToString())) File.WriteAllText(Arquivo, JsonSerializer.Serialize(dados));
    }
}
