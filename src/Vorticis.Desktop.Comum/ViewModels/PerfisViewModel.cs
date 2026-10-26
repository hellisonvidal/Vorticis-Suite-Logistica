using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Seguranca;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Desktop.Comum.ViewModels;

public partial class FuncaoMarcavel(DefinicaoPermissao definicao) : ObservableObject
{
    public DefinicaoPermissao Definicao { get; } = definicao;
    public string Descricao => Definicao.Descricao;
    [ObservableProperty] private bool _marcada;
}

public sealed record GrupoFuncoes(string Nome, IReadOnlyList<FuncaoMarcavel> Funcoes);

public sealed record PerfilLinha(PerfilAcesso Perfil)
{
    public string Numero => $"Nº {Perfil.Id:0000}";
    public string Nome => Perfil.Nome;
    public string Resumo => $"{Perfil.Permissoes.Count} função(ões) · {Perfil.Usuarios} usuário(s){(Perfil.Padrao ? " · padrão" : "")}";
}

/// <summary>Configurações › Perfis de acesso: o administrador define, função a função, o que cada perfil libera.</summary>
public partial class PerfisViewModel : CadastroViewModel
{
    private readonly IReadOnlyList<FuncaoMarcavel> _funcoes;

    public PerfisViewModel(IContexto contexto) : base(contexto)
    {
        _funcoes = Permissoes.Catalogo.Select(d => new FuncaoMarcavel(d)).ToArray();
        Grupos = _funcoes.GroupBy(f => f.Definicao.Grupo).Select(g => new GrupoFuncoes(g.Key, g.ToArray())).ToArray();
    }

    public ObservableCollection<PerfilLinha> Lista { get; } = [];

    protected override string NomeRegistro => "perfil";
    protected override string? DescricaoSelecionado => Selecionado is { } p ? $"o perfil {p.Nome}" : null;
    public override bool PodeManter => Contexto.Sessao?.Administrador == true;
    public IReadOnlyList<GrupoFuncoes> Grupos { get; }

    [ObservableProperty] private PerfilLinha? _selecionado;
    [ObservableProperty] private long? _editandoId;
    [ObservableProperty] private string _nome = "";
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private string _situacao = "Novo perfil";

    /// <summary>Primeiro uso: a definição dos perfis é obrigatória; as demais funções ficam bloqueadas até concluir.</summary>
    [ObservableProperty] private bool _configuracaoInicial;

    /// <summary>Perfil Administrador: todas as funções, sempre (as marcações ficam travadas).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PodeMarcar))] private bool _editandoAdministrador;

    /// <summary>Funções podem ser marcadas em inclusão/alteração, exceto no perfil Administrador (sempre todas).</summary>
    public bool PodeMarcar => EmEdicao && !EditandoAdministrador;

    protected override void AoMudarModo() => OnPropertyChanged(nameof(PodeMarcar));

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao) return;
        var id = EditandoId;
        var geracao = NovaGeracao();
        var perfis = await app.Perfis.ListarAsync(sessao);
        if (!EhAtual(geracao)) return;
        Lista.Clear();
        foreach (var p in perfis) Lista.Add(new PerfilLinha(p));
        Selecionado = Lista.FirstOrDefault(l => l.Perfil.Id == id) ?? (id is null ? null : Lista.FirstOrDefault());
        AtualizarBarra();
    }

    partial void OnSelecionadoChanged(PerfilLinha? value)
    {
        if (EmConsulta) MostrarSelecionado();
        AtualizarBarra();
    }

    protected override void MostrarSelecionado()
    {
        if (Selecionado is not { } value) { LimparFormulario(); return; }
        var p = value.Perfil;
        (EditandoId, Nome, Descricao) = (p.Id, p.Nome, p.Descricao ?? "");
        foreach (var f in _funcoes) f.Marcada = p.Permissoes.Contains(f.Definicao.Permissao);
        EditandoAdministrador = p.Administrador;
        Situacao = p.Administrador ? "Perfil Administrador: libera todas as funções e a administração do sistema (não pode ser excluído)."
                 : p.Padrao ? "Perfil padrão do sistema: pode ser editado, mas não excluído."
                 : p.Usuarios > 0 ? $"Atribuído a {p.Usuarios} usuário(s). Alterações valem a partir da próxima ação deles."
                 : "Não atribuído a nenhum usuário.";
        Erros.Clear();
        Mensagem = null;
    }

    protected override void PrepararInclusao() => LimparFormulario();

    private void LimparFormulario()
    {
        (EditandoId, Nome, Descricao, Situacao, EditandoAdministrador) = (null, "", "", "Novo perfil", false);
        foreach (var f in _funcoes) f.Marcada = false;
    }

    /// <summary>Inclui um perfil novo a partir do selecionado (mesmas funções).</summary>
    [RelayCommand]
    private void Duplicar()
    {
        if (Selecionado is null || EmEdicao) return;
        Erros.Clear();
        Mensagem = null;
        Modo = ModoCadastro.Inclusao;
        (EditandoId, Nome, Situacao, EditandoAdministrador) = (null, $"{Nome} (cópia)", "Cópia ainda não salva", false);
    }

    [RelayCommand]
    private void MarcarTodas(bool marcar)
    {
        foreach (var f in _funcoes) f.Marcada = marcar;
    }

    protected override async Task<bool> GravarAsync(bool inclusao)
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao) return false;
        var r = await app.Perfis.SalvarAsync(sessao, inclusao ? null : EditandoId, Nome, Descricao,
                                             _funcoes.Where(f => f.Marcada).Select(f => f.Definicao.Permissao));
        if (!r.Sucesso) { MostrarResultado(r, ""); return false; }
        EditandoId = r.Valor;
        Modo = ModoCadastro.Consulta;
        await CarregarAsync();
        Selecionado = Lista.FirstOrDefault(l => l.Perfil.Id == r.Valor);
        Mensagem = $"Perfil \"{Nome.Trim()}\" salvo.";
        return true;
    }

    protected override async Task<bool> ExcluirSelecionadoAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Selecionado is not { } linha) return false;
        var r = await app.Perfis.ExcluirAsync(sessao, linha.Perfil.Id);
        if (!r.Sucesso) { MostrarResultado(r, ""); return false; }
        Selecionado = null;
        LimparFormulario();
        await CarregarAsync();
        Mensagem = "Perfil excluído.";
        return true;
    }

    [RelayCommand]
    private async Task ConcluirConfiguracaoInicialAsync()
    {
        Erros.Clear();
        Mensagem = null;
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao) return;
        var r = await app.Perfis.ConcluirConfiguracaoInicialAsync(sessao);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        ConfiguracaoInicial = false;
        Contexto.AoConcluirConfiguracaoInicial();
    }
}
