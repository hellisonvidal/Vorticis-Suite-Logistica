using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Desktop.Comum;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Frota.Desktop.ViewModels;

public sealed record LayoutPedagioLinha(LayoutPedagio L)
{
    public string Nome => L.Nome;
    public string Operadora => L.Operadora ?? "";
    public string Colunas => string.Join(" · ", new[] { $"data: {L.ColunaData}", L.ColunaHora is { } h ? $"hora: {h}" : null, $"placa: {L.ColunaPlaca}",
                                                        L.ColunaPraca is { } p ? $"praça: {p}" : null, $"valor: {L.ColunaValor}" }.Where(x => x is not null));
    public string Cfop => L.Cfop;
    public string Situacao => L.Ativo ? "Ativo" : "Inativo";
}

/// <summary>
/// Operações › Pedágio › Layouts de arquivo: para cada operadora (Sem Parar, ConectCar, Veloe...), o nome das colunas do arquivo dela.
/// "Carregar arquivo de exemplo" lê o cabeçalho da planilha para escolher as colunas sem digitar.
/// </summary>
public partial class LayoutsPedagioViewModel(IContexto contexto) : FrotaBase(contexto, Permissao.FrotaOperacao)
{
    public ObservableCollection<LayoutPedagioLinha> Lista { get; } = [];
    public ObservableCollection<Opcao> Operadoras { get; } = [];
    /// <summary>Colunas do arquivo de exemplo (ou as do layout selecionado), oferecidas nos combos.</summary>
    public ObservableCollection<string> Colunas { get; } = [];
    [ObservableProperty] private LayoutPedagioLinha? _selecionado;
    [ObservableProperty] private string _nome = "";
    [ObservableProperty] private Opcao? _operadora;
    [ObservableProperty] private string? _colunaData;
    [ObservableProperty] private string? _colunaHora;
    [ObservableProperty] private string? _colunaPlaca;
    [ObservableProperty] private string? _colunaPraca;
    [ObservableProperty] private string? _colunaValor;
    [ObservableProperty] private string _cfop = "1949";
    [ObservableProperty] private bool _ativo = true;
    [ObservableProperty] private string _exemplo = "";

    private const string Nenhuma = "(nenhuma)";

    private void GarantirColunas(params string?[] nomes)
    {
        if (!Colunas.Contains(Nenhuma)) Colunas.Insert(0, Nenhuma);
        foreach (var n in nomes.Where(n => !string.IsNullOrWhiteSpace(n) && !Colunas.Contains(n!))) Colunas.Add(n!);
    }

    partial void OnSelecionadoChanged(LayoutPedagioLinha? value)
    {
        if (value?.L is not { } l) return;
        GarantirColunas(l.ColunaData, l.ColunaHora, l.ColunaPlaca, l.ColunaPraca, l.ColunaValor);
        (Nome, Cfop, Ativo) = (l.Nome, l.Cfop, l.Ativo);
        Operadora = Escolher(Operadoras, l.OperadoraId, () => l.Operadora ?? "");
        (ColunaData, ColunaHora, ColunaPlaca, ColunaPraca, ColunaValor) = (l.ColunaData, l.ColunaHora ?? Nenhuma, l.ColunaPlaca, l.ColunaPraca ?? Nenhuma, l.ColunaValor);
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        var participantes = await app.Frota().ParticipantesAsync(empresa, sessao.Id);
        var itens = await app.Frota().LayoutsPedagioAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        Operadoras.Clear();
        Operadoras.Add(new Opcao(null, "(escolha a operadora)"));
        foreach (var p in participantes) Operadoras.Add(new Opcao(p.Id, p.ToString()));
        Lista.Clear();
        foreach (var l in itens) Lista.Add(new LayoutPedagioLinha(l));
        Operadora ??= Operadoras.FirstOrDefault();
        GarantirColunas();
    }

