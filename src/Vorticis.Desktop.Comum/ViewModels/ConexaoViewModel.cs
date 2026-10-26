using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Npgsql;

namespace Vorticis.Desktop.Comum.ViewModels;

/// <summary>Configuração da conexão com o PostgreSQL (antes do login, se a conexão falhar; depois, só administrador).</summary>
public partial class ConexaoViewModel(MainViewModelBase main) : ModuloViewModel(main)
{
    [ObservableProperty] private string _servidor = "localhost";
    [ObservableProperty] private decimal? _porta = 5432;
    [ObservableProperty] private string _banco = "vorticis";
    [ObservableProperty] private string _usuario = "vorticis";
    [ObservableProperty] private string _senha = "";
    [ObservableProperty] private string? _falha;
    [ObservableProperty] private bool _mostrarSenha;

    /// <summary>Modo escolhido na tela: banco local do Vorticis ou servidor PostgreSQL.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(EditandoServidor))] private bool _usarBancoLocal;

    // banco local: porta e troca de senha
    [ObservableProperty] private decimal? _portaLocal = Vorticis.Infra.Sistema.ServidorPostgresEmbutido.PortaPreferida;
    [ObservableProperty] private string _novaSenhaLocal = "";
    [ObservableProperty] private string _confirmacaoSenhaLocal = "";

