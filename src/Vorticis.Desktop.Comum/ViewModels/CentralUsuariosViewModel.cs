using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Vorticis.Application.Seguranca;

namespace Vorticis.Desktop.Comum.ViewModels;

public sealed record RegistroAcessoLinha(RegistroAcesso Registro)
{
    public string Quando => Registro.OcorridoEm.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
    public string Login => Registro.Login;
    public string Resultado => Registro.Sucesso ? "Entrou" : "Recusado";
    public string Motivo => Registro.Motivo ?? "";
    public string Estacao => Registro.Estacao ?? "";
}

/// <summary>
/// Configurações › Usuários: reúne tudo sobre usuários em abas — cadastro de usuários, perfis de acesso,
/// alteração da própria senha e registro de acessos. Usuários comuns veem só a troca de senha.
/// </summary>
public partial class CentralUsuariosViewModel(MainViewModelBase main) : ModuloViewModel(main)
{
    public const int AbaUsuarios = 0;
    public const int AbaPerfis = 1;
    public const int AbaSenha = 2;
    public const int AbaRegistro = 3;

    public MainViewModelBase Main => main;

    [ObservableProperty] private int _abaSelecionada = AbaSenha;

    public ObservableCollection<RegistroAcessoLinha> Registros { get; } = [];

    /// <summary>Cadastro de usuários e registro de acessos: administrador, depois da configuração inicial dos perfis.</summary>
    public bool PodeAdministrar => main.Sessao?.Administrador == true && !main.ConfiguracaoInicialPendente;

    /// <summary>Aba Usuários: administrador (depois da configuração inicial) ou, na implantação, só para incluir o primeiro administrador.</summary>
    public bool PodeUsuarios => PodeAdministrar || main.EmImplantacao;

    public bool PodePerfis => main.Sessao?.Administrador == true;

    partial void OnAbaSelecionadaChanged(int value) => main.CarregarAbaUsuarios();

    public void AtualizarAcesso()
    {
        OnPropertyChanged(nameof(PodeAdministrar));
        OnPropertyChanged(nameof(PodeUsuarios));
        OnPropertyChanged(nameof(PodePerfis));
    }

    public async Task CarregarRegistrosAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao) return;
        var geracao = NovaGeracao();
        var lista = await app.Usuarios.RegistroDeAcessosAsync(sessao);
        if (!EhAtual(geracao)) return;
        Registros.Clear();
        foreach (var r in lista) Registros.Add(new RegistroAcessoLinha(r));
    }
}
