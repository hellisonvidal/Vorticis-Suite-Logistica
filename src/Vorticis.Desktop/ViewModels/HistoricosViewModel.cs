using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Vorticis.Application.Contabil;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Desktop.ViewModels;

public sealed record HistoricoLinha(HistoricoPadrao Historico)
{
    public string Codigo => Historico.Codigo;
    public string Descricao => Historico.Descricao;
    public string Exemplo => $"{Historico.Descricao} …";
}

/// <summary>
/// Cadastros › Históricos (Incluir / Alterar / Excluir). O texto cadastrado é a parte fixa; no lançamento, informar o
/// código preenche esse texto e o restante é digitado livremente (ex.: "Pagamento NF " + "1234 - Fornecedor X").
/// O texto aceita variáveis como {DOCUMENTO} e {COMPETENCIA}, trocadas no lançamento pelos dados da linha.
/// </summary>
public partial class HistoricosViewModel(IContexto contexto) : CadastroViewModel(contexto)
{
    public ObservableCollection<HistoricoLinha> Lista { get; } = [];
    /// <summary>Variáveis de preenchimento automático ({DOCUMENTO}, {DATA}...) que podem entrar no texto do histórico.</summary>
    public IReadOnlyList<VariavelHistorico> Variaveis => VariaveisHistorico.Todas;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TemSelecionado))] private HistoricoLinha? _selecionado;
    [ObservableProperty] private string _codigo = "";
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private string _filtro = "";
    private IReadOnlyList<HistoricoPadrao> _todos = [];

    public bool TemSelecionado => Selecionado is not null;
    public bool PodeEditar => Contexto.Pode(Permissao.LancamentoIncluir);
    public override bool PodeManter => PodeEditar;
    protected override string NomeRegistro => "histórico";
    protected override string? DescricaoSelecionado => Selecionado is { } s ? $"{s.Codigo} – {s.Descricao}" : null;

    public void AtualizarPermissoes()
    {
        OnPropertyChanged(nameof(PodeEditar));
        AtualizarBarra();
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var lista = await app.Historicos.ListarAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        _todos = lista;
        Filtrar();
    }

    partial void OnFiltroChanged(string value) => Filtrar();

    private void Filtrar()
    {
        var id = Selecionado?.Historico.Id;
        var f = Filtro.Trim();
        Lista.Clear();
        foreach (var h in _todos.Where(h => f.Length == 0 || h.Codigo == f || h.Descricao.Contains(f, StringComparison.CurrentCultureIgnoreCase)))
            Lista.Add(new HistoricoLinha(h));
        Selecionado = Lista.FirstOrDefault(l => l.Historico.Id == id);
    }

    partial void OnSelecionadoChanged(HistoricoLinha? value)
    {
        if (EmConsulta) MostrarSelecionado();
        AtualizarBarra();
    }

    protected override void MostrarSelecionado() =>
        (Codigo, Descricao) = Selecionado is { } s ? (s.Codigo, s.Descricao) : ("", "");

    protected override void PrepararInclusao() => (Codigo, Descricao) = (ServicoHistoricos.ProximoCodigo(_todos), "");

    protected override async Task<bool> GravarAsync(bool inclusao)
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            long id;
            if (inclusao)
            {
                var r = await app.Historicos.IncluirAsync(empresa, Codigo, Descricao, usuario);
                if (!r.Sucesso) { MostrarResultado(r, ""); return; }
                id = r.Valor;
            }
            else
            {
                if (Selecionado is null) { Erros.Add("Selecione um histórico."); return; }
                id = Selecionado.Historico.Id;
                var r = await app.Historicos.AlterarAsync(empresa, id, Descricao, usuario);
                if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            }
            ok = true;
            Modo = ModoCadastro.Consulta;
            await CarregarAsync();
            Selecionado = Lista.FirstOrDefault(l => l.Historico.Id == id);
            Mensagem = inclusao ? $"Histórico {Selecionado?.Codigo} incluído." : "Histórico alterado.";
        }, Permissao.LancamentoIncluir);
        return ok;
    }

    protected override async Task<bool> ExcluirSelecionadoAsync()
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            if (Selecionado is null) return;
            var r = await app.Historicos.ExcluirAsync(empresa, Selecionado.Historico.Id, usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            ok = true;
            Selecionado = null;
            await CarregarAsync();
            Mensagem = "Histórico excluído.";
        }, Permissao.LancamentoIncluir);
        return ok;
    }
}
