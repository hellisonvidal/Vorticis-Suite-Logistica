using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vorticis.Desktop.Comum.ViewModels;

public enum ModoCadastro { Consulta, Inclusao, Alteracao }

/// <summary>
/// Padrão dos cadastros: barra com Incluir, Alterar, Excluir, Salvar e Cancelar.
/// Em consulta o formulário mostra o registro selecionado (somente leitura); Incluir limpa o formulário para um
/// registro novo; Alterar libera a edição do selecionado; Excluir pede confirmação.
/// </summary>
public abstract partial class CadastroViewModel(IContexto contexto) : ModuloViewModel(contexto), IAlteracoesPendentes
{
    public bool TemAlteracoesPendentes => EmEdicao;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmEdicao), nameof(EmInclusao), nameof(EmAlteracao), nameof(EmConsulta), nameof(SituacaoCadastro))]
    private ModoCadastro _modo;

    public bool EmEdicao => Modo != ModoCadastro.Consulta;
    public bool EmInclusao => Modo == ModoCadastro.Inclusao;
    public bool EmAlteracao => Modo == ModoCadastro.Alteracao;
    public bool EmConsulta => Modo == ModoCadastro.Consulta;

    /// <summary>Nome do tipo de registro, no singular e com artigo implícito (ex.: "empresa", "sócio").</summary>
    protected abstract string NomeRegistro { get; }

    /// <summary>Descrição do registro selecionado para as mensagens (null se não houver seleção).</summary>
    protected abstract string? DescricaoSelecionado { get; }

    /// <summary>O usuário pode incluir/alterar/excluir neste cadastro (permissão do perfil).</summary>
    public virtual bool PodeManter => true;

    public bool TemRegistroSelecionado => DescricaoSelecionado is not null;

    public string SituacaoCadastro => Modo switch
    {
        ModoCadastro.Inclusao => $"Incluindo: preencha os dados e clique em Salvar.",
        ModoCadastro.Alteracao => $"Alterando {DescricaoSelecionado}: clique em Salvar para gravar ou Cancelar para desistir.",
        _ => DescricaoSelecionado is null ? $"Selecione um registro ou clique em Incluir." : "",
    };

    protected abstract void PrepararInclusao();

    /// <summary>Recarrega o formulário com o registro selecionado (ao cancelar ou após salvar).</summary>
    protected abstract void MostrarSelecionado();

    /// <returns>True se gravou (o cadastro volta para consulta).</returns>
    protected abstract Task<bool> GravarAsync(bool inclusao);

    /// <returns>True se excluiu.</returns>
    protected abstract Task<bool> ExcluirSelecionadoAsync();

    /// <summary>Mensagem extra na confirmação de exclusão (ex.: o que acontece com os dados).</summary>
    protected virtual string DetalheExclusao => "";

    private bool PodeIncluir() => !EmEdicao && PodeManter;
    private bool PodeAlterarOuExcluir() => !EmEdicao && PodeManter && TemRegistroSelecionado;

    [RelayCommand(CanExecute = nameof(PodeIncluir))]
    private void Incluir()
    {
        Erros.Clear();
        Mensagem = null;
        PrepararInclusao();
        Modo = ModoCadastro.Inclusao;
    }

    [RelayCommand(CanExecute = nameof(PodeAlterarOuExcluir))]
    private void Alterar()
    {
        Erros.Clear();
        Mensagem = null;
        Modo = ModoCadastro.Alteracao;
    }

    [RelayCommand(CanExecute = nameof(PodeAlterarOuExcluir))]
    private async Task ExcluirAsync()
    {
        Erros.Clear();
        Mensagem = null;
        var descricao = DescricaoSelecionado;
        if (descricao is null) return;
        if (!await Contexto.ConfirmarAsync($"Excluir {NomeRegistro}", $"Confirma a exclusão de {descricao}?{DetalheExclusao}", "Excluir", "Cancelar"))
            return;
        if (await ExcluirSelecionadoAsync()) Modo = ModoCadastro.Consulta;
    }

    [RelayCommand(CanExecute = nameof(EmEdicao))]
    private async Task SalvarAsync()
    {
        Erros.Clear();
        Mensagem = null;
        if (await GravarAsync(Modo == ModoCadastro.Inclusao)) Modo = ModoCadastro.Consulta;
    }

    [RelayCommand(CanExecute = nameof(EmEdicao))]
    private void Cancelar()
    {
        Erros.Clear();
        Mensagem = null;
        Modo = ModoCadastro.Consulta;
        MostrarSelecionado();
    }

    partial void OnModoChanged(ModoCadastro value)
    {
        AtualizarBarra();
        AoMudarModo();
    }

    protected virtual void AoMudarModo() { }

    /// <summary>Chamado pela janela principal quando o usuário, a empresa ou as permissões mudam.</summary>
    public void AtualizarBarraCadastro() => AtualizarBarra();

    /// <summary>Chamar quando a seleção ou a permissão mudar.</summary>
    protected void AtualizarBarra()
    {
        OnPropertyChanged(nameof(TemRegistroSelecionado));
        OnPropertyChanged(nameof(SituacaoCadastro));
        OnPropertyChanged(nameof(PodeManter));
        IncluirCommand.NotifyCanExecuteChanged();
        AlterarCommand.NotifyCanExecuteChanged();
        ExcluirCommand.NotifyCanExecuteChanged();
        SalvarCommand.NotifyCanExecuteChanged();
        CancelarCommand.NotifyCanExecuteChanged();
    }
}
