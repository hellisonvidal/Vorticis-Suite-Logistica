using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Sistema;

namespace Vorticis.Desktop.Comum.ViewModels;

public sealed record BackupLinha(ArquivoBackup Arquivo)
{
    public string Nome => Arquivo.Nome;
    public string Data => Arquivo.CriadoEm.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public string Tamanho => Arquivo.Bytes >= 1_048_576 ? $"{Arquivo.Bytes / 1_048_576.0:N1} MB" : $"{Arquivo.Bytes / 1024.0:N0} KB";
}

/// <summary>Configurações › Backup e restauração (somente administrador).</summary>
public partial class BackupViewModel(MainViewModelBase main) : ModuloViewModel(main)
{
    public ObservableCollection<BackupLinha> Arquivos { get; } = [];

    [ObservableProperty] private string _pasta = ServicoBackup.PastaPadrao;
    [ObservableProperty] private BackupLinha? _selecionado;

    [RelayCommand]
    private void AtualizarLista() => Atualizar();

    /// <summary>Ao abrir: a pasta escolhida (ou a padrão, no diretório de instalação).</summary>
    public async Task CarregarAsync()
    {
        var g = NovaGeracao();
        if (main.App is { } app)
        {
            var pasta = await app.ConfiguracoesGerais.PastaBackupAsync();
            if (!EhAtual(g)) return;                       // pasta escolhida enquanto carregava: não sobrescreve
            Pasta = pasta;
        }
        Atualizar();
    }

    /// <summary>Guarda a pasta atual como a pasta dos backups (usada também nos backups automáticos).</summary>
    [RelayCommand]
    private async Task SalvarPastaAsync()
    {
        Erros.Clear();
        Mensagem = null;
        if (main.App is not { } app || main.Sessao is not { } sessao) return;
        NovaGeracao();                                      // descarta uma leitura da pasta ainda em andamento
        var padrao = string.Equals(Pasta.Trim(), ServicoBackup.PastaPadrao, StringComparison.OrdinalIgnoreCase);
        var r = await app.ConfiguracoesGerais.SalvarPastaBackupAsync(sessao, padrao ? null : Pasta);
        MostrarResultado(r, padrao ? "Usando a pasta padrão (no diretório de instalação do programa)." : $"Os backups passam a ser gravados em {Pasta.Trim()}.");
        Atualizar();
    }

    [RelayCommand]
    private async Task UsarPastaPadraoAsync()
    {
        Pasta = ServicoBackup.PastaPadrao;
        await SalvarPastaAsync();
    }

    /// <summary>Exclui o arquivo de backup selecionado (pede confirmação).</summary>
    [RelayCommand]
    private async Task ExcluirAsync()
    {
        Erros.Clear();
        Mensagem = null;
        if (main.Sessao is not { } sessao) return;
        if (Selecionado is not { } sel) { Erros.Add("Selecione o backup a excluir."); return; }
        if (!await main.ConfirmarAsync("Excluir backup", $"Excluir definitivamente o arquivo de backup {sel.Nome} ({sel.Data})?", "Excluir", "Cancelar")) return;
        var r = ServicoBackup.Excluir(sessao, sel.Arquivo.Caminho);
        MostrarResultado(r, $"Backup {sel.Nome} excluído.");
        Atualizar();
    }

    public void Atualizar()
    {
        Arquivos.Clear();
        foreach (var a in ServicoBackup.Listar(Pasta)) Arquivos.Add(new BackupLinha(a));
    }

    [RelayCommand]
    private async Task GerarAsync()
    {
        Erros.Clear();
        Mensagem = null;
        if (main.App is not { } app || main.Sessao is not { } sessao) return;
        try
        {
            Ocupado = true;
            var r = await app.Backup.GerarAsync(sessao, Pasta);
            MostrarResultado(r, r.Sucesso ? $"Backup gerado: {r.Valor}" : "");
            Atualizar();
        }
        finally
        {
            Ocupado = false;
        }
    }

    /// <summary>
    /// Substitui o banco pelo backup selecionado. Um alerta pede a senha de administrador (padrão VORTICIS);
    /// encerra a sessão ao final.
    /// </summary>
    [RelayCommand]
    private async Task RestaurarAsync()
    {
        Erros.Clear();
        Mensagem = null;
        if (main.App is not { } app || main.Sessao is not { } sessao) return;
        if (Selecionado is null) { Erros.Add("Selecione o backup a restaurar."); return; }
        var senha = await main.SolicitarSenhaAsync("Restaurar backup",
            $"ATENÇÃO: todo o conteúdo atual do banco será SUBSTITUÍDO pelo backup {Selecionado.Nome} ({Selecionado.Data}). " +
            "Antes, o sistema gera automaticamente uma cópia de segurança do estado atual. Ao final, todos os usuários precisam entrar novamente.\n\n" +
            "Para confirmar, informe a senha de administrador:", "Restaurar", "Cancelar");
        if (senha is null) return;
        if (!await app.ConfiguracoesGerais.VerificarSenhaAdministradorAsync(senha)) { Erros.Add("Senha de administrador incorreta. Nada foi restaurado."); return; }
        try
        {
            Ocupado = true;
            var origem = Selecionado.Arquivo.Caminho;
            var r = await app.Backup.RestaurarAsync(sessao, origem, Pasta);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            // Reconecta (reaplica migrações, se o backup for de versão anterior) e volta ao login.
            await main.ReiniciarParaLoginAsync(
                $"Banco restaurado de {Path.GetFileName(origem)}. O estado anterior foi salvo em {Path.GetFileName(r.Valor)}. Entre novamente.");
        }
        finally
        {
            Ocupado = false;
        }
    }
}
