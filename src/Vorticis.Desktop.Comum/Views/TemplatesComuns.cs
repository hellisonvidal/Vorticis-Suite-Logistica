using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Vorticis.Desktop.Comum.ViewModels;

namespace Vorticis.Desktop.Comum.Views;

/// <summary>
/// Associa os ViewModels das telas comuns às suas Views. Entra em <c>Application.DataTemplates</c> do App.axaml de cada
/// executável (<c>&lt;c:TemplatesComuns /&gt;</c>); as telas do módulo continuam como DataTemplate no XAML do módulo.
/// </summary>
public sealed class TemplatesComuns : IDataTemplate
{
    private static readonly Dictionary<Type, Func<Control>> Mapa = new()
    {
        [typeof(LoginViewModel)] = () => new LoginView(),
        [typeof(ConexaoViewModel)] = () => new ConexaoView(),
        [typeof(InicioViewModel)] = () => new InicioView(),
        [typeof(CadastroEmpresaViewModel)] = () => new CadastroEmpresaView(),
        [typeof(ContabilidadeEmpresaViewModel)] = () => new ContabilidadeEmpresaView(),
        [typeof(SociosViewModel)] = () => new SociosView(),
        [typeof(ClientesViewModel)] = () => new ParticipantesView(),
        [typeof(FornecedoresViewModel)] = () => new ParticipantesView(),
        [typeof(ContasBancariasViewModel)] = () => new ContasBancariasView(),
        [typeof(FormasPagamentoViewModel)] = () => new FormasPagamentoView(),
        [typeof(UsuariosViewModel)] = () => new UsuariosView(),
        [typeof(PerfisViewModel)] = () => new PerfisView(),
        [typeof(AlterarSenhaViewModel)] = () => new AlterarSenhaView(),
        [typeof(CentralUsuariosViewModel)] = () => new CentralUsuariosView(),
        [typeof(CertificadosViewModel)] = () => new CertificadosView(),
        [typeof(ConfiguracoesGeraisViewModel)] = () => new ConfiguracoesGeraisView(),
        [typeof(BackupViewModel)] = () => new BackupView(),
        [typeof(ObrigacoesViewModel)] = () => new ObrigacoesView(),
        [typeof(LicencaViewModel)] = () => new LicencaView(),
        [typeof(SobreViewModel)] = () => new SobreView(),
        [typeof(ManualViewModel)] = () => new ManualView(),
        [typeof(ParametrosAbasViewModel)] = () => new ParametrosAbasView(),
        [typeof(PreferenciasMenuViewModel)] = () => new PreferenciasMenuView(),
        [typeof(ContabilizacaoLoteViewModel)] = () => new ContabilizacaoLoteView(),
        [typeof(ImportacaoCadastrosViewModel)] = () => new ImportacaoCadastrosView(),
    };

    public bool Match(object? data) => data is ParametrosViewModel || (data is not null && Mapa.ContainsKey(data.GetType()));

    public Control? Build(object? data) => data is ParametrosViewModel ? new ParametrosView() : data is not null && Mapa.TryGetValue(data.GetType(), out var criar) ? criar() : null;
}
