using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Relatorios;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra;
using Vorticis.Infra.Importacao;

namespace Vorticis.Desktop.ViewModels;

/// <summary>Linha da grade de um relatório gerencial: os textos já formatados, na ordem das colunas.</summary>
public sealed record LinhaGradeView(string[] Valores, bool Destaque)
{
    public string V0 => Valores.ElementAtOrDefault(0) ?? "";
}

public sealed record OpcaoCentro(long? Id, string Texto);

/// <summary>Base dos relatórios de Relatórios › Gerenciais: filtros comuns, grade montada a partir da tabela exportável.</summary>
public abstract partial class GerencialBaseViewModel(IContexto contexto) : RelatorioViewModel(contexto)
{
    private TabelaRelatorio? _ultima;
    private string _parametros = "";

    public ObservableCollection<LinhaGradeView> Linhas { get; } = [];
    public ObservableCollection<OpcaoCentro> Centros { get; } = [new(null, "Todos os centros")];

    [ObservableProperty] private IReadOnlyList<string> _colunas = [];
    [ObservableProperty] private OpcaoCentro? _centro;
    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, 1, 1);
    [ObservableProperty] private DateTime? _fim = DateTime.Today;
    [ObservableProperty] private string _contaPrefixo = "";
    /// <summary>Nível máximo dos centros exibidos (0 = todos).</summary>
    [ObservableProperty] private decimal? _nivel = 0;
    [ObservableProperty] private string _resumo = "";

    protected override string Parametros => _parametros;
    protected override bool Paisagem => true;
    protected override IReadOnlyList<TabelaRelatorio> Tabelas() => _ultima is null ? [] : [_ultima];

    protected long? CentroId => Centro?.Id;
    protected int NivelMaximo => (int)(Nivel ?? 0);
    protected string? Prefixo => string.IsNullOrWhiteSpace(ContaPrefixo) ? null : ContaPrefixo.Trim();
    protected string SufixoArquivo => _parametros.Length == 0 ? "" : "_" + new string(_parametros.Where(char.IsLetterOrDigit).ToArray());
    protected string TextoCentro => Centro is { Id: not null } c ? $" | Centro: {c.Texto}" : "";
    protected string TextoConta => Prefixo is { } p ? $" | Conta: {p}" : "";

    protected void Exibir(TabelaRelatorio tabela, string parametros)
    {
        _ultima = tabela;
        _parametros = parametros;
        Colunas = tabela.Colunas.Select(c => c.Titulo).ToList();
        Linhas.Clear();
        foreach (var l in tabela.Linhas)
        {
            var textos = l.Valores.Select((v, i) => ExportacaoRelatorio.Texto(v, tabela.Colunas[i].Tipo)).ToArray();
            if (l.Recuo > 0 && textos.Length > 1) textos[1] = new string(' ', l.Recuo * 3) + textos[1];
            Linhas.Add(new LinhaGradeView(textos, l.Destaque));
        }
        TemDados = true;
        AtualizarPermissoesExportacao();
    }

    public async Task CarregarCentrosAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var selecionado = Centro?.Id;
        var lista = await app.PlanoContas.ListarCentrosAsync(empresa, sessao.Id);
        Centros.Clear();
        Centros.Add(new(null, "Todos os centros"));
        foreach (var c in lista.OrderBy(c => c.Codigo, StringComparer.Ordinal)) Centros.Add(new(c.Id, $"{c.Codigo} — {c.Descricao}"));
        Centro = Centros.FirstOrDefault(c => c.Id == selecionado) ?? Centros[0];
    }
}

/// <summary>Relatórios › Gerenciais › Contabilização por centro de custo.</summary>
public sealed partial class CentroCustoRelatorioViewModel(IContexto contexto) : GerencialBaseViewModel(contexto)
{
    [ObservableProperty] private bool _detalhar;

    protected override string TituloRelatorio => TabelasGerenciais.TituloCentroCusto;
    protected override string NomeArquivo => "centro_custo" + SufixoArquivo;

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var ini = Data(Inicio, new DateOnly(DateTime.Today.Year, 1, 1));
        var fim = Data(Fim, ini);
        var r = await app.Gerenciais.PorCentroCustoAsync(empresa, new(ini, fim, CentroId, Prefixo, NivelMaximo, Detalhar), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Exibir(TabelasGerenciais.CentroCusto(r.Valor), $"Período: {ini:dd/MM/yyyy} a {fim:dd/MM/yyyy}{TextoCentro}{TextoConta}");
        Resumo = r.Valor.Count == 0 ? "Nenhum movimento com centro de custo no período." : "";
    }, Permissao.RelatorioBalancete);
}

