using Npgsql;
using Vorticis.Infra.Sistema;

namespace Vorticis.Desktop.Comum;

/// <summary>Resolve a conexão a usar ao abrir o programa e, no modo local, cria/inicia/para o PostgreSQL embutido.</summary>
public static class BancoDoAplicativo
{
    private static ServidorPostgresEmbutido? _servidor;
    private static int? _portaLocal;
    private static FileStream? _presenca;

    /// <summary>Connection string pronta para uso (no modo local, com o servidor já em execução).</summary>
    public static async Task<string> PrepararAsync(Action<string> status, CancellationToken ct = default)
    {
        var cfg = Configuracao.Carregar();
        return cfg.Modo == ModoBanco.Servidor ? cfg.ConnectionString! : await PrepararLocalAsync(cfg, status, ct);
    }

    private static async Task<string> PrepararLocalAsync(ConfiguracaoBanco cfg, Action<string> status, CancellationToken ct)
    {
        var bin = ServidorPostgresEmbutido.LocalizarBinarios()
                  ?? throw new InvalidOperationException(
                      "Esta cópia do Vorticis não contém o banco de dados local (pasta pgsql ao lado do programa). " +
                      "Instale pelo instalador Setup-Vorticis.exe ou use \"Configurar automaticamente\" / informe um servidor PostgreSQL.");
        var servidor = ObterServidor(bin);

        // Contábil, Fiscal e Folha podem abrir juntos: só um por vez cria/inicia o cluster. A configuração é relida
        // depois da trava, porque o outro módulo pode ter acabado de gravar a senha e a porta.
        using var trava = await TravaEntreProcessos.AdquirirAsync(Configuracao.ArquivoTrava, TimeSpan.FromMinutes(4),
            () => status("Aguardando outro módulo do Vorticis preparar o banco de dados..."), ct);
        cfg = Configuracao.Carregar();
        servidor.LimparPidOrfao();

        var senha = cfg.Senha;
        if (!servidor.Inicializado)
        {
            status("Primeira execução: criando o banco de dados local (pode levar até um minuto)...");
            senha ??= ServidorPostgresEmbutido.GerarSenha();
            Configuracao.SalvarLocal(senha: senha);
            await servidor.InicializarAsync(senha, ct);
        }
        else if (senha is null)
        {
            throw new InvalidOperationException(
                $"O banco local existe em {Configuracao.PastaBancoLocal}, mas a senha dele não está em {Configuracao.ArquivoConfiguracao} " +
                "(arquivo apagado ou copiado de outro usuário do Windows). Use \"Configurar automaticamente\" para recriar o banco " +
                "(a pasta atual é preservada como backup) ou restaure o arquivo de configuração.");
        }

        status("Iniciando o banco de dados local...");
        var porta = await servidor.IniciarAsync(cfg.Porta > 0 ? cfg.Porta : ServidorPostgresEmbutido.PortaPreferida, ct);
        if (porta != cfg.Porta) Configuracao.SalvarLocal(porta);
        _portaLocal = porta;
        RegistrarPresenca();   // ainda dentro da trava: o outro módulo só prossegue (e só fecha) sabendo que este existe

        return StringConexao(porta, senha!);
    }

    /// <summary>Connection string do banco local (127.0.0.1: o servidor escuta só em IPv4, sem depender da resolução de "localhost").</summary>
    public static string StringConexao(int porta, string senha) => new NpgsqlConnectionStringBuilder
    {
        Host = "127.0.0.1", Port = porta, Database = Configuracao.NomeBanco,
        Username = ServidorPostgresEmbutido.Usuario, Password = senha,
    }.ConnectionString;

    /// <summary>O servidor local deste processo (criado uma vez; a configuração automática e a abertura do programa usam o mesmo).</summary>
    public static ServidorPostgresEmbutido ObterServidor(string bin) =>
        _servidor ??= new ServidorPostgresEmbutido(bin, Configuracao.PastaBancoLocal, Configuracao.LogBancoLocal);

