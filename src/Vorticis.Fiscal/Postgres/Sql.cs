using Npgsql;
using NpgsqlTypes;
using Vorticis.Domain.Comum;

namespace Vorticis.Fiscal.Postgres;

/// <summary>Auxiliares de acesso ao PostgreSQL dos repositórios fiscais (mesmo padrão dos repositórios do Vorticis.Infra).</summary>
internal static class Sql
{
    public static NpgsqlCommand Com(this NpgsqlCommand cmd, string nome, object? valor)
    {
        cmd.Parameters.AddWithValue(nome, valor ?? DBNull.Value);
        return cmd;
    }

    /// <summary>Parâmetro com tipo explícito — necessário quando o valor pode ser nulo.</summary>
    public static NpgsqlCommand Com(this NpgsqlCommand cmd, string nome, NpgsqlDbType tipo, object? valor)
    {
        cmd.Parameters.AddWithValue(nome, tipo, valor ?? DBNull.Value);
        return cmd;
    }

    /// <summary>Texto opcional: vazio vira NULL.</summary>
    public static NpgsqlCommand ComTexto(this NpgsqlCommand cmd, string nome, string? valor)
        => cmd.Com(nome, NpgsqlDbType.Varchar, string.IsNullOrWhiteSpace(valor) ? null : valor.Trim());

    public static string? Texto(this NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i).Trim();

    public static DateOnly? DataOuNula(this NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetFieldValue<DateOnly>(i);

    public static long? LongOuNulo(this NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);

    /// <summary>Erros das regras do banco (RAISE, CHECK, UNIQUE, FK) viram violação de regra com a mensagem original.</summary>
    public static async Task<T> TraduzirErrosAsync<T>(Func<Task<T>> acao)
    {
        try
        {
            return await acao();
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.RaiseException or PostgresErrorCodes.CheckViolation
                                                       or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation)
        {
            throw new ViolacaoRegraException(ex.MessageText, ex);
        }
    }

    public static async Task DefinirUsuarioAsync(NpgsqlConnection con, NpgsqlTransaction tx, long? usuarioId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT set_config('vorticis.usuario', COALESCE((SELECT login FROM core.usuario WHERE id = @u), current_user), true)
            """, con, tx);
        cmd.Com("u", NpgsqlDbType.Bigint, usuarioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
