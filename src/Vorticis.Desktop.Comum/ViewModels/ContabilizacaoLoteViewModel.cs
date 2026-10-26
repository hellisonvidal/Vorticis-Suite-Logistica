using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Contabil;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra;

namespace Vorticis.Desktop.Comum.ViewModels;

/// <summary>Pendência de contabilização na lista da tela (com caixa de marcação).</summary>
public sealed partial class LinhaPendencia(PendenciaContabil pendencia) : ObservableObject
{
    public PendenciaContabil P { get; } = pendencia;
    public string Competencia => $"{P.Periodo:MM/yyyy}";
    public string Data => $"{P.Periodo:dd/MM/yyyy}";
    public string Origem => OrigensPendencia.Nome(P.Origem);
    public string Descricao => P.Descricao;
    public string Valor => Numeros.Moeda(P.Valor);
    public string Observacao => P.Mensagem ?? "";
    [ObservableProperty] private bool _marcada;
}

public sealed record OpcaoOrigem(string? Origem, string Texto);

public sealed record LinhaLote(LoteContabil L)
{
    public string Texto => $"Lote {L.Id} - {L.CriadoEm.ToLocalTime():dd/MM/yyyy HH:mm} - {L.Quantidade} pendência(s), {Numeros.Moeda(L.Valor)}" + (L.Desfeito ? " (desfeito)" : "");
}

/// <summary>
/// Integrações › Exportar › Contabilização em lote: com o tipo de contabilização "Em lote" (Contábil › Parâmetros), os módulos não geram o
/// lançamento na hora — registram pendências. Aqui o contador vê as pendências por período e origem, confere a prévia do lançamento,
/// contabiliza as selecionadas (as mesmas regras da contabilização on-line) e, se precisar, desfaz um lote inteiro.
/// </summary>
public sealed partial class ContabilizacaoLoteViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public static readonly IReadOnlyList<OpcaoOrigem> Origens =
    [
        new(null, "Todas as origens"),
        .. OrigensPendencia.Todas.Select(o => new OpcaoOrigem(o, OrigensPendencia.Nome(o))),
    ];

    public ObservableCollection<LinhaPendencia> Pendencias { get; } = [];
    public ObservableCollection<string> Resumo { get; } = [];
    public ObservableCollection<string> Previa { get; } = [];
    public ObservableCollection<LinhaLote> Lotes { get; } = [];

    [ObservableProperty] private OpcaoOrigem _origemSelecionada = Origens[0];
    [ObservableProperty] private DateTime? _inicio;
    [ObservableProperty] private DateTime? _fim;
    [ObservableProperty] private LinhaLote? _loteSelecionado;
    [ObservableProperty] private string _modoTexto = "";
    [ObservableProperty] private string _totais = "";

    public bool PodeAcessar => Contexto.EmpresaId is not null;

    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeAcessar));

    public Task CarregarAsync() => ConsultarAsync();

    [RelayCommand]
    private Task ConsultarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        Previa.Clear();
        var modo = await app.Pendencias.ModoAsync(empresa, usuario);
        ModoTexto = modo.Sucesso && modo.Valor == ModoContabilizacao.Lote
            ? "Tipo de contabilização da empresa: Em lote. Os módulos registram as pendências abaixo em vez de lançar na hora."
            : "Tipo de contabilização da empresa: On-line (os lançamentos são gerados na hora). Para usar o lote, mude em Cadastros › Parâmetros › Contábil.";
        var filtro = new FiltroPendencias(OrigemSelecionada.Origem, Inicio is { } i ? DateOnly.FromDateTime(i) : null, Fim is { } f ? DateOnly.FromDateTime(f) : null);
        var lista = await app.Pendencias.ListarAsync(empresa, filtro, usuario);
        if (!lista.Sucesso) { MostrarResultado(lista, ""); return; }
        Pendencias.Clear();
        foreach (var p in lista.Valor) Pendencias.Add(new LinhaPendencia(p));
        Resumo.Clear();
        foreach (var g in lista.Valor.GroupBy(p => (p.Periodo.Year, p.Periodo.Month, p.Origem)).OrderBy(g => g.Key))
            Resumo.Add($"{g.Key.Month:00}/{g.Key.Year} - {OrigensPendencia.Nome(g.Key.Origem)}: {g.Count()} pendência(s), {Numeros.Moeda(g.Sum(p => p.Valor))}");
        Totais = lista.Valor.Count == 0 ? "Nenhuma pendência de contabilização." : $"{lista.Valor.Count} pendência(s), total {Numeros.Moeda(lista.Valor.Sum(p => p.Valor))}.";
        var lotes = await app.Pendencias.ListarLotesAsync(empresa, usuario);
        if (lotes.Sucesso)
        {
            Lotes.Clear();
            foreach (var l in lotes.Valor) Lotes.Add(new LinhaLote(l));
        }
    });

    [RelayCommand]
    private void MarcarTodas()
    {
        foreach (var l in Pendencias) l.Marcada = true;
    }

    [RelayCommand]
    private void DesmarcarTodas()
    {
        foreach (var l in Pendencias) l.Marcada = false;
    }

    private List<long> Marcadas() => [.. Pendencias.Where(l => l.Marcada).Select(l => l.P.Id)];

    [RelayCommand]
    private Task VerPreviaAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var ids = Marcadas();
        if (ids.Count == 0) { Erros.Add("Marque as pendências que deseja conferir."); return; }
        var r = await app.Pendencias.PreviaAsync(empresa, ids, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Previa.Clear();
        foreach (var l in r.Valor) Previa.Add(l);
    });

    /// <summary>Contabilizar selecionadas: gera os lançamentos das pendências marcadas, num lote novo.</summary>
    [RelayCommand]
    private async Task ContabilizarAsync()
    {
        var ids = Marcadas();
        if (ids.Count == 0) { Erros.Clear(); Erros.Add("Marque as pendências que deseja contabilizar."); return; }
        if (!await Contexto.ConfirmarAsync("Contabilizar selecionadas", $"Gerar os lançamentos contábeis de {ids.Count} pendência(s)?", "Contabilizar", "Cancelar")) return;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            var r = await app.Pendencias.ContabilizarAsync(empresa, ids, usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            await ConsultarAsync();
            foreach (var f in r.Valor.Falhas) foreach (var e in f.Erros) Erros.Add($"{f.Descricao}: {e}");
            Mensagem = r.Valor.LoteId is { } lote ? $"Lote {lote}: {r.Valor.Resumo}" : r.Valor.Resumo;
        });
    }

    /// <summary>Desfazer lote: estorna os lançamentos do lote escolhido e devolve as pendências à lista.</summary>
    [RelayCommand]
    private async Task DesfazerLoteAsync()
    {
        if (LoteSelecionado is not { L.Desfeito: false } sel) { Erros.Clear(); Erros.Add("Selecione um lote que ainda não foi desfeito."); return; }
        if (!await Contexto.ConfirmarAsync("Desfazer lote", $"Estornar os lançamentos do lote {sel.L.Id} ({sel.L.Quantidade} pendência(s))? Elas voltam para a lista de pendentes.", "Desfazer", "Cancelar")) return;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            var r = await app.Pendencias.DesfazerLoteAsync(empresa, sel.L.Id, usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            await ConsultarAsync();
            foreach (var f in r.Valor.Falhas) foreach (var e in f.Erros) Erros.Add($"{f.Descricao}: {e}");
            Mensagem = $"Lote {sel.L.Id} desfeito: {r.Valor.Contabilizadas} lançamento(s) estornado(s).";
        });
    }
}