    public static bool EhBancoLocal(string connectionString)
    {
        var b = new NpgsqlConnectionStringBuilder(connectionString);
        return _portaLocal == b.Port && b.Host is "localhost" or "127.0.0.1";
    }

    public static int? PortaLocal => _portaLocal;

    /// <summary>
    /// Troca a senha do superusuário do banco local (ALTER ROLE) e grava a nova senha na configuração.
    /// A conexão informada precisa ser a do banco local, ainda com a senha antiga.
    /// </summary>
    public static async Task AlterarSenhaLocalAsync(string connectionString, string novaSenha, CancellationToken ct = default)
    {
        if (!EhBancoLocal(connectionString)) throw new InvalidOperationException("O programa não está usando o banco local.");
        await using (var con = new NpgsqlConnection(connectionString))
        {
            await con.OpenAsync(ct);
            // ALTER ROLE não aceita parâmetro: literal com aspas duplicadas.
            await using var cmd = new NpgsqlCommand($"ALTER ROLE {ServidorPostgresEmbutido.Usuario} PASSWORD '{novaSenha.Replace("'", "''")}'", con);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        Configuracao.SalvarLocal(senha: novaSenha);
        NpgsqlConnection.ClearAllPools();
    }

    /// <summary>Esquece o servidor em cache (testes que trocam as pastas do perfil entre execuções).</summary>
    internal static void Reiniciar()
    {
        Encerrar();
        _servidor = null;
        _portaLocal = null;
    }

    /// <summary>Para o banco local (para reiniciar em outra porta); a próxima preparação o inicia de novo.</summary>
    public static void PararLocal()
    {
        if (_servidor is { IniciadoPorEsteProcesso: true })
        {
            NpgsqlConnection.ClearAllPools();
            _servidor.Parar();
        }
        _portaLocal = null;
    }

    /// <summary>
    /// Ao sair do programa: para o servidor local se nenhum outro módulo da suíte (Contábil, Fiscal, Folha) o estiver
    /// usando. Os módulos compartilham o mesmo banco: quem fecha por último é quem o para, tenha sido ele ou não quem o iniciou.
    /// </summary>
    public static void Encerrar()
    {
        if (_servidor is null) return;
        NpgsqlConnection.ClearAllPools();
        var outroAberto = OutroModuloAberto();
        LiberarPresenca();
        if (!outroAberto && (_portaLocal is not null || _servidor.IniciadoPorEsteProcesso)) _servidor.Parar();
    }

    /// <summary>Marca este processo como em execução: arquivo mantido aberto e travado até o processo terminar.</summary>
    private static void RegistrarPresenca()
    {
        if (_presenca is not null) return;
        try
        {
            Directory.CreateDirectory(Configuracao.PastaExecucao);
            _presenca = new FileStream(Path.Combine(Configuracao.PastaExecucao, $"{Environment.ProcessId}.lock"),
                FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // sem a marca, os outros módulos podem parar o banco ao fechar; nada mais a fazer
        }
    }

    private static void LiberarPresenca()
    {
        _presenca?.Dispose();
        _presenca = null;
    }

    /// <summary>Há outro processo do Vorticis com marca travada? Marca que se consegue abrir pertence a processo que já terminou.</summary>
    private static bool OutroModuloAberto()
    {
        try
        {
            if (!Directory.Exists(Configuracao.PastaExecucao)) return false;
            var minha = Path.Combine(Configuracao.PastaExecucao, $"{Environment.ProcessId}.lock");
            foreach (var arquivo in Directory.EnumerateFiles(Configuracao.PastaExecucao, "*.lock").Where(a => a != minha))
            {
                try
                {
                    using var _ = new FileStream(arquivo, FileMode.Open, FileAccess.ReadWrite, FileShare.None);   // abriu: órfã
                }
                catch (IOException)
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // não foi possível verificar: considera que não há outros
        }
        return false;
    }
}
