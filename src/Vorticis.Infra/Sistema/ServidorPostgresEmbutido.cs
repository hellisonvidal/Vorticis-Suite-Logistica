using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Vorticis.Infra.Sistema;

/// <summary>
/// PostgreSQL privado do Vorticis: binários distribuídos junto com o programa (pasta <c>pgsql</c>), dados no perfil do
/// usuário e servidor iniciado/parado pelo próprio aplicativo. Dispensa instalar e configurar o PostgreSQL.
/// <list type="bullet">
/// <item>Escuta só em <c>localhost</c>, com autenticação SCRAM e senha aleatória gerada na criação do cluster.</item>
/// <item>Não é um serviço do Windows: roda enquanto o Vorticis está aberto (com o usuário do Windows, sem privilégios).</item>
/// <item>A porta é escolhida na inicialização (a preferida, se livre), então não conflita com outro PostgreSQL da máquina.</item>
/// </list>
/// </summary>
public sealed partial class ServidorPostgresEmbutido(string pastaBinarios, string pastaDados, string arquivoLog) : IPostgresLocal
{
    /// <summary>Código de saída do Windows quando uma DLL não é encontrada (STATUS_DLL_NOT_FOUND, 0xC0000135).</summary>
    public const int CodigoDllNaoEncontrada = unchecked((int)0xC0000135);

    public const string Usuario = "postgres";
    public const int PortaPreferida = 54329;

    public string PastaBinarios { get; } = pastaBinarios;
    public string PastaDados { get; } = pastaDados;
    public string ArquivoLog { get; } = arquivoLog;

    /// <summary>True se este objeto iniciou o servidor (e, portanto, deve pará-lo ao fechar o programa).</summary>
    public bool IniciadoPorEsteProcesso { get; private set; }

    public bool Inicializado => File.Exists(Path.Combine(PastaDados, "PG_VERSION"));

    /// <summary>
    /// Pasta <c>bin</c> dos binários distribuídos com o programa (<c>&lt;pasta do executável&gt;\pgsql\bin</c>),
    /// ou a indicada em VORTICIS_PG_EMBUTIDO_BIN. Null se o programa foi distribuído sem o PostgreSQL.
    /// </summary>
    public static string? LocalizarBinarios()
    {
        var candidatos = new List<string>();
        if (Environment.GetEnvironmentVariable("VORTICIS_PG_EMBUTIDO_BIN") is { Length: > 0 } env) candidatos.Add(env);
        // Executável de arquivo único: AppContext.BaseDirectory pode apontar para a pasta de extração em vez da do .exe;
        // por isso a pasta do próprio processo vem primeiro.
        if (Path.GetDirectoryName(Environment.ProcessPath) is { Length: > 0 } pastaExe) candidatos.Add(Path.Combine(pastaExe, "pgsql", "bin"));
        candidatos.Add(Path.Combine(AppContext.BaseDirectory, "pgsql", "bin"));
        return candidatos.FirstOrDefault(d => File.Exists(Path.Combine(d, Exe("pg_ctl"))) && File.Exists(Path.Combine(d, Exe("initdb"))));
    }

