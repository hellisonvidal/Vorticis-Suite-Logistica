using Npgsql;
using Vorticis.Application.Cadastros;
using Vorticis.Infra;

namespace Vorticis.Infra.Tests;

/// <summary>
/// Cria um banco PostgreSQL descartável por classe de teste. Requer a variável de ambiente
/// VORTICIS_TEST_PG (connection string de um usuário com permissão CREATEDB); sem ela, os testes são pulados.
/// </summary>
public sealed class BancoDeTeste : IAsyncLifetime
{
    public static readonly string? ConexaoServidor = Environment.GetEnvironmentVariable("VORTICIS_TEST_PG");
    private readonly string _nome = $"vorticis_teste_{Guid.NewGuid():N}";

    public VorticisApp App { get; private set; } = null!;
    public string ConnectionString { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        if (ConexaoServidor is null) return;
        await ExecutarNoServidor($"CREATE DATABASE {_nome}");
        ConnectionString = new NpgsqlConnectionStringBuilder(ConexaoServidor) { Database = _nome }.ConnectionString;
        App = await VorticisApp.IniciarAsync(ConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        if (ConexaoServidor is null) return;
        await App.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await ExecutarNoServidor($"DROP DATABASE IF EXISTS {_nome} WITH (FORCE)");
    }

    private readonly SemaphoreSlim _travaAdmin = new(1, 1);
    private long? _adminId;

    /// <summary>Administrador para os testes (criado sob demanda, para não interferir no teste de primeiro acesso).</summary>
    public async Task<long> AdminAsync()
    {
        await _travaAdmin.WaitAsync();
        try
        {
            return _adminId ??= await EscalarAsync<long>(
                "INSERT INTO core.usuario (login, nome, senha_hash, administrador) VALUES ('admin_teste', 'Admin de Teste', @h, TRUE) RETURNING id",
                ("h", Domain.Seguranca.Senha.GerarHash("Senha1234", iteracoes: 1000)));
        }
        finally
        {
            _travaAdmin.Release();
        }
    }

    /// <summary>Id de um perfil pelo nome (ex.: "Consulta", "Administrador").</summary>
    public Task<long> PerfilIdAsync(string nome) => EscalarAsync<long>("SELECT id FROM core.perfil WHERE nome = @n", ("n", nome));

    public static void ExigirBanco() => Assert.SkipWhen(ConexaoServidor is null, "Defina VORTICIS_TEST_PG para rodar testes de integração.");

    public async Task<long> CriarEmpresaAsync(string cnpj, int ano = 2026)
    {
        var r = await App.Empresas.CadastrarAsync(new NovaEmpresa(cnpj, "Empresa de Teste LTDA", null, "2062", "4751201",
            "SP", "3550308", new DateOnly(2020, 1, 1), RegimeTributario.LucroPresumido, ano), TestContext.Current.CancellationToken);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        return r.Valor;
    }

    public async Task<T> EscalarAsync<T>(string sql, params (string, object)[] parametros)
    {
        await using var con = new NpgsqlConnection(ConnectionString);
        await con.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, con);
        foreach (var (n, v) in parametros) cmd.Parameters.AddWithValue(n, v);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task ExecutarNoServidor(string sql)
    {
        await using var con = new NpgsqlConnection(ConexaoServidor);
        await con.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, con);
        await cmd.ExecuteNonQueryAsync();
    }
}

public static class ProcuradoresDeTeste
{
    /// <summary>Cadastra o contador (sem CRC) vinculado à empresa e marcado como procurador; devolve o id do vínculo.</summary>
    public static async Task<long> CadastrarProcuradorAsync(this VorticisApp app, long empresa, long usuario, string nome, string cpf, DateOnly inicio, DateOnly? fim,
                                                     string? email = null, string? telefone = null, string[]? abrangencia = null, string? observacao = null)
    {
        var r = await app.Responsaveis.VincularContadorAsync(empresa,
            new Vorticis.Application.Cadastros.Contador(0, nome, cpf, "", "", email, telefone, null, false), inicio, false, usuario, default,
            new Vorticis.Application.Cadastros.Procuracao(true, inicio, fim, abrangencia ?? ["ECD", "ECF", "ESOCIAL"], observacao));
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        return r.Valor;
    }
}
