using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Contabil;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Desktop.ViewModels;

public sealed record TabelaLinha(TabelaReferencialImportada Tabela)
{
    public string Plano => $"{Tabela.CodPlanRef} - {TipoPlanoReferencial.Todos.FirstOrDefault(t => t.Codigo == Tabela.CodPlanRef)?.Descricao}";
    public int Ano => Tabela.Ano;
    public int Contas => Tabela.Contas;
    public string Arquivo => Tabela.Arquivo;
    public string Importado => Tabela.ImportadoEm.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
}

/// <summary>Configurações › Plano referencial da RFB: importação das tabelas oficiais (somente administrador).</summary>
public partial class ImportacaoReferencialViewModel(MainViewModelBase main) : ModuloViewModel(main)
{
    public static IReadOnlyList<TipoPlanoReferencial> Tipos => TipoPlanoReferencial.Todos;
    public ObservableCollection<TabelaLinha> Tabelas { get; } = [];

    [ObservableProperty] private TipoPlanoReferencial _tipo = TipoPlanoReferencial.Todos[0];
    [ObservableProperty] private decimal? _ano = DateTime.Today.Year;
    [ObservableProperty] private string _arquivo = "";

    public async Task CarregarAsync()
    {
        if (main.App is not { } app) return;
        var g = NovaGeracao();
        var lista = await app.PlanoReferencial.ListarTabelasAsync();
        if (!EhAtual(g)) return;
        Tabelas.Clear();
        foreach (var t in lista) Tabelas.Add(new TabelaLinha(t));
    }

    [RelayCommand]
    private async Task ImportarAsync()
    {
        Erros.Clear();
        Mensagem = null;
        if (main.App is not { } app || main.Sessao is not { } sessao) return;
        if (!File.Exists(Arquivo)) { Erros.Add("Selecione o arquivo da tabela."); return; }
        try
        {
            Ocupado = true;
            var r = await app.PlanoReferencial.ImportarAsync(sessao, Tipo.Codigo, (int)(Ano ?? DateTime.Today.Year),
                                                             await File.ReadAllBytesAsync(Arquivo), Arquivo);
            MostrarResultado(r, r.Sucesso ? r.Valor : "");
            await CarregarAsync();
        }
        catch (IOException ex) { Erros.Add(ex.Message); }
        finally { Ocupado = false; }
    }
}

public partial class VinculoLinha(ContaMapeamento conta) : ObservableObject
{
    public ContaMapeamento Conta { get; } = conta;
    public string Codigo => Conta.Codigo;
    public string Descricao => Conta.Descricao;
    public string Natureza => Conta.CodNat;
    public string Movimento => Conta.TemMovimentoNoAno ? "sim" : "";
    public string Referencial => Conta.CodigoReferencial is null ? "" : $"{Conta.CodigoReferencial} {Conta.DescricaoReferencial}";
    public bool Pendente => Conta.TemMovimentoNoAno && Conta.CodigoReferencial is null;
    [ObservableProperty] private ContaReferencial? _sugestao;
    public string SugestaoTexto => Sugestao is null ? "" : $"{Sugestao.Codigo} {Sugestao.Descricao}";
    partial void OnSugestaoChanged(ContaReferencial? value) => OnPropertyChanged(nameof(SugestaoTexto));
}

