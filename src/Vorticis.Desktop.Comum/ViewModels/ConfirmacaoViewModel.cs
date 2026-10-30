using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vorticis.Desktop.Comum.ViewModels;

public sealed partial class ConfirmacaoViewModel : ObservableObject
{
    public ConfirmacaoViewModel(string titulo, string mensagem)
    {
        Titulo = titulo;
        Mensagem = mensagem;
        ConfirmarCommand = new RelayCommand(() => DialogHost.Close("RootDialog", true));
        CancelarCommand = new RelayCommand(() => DialogHost.Close("RootDialog", false));
    }

    public string Titulo { get; }
    public string Mensagem { get; }
    public IRelayCommand ConfirmarCommand { get; }
    public IRelayCommand CancelarCommand { get; }
}