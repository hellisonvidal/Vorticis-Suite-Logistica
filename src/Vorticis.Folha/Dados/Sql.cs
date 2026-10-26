using Npgsql;
using Vorticis.Domain.Comum;

namespace Vorticis.Folha.Dados;

/// <summary>Auxiliares de acesso ao PostgreSQL usados pelos repositórios da folha.</summary>
internal static class Sql
{
    public static NpgsqlCommand Com(this NpgsqlCommand cmd, string nome, object? valor)
    {
        cmd.Parameters.AddWithValue(nome, valor ?? DBNull.Value);
        return cmd;
    }

    /// <summary>Parâmetro com tipo explícito (necessário quando o valor pode ser nulo e o tipo não é inferível).</summary>
    public static NpgsqlCommand Com(this NpgsqlCommand cmd, string nome, NpgsqlTypes.NpgsqlDbType tipo, object? valor)
    {
        cmd.Parameters.AddWithValue(nome, tipo, valor ?? DBNull.Value);
        return cmd;
    }

    /// <summary>Texto opcional: vazio vira NULL.</summary>
    public static NpgsqlCommand ComTexto(this NpgsqlCommand cmd, string nome, string? valor)
        => cmd.Com(nome, NpgsqlTypes.NpgsqlDbType.Varchar, string.IsNullOrWhiteSpace(valor) ? null : valor.Trim());

    public static NpgsqlCommand ComData(this NpgsqlCommand cmd, string nome, DateOnly? valor)
        => cmd.Com(nome, NpgsqlTypes.NpgsqlDbType.Date, valor);

    public static NpgsqlCommand ComId(this NpgsqlCommand cmd, string nome, long? valor)
        => cmd.Com(nome, NpgsqlTypes.NpgsqlDbType.Bigint, valor);

    public static string? Texto(this NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    public static DateOnly? Data(this NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetFieldValue<DateOnly>(i);
    public static long? Id(this NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);
    public static decimal? Valor(this NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDecimal(i);

    /// <summary>Erros das regras do banco (CHECK, UNIQUE, FK, RAISE) viram <see cref="ViolacaoRegraException"/> com a mensagem do banco.</summary>
    public static async Task<T> TraduzirAsync<T>(Func<Task<T>> acao)
    {
        try
        {
            return await acao();
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.RaiseException or PostgresErrorCodes.CheckViolation
                                                       or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation)
        {
            throw new ViolacaoRegraException(Mensagem(ex), ex);
        }
    }

    private static string Mensagem(PostgresException ex) => ex.SqlState switch
    {
        PostgresErrorCodes.UniqueViolation => ex.ConstraintName switch
        {
            "uq_folha_func_matricula" => "Já existe funcionário com esta matrícula.",
            "uq_folha_func_cpf" => "Já existe funcionário com este CPF.",
            "uq_folha_rubrica" => "Já existe rubrica com este código.",
            "uq_folha_departamento" => "Já existe departamento com este código.",
            "uq_folha_cargo" => "Já existe cargo com este código.",
            "uq_folha_contab_ativa" => "Esta competência já está contabilizada.",
            _ => "Registro duplicado.",
        },
        PostgresErrorCodes.ForeignKeyViolation => "O registro está em uso e não pode ser excluído.",
        PostgresErrorCodes.CheckViolation => $"Dado inválido ({ex.ConstraintName}).",
        _ => ex.MessageText,
    };

    /// <summary>Registra o usuário que altera (trilha de auditoria) na transação.</summary>
    public static async Task DefinirUsuarioAsync(NpgsqlConnection con, NpgsqlTransaction tx, long? usuarioId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT set_config('vorticis.usuario', COALESCE((SELECT login FROM core.usuario WHERE id = @u), current_user), true)
            """, con, tx);
        cmd.Com("u", NpgsqlTypes.NpgsqlDbType.Bigint, usuarioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
