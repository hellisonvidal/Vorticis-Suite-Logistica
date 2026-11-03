using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Relatorios;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Desktop.ViewModels;

/// <summary>Linha da demonstração na tela; <see cref="Anteriores"/> traz os valores dos anos anteriores (relatório comparativo).</summary>
public sealed record LinhaDemonstracaoView(LinhaDemonstracao Linha, IReadOnlyList<decimal>? Anteriores = null)
{
    public string Descricao => (Linha.Tipo == TipoLinhaDemonstracao.Total ? "" : new string(' ', (Linha.Nivel - 1) * 3)) + Linha.Descricao;
    public string Codigo => Linha.Codigo;
    public string Valor => Numeros.Moeda(Linha.Valor);
    private string Ant(int i) => Anteriores is { } a && i < a.Count ? Numeros.Moeda(a[i]) : "";
    public string Anterior1 => Ant(0);
    public string Anterior2 => Ant(1);
    public string Anterior3 => Ant(2);
    public string Anterior4 => Ant(3);
    /// <summary>Variação do valor atual sobre o do ano anterior, em %.</summary>
    public string Variacao => ComparativoDemonstracao.Variacao(Linha.Valor, Anteriores is { Count: > 0 } a ? a[0] : null);
}

/// <summary>Relatórios contábeis comparativos: junta a demonstração do período com as dos mesmos períodos de anos anteriores.</summary>
public static class ComparativoDemonstracao
{
    /// <summary>Opções de comparação (0 = sem comparar).</summary>
    public static IReadOnlyList<OpcaoComparacao> Opcoes { get; } =
        [new(0, "Sem comparar"), new(1, "Com o ano anterior"), new(2, "Com os 2 anos anteriores"), new(3, "Com os 3 anos anteriores"), new(4, "Com os 4 anos anteriores")];

    private static string Chave(LinhaDemonstracao l) => $"{l.Tipo}|{l.Codigo}|{l.Descricao}";

    /// <summary>
    /// Junta as linhas do período atual (primeira lista) com as dos anteriores, pela conta (código e descrição). Contas que só existem num ano anterior
    /// entram logo depois da linha que as antecede naquele ano, com valor zero no período atual.
    /// </summary>
    public static IReadOnlyList<(LinhaDemonstracao Linha, IReadOnlyList<decimal> Anteriores)> Mesclar(IReadOnlyList<IReadOnlyList<LinhaDemonstracao>> porAno)
    {
        var ordem = porAno[0].ToList();
        foreach (var ano in porAno.Skip(1))
        {
            string? anterior = null;
            foreach (var l in ano)
            {
                if (!ordem.Any(o => Chave(o) == Chave(l)))
                {
                    var pos = anterior is null ? 0 : ordem.FindIndex(o => Chave(o) == anterior) + 1;
                    ordem.Insert(pos, l with { Valor = 0 });
                }
                anterior = Chave(l);
            }
        }
        var valores = porAno.Select(a => a.GroupBy(Chave).ToDictionary(g => g.Key, g => g.Sum(x => x.Valor))).ToList();
        return [.. ordem.Select(l => (l with { Valor = valores[0].GetValueOrDefault(Chave(l)) },
                                      (IReadOnlyList<decimal>)[.. valores.Skip(1).Select(v => v.GetValueOrDefault(Chave(l)))]))];
    }

    public static string Variacao(decimal atual, decimal? anterior) =>
        anterior is not { } a || a == 0 ? "" : $"{(atual - a) / Math.Abs(a) * 100:N1}%";
}

public sealed record OpcaoComparacao(int Anos, string Nome)
{
    public override string ToString() => Nome;
}

public static class TabelasDemonstracao
{
    /// <summary>Tabela do relatório; com <paramref name="rotulos"/> (atual e anteriores), uma coluna por ano e a variação sobre o ano anterior.</summary>
    public static TabelaRelatorio Tabela(string titulo, IEnumerable<LinhaDemonstracaoView> linhas, string? rodape = null, IReadOnlyList<string>? rotulos = null)
    {
        if (rotulos is not { Count: > 1 })
            return new(titulo, [new("Código", LarguraCm: 3), new("Descrição"), new("Valor (R$)", TipoColuna.Valor, 3.2)],
                linhas.Select(l => new LinhaRelatorio([l.Codigo, l.Linha.Descricao, l.Linha.Valor],
                    Destaque: l.Linha.Tipo == TipoLinhaDemonstracao.Total, Recuo: l.Linha.Nivel - 1)).ToList(), rodape);
        return new(titulo, [new("Código", LarguraCm: 2.6), new("Descrição"), .. rotulos.Select(r => new ColunaRelatorio(r, TipoColuna.Valor, 2.8)), new("Var. %", LarguraCm: 1.8)],
            linhas.Select(l => new LinhaRelatorio([l.Codigo, l.Linha.Descricao, l.Linha.Valor, .. (l.Anteriores ?? []).Cast<object?>(), l.Variacao],
                Destaque: l.Linha.Tipo == TipoLinhaDemonstracao.Total, Recuo: l.Linha.Nivel - 1)).ToList(), rodape);
    }

