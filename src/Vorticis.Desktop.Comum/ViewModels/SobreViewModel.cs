using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Vorticis.Desktop.Comum.ViewModels;

public sealed record LinhaAtalho(string Teclas, string Descricao);

public partial class SobreViewModel(MainViewModelBase main) : ModuloViewModel(main)
{
    public static string Versao { get; } =
        typeof(SobreViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.1";

    public string Nome => main.Modulo.Nome;
    public string Cabecalho => $"{main.Modulo.Subtitulo} — versão {Versao}";

    /// <summary>Atalhos de teclado do módulo e do casco, como listados na tela Sobre.</summary>
    public IReadOnlyList<LinhaAtalho> Atalhos { get; } = [.. main.Atalhos.Select(a => new LinhaAtalho(a.Gesto, a.Descricao))];

    [ObservableProperty] private string _conexao = "";
    [ObservableProperty] private string _usuario = "";

    public void Atualizar()
    {
        Conexao = Configuracao.Descrever(main.ConnectionString);
        Usuario = main.UsuarioRotulo;
    }
}
