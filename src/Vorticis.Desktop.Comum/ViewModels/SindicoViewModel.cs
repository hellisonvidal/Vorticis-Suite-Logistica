using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Cadastros;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Desktop.Comum.ViewModels;

/// <summary>
/// Aba Síndico de Cadastros › Empresas (módulo Condomínio): dados do síndico, mandato e se ele assina os livros contábeis e fiscais,
/// com assinatura comum ou digital (certificado ICP-Brasil). Marcado para assinar, ele entra no lugar do sócio nas assinaturas dos relatórios.
/// </summary>
public partial class SindicoViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    [ObservableProperty] private string _nome = "";
    [ObservableProperty] private string _cpf = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _telefone = "";
    [ObservableProperty] private DateTime? _mandatoInicio;
    [ObservableProperty] private DateTime? _mandatoFim;
    [ObservableProperty] private bool _assinaLivros;
    [ObservableProperty] private bool _assinaturaDigital;
    [ObservableProperty] private bool _cadastrado;

    public bool PodeEditar => Contexto.Pode(Permissao.EmpresaResponsaveisManter);
    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeEditar));

    /// <summary>Assinatura digital só vale para quem assina os livros: desmarcar a assinatura desmarca a digital.</summary>
    partial void OnAssinaLivrosChanged(bool value) { if (!value) AssinaturaDigital = false; }
    partial void OnAssinaturaDigitalChanged(bool value) { if (value) AssinaLivros = true; }

    private void Limpar()
    {
        (Nome, Cpf, Email, Telefone, MandatoInicio, MandatoFim, AssinaLivros, AssinaturaDigital, Cadastrado) = ("", "", "", "", null, null, false, false, false);
    }

    public Task CarregarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var g = NovaGeracao();
        var r = await app.Responsaveis.ObterSindicoAsync(empresa, usuario);
        if (!EhAtual(g)) return;
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        if (r.Valor is not { } s) { Limpar(); return; }
        (Nome, Cpf, Email, Telefone) = (s.Nome, s.Cpf is { } c ? Domain.Cadastros.Cpf.Formatar(c) : "", s.Email ?? "", s.Telefone ?? "");
        (MandatoInicio, MandatoFim) = (s.MandatoInicio?.ToDateTime(TimeOnly.MinValue), s.MandatoFim?.ToDateTime(TimeOnly.MinValue));
        (AssinaLivros, AssinaturaDigital, Cadastrado) = (s.AssinaLivros, s.AssinaturaDigital, true);
    });

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var s = new Sindico(Nome, Cpf, Email, Telefone, MandatoInicio is { } i ? DateOnly.FromDateTime(i) : null,
                            MandatoFim is { } f ? DateOnly.FromDateTime(f) : null, AssinaLivros, AssinaturaDigital);
        var r = await app.Responsaveis.SalvarSindicoAsync(empresa, s, usuario);
        MostrarResultado(r, "Síndico gravado.");
        if (r.Sucesso) Cadastrado = true;
    }, Permissao.EmpresaResponsaveisManter);

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Responsaveis.ExcluirSindicoAsync(empresa, usuario);
        if (r.Sucesso) Limpar();
        MostrarResultado(r, "Síndico excluído.");
    }, Permissao.EmpresaResponsaveisManter);
}
