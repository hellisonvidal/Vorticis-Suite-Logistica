using Npgsql;

namespace Vorticis.Infra;

/// <summary>Primeira execução: cria o banco da aplicação no PostgreSQL local se ainda não existir.</summary>
public static class BancoLocal
{
    private const string NomeAtual = "vorticis";
    private const string NomeAnterior = "perseus";

    public static async Task GarantirBancoAsync(string connectionString, CancellationToken ct = default)
    {
        var alvo = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(alvo.Database)) throw new ArgumentException("A conexão não informa o banco de dados (Database=...).");

        try
        {
            await using var con = new NpgsqlConnection(connectionString);
            await con.OpenAsync(ct);
            return;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidCatalogName)
        {
            // banco inexistente: cria abaixo
        }

        var servidor = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres", Pooling = false };
        await using var adm = new NpgsqlConnection(servidor.ConnectionString);
        await adm.OpenAsync(ct);
        var nome = alvo.Database.Replace("\"", "\"\"");

        // Banco criado antes da troca de nome do sistema (Perseus → Vorticis): renomeia em vez de criar um vazio.
        if (alvo.Database == NomeAtual)
        {
            await using var existe = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @n", adm);
            existe.Parameters.AddWithValue("n", NomeAnterior);
            if (await existe.ExecuteScalarAsync(ct) is not null)
            {
                await using var renomear = new NpgsqlCommand($"ALTER DATABASE \"{NomeAnterior}\" RENAME TO \"{nome}\"", adm);
                await renomear.ExecuteNonQueryAsync(ct);
                return;
            }
        }

        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{nome}\" ENCODING 'UTF8' TEMPLATE template0", adm);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
