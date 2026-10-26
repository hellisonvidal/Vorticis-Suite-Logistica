using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra;

namespace Vorticis.Desktop.Comum.ViewModels;

/// <summary>Base dos módulos: acesso ao contexto e exibição padronizada de mensagens e erros.</summary>
public abstract partial class ModuloViewModel(IContexto contexto) : ObservableObject
{
    protected IContexto Contexto { get; } = contexto;

    public ObservableCollection<string> Erros { get; } = [];

    private int _geracao;

    [ObservableProperty] private string? _mensagem;

    /// <summary>
    /// Carregamentos podem se sobrepor (abrir a tela, trocar de empresa). Cada carga pega uma geração; ao terminar,
    /// só aplica o resultado se ainda for a mais recente — e as listas são trocadas de uma vez, sem await no meio,
    /// para não duplicar nem misturar dados de empresas diferentes.
    /// </summary>
    protected int NovaGeracao() => Interlocked.Increment(ref _geracao);
    protected bool EhAtual(int geracao) => geracao == Volatile.Read(ref _geracao);
    [ObservableProperty] private bool _ocupado;

    protected static DateOnly Data(DateTime? d, DateOnly padrao) => d is { } v ? DateOnly.FromDateTime(v) : padrao;

    protected void MostrarResultado(Resultado r, string sucesso)
    {
        Erros.Clear();
        foreach (var e in r.Erros) Erros.Add(e);
        Mensagem = r.Sucesso ? sucesso : null;
    }

    /// <summary>
    /// Executa uma ação que exige usuário logado e empresa selecionada, convertendo exceções em mensagem.
    /// A permissão informada é conferida aqui só para responder rápido; os serviços verificam de novo.
    /// </summary>
    protected async Task ExecutarAsync(Func<VorticisApp, long, long, Task> acao, Permissao? exige = null)
    {
        Erros.Clear();
        Mensagem = null;
        if (Contexto.App is not { } app) { Erros.Add("Sem conexão com o banco de dados."); return; }
        if (Contexto.Sessao is not { } sessao) { Erros.Add("Sessão encerrada. Entre novamente."); return; }
        if (Contexto.EmpresaId is not { } empresa) { Erros.Add("Selecione uma empresa."); return; }
        if (exige is { } p && !Contexto.Pode(p)) { Erros.Add($"Seu perfil nesta empresa não permite: {p.Descricao()}."); return; }
        try
        {
            Ocupado = true;
            await acao(app, empresa, sessao.Id);
        }
        catch (Exception ex)
        {
            Erros.Add(ex.Message);
        }
        finally
        {
            Ocupado = false;
        }
    }
}
