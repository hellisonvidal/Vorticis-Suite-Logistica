using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using Vorticis.Infra.Sistema;

namespace Vorticis.Desktop.Comum;

public enum ModoBanco { Local, Servidor }

/// <param name="Modo">Local = PostgreSQL embutido, gerenciado pelo Vorticis; Servidor = PostgreSQL informado pelo usuário.</param>
/// <param name="ConnectionString">Só no modo Servidor.</param>
/// <param name="Porta">Modo Local: última porta usada (0 = ainda não iniciado).</param>
/// <param name="Senha">Modo Local: senha do superusuário do banco local (null antes da criação).</param>
public sealed record ConfiguracaoBanco(ModoBanco Modo, string? ConnectionString = null, int Porta = 0, string? Senha = null);

/// <summary>
/// Onde está o banco. Ordem: variável VORTICIS_DB → %APPDATA%\Vorticis\vorticis.json → banco local embutido (se o
/// programa foi instalado com ele) → servidor padrão em localhost:5432.
/// No Windows as senhas são gravadas cifradas com DPAPI (só o mesmo usuário do Windows consegue decifrar).
/// </summary>
public static class Configuracao
{
    public const string Padrao = "Host=localhost;Port=5432;Database=vorticis;Username=vorticis;Password=vorticis";
    public const string NomeBanco = "vorticis";

    public static string ArquivoConfiguracao =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vorticis", "vorticis.json");

    /// <summary>Dados do banco local: %LOCALAPPDATA%\Vorticis\banco (não sai com a desinstalação do programa).</summary>
    public static string PastaBancoLocal => Path.Combine(PastaLocal, "banco");

    /// <summary>Marcas dos módulos abertos (um arquivo travado por processo): o último a fechar para o banco local compartilhado.</summary>
    public static string PastaExecucao => Path.Combine(PastaLocal, "execucao");

    /// <summary>Trava entre processos: só um módulo por vez cria/inicia o banco local (evita disputa pelo cluster).</summary>
    public static string ArquivoTrava => Path.Combine(PastaLocal, "banco.lock");

    /// <summary>Registro da configuração automática do banco (também exibido na tela de conexão).</summary>
    public static string LogConfiguracaoBanco => Path.Combine(PastaLocal, "logs", "configuracao-banco.log");

    public static string LogBancoLocal => Path.Combine(PastaLocal, "logs", "postgres.log");

    public static string PastaLocal =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vorticis");

