using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Desktop.Comum.Views;

namespace Vorticis.Desktop.Comum.ViewModels;

public sealed partial class ParametrosAbasViewModel : ObservableObject
{
    public ParametrosAbasViewModel(IContexto contexto)
    {
        Contexto = contexto;
        Abas =
        [
            new AbaParametros("Geral", new ConfiguracoesGeraisView()),
            new AbaParametros("Empresa", new CadastroEmpresaView()),
            new AbaParametros("Usuários", new CentralUsuariosView()),
            new AbaParametros("Certificados", new CertificadosView()),
            new AbaParametros("Backup", new BackupView()),
            new AbaParametros("Integrações", new IntegracoesView()),
        ];
        AbaSelecionada = Abas[0];
    }

    public IContexto Contexto { get; }

    [ObservableProperty] private ObservableCollection<AbaParametros> _abas = [];
    [ObservableProperty] private AbaParametros? _abaSelecionada;
}

public sealed class AbaParametros(string titulo, object conteudo)
{
    public string Titulo { get; } = titulo;
    public object Conteudo { get; } = conteudo;
}