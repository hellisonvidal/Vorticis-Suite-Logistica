using System.Net.Sockets;
using System.Text;
using Npgsql;
using Vorticis.Infra;
using Vorticis.Infra.Sistema;

namespace Vorticis.Desktop.Comum;

/// <param name="RecriarBancoExistente">Existe banco local com senha desconhecida: move a pasta antiga para um backup e cria outro.</param>
/// <param name="Usuario">Sem PostgreSQL embutido: usuário do PostgreSQL detectado em localhost:5432.</param>
/// <param name="Senha">Senha do usuário acima.</param>
public sealed record OpcoesConfiguracaoBanco(bool RecriarBancoExistente = false, string? Usuario = null, string? Senha = null);

/// <param name="PodeRecriar">A causa é um banco local existente sem senha conhecida: recriar (com backup da pasta) resolve.</param>
/// <param name="PrecisaCredenciais">Há um PostgreSQL local em localhost:5432 e só faltam usuário e senha.</param>
public sealed record ResultadoConfiguracaoBanco(bool Sucesso, string Resumo, string? ConnectionString = null,
                                                bool PodeRecriar = false, bool PrecisaCredenciais = false);

/// <summary>
/// Configuração automática do acesso ao banco, passo a passo e com log: localiza os binários do PostgreSQL embutido,
/// confere o runtime do Visual C++, limpa <c>postmaster.pid</c> órfão, cria o cluster (ou oferece recriar), escolhe a
/// porta, inicia, cria o banco, aplica as migrações, grava a configuração e testa a conexão. Sem PostgreSQL embutido,
/// tenta um PostgreSQL local em localhost:5432. Todas as dependências externas são substituíveis (testes).
/// </summary>
public sealed class ConfiguradorBanco
{
    public const int PortaServidorPadrao = 5432;

