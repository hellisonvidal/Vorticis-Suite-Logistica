using System.Diagnostics;
using Npgsql;
using Vorticis.Application.Sistema;

namespace Vorticis.Infra.Sistema;

/// <summary>
/// Backup/restauração com as ferramentas oficiais do PostgreSQL (pg_dump/pg_restore, formato custom compactado).
/// Localiza os executáveis em VORTICIS_PG_BIN, no PATH ou em C:\Program Files\PostgreSQL\*\bin (a versão mais nova).
/// A senha é passada pela variável PGPASSWORD do processo filho, nunca pela linha de comando.
/// </summary>
public sealed class BackupPostgres(string connectionString, Action? antesDeRestaurar = null) : IBackupBanco
{
    public async Task<string> GerarAsync(string pasta, string prefixo, CancellationToken ct)
    {
        var b = new NpgsqlConnectionStringBuilder(connectionString);
        var arquivo = Path.Combine(pasta, $"{prefixo}_{b.Database}_{DateTime.Now:yyyyMMdd_HHmmss}{ServicoBackup.Extensao}");
        await ExecutarAsync("pg_dump", ["--format=custom", "--compress=6", "--no-owner", "--no-privileges", "--file", arquivo], b, ct);
        return arquivo;
    }

    public async Task RestaurarAsync(string arquivo, CancellationToken ct)
    {
        var b = new NpgsqlConnectionStringBuilder(connectionString);
        antesDeRestaurar?.Invoke();
        NpgsqlConnection.ClearAllPools();
        // --clean --if-exists: remove os objetos atuais antes de recriá-los; --single-transaction: tudo ou nada.
        await ExecutarAsync("pg_restore", ["--clean", "--if-exists", "--no-owner", "--no-privileges", "--single-transaction", arquivo], b, ct);
        NpgsqlConnection.ClearAllPools();
    }

    private static async Task ExecutarAsync(string ferramenta, IEnumerable<string> argumentos, NpgsqlConnectionStringBuilder b, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(LocalizarFerramenta(ferramenta))
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "--host", b.Host ?? "localhost", "--port", b.Port.ToString(), "--username", b.Username ?? "",
                                  "--dbname", b.Database ?? "", "--no-password" }.Concat(argumentos))
            psi.ArgumentList.Add(a);
        psi.Environment["PGPASSWORD"] = b.Password ?? "";
        psi.Environment["PGCONNECT_TIMEOUT"] = "10";

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Não foi possível iniciar {ferramenta}.");
        var erro = p.StandardError.ReadToEndAsync(ct);
        var saida = p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        await saida;
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{ferramenta} terminou com código {p.ExitCode}: {(await erro).Trim()}");
    }

    public static string LocalizarFerramenta(string nome)
    {
        var exe = OperatingSystem.IsWindows() ? nome + ".exe" : nome;
        var candidatos = new List<string>();
        if (Environment.GetEnvironmentVariable("VORTICIS_PG_BIN") is { Length: > 0 } bin) candidatos.Add(Path.Combine(bin, exe));
        // binários distribuídos com o programa (mesma versão do banco local)
        if (ServidorPostgresEmbutido.LocalizarBinarios() is { } embutido) candidatos.Add(Path.Combine(embutido, exe));
        candidatos.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Select(d => Path.Combine(d, exe)));
        if (OperatingSystem.IsWindows())
        {
            var raiz = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PostgreSQL");
            if (Directory.Exists(raiz))
                candidatos.AddRange(Directory.GetDirectories(raiz)
                    .OrderByDescending(d => int.TryParse(Path.GetFileName(d), out var v) ? v : 0)
                    .Select(d => Path.Combine(d, "bin", exe)));
        }
        return candidatos.FirstOrDefault(File.Exists)
               ?? throw new FileNotFoundException(
                   $"{exe} não encontrado. Instale o PostgreSQL (ferramentas de linha de comando) ou informe a pasta em VORTICIS_PG_BIN.");
    }
}
