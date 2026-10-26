using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Relatorios;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Folha.Desktop.ViewModels;

/// <summary>Base dos relatórios da folha por competência: a tabela mostrada na tela é a que vai para PDF, Excel e CSV.</summary>
public abstract partial class RelatorioFolhaBase(IContexto contexto) : RelatorioViewModel(contexto)
{
    [ObservableProperty] private DateTime? _competencia = FolhaExtensoes.Mes(contexto.PeriodoTrabalho);
    [ObservableProperty] private TabelaRelatorio? _tabela;
    [ObservableProperty] private TabelaRelatorio? _tabela2;

    protected DateOnly Comp => FolhaExtensoes.InicioMes(Competencia);
    protected DateOnly CompGerada { get; set; }

    protected override string Parametros => $"Competência {CompGerada:MM/yyyy}";
    protected override bool Paisagem => true;

    protected override IReadOnlyList<TabelaRelatorio> Tabelas() => new[] { Tabela, Tabela2 }.OfType<TabelaRelatorio>().ToArray();

    protected void Mostrar(IReadOnlyList<TabelaRelatorio> tabelas)
    {
        CompGerada = Comp;
        Tabela = tabelas.Count > 0 ? tabelas[0] : null;
        Tabela2 = tabelas.Count > 1 ? tabelas[1] : null;
        TemDados = tabelas.Count > 0;
        AtualizarPermissoesExportacao();
    }

    public void Limpar() { Tabela = Tabela2 = null; TemDados = false; }
}

/// <summary>Folha › Relatórios › Resumo da folha: por funcionário e totais por rubrica.</summary>
public partial class ResumoFolhaViewModel(IContexto contexto) : RelatorioFolhaBase(contexto)
{
    protected override string TituloRelatorio => "Resumo da folha de pagamento";
    protected override string NomeArquivo => $"resumo_folha_{CompGerada:yyyyMM}";

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Folha().Relatorios.ResumoFolhaAsync(empresa, Comp, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Mostrar(r.Valor);
        if (r.Valor[0].Linhas.Count <= 1) Mensagem = "Não há folha calculada nesta competência.";
    }, Permissao.FolhaRelatorios);
}

/// <summary>Folha › Relatórios › Encargos patronais: INSS patronal, RAT x FAP, terceiros e FGTS.</summary>
public partial class EncargosFolhaViewModel(IContexto contexto) : RelatorioFolhaBase(contexto)
{
    protected override string TituloRelatorio => "Encargos patronais da folha";
    protected override string NomeArquivo => $"encargos_folha_{CompGerada:yyyyMM}";

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Folha().Relatorios.EncargosAsync(empresa, Comp, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Mostrar([r.Valor]);
    }, Permissao.FolhaRelatorios);
}

/// <summary>Folha › Relatórios › Provisões: cálculo mensal das provisões de férias e 13º com encargos, e relatório.</summary>
public partial class ProvisoesFolhaViewModel(IContexto contexto) : RelatorioFolhaBase(contexto)
{
    protected override string TituloRelatorio => "Provisões de férias e 13º salário";
    protected override string NomeArquivo => $"provisoes_{CompGerada:yyyyMM}";

    public bool PodeCalcular => Contexto.Pode(Permissao.FolhaCalcular);
    public void AtualizarPermissoes() { OnPropertyChanged(nameof(PodeCalcular)); AtualizarPermissoesExportacao(); }

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Folha().Relatorios.ProvisoesAsync(empresa, Comp, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Mostrar([r.Valor]);
        if (r.Valor.Linhas.Count <= 1) Mensagem = "Não há provisões calculadas nesta competência: clique em \"Calcular provisões\".";
    }, Permissao.FolhaRelatorios);

    [RelayCommand]
    private Task CalcularAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Folha().Folha.CalcularProvisoesAsync(empresa, Comp, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var t = await app.Folha().Relatorios.ProvisoesAsync(empresa, Comp, usuario);
        if (t.Sucesso) Mostrar([t.Valor]);
        foreach (var a in r.Valor.Avisos) Erros.Add(a);
        Mensagem = $"Provisões calculadas para {r.Valor.Calculados} funcionário(s): {Vorticis.Domain.Comum.Formato.Moeda(r.Valor.Liquido)} com encargos.";
    }, Permissao.FolhaCalcular);
}
