using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vorticis.Desktop.Comum;

/// <summary>
/// "Salvar login neste computador": usuário e senha gravados em %APPDATA%\Vorticis\login.json.
/// A senha é cifrada com DPAPI (só o mesmo usuário do Windows, neste computador, consegue decifrá-la);
/// fora do Windows só o usuário é lembrado.
/// </summary>
public static class LoginSalvo
{
    public sealed record Dados(string Usuario, string? Senha);

    public static string Arquivo =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vorticis", "login.json");

    public static Dados? Obter()
    {
        try
        {
            if (!File.Exists(Arquivo) || JsonNode.Parse(File.ReadAllText(Arquivo)) is not JsonObject j
                || j["Usuario"]?.GetValue<string>() is not { Length: > 0 } usuario)
                return null;
            string? senha = null;
            if (OperatingSystem.IsWindows() && j["SenhaProtegida"]?.GetValue<string>() is { Length: > 0 } protegida)
                senha = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protegida), Entropia, DataProtectionScope.CurrentUser));
            return new Dados(usuario, senha);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or FormatException
                                       or CryptographicException or InvalidOperationException)
        {
            return null;   // arquivo ilegível ou de outro usuário do Windows: ignora
        }
    }

    public static void Salvar(string usuario, string senha)
    {
        var j = new JsonObject { ["Usuario"] = usuario };
        if (OperatingSystem.IsWindows())
            j["SenhaProtegida"] = Convert.ToBase64String(
                ProtectedData.Protect(Encoding.UTF8.GetBytes(senha), Entropia, DataProtectionScope.CurrentUser));
        Directory.CreateDirectory(Path.GetDirectoryName(Arquivo)!);
        File.WriteAllText(Arquivo, j.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void Esquecer()
    {
        try { File.Delete(Arquivo); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // Separa estas senhas de outros dados cifrados com DPAPI pelo mesmo usuário.
    private static readonly byte[] Entropia = "Vorticis.LoginSalvo"u8.ToArray();
}
