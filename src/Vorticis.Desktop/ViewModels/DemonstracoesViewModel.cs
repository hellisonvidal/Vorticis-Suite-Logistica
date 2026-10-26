using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Relatorios;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Desktop.ViewModels;

public sealed record LinhaDemonstracaoView(LinhaDemonstracao Linha)
{
    public string Descricao => (Linha.Tipo == TipoLinhaDemonstracao.Total ? "" : new string(' ', (Linha.Nivel - 1) * 3)) + Linha.Descricao;
    public string Codigo => Linha.Codigo;
    public string Valor => Numeros.Moeda(Linha.Valor);
}

public static class TabelasDemonstracao
{
    public static TabelaRelatorio Tabela(string titulo, IEnumerable<LinhaDemonstracaoView> linhas, string? rodape = null) =>
        new(titulo, [new("Código", LarguraCm: 3), new("Descrição"), new("Valor (R$)", TipoColuna.Valor, 3.2)],
            linhas.Select(l => new LinhaRelatorio([l.Codigo, l.Linha.Descricao, l.Linha.Valor],
                Destaque: l.Linha.Tipo == TipoLinhaDemonstracao.Total, Recuo: l.Linha.Nivel - 1)).ToList(), rodape);

    public static void Substituir(ObservableCollection<LinhaDemonstracaoView> destino, IEnumerable<LinhaDemonstracao> linhas)
    {
        destino.Clear();
        foreach (var l in linhas) destino.Add(new LinhaDemonstracaoView(l));
    }
}

/// <summary>Relatórios › Contábeis › Balanço Patrimonial.</summary>
public partial class BalancoViewModel(IContexto contexto) : RelatorioViewModel(contexto)
{
    private DateOnly _data;

    protected override string TituloRelatorio => "Balanço Patrimonial";
    protected override string Parametros => $"Balanço em {_data:dd/MM/yyyy}";
    protected override string NomeArquivo => $"balanco_patrimonial_{_data:yyyyMMdd}";
    protected override IReadOnlyList<TabelaRelatorio> Tabelas() =>
        [TabelasDemonstracao.Tabela("Ativo", Ativo), TabelasDemonstracao.Tabela("Passivo e PL", PassivoPl, Resumo)];

    public static IReadOnlyList<short> Niveis { get; } = [1, 2, 3, 4, 5];

    public ObservableCollection<LinhaDemonstracaoView> Ativo { get; } = [];
    public ObservableCollection<LinhaDemonstracaoView> PassivoPl { get; } = [];

    [ObservableProperty] private DateTime? _data31 = new DateTime(DateTime.Today.Year, 12, 31);
    [ObservableProperty] private short _nivel = 3;
    [ObservableProperty] private string _resumo = "";

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var data = Data(Data31, DateOnly.FromDateTime(DateTime.Today));
        var bp = await app.Demonstracoes.BalancoAsync(empresa, data, Nivel, usuario);
        if (!bp.Sucesso) { MostrarResultado(bp, ""); return; }
        TabelasDemonstracao.Substituir(Ativo, bp.Valor.Ativo);
        TabelasDemonstracao.Substituir(PassivoPl, bp.Valor.PassivoPl);
        _data = data;
        TemDados = true;
        AtualizarPermissoesExportacao();
        Resumo = (bp.Valor.Fecha ? "✓ Ativo = Passivo + PL" : $"✗ Diferença no balanço: {Numeros.Moeda(bp.Valor.TotalAtivo - bp.Valor.TotalPassivoPl)}") +
                 (bp.Valor.ResultadoNaoEncerrado != 0 ? " · resultado do período ainda não encerrado (aparece destacado no PL)" : "");
    }, Permissao.RelatorioDemonstracoes);
}

/// <summary>Relatórios › Contábeis › Demonstração do Resultado (DRE).</summary>
public partial class DreViewModel(IContexto contexto) : RelatorioViewModel(contexto)
{
    private (DateOnly Ini, DateOnly Fim) _periodo;

    protected override string TituloRelatorio => "Demonstração do Resultado do Exercício";
    protected override string Parametros => $"DRE de {_periodo.Ini:dd/MM/yyyy} a {_periodo.Fim:dd/MM/yyyy}";
    protected override string NomeArquivo => $"dre_{_periodo.Ini:yyyyMMdd}_{_periodo.Fim:yyyyMMdd}";
    protected override IReadOnlyList<TabelaRelatorio> Tabelas() => [TabelasDemonstracao.Tabela("DRE", Dre, Resumo)];

    public static IReadOnlyList<short> Niveis => BalancoViewModel.Niveis;

    public ObservableCollection<LinhaDemonstracaoView> Dre { get; } = [];

    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, 1, 1);
    [ObservableProperty] private DateTime? _fim = new DateTime(DateTime.Today.Year, 12, 31);
    [ObservableProperty] private short _nivel = 3;
    [ObservableProperty] private string _resumo = "";

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var ini = Data(Inicio, DateOnly.FromDateTime(DateTime.Today));
        var fim = Data(Fim, ini);
        var dre = await app.Demonstracoes.DreAsync(empresa, ini, fim, Nivel, usuario);
        if (!dre.Sucesso) { MostrarResultado(dre, ""); return; }
        TabelasDemonstracao.Substituir(Dre, dre.Valor.Linhas);
        _periodo = (ini, fim);
        TemDados = true;
        AtualizarPermissoesExportacao();
        Resumo = "A DRE desconsidera os lançamentos de encerramento.";
    }, Permissao.RelatorioDemonstracoes);
}