    /// <summary>Lê o cabeçalho do arquivo de exemplo da operadora e sugere as colunas pelos nomes mais comuns.</summary>
    public void CarregarExemplo(string caminho)
    {
        Erros.Clear();
        Mensagem = null;
        var r = Vorticis.Infra.Importacao.LeitorPlanilha.Ler(caminho);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var t = r.Valor;
        Colunas.Clear();
        GarantirColunas([.. t.Colunas]);
        string? Sugerir(params string[] nomes) => t.Coluna(nomes) is var i and >= 0 ? t.Colunas[i] : null;
        ColunaData = Sugerir("data", "data_passagem", "data_hora", "dt_passagem", "data_transacao") ?? ColunaData;
        ColunaHora = Sugerir("hora", "hora_passagem") ?? Nenhuma;
        ColunaPlaca = Sugerir("placa", "placa_veiculo", "veiculo") ?? ColunaPlaca;
        ColunaPraca = Sugerir("praca", "praca_pedagio", "local", "estabelecimento", "descricao") ?? Nenhuma;
        ColunaValor = Sugerir("valor", "valor_passagem", "valor_r", "tarifa", "valor_cobrado") ?? ColunaValor;
        Exemplo = $"{System.IO.Path.GetFileName(caminho)}: {t.Linhas.Count} linha(s), colunas {string.Join(", ", t.Colunas)}.";
    }

    [RelayCommand]
    private void Novo()
    {
        Selecionado = null;
        (Nome, Cfop, Ativo, ColunaData, ColunaHora, ColunaPlaca, ColunaPraca, ColunaValor, Exemplo) = ("", "1949", true, null, Nenhuma, null, Nenhuma, null, "");
        Operadora = Operadoras.FirstOrDefault();
    }

    private static string? Col(string? c) => c is null || c == Nenhuma ? null : c;

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var l = new LayoutPedagio(Selecionado?.L.Id ?? 0, Nome, Operadora?.Id ?? 0, Col(ColunaData) ?? "", Col(ColunaHora), Col(ColunaPlaca) ?? "", Col(ColunaPraca),
                                  Col(ColunaValor) ?? "", Cfop, Ativo);
        var r = await app.Frota().GravarLayoutPedagioAsync(empresa, l, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Selecionado = Lista.FirstOrDefault(x => x.L.Id == r.Valor);
        Mensagem = "Layout gravado. Importe os arquivos em Operações › Pedágio › Passagens e notas.";
    }, Permissao.FrotaOperacao);

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado?.L is not { } l) { Erros.Add("Selecione o layout."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir layout", $"Excluir o layout {l.Nome}? As passagens já importadas continuam.")) return;
        var r = await app.Frota().ExcluirLayoutPedagioAsync(empresa, l.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Novo();
        await CarregarAsync();
        Mensagem = "Layout excluído.";
    }, Permissao.FrotaOperacao);
}

public sealed partial class PassagemLinha(PassagemPedagio p) : ObservableObject
{
    public PassagemPedagio P { get; } = p;
    [ObservableProperty] private bool _marcada;
    public string DataHora => P.DataHora.ToString("dd/MM/yyyy HH:mm");
    public string Placa => RegrasFrota.FormatarPlaca(P.Placa);
    public string Veiculo => P.Veiculo ?? "(placa não cadastrada)";
    public string Praca => P.Praca;
    public string Valor => Formato.Moeda(P.Valor);
    public string Nota => P.Nota is { } n ? $"NF {n}" : "";
}

/// <summary>
/// Operações › Pedágio › Passagens e notas: importa o arquivo da operadora pelo layout, mostra as passagens por veículo e gera a nota fiscal de
/// entrada (NF-e) da operadora com as passagens ainda sem nota no período.
/// </summary>
public partial class PedagioViewModel(IContexto contexto) : FrotaBase(contexto, Permissao.FrotaOperacao)
{
    public ObservableCollection<PassagemLinha> Lista { get; } = [];
    public ObservableCollection<Opcao> Layouts { get; } = [];
    [ObservableProperty] private Opcao? _layout;
    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
    [ObservableProperty] private DateTime? _fim = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddDays(-1);
    [ObservableProperty] private bool _soSemNota = true;
    [ObservableProperty] private string _totais = "";
    [ObservableProperty] private string _numeroNota = "";
    [ObservableProperty] private string _serieNota = "1";
    [ObservableProperty] private DateTime? _emissaoNota = DateTime.Today;
    [ObservableProperty] private string _chaveNota = "";

    private bool _montandoLayouts;

