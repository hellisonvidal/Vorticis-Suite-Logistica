using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vorticis.Desktop.Comum.ViewModels;

public partial class AlterarSenhaViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    [ObservableProperty] private string _senhaAtual = "";
    [ObservableProperty] private string _novaSenha = "";
    [ObservableProperty] private string _confirmacao = "";

    [RelayCommand]
    private async Task AlterarAsync()
    {
        Erros.Clear();
        Mensagem = null;
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao) return;
        var r = await app.Usuarios.AlterarPropriaSenhaAsync(sessao, SenhaAtual, NovaSenha, Confirmacao);
        MostrarResultado(r, "Senha alterada.");
        if (r.Sucesso) (SenhaAtual, NovaSenha, Confirmacao) = ("", "", "");
    }
}
