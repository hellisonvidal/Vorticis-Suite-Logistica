using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Sistema;

namespace Vorticis.Desktop.Comum.ViewModels;

public sealed record ComponenteTerceiro(string Nome, string Licenca, string Uso);

/// <summary>Ajuda › Licença e registro: a quem o sistema está registrado e licenças dos componentes de terceiros.</summary>
public partial class LicencaViewModel(MainViewModelBase main) : ModuloViewModel(main)
{
    public static IReadOnlyList<ComponenteTerceiro> Componentes { get; } =
    [
        new(".NET", "MIT", "plataforma de execução"),
        new("Avalonia UI", "MIT", "interface gráfica"),
        new("CommunityToolkit.Mvvm", "MIT", "estrutura das telas"),
        new("PostgreSQL", "PostgreSQL License", "banco de dados local"),
        new("Npgsql", "PostgreSQL License", "acesso ao banco de dados"),
        new("PDFsharp / MigraDoc", "MIT", "relatórios em PDF"),
        new("MiniExcel", "Apache 2.0", "planilhas Excel (importação e exportação)"),
        new("Liberation Sans · Inter", "SIL Open Font License 1.1", "fontes"),
    ];

    public string Versao => $"{main.Modulo.Nome} {SobreViewModel.Versao}";
    public bool EhAdministrador => main.Sessao?.Administrador == true;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Situacao))] private RegistroLicenca? _registro;
    [ObservableProperty] private string _titular = "";
    [ObservableProperty] private string _documento = "";
    [ObservableProperty] private string _email = "";

    public string Situacao => Registro is { Registrado: true } r
        ? $"Registrado para: {r.Titular}" + (string.IsNullOrEmpty(r.Documento) ? "" : $" — {r.DocumentoFormatado}")
        : "Cópia ainda não registrada. O administrador pode informar abaixo o titular da licença.";

    public string Detalhe => Registro is { Registrado: true, RegistradoEm: { } quando } r
        ? $"Registro em {quando.ToLocalTime():dd/MM/yyyy}" + (string.IsNullOrEmpty(r.Email) ? "" : $" · {r.Email}")
        : "";

    public async Task CarregarAsync()
    {
        OnPropertyChanged(nameof(EhAdministrador));
        if (main.App is not { } app) return;
        Registro = await app.Licenca.ObterAsync();
        (Titular, Documento, Email) = (Registro.Titular ?? "", Registro.DocumentoFormatado, Registro.Email ?? "");
        OnPropertyChanged(nameof(Detalhe));
    }

    [RelayCommand]
    private async Task RegistrarAsync()
    {
        Erros.Clear();
        Mensagem = null;
        if (main.App is not { } app || main.Sessao is not { } sessao) return;
        var r = await app.Licenca.RegistrarAsync(sessao, Titular, Documento, Email);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = "Registro da licença salvo.";
    }
}
