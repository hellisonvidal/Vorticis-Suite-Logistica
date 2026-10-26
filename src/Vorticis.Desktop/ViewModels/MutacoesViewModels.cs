using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Relatorios;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Desktop.ViewModels;

/// <summary>Linha da DMPL na tela: um valor formatado por coluna do PL e o total (última posição).</summary>
public sealed record LinhaMutacaoView(string Descricao, string[] Valores, bool Saldo);

public sealed record LinhaDlpaView(string Descricao, string Valor, bool Destaque);

/// <summary>Período e geração comuns à DMPL e à DLPA (a mesma apuração das mutações do PL alimenta as duas).</summary>
public abstract partial class MutacoesBaseViewModel(IContexto contexto) : RelatorioViewModel(contexto)
{
    protected DemonstracaoMutacoes? Ultima { get; private set; }

    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, 1, 1);
    [ObservableProperty] private DateTime? _fim = new DateTime(DateTime.Today.Year, 12, 31);
    [ObservableProperty] private string _resumo = "";

    protected override string Parametros => Ultima is { } d ? $"Período: {d.Inicio:dd/MM/yyyy} a {d.Fim:dd/MM/yyyy}" : "";
    protected string Sufixo => Ultima is { } d ? $"_{d.Inicio:yyyyMMdd}_{d.Fim:yyyyMMdd}" : "";

    /// <summary>Preenche as grades da tela a partir da apuração e devolve o resumo.</summary>
    protected abstract string Preencher(DemonstracaoMutacoes d);

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var ini = Data(Inicio, new DateOnly(DateTime.Today.Year, 1, 1));
        var r = await app.Demonstracoes.MutacoesAsync(empresa, ini, Data(Fim, ini), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Ultima = r.Valor;
        Resumo = Preencher(r.Valor);
        TemDados = true;
        AtualizarPermissoesExportacao();
    }, Permissao.RelatorioDemonstracoes);
}

/// <summary>Relatórios › Contábeis › DMPL: mutações do patrimônio líquido (CPC 26), uma coluna por componente do PL.</summary>
public sealed partial class DmplViewModel(IContexto contexto) : MutacoesBaseViewModel(contexto)
{
    public ObservableCollection<LinhaMutacaoView> Dmpl { get; } = [];

    /// <summary>Cabeçalhos das colunas da DMPL (as do PL, mais o total).</summary>
    [ObservableProperty] private IReadOnlyList<string> _colunasDmpl = [];

    protected override string TituloRelatorio => "Demonstração das Mutações do Patrimônio Líquido (DMPL)";
    protected override string NomeArquivo => "dmpl" + Sufixo;
    protected override bool Paisagem => true;

    protected override IReadOnlyList<TabelaRelatorio> Tabelas() => Ultima is { } d ? [TabelasMutacoes.Dmpl(d, Resumo)] : [];

    protected override string Preencher(DemonstracaoMutacoes d)
    {
        Dmpl.Clear();
        foreach (var l in d.Dmpl)
            Dmpl.Add(new LinhaMutacaoView(l.Descricao, d.Colunas.Select(c => Numeros.MoedaOuVazio(l.Valor(c))).Append(Numeros.Moeda(l.Total)).ToArray(), l.Saldo));
        ColunasDmpl = d.Colunas.Select(DemonstracaoMutacoes.NomeColuna).Append("Total").ToList();
        return $"Patrimônio líquido: {Numeros.Moeda(d.Dmpl[0].Total)} → {Numeros.Moeda(d.Dmpl[^1].Total)}";
    }
}

/// <summary>Relatórios › Contábeis › DLPA: lucros ou prejuízos acumulados (Lei 6.404/76, art. 186).</summary>
public sealed class DlpaViewModel(IContexto contexto) : MutacoesBaseViewModel(contexto)
{
    public ObservableCollection<LinhaDlpaView> Dlpa { get; } = [];

    protected override string TituloRelatorio => "Demonstração de Lucros ou Prejuízos Acumulados (DLPA)";
    protected override string NomeArquivo => "dlpa" + Sufixo;

    protected override IReadOnlyList<TabelaRelatorio> Tabelas() => Ultima is { } d ? [TabelasMutacoes.Dlpa(d)] : [];

    protected override string Preencher(DemonstracaoMutacoes d)
    {
        Dlpa.Clear();
        foreach (var l in d.Dlpa) Dlpa.Add(new LinhaDlpaView(l.Descricao, l.SemValor ? "" : Numeros.Moeda(l.Valor), l.Destaque));
        return $"Lucros ou prejuízos acumulados: {Numeros.Moeda(d.Dlpa[0].Valor)} → {Numeros.Moeda(d.Dlpa[^1].Valor)}";
    }
}
