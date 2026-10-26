using Npgsql;
using Vorticis.Application.Cadastros;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra;

namespace Vorticis.Folha.Tests;

/// <summary>
/// Banco PostgreSQL descartável por classe de teste (como em Vorticis.Infra.Tests). Requer VORTICIS_TEST_PG com permissão
/// CREATEDB; sem ela, os testes de integração são pulados.
/// </summary>
public sealed class BancoFolha : IAsyncLifetime
{
    public static readonly string? ConexaoServidor = Environment.GetEnvironmentVariable("VORTICIS_TEST_PG");
    private readonly string _nome = $"vorticis_folha_{Guid.NewGuid():N}";

    public VorticisApp App { get; private set; } = null!;
    public ServicosFolha Folha { get; private set; } = null!;
    public string ConnectionString { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        if (ConexaoServidor is null) return;
        await Servidor($"CREATE DATABASE {_nome}");
        ConnectionString = new NpgsqlConnectionStringBuilder(ConexaoServidor) { Database = _nome }.ConnectionString;
        App = await VorticisApp.IniciarAsync(ConnectionString);
        Folha = ServicosFolha.De(App);
    }

    public async ValueTask DisposeAsync()
    {
        if (ConexaoServidor is null) return;
        await App.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await Servidor($"DROP DATABASE IF EXISTS {_nome} WITH (FORCE)");
    }

    public static void ExigirBanco() => Assert.SkipWhen(ConexaoServidor is null, "Defina VORTICIS_TEST_PG para rodar testes de integração.");

    private long? _admin;

    public async Task<long> AdminAsync()
        => _admin ??= await Escalar<long>("INSERT INTO core.usuario (login, nome, senha_hash, administrador) VALUES ('admin_folha', 'Admin', @h, TRUE) RETURNING id",
            ("h", Domain.Seguranca.Senha.GerarHash("Senha1234", iteracoes: 1000)));

    public static readonly SessaoUsuario SessaoAdmin = new(0, "teste", "Teste", true, false);

    public async Task<long> CriarEmpresaAsync(string cnpj, RegimeTributario regime = RegimeTributario.LucroPresumido, int ano = 2026)
    {
        var r = await App.Empresas.CadastrarAsync(new NovaEmpresa(cnpj, "Empresa de Teste LTDA", null, "2062", "4751201", "SP", "3550308",
            new DateOnly(2020, 1, 1), regime, ano), TestContext.Current.CancellationToken);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        return r.Valor;
    }

    public async Task<T> Escalar<T>(string sql, params (string, object)[] parametros)
    {
        await using var con = new NpgsqlConnection(ConnectionString);
        await con.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, con);
        foreach (var (n, v) in parametros) cmd.Parameters.AddWithValue(n, v);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task Servidor(string sql)
    {
        await using var con = new NpgsqlConnection(ConexaoServidor);
        await con.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, con);
        await cmd.ExecuteNonQueryAsync();
    }
}
