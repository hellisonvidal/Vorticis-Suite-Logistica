using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Contabil;
using Vorticis.Application.Relatorios;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra;
using Vorticis.Infra.Cadastros;

namespace Vorticis.Desktop.ViewModels;

internal static class FiliaisApp
{
    public static ServicoFiliais Filiais(this VorticisApp app) => new(app.DataSource, app.Autorizador);
}

/// <summary>
/// Cadastros › Filiais: diz se a empresa ativa é filial de outra (mesma raiz de CNPJ) e mostra o grupo. Cada filial continua com a sua
/// escrituração; o grupo serve aos relatórios de matriz e filiais (individuais ou agrupados).
/// </summary>
public partial class FiliaisViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public ObservableCollection<EmpresaGrupo> Grupo { get; } = [];
    public ObservableCollection<EmpresaGrupo?> Matrizes { get; } = [];
    [ObservableProperty] private EmpresaGrupo? _matriz;
    [ObservableProperty] private string _situacao = "";

    public bool PodeAlterar => Contexto.Pode(Permissao.AdministrarSistema) || Contexto.Pode(Permissao.PlanoContasManter);
    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeAlterar));

    public Task CarregarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var grupo = await app.Filiais().GrupoAsync(empresa, usuario);
        var matrizes = await app.Filiais().MatrizesPossiveisAsync(empresa, usuario);
        Grupo.Clear();
        foreach (var g in grupo) Grupo.Add(g);
        Matrizes.Clear();
        Matrizes.Add(null);
        foreach (var m in matrizes) Matrizes.Add(m);
        var eu = grupo.FirstOrDefault(g => g.Id == empresa);
        Matriz = Matrizes.FirstOrDefault(m => m?.Id == eu?.MatrizId);
        Situacao = eu is null ? "" : eu.MatrizId is not null ? "Esta empresa é filial." : grupo.Count > 1 ? $"Esta empresa é a matriz de {grupo.Count - 1} filial(is)." : "Esta empresa não tem filiais ligadas.";
    });

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Filiais().DefinirMatrizAsync(empresa, Matriz?.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = Matriz is null ? "A empresa não é mais filial." : $"A empresa agora é filial de {Matriz.RazaoSocial}.";
    });
}

/// <summary>Empresa do grupo marcada (ou não) para o relatório.</summary>
public sealed partial class EmpresaMarcada(EmpresaGrupo empresa, bool marcada) : ObservableObject
{
    public EmpresaGrupo Empresa { get; } = empresa;
    public string Rotulo => Empresa.Rotulo;
    [ObservableProperty] private bool _marcada = marcada;
}

/// <summary>Linha do relatório na tela (valores já formatados, um por coluna).</summary>
public sealed record LinhaRelatorioGrupo(string[] Valores, bool Destaque, int Recuo);

/// <summary>
/// Relatórios › Contábeis › Relatórios de matriz e filiais: balancete, DRE ou balanço de uma empresa do grupo (individual) ou de várias,
/// somadas (agrupado) ou lado a lado; exportado em PDF, Excel e CSV como os demais.
/// </summary>
public partial class RelatorioGrupoViewModel(IContexto contexto) : RelatorioViewModel(contexto)
{
    public static IReadOnlyList<string> Relatorios { get; } = ["Balancete de verificação", "Demonstração do Resultado (DRE)", "Balanço Patrimonial"];
    public static IReadOnlyList<string> Formas { get; } = ["Agrupado (somado)", "Lado a lado (uma coluna por empresa)"];
    public static IReadOnlyList<short> Niveis => BalancoViewModel.Niveis;

