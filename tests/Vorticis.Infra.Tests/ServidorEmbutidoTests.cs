using System.Net;
using System.Net.Sockets;
using Npgsql;
using Vorticis.Infra.Sistema;

namespace Vorticis.Infra.Tests;

/// <summary>
/// Ciclo de vida do PostgreSQL embutido (initdb, start, reuso, porta ocupada, stop) e migrações sobre ele.
/// Usa VORTICIS_PG_EMBUTIDO_BIN ou, no Linux, /usr/lib/postgresql/*/bin. O PostgreSQL não roda como root no Linux.
/// </summary>
public sealed class ServidorEmbutidoTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), $"vorticis-embutido-{Guid.NewGuid():N}");
    private ServidorPostgresEmbutido? _servidor;

    private static string? Binarios()
    {
        if (ServidorPostgresEmbutido.LocalizarBinarios() is { } b) return b;
        if (!OperatingSystem.IsLinux() || !Directory.Exists("/usr/lib/postgresql")) return null;
        return Directory.GetDirectories("/usr/lib/postgresql").OrderByDescending(d => d)
            .Select(d => Path.Combine(d, "bin")).FirstOrDefault(d => File.Exists(Path.Combine(d, "initdb")));
    }

    [Fact]
    public async Task Cria_inicia_reaproveita_e_para_o_servidor_local()
    {
        var bin = Binarios();
        Assert.SkipWhen(bin is null, "Binários do PostgreSQL não encontrados (defina VORTICIS_PG_EMBUTIDO_BIN).");
        Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.UserName == "root", "O PostgreSQL não roda como root.");
        var ct = TestContext.Current.CancellationToken;

        _servidor = new ServidorPostgresEmbutido(bin!, Path.Combine(_pasta, "dados"), Path.Combine(_pasta, "logs", "pg.log"));
        var senha = ServidorPostgresEmbutido.GerarSenha();
        await _servidor.InicializarAsync(senha, ct);
        Assert.True(_servidor.Inicializado);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _servidor.InicializarAsync(senha, ct));

        // porta preferida ocupada por outro programa: escolhe outra
        var ocupante = new TcpListener(IPAddress.Loopback, 0);
        ocupante.Start();
        var ocupada = ((IPEndPoint)ocupante.LocalEndpoint).Port;
        int porta;
        try
        {
            porta = await _servidor.IniciarAsync(ocupada, ct);
        }
        finally
        {
            ocupante.Stop();
        }
        Assert.NotEqual(ocupada, porta);
        Assert.True(_servidor.IniciadoPorEsteProcesso);

        var cs = new NpgsqlConnectionStringBuilder
        {
            Host = "localhost", Port = porta, Database = "vorticis", Username = ServidorPostgresEmbutido.Usuario, Password = senha, Pooling = false,
        }.ConnectionString;
        await BancoLocal.GarantirBancoAsync(cs, ct);
        await using (var app = await VorticisApp.IniciarAsync(cs, ct: ct))
            Assert.True(await app.Usuarios.ImplantacaoAtivaAsync(ct));

        // senha errada é recusada (SCRAM, não "trust")
        await using (var errada = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(cs) { Password = "errada" }.ConnectionString))
            await Assert.ThrowsAsync<PostgresException>(() => errada.OpenAsync(ct));

        // segunda instância do programa: reaproveita o servidor em execução e não o para ao sair
        var outra = new ServidorPostgresEmbutido(bin!, _servidor.PastaDados, _servidor.ArquivoLog);
        Assert.Equal(porta, await outra.IniciarAsync(ct: ct));
        Assert.False(outra.IniciadoPorEsteProcesso);

        _servidor.Parar();
        Assert.Null(await _servidor.PortaEmExecucaoAsync(ct));

        // reinício preserva os dados
        porta = await _servidor.IniciarAsync(porta, ct);
        await using var con = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(cs) { Port = porta }.ConnectionString);
        await con.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM vorticis_migracao", con);
        Assert.True((long)(await cmd.ExecuteScalarAsync(ct))! >= 7);
    }

    public void Dispose()
    {
        NpgsqlConnection.ClearAllPools();
        _servidor?.Parar();
        try
        {
            if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
        }
        catch (IOException)
        {
            // Windows pode segurar arquivos por alguns instantes; a pasta é temporária
        }
    }
}