    public static void Substituir(ObservableCollection<LinhaDemonstracaoView> destino, IEnumerable<LinhaDemonstracao> linhas)
    {
        destino.Clear();
        foreach (var l in linhas) destino.Add(new LinhaDemonstracaoView(l));
    }

    public static void Substituir(ObservableCollection<LinhaDemonstracaoView> destino, IReadOnlyList<IReadOnlyList<LinhaDemonstracao>> porAno)
    {
        destino.Clear();
        foreach (var (l, ant) in ComparativoDemonstracao.Mesclar(porAno)) destino.Add(new LinhaDemonstracaoView(l, porAno.Count > 1 ? ant : null));
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
        [TabelasDemonstracao.Tabela("Ativo", Ativo, null, Rotulos), TabelasDemonstracao.Tabela("Passivo e PL", PassivoPl, Resumo, Rotulos)];

    public static IReadOnlyList<OpcaoComparacao> OpcoesComparacao => ComparativoDemonstracao.Opcoes;
    [ObservableProperty] private OpcaoComparacao _comparacao = ComparativoDemonstracao.Opcoes[0];
    /// <summary>Títulos das colunas de valor (a data atual e as dos anos anteriores comparados).</summary>
    [ObservableProperty] private IReadOnlyList<string> _rotulos = [];

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
        var anteriores = new List<BalancoPatrimonial>();
        for (var i = 1; i <= Comparacao.Anos; i++)
            if (await app.Demonstracoes.BalancoAsync(empresa, data.AddYears(-i), Nivel, usuario) is { Sucesso: true } a) anteriores.Add(a.Valor);
        TabelasDemonstracao.Substituir(Ativo, [bp.Valor.Ativo, .. anteriores.Select(a => a.Ativo)]);
        TabelasDemonstracao.Substituir(PassivoPl, [bp.Valor.PassivoPl, .. anteriores.Select(a => a.PassivoPl)]);
        Rotulos = anteriores.Count == 0 ? [] : [.. Enumerable.Range(0, anteriores.Count + 1).Select(i => $"{data.AddYears(-i):dd/MM/yyyy}")];
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
    protected override IReadOnlyList<TabelaRelatorio> Tabelas() => [TabelasDemonstracao.Tabela("DRE", Dre, Resumo, Rotulos)];

    public static IReadOnlyList<OpcaoComparacao> OpcoesComparacao => ComparativoDemonstracao.Opcoes;
    [ObservableProperty] private OpcaoComparacao _comparacao = ComparativoDemonstracao.Opcoes[0];
    [ObservableProperty] private IReadOnlyList<string> _rotulos = [];

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
        var anteriores = new List<IReadOnlyList<LinhaDemonstracao>>();
        for (var i = 1; i <= Comparacao.Anos; i++)
            if (await app.Demonstracoes.DreAsync(empresa, ini.AddYears(-i), fim.AddYears(-i), Nivel, usuario) is { Sucesso: true } a) anteriores.Add(a.Valor.Linhas);
        TabelasDemonstracao.Substituir(Dre, [dre.Valor.Linhas, .. anteriores]);
        static string Rotulo(DateOnly a, DateOnly b) => a.Month == 1 && a.Day == 1 && b.Month == 12 && b.Day == 31 && a.Year == b.Year ? a.Year.ToString() : $"{a:dd/MM/yy} a {b:dd/MM/yy}";
        Rotulos = anteriores.Count == 0 ? [] : [.. Enumerable.Range(0, anteriores.Count + 1).Select(i => Rotulo(ini.AddYears(-i), fim.AddYears(-i)))];
        _periodo = (ini, fim);
        TemDados = true;
        AtualizarPermissoesExportacao();
        Resumo = "A DRE desconsidera os lançamentos de encerramento.";
    }, Permissao.RelatorioDemonstracoes);
}