/// <summary>Integrações › Importar › Vínculo ao plano referencial (ECD I051).</summary>
public partial class MapeamentoReferencialViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    private IReadOnlyList<ContaMapeamento> _contas = [];
    private IReadOnlyList<ContaReferencial> _referenciais = [];

    public ObservableCollection<VinculoLinha> Linhas { get; } = [];
    public ObservableCollection<ContaReferencial> Opcoes { get; } = [];
    public ObservableCollection<TipoPlanoReferencial> PlanosDisponiveis { get; } = [];

    [ObservableProperty] private decimal? _ano = DateTime.Today.Year;
    [ObservableProperty] private TipoPlanoReferencial? _plano;
    [ObservableProperty] private string _planoAtual = "";
    [ObservableProperty] private bool _somentePendentes;
    [ObservableProperty] private string _filtroReferencial = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TemSelecionada))] private VinculoLinha? _selecionada;
    [ObservableProperty] private ContaReferencial? _referencialEscolhida;
    [ObservableProperty] private string _resumo = "";

    public bool TemSelecionada => Selecionada is not null;
    public bool PodeEditar => Contexto.Pode(Permissao.PlanoReferencialMapear);
    private int AnoInt => (int)(Ano ?? DateTime.Today.Year);

    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeEditar));

    [RelayCommand]
    private Task RecarregarAsync() => CarregarAsync();

    public async Task CarregarAsync()
    {
        OnPropertyChanged(nameof(PodeEditar));
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var tabelas = await app.PlanoReferencial.ListarTabelasAsync();
        var dados = await app.PlanoReferencial.CarregarAsync(empresa, AnoInt, sessao.Id);
        var referenciais = dados.Sucesso && dados.Valor.Plano is { } p ? await app.PlanoReferencial.ListarContasAsync(p, AnoInt) : [];
        if (!EhAtual(g)) return;
        if (!dados.Sucesso) { MostrarResultado(dados, ""); return; }

        PlanosDisponiveis.Clear();
        foreach (var t in TipoPlanoReferencial.Todos.Where(t => tabelas.Any(x => x.Ano == AnoInt && x.CodPlanRef == t.Codigo))) PlanosDisponiveis.Add(t);
        Plano = PlanosDisponiveis.FirstOrDefault(t => t.Codigo == dados.Valor.Plano) ?? PlanosDisponiveis.FirstOrDefault();
        PlanoAtual = dados.Valor.Plano is null
            ? (PlanosDisponiveis.Count == 0 ? $"Nenhuma tabela referencial de {AnoInt} importada (Configurações › Plano referencial da RFB)." : "Escolha o plano e clique em \"Usar este plano\".")
            : $"Plano do exercício: {TipoPlanoReferencial.Todos.First(t => t.Codigo == dados.Valor.Plano).Descricao}";
        _contas = dados.Valor.Contas;
        _referenciais = referenciais;
        Filtrar();
    }

    partial void OnSomentePendentesChanged(bool value) => Filtrar();
    partial void OnFiltroReferencialChanged(string value) => FiltrarOpcoes();
    partial void OnSelecionadaChanged(VinculoLinha? value) => FiltrarOpcoes();

    private void Filtrar()
    {
        var sugestoes = Linhas.Where(l => l.Sugestao is not null).ToDictionary(l => l.Conta.ContaId, l => l.Sugestao);
        Linhas.Clear();
        foreach (var c in _contas.Where(c => !SomentePendentes || (c.TemMovimentoNoAno && c.CodigoReferencial is null)))
            Linhas.Add(new VinculoLinha(c) { Sugestao = sugestoes.GetValueOrDefault(c.ContaId) });
        var pendentes = _contas.Count(c => c.TemMovimentoNoAno && c.CodigoReferencial is null);
        var vinculadas = _contas.Count(c => c.CodigoReferencial is not null);
        Resumo = $"{vinculadas} de {_contas.Count} contas analíticas vinculadas · " +
                 (pendentes == 0 ? "✓ nenhuma pendência para a ECD" : $"{pendentes} conta(s) com movimento sem vínculo (impedem a ECD)");
    }

    private void FiltrarOpcoes()
    {
        Opcoes.Clear();
        if (Selecionada is null) return;
        var f = LeitorPlanoReferencial.Normalizar(FiltroReferencial);
        foreach (var r in _referenciais.Where(r => r.Analitica && r.CodNat == Selecionada.Conta.CodNat
                     && (f.Length == 0 || r.Codigo.StartsWith(FiltroReferencial.Trim(), StringComparison.Ordinal)
                         || LeitorPlanoReferencial.Normalizar(r.Descricao).Contains(f))).Take(300))
            Opcoes.Add(r);
        ReferencialEscolhida = Opcoes.FirstOrDefault(o => o.Codigo == (Selecionada.Conta.CodigoReferencial ?? Selecionada.Sugestao?.Codigo));
    }

    [RelayCommand]
    private Task UsarPlanoAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Plano is null) { Erros.Add("Escolha o plano referencial."); return; }
        var r = await app.PlanoReferencial.DefinirPlanoAsync(empresa, AnoInt, Plano.Codigo, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = "Plano referencial do exercício definido.";
    }, Permissao.PlanoReferencialMapear);

    [RelayCommand]
    private Task VincularAsync() => Gravar(Selecionada is null || ReferencialEscolhida is null ? null
        : new Dictionary<long, string?> { [Selecionada.Conta.ContaId] = ReferencialEscolhida.Codigo }, "Vínculo gravado.");

    [RelayCommand]
    private Task RemoverVinculoAsync() => Gravar(Selecionada is null ? null
        : new Dictionary<long, string?> { [Selecionada.Conta.ContaId] = null }, "Vínculo removido.");

    [RelayCommand]
    private Task SugerirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var s = await app.PlanoReferencial.SugerirAsync(empresa, AnoInt, usuario);
        foreach (var l in Linhas) l.Sugestao = s.TryGetValue(l.Conta.ContaId, out var x) ? x.Conta : null;
        Mensagem = s.Count == 0 ? "Nenhuma sugestão encontrada." : $"{s.Count} sugestão(ões). Revise e clique em \"Aceitar sugestões\".";
    });

    [RelayCommand]
    private Task AceitarSugestoesAsync() => Gravar(
        Linhas.Where(l => l.Sugestao is not null).ToDictionary(l => l.Conta.ContaId, l => (string?)l.Sugestao!.Codigo), "Sugestões aceitas.");

    [RelayCommand]
    private Task CopiarAnoAnteriorAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.PlanoReferencial.CopiarDoAnoAnteriorAsync(empresa, AnoInt, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = $"{r.Valor} vínculo(s) copiados de {AnoInt - 1}.";
    }, Permissao.PlanoReferencialMapear);

    private Task Gravar(Dictionary<long, string?>? vinculos, string sucesso) => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (vinculos is null || vinculos.Count == 0) { Erros.Add("Nada a gravar: selecione a conta e a referencial."); return; }
        var r = await app.PlanoReferencial.VincularAsync(empresa, AnoInt, vinculos, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var id = Selecionada?.Conta.ContaId;
        await CarregarAsync();
        foreach (var l in Linhas.Where(l => vinculos.ContainsKey(l.Conta.ContaId))) l.Sugestao = null;
        Selecionada = Linhas.FirstOrDefault(l => l.Conta.ContaId == id);
        Mensagem = sucesso;
    }, Permissao.PlanoReferencialMapear);
}
