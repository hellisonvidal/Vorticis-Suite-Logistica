using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Infra.Api;

namespace Vorticis.Desktop.Comum.ViewModels;

public sealed record ChaveApiLinha(ChaveApi C)
{
    public string Nome => C.Nome;
    public string Inicio => C.Prefixo + "…";
    public string Usuario => C.Usuario;
    public string Acesso => C.Escrita ? "Leitura e gravação" : "Só leitura";
    public string Criada => $"{C.CriadaEm.ToLocalTime():dd/MM/yyyy HH:mm}";
    public string UltimoUso => C.UltimoUso is { } u ? $"{u.ToLocalTime():dd/MM/yyyy HH:mm}" : "nunca";
    public string Situacao => C.Ativa ? "Ativa" : $"Revogada em {C.RevogadaEm!.Value.ToLocalTime():dd/MM/yyyy}";
}

public sealed record RotaApiLinha(RotaApi R)
{
    public string Metodo => R.Metodo;
    public string Caminho => R.Caminho;
    public string Descricao => R.Descricao + (R.Grava ? " (exige chave com gravação)" : "");
}

/// <summary>
/// Configurações › Integração por API: chaves de acesso de sistemas externos (geradas por administradores, por empresa) e o servidor da API
/// dentro do programa (http://localhost:porta). As rotas são as do programa (<see cref="Modulos.IModuloDesktop.RotasApi"/>).
/// </summary>
public sealed partial class ApiViewModel(MainViewModelBase main) : ModuloViewModel(main)
{
    /// <summary>Um servidor por programa aberto (continua ligado ao trocar de tela ou de módulo).</summary>
    private static ServidorApi? _servidor;
    public static ServidorApi? Servidor => _servidor;

    public ObservableCollection<ChaveApiLinha> Chaves { get; } = [];
    public IReadOnlyList<RotaApiLinha> Rotas { get; } = [.. main.Modulo.RotasApi.Select(r => new RotaApiLinha(r))];
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(RevogarCommand))] private ChaveApiLinha? _selecionada;
    [ObservableProperty] private string _nomeChave = "";
    [ObservableProperty] private bool _escrita;
    [ObservableProperty] private string _chaveGerada = "";
    [ObservableProperty] private string _porta = "5180";
    [ObservableProperty] private string _situacaoServidor = "";

    public string Endereco => $"http://localhost:{Porta.Trim()}/api/v1";
    partial void OnPortaChanged(string value) => OnPropertyChanged(nameof(Endereco));

    public Task CarregarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        AtualizarSituacao();
        var r = await app.ChavesApi.ListarAsync(empresa, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Chaves.Clear();
        foreach (var c in r.Valor) Chaves.Add(new ChaveApiLinha(c));
    });

    private void AtualizarSituacao() =>
        SituacaoServidor = _servidor is { Ativo: true } s ? $"Ligado em http://localhost:{s.Porta}/api/v1" : "Desligado";

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.ChavesApi.GerarAsync(empresa, NomeChave, Escrita, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        ChaveGerada = r.Valor;
        NomeChave = "";
        await CarregarAsync();
        Mensagem = "Chave gerada. Copie agora: ela não aparece de novo.";
    });

    private bool TemSelecionada() => Selecionada is { C.Ativa: true };

    [RelayCommand(CanExecute = nameof(TemSelecionada))]
    private Task RevogarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada is not { } s) return;
        if (!await Contexto.ConfirmarAsync("Revogar chave", $"Revogar a chave \"{s.Nome}\"? O sistema que a usa deixa de ter acesso na hora.", "Revogar", "Cancelar")) return;
        var r = await app.ChavesApi.RevogarAsync(empresa, s.C.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = "Chave revogada.";
    });

    [RelayCommand]
    private async Task LigarAsync()
    {
        Erros.Clear();
        Mensagem = null;
        if (Contexto.App is not { } app) { Erros.Add("Sem conexão com o banco de dados."); return; }
        if (!int.TryParse(Porta.Trim(), out var porta) || porta is < 1024 or > 65535) { Erros.Add("Porta: um número de 1024 a 65535 (padrão 5180)."); return; }
        if (Rotas.Count == 0) { Erros.Add("Este programa não tem funções de API."); return; }
        if (_servidor is not null) await _servidor.PararAsync();
        try
        {
            _servidor = new ServidorApi(app, [.. Rotas.Select(r => r.R)]);
            _servidor.Iniciar(porta);
            Mensagem = $"API ligada em http://localhost:{porta}/api/v1.";
        }
        catch (Exception ex)
        {
            _servidor = null;
            Erros.Add($"Não foi possível ligar a API na porta {porta}: {ex.Message}");
        }
        AtualizarSituacao();
    }

    [RelayCommand]
    private async Task DesligarAsync()
    {
        if (_servidor is not null) await _servidor.PararAsync();
        _servidor = null;
        AtualizarSituacao();
        Mensagem = "API desligada.";
    }
}