    // configuração automática
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PodeConfigurar))] private bool _configurando;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TemLog))] private string _logConfiguracao = "";
    [ObservableProperty] private bool _oferecerRecriar;
    [ObservableProperty] private bool _pedirCredenciais;
    [ObservableProperty] private string _usuarioAuto = "postgres";
    [ObservableProperty] private string _senhaAuto = "";
    /// <summary>Servidor PostgreSQL externo é opção avançada: só abre sozinho quando não há banco local.</summary>
    [ObservableProperty] private bool _servidorExpandido;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TemFalhaLocal))] private bool _falhouNoBancoLocal;

    public bool PodeConfigurar => !Configurando;
    public bool TemLog => LogConfiguracao.Length > 0;
    public bool TemFalhaLocal => FalhouNoBancoLocal;
    public string ArquivoLogConfiguracao => Configuracao.LogConfiguracaoBanco;

    public string AvisoLogin =>
        "Atenção: \"vorticis\" / \"vorticis\" é o login do aplicativo no primeiro acesso (depois que o banco estiver conectado), " +
        "não o usuário e a senha do banco de dados. O banco local é criado e configurado sozinho; use o botão abaixo.";

    /// <summary>Texto para "Copiar diagnóstico": falha, ambiente e o log da configuração automática.</summary>
    public string Diagnostico =>
        $"Vorticis - diagnóstico do banco de dados ({DateTime.Now:yyyy-MM-dd HH:mm:ss}){Environment.NewLine}" +
        $"Falha: {Falha ?? "(nenhuma)"}{Environment.NewLine}Binários: {Vorticis.Infra.Sistema.ServidorPostgresEmbutido.LocalizarBinarios() ?? "(não encontrados)"}{Environment.NewLine}" +
        $"Dados: {PastaBancoLocal}{Environment.NewLine}Configuração: {Arquivo}{Environment.NewLine}Log: {ArquivoLogConfiguracao}{Environment.NewLine}" +
        $"{Environment.NewLine}{LogConfiguracao}";

    public bool EditandoServidor
    {
        get => !UsarBancoLocal;
        set => UsarBancoLocal = !value;
    }
    public char CaractereSenha => MostrarSenha ? '\0' : '●';
    partial void OnMostrarSenhaChanged(bool value) => OnPropertyChanged(nameof(CaractereSenha));

    public string ConexaoAtual => main.App is null || string.IsNullOrEmpty(main.ConnectionString)
        ? "Sem conexão."
        : $"Em uso: {Configuracao.Descrever(main.ConnectionString)}";

    public string PastaBancoLocal => Configuracao.PastaBancoLocal;

    public string Arquivo => Configuracao.ArquivoConfiguracao;

    /// <summary>O programa foi instalado com o PostgreSQL embutido (pasta pgsql).</summary>
    public bool BancoLocalDisponivel => Configuracao.BancoLocalDisponivel;

    [ObservableProperty] private bool _usandoBancoLocal;

    public string DescricaoBancoLocal =>
        $"O Vorticis inclui um banco de dados próprio, criado e iniciado automaticamente, sem nenhuma configuração. " +
        $"Os dados ficam em {Configuracao.PastaBancoLocal}. Use um servidor PostgreSQL abaixo apenas para compartilhar " +
        "o banco entre vários computadores da rede.";

    public void CarregarDe(string connectionString, string? falha)
    {
        var b = new NpgsqlConnectionStringBuilder(connectionString);
        (Servidor, Porta, Banco, Usuario, Senha) = (b.Host ?? "localhost", b.Port, b.Database ?? "vorticis", b.Username ?? "", b.Password ?? "");
        Falha = falha;
        UsandoBancoLocal = main.App is not null && BancoDoAplicativo.EhBancoLocal(main.ConnectionString);
        UsarBancoLocal = BancoLocalDisponivel && (UsandoBancoLocal || Configuracao.Carregar().Modo == ModoBanco.Local);
        FalhouNoBancoLocal = falha is not null && UsarBancoLocal;
        ServidorExpandido = !UsarBancoLocal;
        (OferecerRecriar, PedirCredenciais) = (false, false);
        var cfg = Configuracao.Carregar();
        PortaLocal = BancoDoAplicativo.PortaLocal ?? (cfg.Modo == ModoBanco.Local && cfg.Porta > 0 ? cfg.Porta : Vorticis.Infra.Sistema.ServidorPostgresEmbutido.PortaPreferida);
        (NovaSenhaLocal, ConfirmacaoSenhaLocal, MostrarSenha) = ("", "", false);
        OnPropertyChanged(nameof(ConexaoAtual));
        Erros.Clear();
        Mensagem = null;
    }

    /// <summary>Configuração automática do banco (embutido ou PostgreSQL local), com log passo a passo.</summary>
    [RelayCommand]
    private Task ConfigurarAutomaticamenteAsync() => ConfigurarAsync(new OpcoesConfiguracaoBanco(
        Usuario: PedirCredenciais ? UsuarioAuto : null, Senha: PedirCredenciais ? SenhaAuto : null));

    /// <summary>Existe banco local com senha desconhecida: preserva a pasta antiga (backup) e cria outro.</summary>
    [RelayCommand]
    private Task RecriarBancoLocalAsync() => ConfigurarAsync(new OpcoesConfiguracaoBanco(RecriarBancoExistente: true));

    private async Task ConfigurarAsync(OpcoesConfiguracaoBanco opcoes)
    {
        if (Configurando) return;
        Configurando = true;
        Erros.Clear();
        Mensagem = null;
        OferecerRecriar = false;
        LogConfiguracao = "";
        try
        {
            var r = await new ConfiguradorBanco(linha => LogConfiguracao += linha + Environment.NewLine).ExecutarAsync(opcoes);
            if (r.Sucesso)
            {
                Falha = null;
                (FalhouNoBancoLocal, PedirCredenciais) = (false, false);
                Mensagem = $"Configuração concluída: {r.Resumo}";
                await main.IniciarAsync();
            }
            else
            {
                Erros.Add(r.Resumo);
                (OferecerRecriar, PedirCredenciais) = (r.PodeRecriar, r.PrecisaCredenciais);
                if (r.PrecisaCredenciais) ServidorExpandido = false;
            }
        }
        finally
        {
            Configurando = false;
        }
    }

    /// <summary>Salva o modo escolhido (local ou servidor) e reconecta.</summary>
    [RelayCommand]
    private async Task SalvarConfiguracaoAsync()
    {
        if (UsarBancoLocal) await SalvarLocalAsync();
        else await SalvarEConectarAsync();
    }

    private async Task SalvarLocalAsync()
    {
        Erros.Clear();
        Mensagem = null;
        var porta = (int)(PortaLocal ?? 0);
        if (porta is < 1024 or > 65535) { Erros.Add("Porta do banco local deve estar entre 1024 e 65535."); return; }
        var trocarSenha = NovaSenhaLocal.Length > 0 || ConfirmacaoSenhaLocal.Length > 0;
        if (trocarSenha)
        {
            if (NovaSenhaLocal.Length < 8) { Erros.Add("A nova senha do banco local deve ter ao menos 8 caracteres."); return; }
            if (NovaSenhaLocal != ConfirmacaoSenhaLocal) { Erros.Add("A confirmação não confere com a nova senha."); return; }
            if (!UsandoBancoLocal) { Erros.Add("Para trocar a senha, conecte-se primeiro ao banco local."); return; }
        }
        try
        {
            if (trocarSenha) await BancoDoAplicativo.AlterarSenhaLocalAsync(main.ConnectionString, NovaSenhaLocal);
            var portaMudou = UsandoBancoLocal && BancoDoAplicativo.PortaLocal != porta;
            Configuracao.SalvarLocal(porta);
            if (portaMudou) BancoDoAplicativo.PararLocal();   // reinicia na nova porta ao reconectar
        }
        catch (Exception ex)
        {
            Erros.Add($"Não foi possível gravar a configuração: {ex.Message}");
            return;
        }
        await main.IniciarAsync();
    }

    private string Montar() => new NpgsqlConnectionStringBuilder
    {
        Host = Servidor.Trim(), Port = (int)(Porta ?? 5432), Database = Banco.Trim(), Username = Usuario.Trim(), Password = Senha,
    }.ConnectionString;

    [RelayCommand]
    private async Task TestarAsync()
    {
        Erros.Clear();
        Mensagem = null;
        try
        {
            await using var con = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(Montar()) { Database = "postgres", Timeout = 5 }.ConnectionString);
            await con.OpenAsync();
            Mensagem = $"Conexão com o servidor OK (PostgreSQL {con.PostgreSqlVersion}).";
        }
        catch (Exception ex)
        {
            Erros.Add($"Falha: {ex.Message}");
        }
    }

    /// <summary>Volta a usar o banco local embutido (criando-o, se ainda não existir) e reconecta.</summary>
    [RelayCommand]
    private async Task UsarBancoLocalAsync()
    {
        Erros.Clear();
        try
        {
            Configuracao.SalvarLocal();
        }
        catch (Exception ex)
        {
            Erros.Add($"Não foi possível gravar a configuração: {ex.Message}");
            return;
        }
        await main.IniciarAsync();
    }

    /// <summary>Grava a configuração e reconecta (volta à tela de login).</summary>
    [RelayCommand]
    private async Task SalvarEConectarAsync()
    {
        Erros.Clear();
        try
        {
            Configuracao.Salvar(Montar());
        }
        catch (Exception ex)
        {
            Erros.Add($"Não foi possível gravar a configuração: {ex.Message}");
            return;
        }
        await main.InicializarAsync(Montar());
    }
}