/// <summary>Relatórios › Gerenciais › Relação de custos.</summary>
public sealed partial class RelacaoCustosViewModel(IContexto contexto) : GerencialBaseViewModel(contexto)
{
    public IReadOnlyList<string> Agrupamentos { get; } = ["Por centro de custo (e contas)", "Por conta (e centros)"];
    public IReadOnlyList<string> Comparacoes { get; } = ["Sem comparação", "Período anterior (mesma duração)", "Mesmo período do ano anterior"];
    [ObservableProperty] private int _agrupamento;
    [ObservableProperty] private int _comparacao = 1;

    protected override string TituloRelatorio => TabelasGerenciais.TituloCustos;
    protected override string NomeArquivo => "relacao_custos" + SufixoArquivo;

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var ini = Data(Inicio, new DateOnly(DateTime.Today.Year, 1, 1));
        var fim = Data(Fim, ini);
        var comparar = (ComparacaoPeriodo)Comparacao;
        var r = await app.Gerenciais.RelacaoCustosAsync(empresa, new(ini, fim, (AgrupamentoCustos)Agrupamento, comparar, CentroId, Prefixo, NivelMaximo), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var cmp = Gerenciais.PeriodoComparado(ini, fim, comparar);
        Exibir(TabelasGerenciais.Custos(r.Valor, comparar != ComparacaoPeriodo.Nenhuma),
               $"Período: {ini:dd/MM/yyyy} a {fim:dd/MM/yyyy}" + (cmp is { } c ? $" | Comparado com {c.Inicio:dd/MM/yyyy} a {c.Fim:dd/MM/yyyy}" : "") + TextoCentro + TextoConta);
        Resumo = r.Valor.Count == 0 ? "Nenhum custo ou despesa no período." : "";
    }, Permissao.RelatorioDemonstracoes);
}

/// <summary>Relatórios › Gerenciais › Receita por centro de custo.</summary>
public sealed partial class ReceitaCentroViewModel(IContexto contexto) : GerencialBaseViewModel(contexto)
{
    protected override string TituloRelatorio => TabelasGerenciais.TituloReceitaCentro;
    protected override string NomeArquivo => "receita_centro" + SufixoArquivo;

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var ini = Data(Inicio, new DateOnly(DateTime.Today.Year, 1, 1));
        var fim = Data(Fim, ini);
        var r = await app.Gerenciais.ReceitaPorCentroAsync(empresa, new(ini, fim, CentroId, NivelMaximo), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Exibir(TabelasGerenciais.ReceitaCentro(r.Valor), $"Período: {ini:dd/MM/yyyy} a {fim:dd/MM/yyyy}{TextoCentro}");
        Resumo = r.Valor.Linhas.Count == 0 ? "Nenhuma receita ou custo com centro de custo no período." : "";
    }, Permissao.RelatorioDemonstracoes);
}

/// <summary>Relatórios › Gerenciais › Orçado x Realizado.</summary>
public sealed partial class OrcadoRealizadoViewModel(IContexto contexto) : GerencialBaseViewModel(contexto)
{
    [ObservableProperty] private decimal? _ano = DateTime.Today.Year;
    [ObservableProperty] private decimal? _mesInicial = 1;
    [ObservableProperty] private decimal? _mesFinal = DateTime.Today.Month;
    [ObservableProperty] private bool _mensal;
    [ObservableProperty] private bool _ignorarCentro;

