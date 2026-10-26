using Npgsql;
using Vorticis.Domain.Comum;

namespace Vorticis.Infra.Postgres;

/// <summary>Acesso enxuto ao PostgreSQL: parâmetros nomeados e erros das regras do banco traduzidos para <see cref="ViolacaoRegraException"/>.</summary>
public sealed class BancoModulo(NpgsqlDataSource ds)
{
    public NpgsqlDataSource Fonte => ds;

    private NpgsqlCommand Comando(string sql, (string Nome, object? Valor)[] p)
    {
        var cmd = ds.CreateCommand(sql);
        foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return cmd;
    }

    public async Task<List<T>> ListarAsync<T>(string sql, Func<NpgsqlDataReader, T> ler, CancellationToken ct, params (string, object?)[] p)
    {
        await using var cmd = Comando(sql, p);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var res = new List<T>();
        while (await r.ReadAsync(ct)) res.Add(ler(r));
        return res;
    }

    public Task<T?> EscalarAsync<T>(string sql, CancellationToken ct, params (string, object?)[] p) => Traduzir(async () =>
    {
        await using var cmd = Comando(sql, p);
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is null or DBNull ? default : (T)v;
    });

    public Task<int> ExecutarAsync(string sql, CancellationToken ct, params (string, object?)[] p) => Traduzir(async () =>
    {
        await using var cmd = Comando(sql, p);
        return await cmd.ExecuteNonQueryAsync(ct);
    });

    public static async Task<T> Traduzir<T>(Func<Task<T>> acao)
    {
        try { return await acao(); }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.RaiseException or PostgresErrorCodes.CheckViolation
                                                       or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation)
        {
            throw new ViolacaoRegraException(ex.SqlState switch
            {
                PostgresErrorCodes.UniqueViolation => "Registro repetido: já existe um cadastro com estes dados.",
                PostgresErrorCodes.ForeignKeyViolation => "O registro está em uso (ou depende de outro que não existe) e não pode ser gravado ou excluído.",
                _ => ex.MessageText,
            }, ex);
        }
    }

    /// <summary>
    /// Filtro SQL: empresas em que o usuário (@u) tem a permissão (@perm): administrador do sistema, perfil Administrador ou perfil
    /// com a função. <paramref name="colunaEmpresa"/> é a coluna da empresa na consulta.
    /// </summary>
    public static string Permitida(string colunaEmpresa) => $"""
        (EXISTS (SELECT 1 FROM core.usuario uu WHERE uu.id = @u AND uu.administrador)
         OR EXISTS (SELECT 1 FROM core.usuario_empresa ue JOIN core.perfil pf ON pf.id = ue.perfil_id
                     WHERE ue.usuario_id = @u AND ue.empresa_id = {colunaEmpresa}
                       AND (pf.administrador OR EXISTS (SELECT 1 FROM core.perfil_permissao pp WHERE pp.perfil_id = pf.id AND pp.permissao = @perm))))
        """;
}

public static class LeitorModuloExtensoes
{
    public static string? Texto(this NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    public static DateOnly? Data(this NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetFieldValue<DateOnly>(i);
    public static long? Id(this NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);
    public static decimal? Valor(this NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDecimal(i);
}
