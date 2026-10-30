using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vorticis.Desktop.Comum.ViewModels;

public sealed partial class SelecaoListaViewModel : ObservableObject
{
    public SelecaoListaViewModel(string titulo, IEnumerable<string> opcoes)
    {
        Titulo = titulo;
        Opcoes = new(opcoes);
        SelecionarCommand = new RelayCommand<string>(Selecionar);
        CancelarCommand = new RelayCommand(() => DialogHost.Close("RootDialog", null));
    }

    public string Titulo { get; }
    public ObservableCollection<string> Opcoes { get; }
    public IRelayCommand<string> SelecionarCommand { get; }
    public IRelayCommand CancelarCommand { get; }

    private void Selecionar(string? opcao)
    {
        if (opcao != null) DialogHost.Close("RootDialog", opcao);
    }
}