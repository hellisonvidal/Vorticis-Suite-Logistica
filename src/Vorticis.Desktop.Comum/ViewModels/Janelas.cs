using CommunityToolkit.Mvvm.ComponentModel;

namespace Vorticis.Desktop.Comum.ViewModels;

/// <summary>Tela que sabe se há digitação não salva; a tecla Esc pergunta antes de fechar a função.</summary>
public interface IAlteracoesPendentes
{
    bool TemAlteracoesPendentes { get; }
}

/// <summary>Função aberta em janela flutuante (uma janela por tela; reabrir a mesma função traz a janela para a frente).</summary>
public sealed partial class FuncaoAberta(object viewModel, string tela, string titulo) : ObservableObject
{
    public object ViewModel { get; } = viewModel;
    [ObservableProperty] private string _tela = tela;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Rotulo))] private string _titulo = titulo;

    /// <summary>Nome curto para a barra de janelas (último trecho do caminho do menu).</summary>
    public string Rotulo => Titulo.Split('›').Last().Trim();
    [ObservableProperty] private bool _ativa;
}

/// <summary>
/// Pergunta ao usuário (Sim/Não) exibida em diálogo modal; a resposta conclui <see cref="Resposta"/>.
/// Com <see cref="PedeSenha"/>, o diálogo tem um campo de senha (lida em <see cref="Senha"/> ao confirmar).
/// </summary>
/// <summary>Caixa de seleção de um diálogo de confirmação; a obrigatória fica marcada e não pode ser desmarcada.</summary>
public sealed class OpcaoConfirmacao(string texto, bool marcada, bool obrigatoria = false)
{
    public string Texto { get; } = texto;
    public bool Obrigatoria { get; } = obrigatoria;
    public bool Marcada { get; set; } = marcada || obrigatoria;
}

public sealed class PedidoConfirmacao(string titulo, string mensagem, string textoSim, string textoNao, bool pedeSenha = false)
{
    public bool PedeSenha { get; } = pedeSenha;
    public string Senha { get; set; } = "";

    /// <summary>Caixas de seleção exibidas entre a mensagem e os botões (vazio na maioria dos diálogos).</summary>
    public IReadOnlyList<OpcaoConfirmacao> Opcoes { get; init; } = [];

    private readonly TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Titulo { get; } = titulo;
    public string Mensagem { get; } = mensagem;
    public string TextoSim { get; } = textoSim;
    public string TextoNao { get; } = textoNao;
    public Task<bool> Resposta => _tcs.Task;

    public void Responder(bool sim) => _tcs.TrySetResult(sim);
}
