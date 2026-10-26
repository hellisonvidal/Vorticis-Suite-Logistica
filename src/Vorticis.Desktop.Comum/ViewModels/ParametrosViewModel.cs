using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra;

namespace Vorticis.Desktop.Comum.ViewModels;

public enum TipoCampoParametro { SimNao, Texto, Data, Escolha }

public sealed record OpcaoParametro(string Valor, string Texto);

/// <summary>Um parâmetro da tela Cadastros › Parâmetros: o tipo define qual propriedade vale (Marcado, Texto, Data ou Escolha).</summary>
public sealed partial class CampoParametro(string chave, string rotulo, TipoCampoParametro tipo, string? ajuda = null,
                                           IReadOnlyList<OpcaoParametro>? opcoes = null) : ObservableObject
{
    public string Chave { get; } = chave;
    public string Rotulo { get; } = rotulo;
    public TipoCampoParametro Tipo { get; } = tipo;
    public string? Ajuda { get; } = ajuda;
    public IReadOnlyList<OpcaoParametro> Opcoes { get; } = opcoes ?? [];

    public bool EhSimNao => Tipo == TipoCampoParametro.SimNao;
    public bool EhTexto => Tipo == TipoCampoParametro.Texto;
    public bool EhData => Tipo == TipoCampoParametro.Data;
    public bool EhEscolha => Tipo == TipoCampoParametro.Escolha;
    public bool TemAjuda => !string.IsNullOrWhiteSpace(Ajuda);

    [ObservableProperty] private bool _marcado;
    [ObservableProperty] private string _texto = "";
    [ObservableProperty] private DateTime? _data;
    [ObservableProperty] private OpcaoParametro? _escolha;

    /// <summary>Valor escolhido na lista (null se nada escolhido).</summary>
    public string? ValorEscolha => Escolha?.Valor;

    public void DefinirEscolha(string? valor) => Escolha = Opcoes.FirstOrDefault(o => o.Valor == valor) ?? Opcoes.FirstOrDefault();
}

/// <summary>Atalho para a tela própria de uma configuração complexa (em vez de duplicar os campos).</summary>
public sealed class AtalhoParametro(string texto, Action abrir)
{
    public string Texto { get; } = texto;
    public ICommand Abrir { get; } = new RelayCommand(abrir);
}

/// <summary>Grupo de parâmetros com um texto curto de ajuda.</summary>
public sealed class GrupoParametros(string titulo, string ajuda, IReadOnlyList<CampoParametro> campos, IReadOnlyList<AtalhoParametro>? atalhos = null)
{
    public string Titulo { get; } = titulo;
    public string Ajuda { get; } = ajuda;
    public IReadOnlyList<CampoParametro> Campos { get; } = campos;
    public IReadOnlyList<AtalhoParametro> Atalhos { get; } = atalhos ?? [];
    public bool TemAtalhos => Atalhos.Count > 0;
    public bool TemCampos => Campos.Count > 0;
}

/// <summary>
/// Cadastros › Parâmetros: parâmetros do módulo para a empresa ativa. Cada módulo deriva esta classe e informa os grupos
/// (<see cref="MontarGrupos"/>), como carregar (<see cref="CarregarValoresAsync"/>) e gravar (<see cref="GravarValoresAsync"/>);
/// a gravação reaproveita os serviços de configuração que já existem, que conferem a permissão.
/// </summary>
public abstract partial class ParametrosViewModel : ModuloViewModel, IAlteracoesPendentes
{
    private bool _carregando;

    protected ParametrosViewModel(IContexto contexto) : base(contexto) => Grupos = MontarGrupos();

    public IReadOnlyList<GrupoParametros> Grupos { get; }

    /// <summary>Título da tela (ex.: "Parâmetros do Contábil").</summary>
    public abstract string Titulo { get; }

    /// <summary>Texto de abertura da tela.</summary>
    public abstract string Descricao { get; }

    /// <summary>Permissão exigida para gravar (conferida de novo no serviço).</summary>
    protected abstract Permissao PermissaoGravar { get; }

    public bool PodeGravar => Contexto.Pode(PermissaoGravar);

    [ObservableProperty] private bool _alterado;

    public bool TemAlteracoesPendentes => Alterado;

    protected abstract IReadOnlyList<GrupoParametros> MontarGrupos();

    protected abstract Task CarregarValoresAsync(VorticisApp app, long empresa, long usuario);

    /// <summary>Grava os valores; erros vão para <see cref="ModuloViewModel.Erros"/> e a gravação segue nos demais grupos.</summary>
    protected abstract Task GravarValoresAsync(VorticisApp app, long empresa, long usuario);

    protected CampoParametro Campo(string chave) => Grupos.SelectMany(g => g.Campos).First(c => c.Chave == chave);

    protected AtalhoParametro Atalho(string texto, string tela) => new(texto, () => Contexto.AbrirTela(tela));

    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeGravar));

    protected void Observar(IEnumerable<CampoParametro> campos)
    {
        foreach (var c in campos) c.PropertyChanged += AoMudarCampo;
    }

    private void AoMudarCampo(object? sender, PropertyChangedEventArgs e)
    {
        if (!_carregando) Alterado = true;
    }

    public async Task CarregarAsync()
    {
        OnPropertyChanged(nameof(PodeGravar));
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var geracao = NovaGeracao();
        Erros.Clear();
        try
        {
            _carregando = true;
            await CarregarValoresAsync(app, empresa, sessao.Id);
            if (!EhAtual(geracao)) return;
            Alterado = false;
        }
        catch (Exception ex) { Erros.Add(ex.Message); }
        finally { _carregando = false; }
    }

    [RelayCommand]
    private async Task SalvarAsync()
    {
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            await GravarValoresAsync(app, empresa, usuario);
            if (Erros.Count == 0) { Mensagem = "Parâmetros gravados."; Alterado = false; }
        }, PermissaoGravar);
    }

    [RelayCommand]
    private Task DescartarAsync() => CarregarAsync();
}