    public Func<string?> LocalizarBinarios { get; init; } = ServidorPostgresEmbutido.LocalizarBinarios;
    public Func<string, IPostgresLocal> CriarServidor { get; init; } = bin => BancoDoAplicativo.ObterServidor(bin);
    public Func<string, IReadOnlyList<string>> RuntimeAusente { get; init; } = bin => RuntimeVisualCpp.Ausentes(bin);
    public Func<int, bool> PortaLivre { get; init; } = ServidorPostgresEmbutido.PortaLivre;
    public Func<ConfiguracaoBanco> CarregarConfiguracao { get; init; } = Configuracao.Carregar;
    public Action<int?, string?> SalvarLocal { get; init; } = (porta, senha) => Configuracao.SalvarLocal(porta, senha);
    public Action<string> SalvarServidor { get; init; } = Configuracao.Salvar;
    public Func<string, CancellationToken, Task> CriarBanco { get; init; } = BancoLocal.GarantirBancoAsync;
    public Func<string, CancellationToken, Task> AplicarMigracoes { get; init; } = async (cs, ct) =>
    {
        await using var app = await VorticisApp.IniciarAsync(cs, ct: ct);
    };
    /// <summary>Abre uma conexão e devolve a versão do PostgreSQL.</summary>
    public Func<string, CancellationToken, Task<string>> TestarConexao { get; init; } = async (cs, ct) =>
    {
        await using var con = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(cs) { Timeout = 10, Pooling = false }.ConnectionString);
        await con.OpenAsync(ct);
        return con.PostgreSqlVersion.ToString();
    };
    /// <summary>Há algo escutando em 127.0.0.1:5432?</summary>
    public Func<CancellationToken, Task<bool>> ServidorPadraoAtivo { get; init; } = DetectarServidorPadraoAsync;
    public string ArquivoTrava { get; init; } = Configuracao.ArquivoTrava;
    public TimeSpan EsperaTrava { get; init; } = TimeSpan.FromMinutes(4);
    /// <summary>Arquivo de log (null = não grava).</summary>
    public string? ArquivoLog { get; init; } = Configuracao.LogConfiguracaoBanco;
    public Func<DateTime> Agora { get; init; } = () => DateTime.Now;

    private readonly StringBuilder _log = new();
    private readonly Action<string>? _aoRegistrar;

    public ConfiguradorBanco(Action<string>? aoRegistrar = null) => _aoRegistrar = aoRegistrar;

    /// <summary>Log completo desta execução (para "Copiar diagnóstico").</summary>
    public string Log => _log.ToString();

    private void Registrar(string mensagem)
    {
        var linha = $"{Agora():HH:mm:ss} {mensagem}";
        _log.AppendLine(linha);
        _aoRegistrar?.Invoke(linha);
        if (ArquivoLog is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ArquivoLog))!);
            File.AppendAllText(ArquivoLog, linha + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // o log em arquivo é um auxílio; a tela continua mostrando
        }
    }

    public async Task<ResultadoConfiguracaoBanco> ExecutarAsync(OpcoesConfiguracaoBanco? opcoes = null, CancellationToken ct = default)
    {
        opcoes ??= new();
        AparaArquivoLog();
        Registrar("==== Configuração automática do banco de dados ====");
        Registrar($"Sistema: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}; usuário: {Environment.UserName}");
        try
        {
            var r = await ExecutarPassosAsync(opcoes, ct);
            Registrar(r.Sucesso ? $"CONCLUÍDO: {r.Resumo}" : $"FALHA: {r.Resumo}");
            return r;
        }
        catch (OperationCanceledException)
        {
            Registrar("FALHA: operação cancelada.");
            return new(false, "Configuração cancelada.");
        }
        catch (Exception ex)
        {
            Registrar(ex.ToString());
            var (msg, recriar) = Traduzir(ex);
            Registrar($"FALHA: {msg}");
            return new(false, msg, PodeRecriar: recriar);
        }
    }

    private void AparaArquivoLog()
    {
        try
        {
            if (ArquivoLog is not null && File.Exists(ArquivoLog) && new FileInfo(ArquivoLog).Length > 512 * 1024) File.Delete(ArquivoLog);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // segue anexando
        }
    }

    private async Task<ResultadoConfiguracaoBanco> ExecutarPassosAsync(OpcoesConfiguracaoBanco opcoes, CancellationToken ct)
    {
        Registrar("[1/9] Localizando os binários do PostgreSQL embutido...");
        var bin = LocalizarBinarios();
        if (bin is null)
        {
            Registrar("Binários não encontrados (pasta pgsql\\bin ao lado do programa).");
            return await ServidorExistenteAsync(opcoes, ct);
        }
        Registrar($"Encontrado: {bin}");

        Registrar("[2/9] Verificando o runtime do Microsoft Visual C++ (2015-2022 x64)...");
        var ausentes = RuntimeAusente(bin);
        if (ausentes.Count > 0)
            return new(false, $"Faltam DLLs do Microsoft Visual C++ ({string.Join(", ", ausentes)}). Instale o Visual C++ Redistributable " +
                              $"2015-2022 (x64) — {RuntimeVisualCpp.UrlDownload} — e tente novamente.");
        Registrar("Runtime do Visual C++ presente.");

        var servidor = CriarServidor(bin);
        Registrar("Aguardando a trava entre módulos (Contábil, Fiscal e Folha não preparam o banco ao mesmo tempo)...");
        using var trava = await TravaEntreProcessos.AdquirirAsync(ArquivoTrava, EsperaTrava,
            () => Registrar("Outro módulo do Vorticis está preparando o banco; aguardando..."), ct);

        Registrar("[3/9] Procurando postmaster.pid órfão (sobra de queda do programa ou do Windows)...");
        Registrar(servidor.LimparPidOrfao() ? "postmaster.pid órfão removido." : "Nenhum postmaster.pid órfão.");

        Registrar("[4/9] Verificando o cluster do banco local...");
        var cfg = CarregarConfiguracao();
        string senha;
        if (servidor.Inicializado && !opcoes.RecriarBancoExistente && cfg.Senha is not null)
        {
            senha = cfg.Senha;
            Registrar($"Banco local existente em {servidor.PastaDados}; senha conhecida.");
        }
        else
        {
            if (servidor.Inicializado && !opcoes.RecriarBancoExistente)
            {
                Registrar($"Existe um banco local em {servidor.PastaDados}, mas a senha dele não está na configuração " +
                          "(arquivo apagado, ou gravado por outro usuário do Windows).");
                return new(false, "Existe um banco local cuja senha não é conhecida. Recrie o banco: a pasta atual é preservada " +
                                  "como backup (renomeada) e um banco novo é criado.", PodeRecriar: true);
            }
            if (Directory.Exists(servidor.PastaDados) && Directory.EnumerateFileSystemEntries(servidor.PastaDados).Any())
            {
                Registrar(servidor.Inicializado ? "Recriação pedida: parando o servidor e preservando a pasta antiga..."
                                                : "Pasta de dados incompleta (criação interrompida): preservando-a como backup...");
                if (servidor.Inicializado) servidor.Parar();
                NpgsqlConnection.ClearAllPools();
                var backup = $"{servidor.PastaDados.TrimEnd('\\', '/')}.backup-{Agora():yyyyMMdd-HHmmss}";
                Directory.Move(servidor.PastaDados, backup);
                Registrar($"Pasta antiga movida para {backup}");
            }
            Registrar("Criando o cluster (initdb; pode levar até um minuto)...");
            senha = ServidorPostgresEmbutido.GerarSenha();
            SalvarLocal(null, senha);   // a senha é gravada antes: nunca existe cluster sem senha conhecida
            await servidor.InicializarAsync(senha, ct);
            Registrar("Cluster criado.");
        }

        Registrar("[5/9] Escolhendo a porta...");
        var preferida = cfg.Porta > 0 ? cfg.Porta : ServidorPostgresEmbutido.PortaPreferida;
        Registrar(PortaLivre(preferida) ? $"Porta {preferida} livre."
                                        : $"Porta {preferida} ocupada ou reservada pelo Windows; será usada outra porta livre.");

        Registrar("[6/9] Iniciando o servidor...");
        var porta = await servidor.IniciarAsync(preferida, ct);
        Registrar($"Servidor em execução na porta {porta}.");
        var cs = BancoDoAplicativo.StringConexao(porta, senha);

        Registrar($"[7/9] Criando o banco \"{Configuracao.NomeBanco}\" (se não existir) e aplicando as migrações...");
        await CriarBanco(cs, ct);
        await AplicarMigracoes(cs, ct);
        Registrar("Banco e migrações em dia.");

        Registrar("[8/9] Gravando a configuração...");
        SalvarLocal(porta, null);
        Registrar("Configuração gravada (modo: banco local; senha protegida).");

        Registrar("[9/9] Testando a conexão...");
        var versao = await TestarConexao(cs, ct);
        return new(true, $"banco local pronto na porta {porta} (PostgreSQL {versao}).", cs);
    }

    /// <summary>Sem PostgreSQL embutido: usa um PostgreSQL já instalado em localhost:5432, pedindo só usuário e senha.</summary>
    private async Task<ResultadoConfiguracaoBanco> ServidorExistenteAsync(OpcoesConfiguracaoBanco opcoes, CancellationToken ct)
    {
        Registrar($"Procurando um PostgreSQL em localhost:{PortaServidorPadrao}...");
        if (!await ServidorPadraoAtivo(ct))
            return new(false, "Esta cópia do Vorticis não inclui o banco de dados embutido e não há PostgreSQL em localhost:5432. " +
                              "Instale pelo instalador completo (Setup-Vorticis.exe) ou informe um servidor PostgreSQL abaixo.");
        Registrar("PostgreSQL encontrado.");
        if (string.IsNullOrWhiteSpace(opcoes.Usuario))
            return new(false, "Foi encontrado um PostgreSQL em localhost:5432. Informe o usuário e a senha dele e clique em Configurar automaticamente.",
                       PrecisaCredenciais: true);

        var cs = new NpgsqlConnectionStringBuilder
        {
            Host = "localhost", Port = PortaServidorPadrao, Database = Configuracao.NomeBanco,
            Username = opcoes.Usuario.Trim(), Password = opcoes.Senha ?? "",
        }.ConnectionString;
        Registrar($"Conectando como \"{opcoes.Usuario.Trim()}\"...");
        try
        {
            await CriarBanco(cs, ct);
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.InvalidPassword or PostgresErrorCodes.InvalidAuthorizationSpecification)
        {
            Registrar(ex.Message);
            return new(false, "Usuário ou senha do PostgreSQL incorretos.", PrecisaCredenciais: true);
        }
        Registrar("Aplicando as migrações...");
        await AplicarMigracoes(cs, ct);
        Registrar("Gravando a configuração (modo: servidor)...");
        SalvarServidor(cs);
        Registrar("Testando a conexão...");
        var versao = await TestarConexao(cs, ct);
        return new(true, $"conectado ao PostgreSQL {versao} em localhost:{PortaServidorPadrao}.", cs);
    }

    private static (string Mensagem, bool Recriar) Traduzir(Exception ex) => ex switch
    {
        PostgresException { SqlState: PostgresErrorCodes.InvalidPassword or PostgresErrorCodes.InvalidAuthorizationSpecification } =>
            ("O banco local recusou a senha gravada (ela não corresponde ao banco existente). Recrie o banco: a pasta atual é preservada como backup.", true),
        NpgsqlException e => ($"Falha de comunicação com o PostgreSQL: {e.Message}", false),
        TimeoutException e => (e.Message, false),
        IOException or UnauthorizedAccessException =>
            ($"Sem permissão ou arquivo em uso: {ex.Message}. Feche os outros módulos do Vorticis e tente de novo.", false),
        _ => (ex.Message, false),
    };

    private static async Task<bool> DetectarServidorPadraoAsync(CancellationToken ct)
    {
        try
        {
            using var tcp = new TcpClient();
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(2));
            await tcp.ConnectAsync("127.0.0.1", PortaServidorPadrao, limite.Token);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            ct.ThrowIfCancellationRequested();
            return false;
        }
    }
}