    /// <summary>
    /// Instalações anteriores à troca de nome guardavam tudo em %APPDATA%\Perseus e %LOCALAPPDATA%\Perseus. Na primeira
    /// abertura do Vorticis essas pastas são renomeadas (o banco local ainda não está em execução nesse momento).
    /// </summary>
    public static void MigrarPastasDoNomeAnterior()
    {
        foreach (var raiz in new[] { Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData })
        {
            var baseDir = Environment.GetFolderPath(raiz);
            var antiga = Path.Combine(baseDir, "Perseus");
            var nova = Path.Combine(baseDir, "Vorticis");
            try
            {
                if (!Directory.Exists(antiga) || Directory.Exists(nova)) continue;
                Directory.Move(antiga, nova);
                var cfgAntiga = Path.Combine(nova, "perseus.json");
                if (File.Exists(cfgAntiga) && !File.Exists(Path.Combine(nova, "vorticis.json")))
                    File.Move(cfgAntiga, Path.Combine(nova, "vorticis.json"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // pasta em uso ou sem permissão: segue com a pasta nova; a tela de conexão mostra o que faltar
            }
        }
    }

    public static bool BancoLocalDisponivel => ServidorPostgresEmbutido.LocalizarBinarios() is not null;

    public static ConfiguracaoBanco Carregar()
    {
        var env = Environment.GetEnvironmentVariable("VORTICIS_DB");
        if (!string.IsNullOrWhiteSpace(env)) return new(ModoBanco.Servidor, env);

        try
        {
            if (Ler() is { } cfg)
            {
                // O banco local guarda porta e senha em uma seção própria, preservada quando se configura um servidor.
                var local = cfg["Local"] as JsonObject;
                var senhaLocal = local is null ? null : RevelarOuNulo(local);
                if (cfg["Modo"]?.GetValue<string>() == nameof(ModoBanco.Local))
                    return new(ModoBanco.Local, Porta: local?["Porta"]?.GetValue<int>() ?? 0, Senha: senhaLocal);
                if (cfg["ConnectionString"]?.GetValue<string>() is { Length: > 0 } cs)
                {
                    var senha = Revelar(cfg);
                    return new(ModoBanco.Servidor, senha is null ? cs : new NpgsqlConnectionStringBuilder(cs) { Password = senha }.ConnectionString);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or FormatException
                                       or CryptographicException or InvalidOperationException or ArgumentException)
        {
            // configuração ilegível: segue para o padrão; a tela de conexão mostra o erro, se houver
        }
        return BancoLocalDisponivel || Directory.Exists(PastaBancoLocal) ? new(ModoBanco.Local) : new(ModoBanco.Servidor, Padrao);
    }

    /// <summary>Grava a conexão com um servidor PostgreSQL informado pelo usuário (mantém os dados do banco local).</summary>
    public static void Salvar(string connectionString)
    {
        var b = new NpgsqlConnectionStringBuilder(connectionString);
        var cfg = new JsonObject { ["Modo"] = nameof(ModoBanco.Servidor) };
        Proteger(cfg, b.Password);
        b.Password = null;
        cfg["ConnectionString"] = b.ConnectionString;
        if (LerSeguro()?["Local"] is JsonObject local) cfg["Local"] = local.DeepClone();
        Gravar(cfg);
    }

    /// <summary>
    /// Passa a usar o banco local. Porta/senha nulas mantêm as já gravadas. A senha é gravada antes de criar o
    /// cluster, para nunca existir um banco local sem senha conhecida.
    /// </summary>
    public static void SalvarLocal(int? porta = null, string? senha = null)
    {
        var anterior = LerSeguro()?["Local"] as JsonObject;
        var local = anterior?.DeepClone() as JsonObject ?? new JsonObject();
        if (porta is not null) local["Porta"] = porta;
        if (senha is not null)
        {
            local.Remove("SenhaProtegida");
            local.Remove("Senha");
            Proteger(local, senha);
        }
        Gravar(new JsonObject { ["Modo"] = nameof(ModoBanco.Local), ["Local"] = local });
    }

    /// <summary>Descrição sem senha, para exibir na tela.</summary>
    public static string Descrever(string connectionString)
    {
        var b = new NpgsqlConnectionStringBuilder(connectionString);
        return BancoDoAplicativo.EhBancoLocal(connectionString)
            ? $"banco local do Vorticis (porta {b.Port})"
            : $"{b.Host}:{b.Port}/{b.Database} (usuário {b.Username})";
    }

    private static void Proteger(JsonObject cfg, string? senha)
    {
        if (string.IsNullOrEmpty(senha)) return;
        if (OperatingSystem.IsWindows())
            cfg["SenhaProtegida"] = Convert.ToBase64String(
                ProtectedData.Protect(Encoding.UTF8.GetBytes(senha), null, DataProtectionScope.CurrentUser));
        else
            cfg["Senha"] = senha;   // fora do Windows (desenvolvimento/testes) o arquivo fica no perfil do usuário
    }

    private static JsonObject? Ler() =>
        File.Exists(ArquivoConfiguracao) ? JsonNode.Parse(File.ReadAllText(ArquivoConfiguracao)) as JsonObject : null;

    private static JsonObject? LerSeguro()
    {
        try
        {
            return Ler();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Senha cifrada por outro usuário do Windows (ou de outra máquina) não é decifrável: trata como desconhecida.</summary>
    private static string? RevelarOuNulo(JsonObject cfg)
    {
        try
        {
            return Revelar(cfg);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }

    private static string? Revelar(JsonObject cfg) =>
        Revelar(cfg["SenhaProtegida"]?.GetValue<string>(), cfg["Senha"]?.GetValue<string>());

    private static string? Revelar(string? protegida, string? aberta)
    {
        if (protegida is { Length: > 0 } && OperatingSystem.IsWindows())
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protegida), null, DataProtectionScope.CurrentUser));
        return aberta is { Length: > 0 } ? aberta : null;
    }

    private static void Gravar(JsonObject cfg)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ArquivoConfiguracao)!);
        File.WriteAllText(ArquivoConfiguracao, cfg.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