    public static string GerarSenha() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    /// <summary>Cria o cluster (initdb). Só pode ser chamado uma vez; a senha do superusuário deve ser guardada pelo chamador.</summary>
    public async Task InicializarAsync(string senha, CancellationToken ct = default)
    {
        if (Inicializado) throw new InvalidOperationException($"O banco local já existe em {PastaDados}.");
        if (Directory.Exists(PastaDados) && Directory.EnumerateFileSystemEntries(PastaDados).Any())
            throw new InvalidOperationException($"A pasta {PastaDados} não está vazia e não contém um banco do PostgreSQL.");

        // A pasta (vazia) precisa existir para obter o caminho curto no Windows (perfil com acentos).
        Directory.CreateDirectory(PastaDados);
        var arquivoSenha = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(PastaDados))!, $"vorticis-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(arquivoSenha, senha, ct);
        try
        {
            string[] basicos = ["-D", Dados, "-U", Usuario, "-A", "scram-sha-256", $"--pwfile={CaminhoSeguro(arquivoSenha)}", "-E", "UTF8", "--locale=C"];
            // Ordenação em português (acentos) via ICU; se a distribuição não tiver ICU, usa a ordenação binária.
            var (codigo, saida) = await ExecutarAsync("initdb", [.. basicos, "--locale-provider=icu", "--icu-locale=pt-BR"], ct);
            if (codigo != 0 && codigo != CodigoDllNaoEncontrada)
            {
                LimparPastaDados();
                var (codigo2, saida2) = await ExecutarAsync("initdb", basicos, ct);
                saida = $"{saida.Trim()}{Environment.NewLine}{saida2}";
                codigo = codigo2;
            }
            if (codigo != 0)
            {
                LimparPastaDados();
                throw new InvalidOperationException(codigo == CodigoDllNaoEncontrada
                    ? "O Windows não encontrou uma DLL necessária ao PostgreSQL (initdb, código 0xC0000135). Instale o Microsoft Visual C++ " +
                      "Redistributable 2015-2022 (x64): https://aka.ms/vs/17/release/vc_redist.x64.exe"
                    : $"Não foi possível criar o banco local (initdb, código {codigo}): {saida.Trim()}");
            }
        }
        finally
        {
            File.Delete(arquivoSenha);
        }
    }

    /// <summary>
    /// Remove o <c>postmaster.pid</c> deixado por uma queda (energia, encerramento forçado) quando o processo gravado nele
    /// não existe mais ou, por reaproveitamento do número, é outro programa. Devolve true se o arquivo foi removido.
    /// </summary>
    public bool LimparPidOrfao(Func<int, string?>? nomeDoProcesso = null)
    {
        var arquivo = Path.Combine(PastaDados, "postmaster.pid");
        try
        {
            if (!File.Exists(arquivo) || !PidOrfao(arquivo, nomeDoProcesso ?? NomeDoProcesso)) return false;
            File.Delete(arquivo);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>O arquivo postmaster.pid é de um servidor que não existe mais?</summary>
    public static bool PidOrfao(string arquivoPid, Func<int, string?> nomeDoProcesso)
    {
        string? primeira;
        try
        {
            primeira = File.ReadLines(arquivoPid).FirstOrDefault()?.Trim();
        }
        catch (IOException)
        {
            return false;
        }
        if (!int.TryParse(primeira, out var pid) || pid <= 0) return true;   // vazio ou corrompido
        var nome = nomeDoProcesso(pid);
        return nome is null || !(nome.StartsWith("postgres", StringComparison.OrdinalIgnoreCase)
                                 || nome.StartsWith("postmaster", StringComparison.OrdinalIgnoreCase));
    }

    private static string? NomeDoProcesso(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;   // não existe
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return "postgres";   // existe mas não foi possível consultar: não mexe no arquivo
        }
    }

    /// <summary>Porta do servidor em execução neste cluster, ou null se parado.</summary>
    public async Task<int?> PortaEmExecucaoAsync(CancellationToken ct = default)
    {
        var (codigo, _) = await ExecutarAsync("pg_ctl", ["status", "-D", Dados], ct);
        if (codigo != 0) return null;
        // postmaster.pid: linha 4 = porta
        try
        {
            var linhas = await File.ReadAllLinesAsync(Path.Combine(PastaDados, "postmaster.pid"), ct);
            return linhas.Length > 3 && int.TryParse(linhas[3].Trim(), out var p) ? p : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Inicia o servidor (ou reaproveita o que já está rodando neste cluster) e devolve a porta.</summary>
    public async Task<int> IniciarAsync(int portaPreferida = PortaPreferida, CancellationToken ct = default)
    {
        if (!Inicializado) throw new InvalidOperationException("O banco local ainda não foi criado.");
        LimparPidOrfao();
        if (await PortaEmExecucaoAsync(ct) is { } emExecucao) return emExecucao;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ArquivoLog))!);
        // Só TCP: fora do Windows, sem socket Unix (o diretório padrão das distribuições, /var/run/postgresql,
        // não é gravável pelo usuário comum). Escuta em 127.0.0.1 (IPv4), a mesma verificada em PortaLivre.
        var soTcp = OperatingSystem.IsWindows() ? "" : " -c unix_socket_directories=";
        var porta = PortaLivre(portaPreferida) ? portaPreferida : PortaQualquer();
        for (var tentativa = 1; ; tentativa++)
        {
            var (codigo, saida) = await ExecutarAsync("pg_ctl",
                ["start", "-D", Dados, "-l", CaminhoSeguroArquivo(ArquivoLog), "-w", "-t", "120", "-o", $"-p {porta} -c listen_addresses=127.0.0.1{soTcp}"],
                ct, capturarSaida: false);
            if (codigo == 0) break;
            var log = UltimasLinhasLog();
            // porta tomada entre a verificação e a subida (ou reservada pelo Windows): tenta outra
            if (tentativa < 3 && PortaOcupadaNoLog().IsMatch(log)) { porta = PortaQualquer(); continue; }
            throw new InvalidOperationException(
                $"Não foi possível iniciar o banco local (pg_ctl, código {codigo}). {saida}Detalhes no arquivo {ArquivoLog}:{Environment.NewLine}{log}");
        }
        IniciadoPorEsteProcesso = true;
        return porta;
    }

    [GeneratedRegex("bind|already in use|Only one usage|address|socket|permission denied|permissão negada", RegexOptions.IgnoreCase)]
    private static partial Regex PortaOcupadaNoLog();

    /// <summary>Para o servidor (desligamento rápido: conexões abertas são encerradas, transações pendentes desfeitas).</summary>
    public void Parar()
    {
        if (!Inicializado) return;
        try
        {
            using var p = Process.Start(Configurar("pg_ctl", ["stop", "-D", Dados, "-m", "fast", "-w", "-t", "60"], capturarSaida: false));
            if (p is not null && !p.WaitForExit(TimeSpan.FromSeconds(70))) p.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // servidor já parado ou binários removidos: nada a fazer
        }
        IniciadoPorEsteProcesso = false;
    }

    private string UltimasLinhasLog()
    {
        try
        {
            return string.Join(Environment.NewLine, File.ReadLines(ArquivoLog).TakeLast(8));
        }
        catch (IOException)
        {
            return "(log indisponível)";
        }
    }

    /// <summary>Esvazia a pasta dos dados (initdb que falhou), mantendo a própria pasta.</summary>
    private void LimparPastaDados()
    {
        if (!Directory.Exists(PastaDados)) return;
        foreach (var d in Directory.GetDirectories(PastaDados)) Directory.Delete(d, recursive: true);
        foreach (var f in Directory.GetFiles(PastaDados)) File.Delete(f);
    }

    /// <summary>Pasta dos dados na forma passada ao PostgreSQL (caminho curto 8.3 no Windows se houver acentos).</summary>
    private string Dados => CaminhoSeguro(PastaDados);

    /// <summary>
    /// Ferramentas do PostgreSQL no Windows interpretam argumentos na página de código ANSI; um perfil como
    /// C:\Users\João só é seguro pelo nome curto. Sem acentos, ou fora do Windows, devolve o caminho como está.
    /// </summary>
    public static string CaminhoSeguro(string caminho)
    {
        if (!OperatingSystem.IsWindows() || caminho.All(c => c < 128) || !Path.Exists(caminho)) return caminho;
        try
        {
            var sb = new StringBuilder(520);
            var n = GetShortPathName(caminho, sb, (uint)sb.Capacity);
            return n > 0 && n < sb.Capacity ? sb.ToString() : caminho;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return caminho;
        }
    }

    private static string CaminhoSeguroArquivo(string arquivo) =>
        Path.Combine(CaminhoSeguro(Path.GetDirectoryName(Path.GetFullPath(arquivo))!), Path.GetFileName(arquivo));

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetShortPathNameW")]
    private static extern uint GetShortPathName(string caminhoLongo, StringBuilder caminhoCurto, uint tamanho);

    /// <summary>A porta TCP em 127.0.0.1 está livre para o servidor?</summary>
    public static bool PortaLivre(int porta)
    {
        try
        {
            var l = new TcpListener(IPAddress.Loopback, porta);
            l.Start();
            l.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static int PortaQualquer()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var porta = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return porta;
    }

    private static string Exe(string nome) => OperatingSystem.IsWindows() ? nome + ".exe" : nome;

    private static bool _errosSemCaixa;

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint modo);

    /// <summary>
    /// Os processos filhos herdam o modo de erro: com SEM_FAILCRITICALERRORS, uma DLL ausente encerra o initdb/pg_ctl com
    /// o código 0xC0000135 em vez de abrir uma caixa de diálogo que deixaria o programa esperando para sempre.
    /// </summary>
    private static void EvitarCaixasDeErro()
    {
        if (_errosSemCaixa || !OperatingSystem.IsWindows()) return;
        _errosSemCaixa = true;
        try { SetErrorMode(0x0001); } catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    private ProcessStartInfo Configurar(string ferramenta, IEnumerable<string> argumentos, bool capturarSaida)
    {
        EvitarCaixasDeErro();
        // Sem captura no "pg_ctl start": o processo do servidor herdaria o pipe e a leitura nunca terminaria.
        var psi = new ProcessStartInfo(Path.Combine(CaminhoSeguro(PastaBinarios), Exe(ferramenta)))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = capturarSaida,
            RedirectStandardError = capturarSaida,
        };
        foreach (var a in argumentos) psi.ArgumentList.Add(a);
        psi.Environment["LC_MESSAGES"] = "C";
        // DLLs do Visual C++ distribuídas em pgsql\bin têm precedência sobre o que houver no sistema
        psi.Environment["PATH"] = CaminhoSeguro(PastaBinarios) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        return psi;
    }

    private async Task<(int Codigo, string Saida)> ExecutarAsync(string ferramenta, IEnumerable<string> argumentos, CancellationToken ct,
                                                                bool capturarSaida = true)
    {
        using var p = Process.Start(Configurar(ferramenta, argumentos, capturarSaida))
                      ?? throw new InvalidOperationException($"Não foi possível executar {ferramenta}.");
        if (!capturarSaida)
        {
            await p.WaitForExitAsync(ct);
            return (p.ExitCode, "");
        }
        var saida = p.StandardOutput.ReadToEndAsync(ct);
        var erro = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return (p.ExitCode, (await erro) + (await saida));
    }
}