/// <summary>Linha de comando <c>--configurar-banco</c>: executa a configuração automática sem interface (usada pelo instalador).</summary>
public static class ConfiguracaoAutomatica
{
    public const string Argumento = "--configurar-banco";

    public static bool Solicitada(IEnumerable<string> args) => args.Any(a => string.Equals(a, Argumento, StringComparison.OrdinalIgnoreCase));

    /// <summary>Opções adicionais: <c>--recriar</c>; <c>--usuario=X</c> e <c>--senha=Y</c> (servidor PostgreSQL local, sem banco embutido).</summary>
    public static OpcoesConfiguracaoBanco Opcoes(IEnumerable<string> args)
    {
        string? Valor(string nome) => args.FirstOrDefault(a => a.StartsWith(nome + "=", StringComparison.OrdinalIgnoreCase))?[(nome.Length + 1)..];
        return new(args.Any(a => string.Equals(a, "--recriar", StringComparison.OrdinalIgnoreCase)), Valor("--usuario"), Valor("--senha"));
    }

    /// <summary>Código de saída: 0 = banco pronto; 1 = falha (detalhes no log).</summary>
    public static async Task<int> ExecutarAsync(IEnumerable<string> args, ConfiguradorBanco? configurador = null)
    {
        var lista = args.ToList();
        configurador ??= new ConfiguradorBanco(linha => { try { Console.WriteLine(linha); } catch (IOException) { } });
        var r = await configurador.ExecutarAsync(Opcoes(lista));
        return r.Sucesso ? 0 : 1;
    }
}
