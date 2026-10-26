using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Contabil;
using Vorticis.Application.Relatorios;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Desktop.ViewModels;

public sealed record LancamentoExportadoView(long Numero, string Data, string Conta, string Descricao, string DC, string Valor, string Historico,
                                             string Documento, string CentroCusto, string Participante, string Situacao);

/// <summary>
/// Integrações › Exportar › Lançamentos contábeis: as partidas do período (uma linha por conta debitada/creditada) em
/// Excel (.xlsx), PDF ou CSV, com documento, centro de custo e participante.
/// </summary>
public partial class ExportacaoLancamentosViewModel(IContexto contexto) : RelatorioViewModel(contexto)
{
    private IReadOnlyList<LinhaDiario> _ultimo = [];
    private (DateOnly Ini, DateOnly Fim) _periodo;

    public ObservableCollection<LancamentoExportadoView> Linhas { get; } = [];

    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, 1, 1);
    [ObservableProperty] private DateTime? _fim = DateTime.Today;
    /// <summary>Inclui os lançamentos estornados e os estornos (desmarcado: só o que está valendo).</summary>
    [ObservableProperty] private bool _incluirEstornados = true;
    [ObservableProperty] private string _resumo = "";

    protected override string TituloRelatorio => "Lançamentos contábeis";
    protected override string Parametros => $"Período: {_periodo.Ini:dd/MM/yyyy} a {_periodo.Fim:dd/MM/yyyy}" +
                                            (IncluirEstornados ? "" : " · sem estornados e estornos");
    protected override string NomeArquivo => $"lancamentos_{_periodo.Ini:yyyyMMdd}_{_periodo.Fim:yyyyMMdd}";
    protected override bool Paisagem => true;

    protected override IReadOnlyList<TabelaRelatorio> Tabelas() =>
    [
        new("Lançamentos", [
                new("Nº", TipoColuna.Inteiro), new("Data", TipoColuna.Data), new("Conta", LarguraCm: 2.4), new("Descrição da conta", LarguraCm: 4.5),
                new("D/C", LarguraCm: 0.9), new("Valor", TipoColuna.Valor), new("Histórico"), new("Documento", LarguraCm: 2),
                new("C. custo", LarguraCm: 1.6), new("Participante", LarguraCm: 1.8), new("Situação", LarguraCm: 1.8)],
            _ultimo.Select(l => new LinhaRelatorio(
                [l.Numero, l.Data, l.CodigoConta, l.DescricaoConta, l.DC.ToString(), l.Valor, l.Historico, l.Documento ?? "",
                 l.CentroCusto ?? "", l.Participante ?? "", Situacao(l)])).ToList(),
            Resumo),
    ];

    private static string Situacao(LinhaDiario l) => l.Status == StatusLancamento.Estornado ? "Estornado"
        : l.Historico.StartsWith("Estorno", StringComparison.OrdinalIgnoreCase) ? "Estorno" : "";

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var ini = Data(Inicio, new DateOnly(DateTime.Today.Year, 1, 1));
        var fim = Data(Fim, ini);
        var r = await app.Relatorios.DiarioAsync(empresa, ini, fim, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var linhas = r.Valor;
        if (!IncluirEstornados)
        {
            // Fora: o estornado e o estorno dele (o par se anula).
            var fora = linhas.Where(l => l.Status == StatusLancamento.Estornado || Situacao(l) == "Estorno").Select(l => l.LancamentoId).ToHashSet();
            linhas = linhas.Where(l => !fora.Contains(l.LancamentoId)).ToList();
        }
        (_ultimo, _periodo) = (linhas, (ini, fim));
        Linhas.Clear();
        foreach (var l in linhas)
            Linhas.Add(new LancamentoExportadoView(l.Numero, l.Data.ToString("dd/MM/yyyy"), l.CodigoConta, l.DescricaoConta, l.DC.ToString(),
                Numeros.Moeda(l.Valor), l.Historico, l.Documento ?? "", l.CentroCusto ?? "", l.Participante ?? "", Situacao(l)));
        var deb = linhas.Where(l => l.DC == IndicadorDC.D).Sum(l => l.Valor);
        var cred = linhas.Where(l => l.DC == IndicadorDC.C).Sum(l => l.Valor);
        Resumo = $"{linhas.Select(l => l.LancamentoId).Distinct().Count()} lançamento(s), {linhas.Count} partida(s) · débitos {Numeros.Moeda(deb)} | créditos {Numeros.Moeda(cred)}";
        TemDados = true;
        AtualizarPermissoesExportacao();
        Mensagem = linhas.Count == 0 ? "Nenhum lançamento no período." : "Escolha o formato em Exportar: PDF, Excel (.xlsx) ou CSV.";
    });
}