    private IReadOnlyList<TabelaRelatorio> _tabelas = [];
    public ObservableCollection<EmpresaMarcada> Empresas { get; } = [];
    public ObservableCollection<LinhaRelatorioGrupo> Linhas { get; } = [];
    public ObservableCollection<string> Colunas { get; } = [];
    [ObservableProperty] private string _relatorio = Relatorios[0];
    [ObservableProperty] private string _forma = Formas[0];
    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, 1, 1);
    [ObservableProperty] private DateTime? _fim = new DateTime(DateTime.Today.Year, 12, 31);
    [ObservableProperty] private short _nivel = 3;
    [ObservableProperty] private string _resumo = "";

    protected override string TituloRelatorio => _tabelas.FirstOrDefault()?.Titulo ?? Relatorio;
    protected override string Parametros => $"{Data(Inicio, default):dd/MM/yyyy} a {Data(Fim, default):dd/MM/yyyy}";
    protected override string NomeArquivo => "grupo_" + (Relatorio.StartsWith("Bal", StringComparison.Ordinal) ? (Relatorio.StartsWith("Balanço", StringComparison.Ordinal) ? "balanco" : "balancete") : "dre");
    protected override bool Paisagem => Forma == Formas[1];
    protected override IReadOnlyList<TabelaRelatorio> Tabelas() => _tabelas;

    public Task CarregarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var grupo = await app.Filiais().GrupoAsync(empresa, usuario);
        Empresas.Clear();
        foreach (var g in grupo) Empresas.Add(new EmpresaMarcada(g, true));
        Resumo = grupo.Count > 1 ? "Marque as empresas: uma só gera o relatório individual dela; várias, o agrupado." : "A empresa ativa não tem filiais ligadas (Cadastros › Filiais): o relatório é o individual dela.";
    });

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var escolhidas = Empresas.Where(e => e.Marcada).Select(e => new EmpresaRelatorio(e.Empresa.Id, e.Empresa.RazaoSocial)).ToList();
        var svc = new ServicoConsolidacao(app.Relatorios, app.Demonstracoes);
        var forma = Forma == Formas[1] ? FormaConsolidacao.LadoALado : FormaConsolidacao.Agrupado;
        var (ini, fim) = (Data(Inicio, new DateOnly(DateTime.Today.Year, 1, 1)), Data(Fim, DateOnly.FromDateTime(DateTime.Today)));
        Resultado<IReadOnlyList<TabelaRelatorio>> r = Relatorio == Relatorios[0]
            ? Embrulhar(await svc.BalanceteAsync(escolhidas, ini, fim, forma, usuario))
            : Relatorio == Relatorios[1] ? Embrulhar(await svc.DreAsync(escolhidas, ini, fim, Nivel, forma, usuario))
            : await svc.BalancoAsync(escolhidas, fim, Nivel, forma, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        _tabelas = r.Valor;
        Colunas.Clear();
        foreach (var c in _tabelas[0].Colunas) Colunas.Add(c.Titulo);
        Linhas.Clear();
        foreach (var t in _tabelas)
        {
            if (_tabelas.Count > 1) Linhas.Add(new LinhaRelatorioGrupo([.. t.Colunas.Select((_, i) => i == 1 ? t.Titulo.Split(" – ").Last().ToUpperInvariant() : "")], true, 0));
            foreach (var l in t.Linhas)
                Linhas.Add(new LinhaRelatorioGrupo([.. l.Valores.Select((v, i) => (i == 1 ? new string(' ', l.Recuo * 2) : "") + ExportacaoRelatorio.Texto(v, t.Colunas[i].Tipo))], l.Destaque, l.Recuo));
        }
        Resumo = string.Join(" ", _tabelas.Select(t => t.Rodape).Where(x => !string.IsNullOrEmpty(x)).Distinct());
        TemDados = true;
        AtualizarPermissoesExportacao();
    });

    private static Resultado<IReadOnlyList<TabelaRelatorio>> Embrulhar(Resultado<TabelaRelatorio> r) =>
        r.Sucesso ? Resultado<IReadOnlyList<TabelaRelatorio>>.Ok([r.Valor]) : Resultado<IReadOnlyList<TabelaRelatorio>>.Falha(r.Erros);
}
