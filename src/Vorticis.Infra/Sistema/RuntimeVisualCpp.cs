namespace Vorticis.Infra.Sistema;

/// <summary>
/// Os binários do PostgreSQL para Windows dependem do Microsoft Visual C++ Redistributable 2015-2022 (x64). Sem ele o
/// Windows mostra uma caixa "vcruntime140.dll não foi encontrado" e o processo fica parado esperando alguém clicar.
/// A pasta pgsql\bin traz as DLLs ao lado dos executáveis; esta verificação confere se estão lá (ou instaladas no sistema).
/// </summary>
public static class RuntimeVisualCpp
{
    public static readonly string[] Dlls = ["vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll"];

    public const string UrlDownload = "https://aka.ms/vs/17/release/vc_redist.x64.exe";

    /// <summary>DLLs que não estão na pasta dos binários nem no diretório do sistema (vazio fora do Windows).</summary>
    public static IReadOnlyList<string> Ausentes(string pastaBinarios, string? pastaSistema = null, bool? windows = null)
    {
        if (!(windows ?? OperatingSystem.IsWindows())) return [];
        pastaSistema ??= Environment.SystemDirectory;
        return [.. Dlls.Where(d => !File.Exists(Path.Combine(pastaBinarios, d)) && !File.Exists(Path.Combine(pastaSistema, d)))];
    }
}