    partial void OnLayoutChanged(Opcao? value) { if (!_montandoLayouts) _ = CarregarAsync(); }
    partial void OnInicioChanged(DateTime? value) => _ = CarregarAsync();
    partial void OnFimChanged(DateTime? value) => _ = CarregarAsync();
    partial void OnSoSemNotaChanged(bool value) => _ = CarregarAsync();

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        var layouts = await app.Frota().LayoutsPedagioAsync(empresa, sessao.Id);
        var itens = await app.Frota().PassagensPedagioAsync(empresa, Ext.Dia(Inicio), Ext.Dia(Fim), Layout?.Id, SoSemNota, sessao.Id);
        if (!EhAtual(g)) return;
        var atual = Layout?.Id;
        if (Layouts.Count != layouts.Count(l => l.Ativo) + 1)
        {
            Layouts.Clear();
            Layouts.Add(new Opcao(null, "(todas as operadoras)"));
            foreach (var l in layouts.Where(l => l.Ativo)) Layouts.Add(new Opcao(l.Id, l.ToString()));
            _montandoLayouts = true;
            Layout = Layouts.FirstOrDefault(o => o.Id == atual) ?? Layouts[0];
            _montandoLayouts = false;
        }
        Lista.Clear();
        foreach (var p in itens) Lista.Add(new PassagemLinha(p));
        var semVeiculo = itens.Count(p => p.VeiculoId is null);
        Totais = $"{itens.Count} passagem(ns) · {itens.Select(p => p.Placa).Distinct().Count()} veículo(s) · total {Formato.Moeda(itens.Sum(p => p.Valor))}"
                 + (semVeiculo > 0 ? $" · {semVeiculo} com placa não cadastrada" : "") + $" · {itens.Count(p => p.DocumentoId is null)} sem nota";
    }

    /// <summary>Importa o arquivo escolhido pelo layout da operadora selecionada.</summary>
    public Task ImportarAsync(string caminho) => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Layout?.Id is not { } layout) { Erros.Add("Escolha o layout da operadora antes de importar."); return; }
        var t = Vorticis.Infra.Importacao.LeitorPlanilha.Ler(caminho);
        if (!t.Sucesso) { MostrarResultado(t, ""); return; }
        var r = await app.Frota().ImportarPedagioAsync(empresa, layout, t.Valor, System.IO.Path.GetFileName(caminho), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        foreach (var e in r.Valor.Erros.Take(10)) Erros.Add(e);
        if (r.Valor.Erros.Count > 10) Erros.Add($"... e mais {r.Valor.Erros.Count - 10} linha(s) recusada(s).");
        await CarregarAsync();
        Mensagem = $"{r.Valor.Importadas} passagem(ns) importada(s) ({Formato.Moeda(r.Valor.Valor)}); {r.Valor.Duplicadas} já estavam importadas"
                   + (r.Valor.SemVeiculo > 0 ? $"; {r.Valor.SemVeiculo} com placa que não está em Cadastros › Veículos" : "") + ".";
    }, Permissao.FrotaOperacao);

    [RelayCommand]
    private Task GerarNotaAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Layout?.Id is not { } layout) { Erros.Add("Escolha a operadora (layout) da nota."); return; }
        if (string.IsNullOrWhiteSpace(NumeroNota)) { Erros.Add("Informe o número da nota fiscal da operadora."); return; }
        var r = await app.Frota().GerarNotaPedagioAsync(empresa, layout, Ext.Dia(Inicio), Ext.Dia(Fim), NumeroNota.Trim(), SerieNota.Trim(), Ext.Dia(EmissaoNota),
                                                        ChaveNota, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        (NumeroNota, ChaveNota) = ("", "");
        await CarregarAsync();
        Mensagem = $"Nota fiscal de entrada gravada: {r.Valor.Passagens} passagem(ns), {Formato.Moeda(r.Valor.Valor)}. Ela está em Operações › Compras › Notas fiscais de entrada.";
    }, Permissao.FrotaOperacao);

    [RelayCommand]
    private void MarcarTodas()
    {
        var marcar = Lista.Any(l => !l.Marcada);
        foreach (var l in Lista) l.Marcada = marcar;
    }

    [RelayCommand]
    private Task ExcluirMarcadasAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var ids = Lista.Where(l => l.Marcada && l.P.DocumentoId is null).Select(l => l.P.Id).ToList();
        if (ids.Count == 0) { Erros.Add("Marque as passagens sem nota que quer excluir."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir passagens", $"Excluir {ids.Count} passagem(ns) marcada(s)? Elas podem ser importadas de novo.")) return;
        var r = await app.Frota().ExcluirPassagensAsync(empresa, ids, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = $"{r.Valor} passagem(ns) excluída(s).";
    }, Permissao.FrotaOperacao);
}