    protected override string TituloRelatorio => TabelasGerenciais.TituloOrcadoRealizado;
    protected override string NomeArquivo => $"orcado_realizado_{(int)(Ano ?? 0)}{(Mensal ? "_mensal" : "")}";

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var ano = (int)(Ano ?? DateTime.Today.Year);
        var mi = (int)(MesInicial ?? 1);
        var mf = (int)(MesFinal ?? 12);
        if (mi < 1 || mf > 12 || mf < mi) { Erros.Add("Informe meses de 1 a 12, com o mês final igual ou posterior ao inicial."); return; }
        var r = await app.Gerenciais.OrcadoRealizadoAsync(empresa, new(ano, mi, mf, CentroId, Prefixo, Mensal, IgnorarCentro), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Exibir(TabelasGerenciais.OrcadoRealizado(r.Valor, Mensal), $"Ano {ano}, meses {mi:00} a {mf:00}{TextoCentro}{TextoConta}");
        Resumo = r.Valor.Count == 0 ? "Sem orçamento nem movimento de receitas, custos ou despesas no período." : "";
    }, Permissao.RelatorioDemonstracoes);
}

/// <summary>Linha editável da grade do orçamento: conta, centro (opcional) e os 12 meses.</summary>
public sealed partial class LinhaOrcamentoEdicao : ObservableObject
{
    [ObservableProperty] private string _conta = "";
    [ObservableProperty] private string _centro = "";
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Total))] private string _m1 = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Total))] private string _m2 = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Total))] private string _m3 = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Total))] private string _m4 = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Total))] private string _m5 = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Total))] private string _m6 = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Total))] private string _m7 = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Total))] private string _m8 = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Total))] private string _m9 = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Total))] private string _m10 = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Total))] private string _m11 = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Total))] private string _m12 = "";

    public string[] Meses
    {
        get => [M1, M2, M3, M4, M5, M6, M7, M8, M9, M10, M11, M12];
        set
        {
            M1 = value[0]; M2 = value[1]; M3 = value[2]; M4 = value[3]; M5 = value[4]; M6 = value[5];
            M7 = value[6]; M8 = value[7]; M9 = value[8]; M10 = value[9]; M11 = value[10]; M12 = value[11];
        }
    }

    public string Total => Numeros.Moeda(Meses.Sum(m => Numeros.LerValor(m) ?? 0));
}

