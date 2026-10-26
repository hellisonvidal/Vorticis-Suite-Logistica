using Vorticis.Desktop.Comum;
using Vorticis.Infra.Sistema;

namespace Vorticis.Desktop.Tests;

/// <summary>Sequência da configuração automática do banco com servidor, portas e arquivos simulados.</summary>
public sealed class ConfiguradorBancoTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), $"vorticis-cfg-{Guid.NewGuid():N}");

    private sealed class Falso(string dados) : IPostgresLocal
    {
        public string PastaDados => dados;
        public string ArquivoLog => Path.Combine(dados, "..", "pg.log");
        public bool Inicializado => File.Exists(Path.Combine(dados, "PG_VERSION"));
        public bool IniciadoPorEsteProcesso { get; private set; }
        public List<string> Chamadas { get; } = [];
        public int PortaEscolhida = 54329;
        public Task InicializarAsync(string senha, CancellationToken ct = default)
        {
            Chamadas.Add("initdb");
            Directory.CreateDirectory(PastaDados);
            File.WriteAllText(Path.Combine(PastaDados, "PG_VERSION"), "16");
            return Task.CompletedTask;
        }
        public bool LimparPidOrfao(Func<int, string?>? n = null)
        {
            Chamadas.Add("pid");
            var pid = Path.Combine(PastaDados, "postmaster.pid");
            if (!File.Exists(pid)) return false;
            File.Delete(pid);
            return true;
        }
        public Task<int> IniciarAsync(int pref = 54329, CancellationToken ct = default)
        {
            Chamadas.Add($"start:{pref}");
            IniciadoPorEsteProcesso = true;
            return Task.FromResult(PortaEscolhida);
        }
        public void Parar() { Chamadas.Add("stop"); IniciadoPorEsteProcesso = false; }
    }

    private sealed class Cenario
    {
        public required Falso Servidor;
        public ConfiguracaoBanco Config = new(ModoBanco.Local);
        public List<string> Gravado = [];
        public List<string> Etapas = [];
        public IReadOnlyList<string> VcAusente = [];
        public bool PortaOcupada;
    }

    private Cenario Novo() => new() { Servidor = new Falso(Path.Combine(_pasta, "banco")) };

    private ConfiguradorBanco Criar(Cenario c, string? bin = "/pg/bin", Action<string>? log = null) => new(log)
    {
        LocalizarBinarios = () => bin,
        CriarServidor = _ => c.Servidor,
        RuntimeAusente = _ => c.VcAusente,
        PortaLivre = _ => !c.PortaOcupada,
        CarregarConfiguracao = () => c.Config,
        SalvarLocal = (p, s) => { c.Gravado.Add($"local:{p}:{s}"); c.Config = new(ModoBanco.Local, Porta: p ?? c.Config.Porta, Senha: s ?? c.Config.Senha); },
        SalvarServidor = cs => c.Gravado.Add("servidor"),
        CriarBanco = (_, _) => { c.Etapas.Add("banco"); return Task.CompletedTask; },
        AplicarMigracoes = (_, _) => { c.Etapas.Add("migracoes"); return Task.CompletedTask; },
        TestarConexao = (_, _) => { c.Etapas.Add("teste"); return Task.FromResult("16.4"); },
        ServidorPadraoAtivo = _ => Task.FromResult(false),
        ArquivoTrava = Path.Combine(_pasta, "banco.lock"),
        ArquivoLog = Path.Combine(_pasta, "logs", "configuracao-banco.log"),
    };

    [Fact]
    public async Task Primeira_execucao_cria_inicia_migra_grava_e_testa_na_ordem()
    {
        var c = Novo();
        var linhas = new List<string>();
        var r = await Criar(c, log: linhas.Add).ExecutarAsync(ct: TestContext.Current.CancellationToken);

        Assert.True(r.Sucesso, r.Resumo);
        Assert.Equal(["pid", "initdb", "start:54329"], c.Servidor.Chamadas);
        Assert.Equal(["banco", "migracoes", "teste"], c.Etapas);
        Assert.StartsWith("local:", c.Gravado[0]);                     // senha gravada antes do initdb
        Assert.Contains(c.Gravado, g => g.StartsWith("local:54329:"));  // porta gravada depois
        Assert.Contains("Port=54329", r.ConnectionString);
        Assert.Contains(linhas, l => l.Contains("[1/9]"));
        Assert.Contains(linhas, l => l.Contains("[9/9]"));
        Assert.Contains("CONCLUÍDO", File.ReadAllText(Path.Combine(_pasta, "logs", "configuracao-banco.log")));
    }

    [Fact]
    public async Task Porta_ocupada_e_registrada_e_usa_a_porta_que_o_servidor_escolheu()
    {
        var c = Novo();
        c.PortaOcupada = true;
        c.Servidor.PortaEscolhida = 61000;
        var cfg = Criar(c);
        var r = await cfg.ExecutarAsync(ct: TestContext.Current.CancellationToken);
        Assert.True(r.Sucesso);
        Assert.Contains("Port=61000", r.ConnectionString);
        Assert.Contains("ocupada", cfg.Log);
    }

    [Fact]
    public async Task Runtime_do_Visual_Cpp_ausente_falha_antes_de_mexer_no_banco()
    {
        var c = Novo();
        c.VcAusente = ["vcruntime140.dll"];
        var r = await Criar(c).ExecutarAsync(ct: TestContext.Current.CancellationToken);
        Assert.False(r.Sucesso);
        Assert.Contains("vcruntime140.dll", r.Resumo);
        Assert.Empty(c.Servidor.Chamadas);
        Assert.Empty(c.Gravado);
    }

    [Fact]
    public async Task Pid_orfao_e_removido()
    {
        var c = Novo();
        Directory.CreateDirectory(c.Servidor.PastaDados);
        File.WriteAllText(Path.Combine(c.Servidor.PastaDados, "PG_VERSION"), "16");
        File.WriteAllText(Path.Combine(c.Servidor.PastaDados, "postmaster.pid"), "999999");
        c.Config = new(ModoBanco.Local, null, 54329, "senha");
        var r = await Criar(c).ExecutarAsync(ct: TestContext.Current.CancellationToken);
        Assert.True(r.Sucesso);
        Assert.False(File.Exists(Path.Combine(c.Servidor.PastaDados, "postmaster.pid")));
        Assert.DoesNotContain("initdb", c.Servidor.Chamadas);            // cluster existente reaproveitado
    }

    [Fact]
    public async Task Cluster_sem_senha_conhecida_oferece_recriar_e_recriar_preserva_a_pasta_antiga()
    {
        var c = Novo();
        Directory.CreateDirectory(c.Servidor.PastaDados);
        File.WriteAllText(Path.Combine(c.Servidor.PastaDados, "PG_VERSION"), "16");
        File.WriteAllText(Path.Combine(c.Servidor.PastaDados, "dado.txt"), "x");
        c.Config = new(ModoBanco.Local);                                  // senha desconhecida (DPAPI de outro usuário)

        var r = await Criar(c).ExecutarAsync(ct: TestContext.Current.CancellationToken);
        Assert.False(r.Sucesso);
        Assert.True(r.PodeRecriar);
        Assert.DoesNotContain("initdb", c.Servidor.Chamadas);

        r = await Criar(c).ExecutarAsync(new OpcoesConfiguracaoBanco(RecriarBancoExistente: true), TestContext.Current.CancellationToken);
        Assert.True(r.Sucesso, r.Resumo);
        var backup = Assert.Single(Directory.GetDirectories(_pasta, "banco.backup-*"));
        Assert.True(File.Exists(Path.Combine(backup, "dado.txt")));
        Assert.Contains("initdb", c.Servidor.Chamadas);
        Assert.Contains("stop", c.Servidor.Chamadas);
    }

    [Fact]
    public async Task Pasta_de_dados_incompleta_vira_backup_automaticamente()
    {
        var c = Novo();
        Directory.CreateDirectory(c.Servidor.PastaDados);
        File.WriteAllText(Path.Combine(c.Servidor.PastaDados, "lixo"), "x");   // sem PG_VERSION
        var r = await Criar(c).ExecutarAsync(ct: TestContext.Current.CancellationToken);
        Assert.True(r.Sucesso, r.Resumo);
        Assert.Single(Directory.GetDirectories(_pasta, "banco.backup-*"));
    }

    [Fact]
    public async Task Sem_banco_embutido_pede_credenciais_quando_ha_postgresql_local()
    {
        var c = Novo();
        var cfg = Criar(c, bin: null);
        var r = await cfg.ExecutarAsync(ct: TestContext.Current.CancellationToken);
        Assert.False(r.Sucesso);
        Assert.False(r.PrecisaCredenciais);                               // nada em 5432

        var com = new ConfiguradorBanco
        {
            LocalizarBinarios = () => null, ServidorPadraoAtivo = _ => Task.FromResult(true),
            CriarBanco = (_, _) => { c.Etapas.Add("banco"); return Task.CompletedTask; },
            AplicarMigracoes = (_, _) => Task.CompletedTask,
            TestarConexao = (_, _) => Task.FromResult("16.4"),
            SalvarServidor = cs => c.Gravado.Add("servidor:" + cs), ArquivoLog = null,
        };
        r = await com.ExecutarAsync(ct: TestContext.Current.CancellationToken);
        Assert.True(r.PrecisaCredenciais);
        r = await com.ExecutarAsync(new OpcoesConfiguracaoBanco(Usuario: "postgres", Senha: "s"), TestContext.Current.CancellationToken);
        Assert.True(r.Sucesso, r.Resumo);
        Assert.Contains(c.Gravado, g => g.StartsWith("servidor:") && g.Contains("Port=5432"));
    }

    [Fact]
    public void Argumento_de_linha_de_comando_e_opcoes()
    {
        Assert.True(ConfiguracaoAutomatica.Solicitada(["--configurar-banco"]));
        Assert.True(ConfiguracaoAutomatica.Solicitada(["x", "--CONFIGURAR-BANCO"]));
        Assert.False(ConfiguracaoAutomatica.Solicitada([]));
        var o = ConfiguracaoAutomatica.Opcoes(["--configurar-banco", "--recriar", "--usuario=pg", "--senha=a=b"]);
        Assert.Equal(new OpcoesConfiguracaoBanco(true, "pg", "a=b"), o);
    }

    [Fact]
    public async Task Codigo_de_saida_da_linha_de_comando()
    {
        var c = Novo();
        Assert.Equal(0, await ConfiguracaoAutomatica.ExecutarAsync(["--configurar-banco"], Criar(c)));
        var d = Novo();
        d.VcAusente = ["msvcp140.dll"];
        Assert.NotEqual(0, await ConfiguracaoAutomatica.ExecutarAsync(["--configurar-banco"], Criar(d)));
    }

    public void Dispose()
    {
        try { Directory.Delete(_pasta, recursive: true); } catch (IOException) { }
    }
}