/// <summary>Relatórios › Gerenciais › Orçamento: digitação do orçamento anual em grade (12 meses), cópia do ano anterior e importação de planilha.</summary>
public sealed partial class OrcamentoViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    private IReadOnlyList<ContaPlano> _plano = [];
    private IReadOnlyList<CentroPlano> _centros = [];

    public ObservableCollection<LinhaOrcamentoEdicao> Linhas { get; } = [];
    [ObservableProperty] private decimal? _ano = DateTime.Today.Year + 1;
    /// <summary>Reajuste aplicado ao copiar do ano anterior (ex.: 5 = +5%; negativo reduz).</summary>
    [ObservableProperty] private decimal? _percentual = 0;
    [ObservableProperty] private string _arquivo = "";
    [ObservableProperty] private LinhaOrcamentoEdicao? _selecionada;

    public bool PodeManter => Contexto.Pode(Permissao.OrcamentoManter);
    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeManter));
    private int AnoInt => (int)(Ano ?? DateTime.Today.Year);

    private async Task CarregarReferenciasAsync(VorticisApp app, long empresa)
    {
        _plano = await app.Consultas.ObterPlanoAsync(empresa, default);
        _centros = await app.Consultas.ObterCentrosAsync(empresa, default);
    }

    private void Preencher(IReadOnlyList<LinhaOrcamento> linhas)
    {
        var contas = _plano.ToDictionary(c => c.Id);
        var centros = _centros.ToDictionary(c => c.Id);
        Linhas.Clear();
        foreach (var l in linhas)
            Linhas.Add(new LinhaOrcamentoEdicao
            {
                Conta = contas.TryGetValue(l.ContaId, out var c) ? c.Codigo : "",
                Descricao = c?.Descricao ?? "",
                Centro = l.CentroId != 0 && centros.TryGetValue(l.CentroId, out var cc) ? cc.Codigo : "",
                Meses = l.Valores.Select(v => v == 0 ? "" : Numeros.Moeda(v)).ToArray(),
            });
    }

    public Task CarregarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        await CarregarReferenciasAsync(app, empresa);
        var r = await app.Gerenciais.ListarOrcamentoAsync(empresa, AnoInt, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Preencher(r.Valor);
        Mensagem = r.Valor.Count == 0 ? $"Ainda não há orçamento de {AnoInt}. Digite as linhas, copie do ano anterior ou importe uma planilha." : null;
    });

    [RelayCommand] private Task CarregarOrcamentoAsync() => CarregarAsync();

    [RelayCommand]
    private void AdicionarLinha()
    {
        var l = new LinhaOrcamentoEdicao();
        Linhas.Add(l);
        Selecionada = l;
    }

    [RelayCommand]
    private void RemoverLinha()
    {
        if (Selecionada is { } l) Linhas.Remove(l);
    }

    /// <summary>Atualiza a descrição ao digitar o código da conta.</summary>
    public void ConferirLinha(LinhaOrcamentoEdicao l) =>
        l.Descricao = _plano.FirstOrDefault(c => c.Codigo == l.Conta.Trim() && c.Analitica)?.Descricao ?? "";

    [RelayCommand]
    private Task CopiarOrcadoAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        await CarregarReferenciasAsync(app, empresa);
        var r = await app.Gerenciais.CopiarOrcamentoAnteriorAsync(empresa, AnoInt, Percentual ?? 0, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Preencher(r.Valor);
        Mensagem = $"Orçamento de {AnoInt - 1} copiado com reajuste de {Numeros.Moeda(Percentual ?? 0)}%. Confira e clique em Salvar orçamento.";
    }, Permissao.OrcamentoManter);

    [RelayCommand]
    private Task CopiarRealizadoAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        await CarregarReferenciasAsync(app, empresa);
        var r = await app.Gerenciais.CopiarRealizadoAnteriorAsync(empresa, AnoInt, Percentual ?? 0, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Preencher(r.Valor);
        Mensagem = $"Realizado de {AnoInt - 1} copiado com reajuste de {Numeros.Moeda(Percentual ?? 0)}%. Confira e clique em Salvar orçamento.";
    }, Permissao.OrcamentoManter);

    [RelayCommand]
    private Task ImportarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var lida = LeitorPlanilha.Ler(Arquivo);
        if (!lida.Sucesso) { MostrarResultado(lida, ""); return; }
        await CarregarReferenciasAsync(app, empresa);
        var r = await app.Gerenciais.LerPlanilhaAsync(empresa, lida.Valor, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Preencher(r.Valor);
        Mensagem = $"Planilha lida: {r.Valor.Count} linha(s) de orçamento. Confira e clique em Salvar orçamento.";
    }, Permissao.OrcamentoManter);

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        await CarregarReferenciasAsync(app, empresa);
        var contas = _plano.Where(c => c.Analitica).ToDictionary(c => c.Codigo);
        var centros = _centros.ToDictionary(c => c.Codigo);
        var linhas = new List<LinhaOrcamento>();
        var erros = new List<string>();
        var n = 0;
        foreach (var l in Linhas)
        {
            n++;
            if (l.Conta.Trim().Length == 0 && l.Meses.All(string.IsNullOrWhiteSpace)) continue;
            if (!contas.TryGetValue(l.Conta.Trim(), out var conta)) { erros.Add($"Linha {n}: a conta {l.Conta} não existe ou não é analítica."); continue; }
            long centro = 0;
            if (l.Centro.Trim().Length > 0)
            {
                if (!centros.TryGetValue(l.Centro.Trim(), out var cc)) { erros.Add($"Linha {n}: o centro de custo {l.Centro} não existe."); continue; }
                centro = cc.Id;
            }
            var valores = new decimal[12];
            for (var m = 0; m < 12; m++)
            {
                var txt = l.Meses[m];
                if (string.IsNullOrWhiteSpace(txt)) continue;
                if (Numeros.LerValor(txt) is not { } v) { erros.Add($"Linha {n}: valor inválido em {m + 1:00}/{AnoInt}."); break; }
                valores[m] = v;
            }
            linhas.Add(new LinhaOrcamento(conta.Id, centro, valores));
        }
        if (erros.Count > 0) { foreach (var e in erros) Erros.Add(e); return; }
        var r = await app.Gerenciais.SalvarOrcamentoAsync(empresa, AnoInt, linhas, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var lido = await app.Gerenciais.ListarOrcamentoAsync(empresa, AnoInt, usuario);
        if (lido.Sucesso) Preencher(lido.Valor);
        Mensagem = $"Orçamento de {AnoInt} salvo.";
    }, Permissao.OrcamentoManter);
}
