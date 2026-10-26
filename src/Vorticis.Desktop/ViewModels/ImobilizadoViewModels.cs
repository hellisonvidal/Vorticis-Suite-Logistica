using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Imobilizado;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Imobilizado;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Desktop.ViewModels;

public sealed record OpcaoMetodo(MetodoDepreciacao Metodo, string Nome)
{
    public static IReadOnlyList<OpcaoMetodo> Todos { get; } =
        [new(MetodoDepreciacao.Linear, "Linear (quotas constantes)"), new(MetodoDepreciacao.SomaDigitos, "Soma dos dígitos (decrescente)")];
}

public sealed record BemLinha(BemImobilizado Bem, string Grupo)
{
    public int Codigo => Bem.Codigo;
    public string Descricao => Bem.Descricao;
    public string Aquisicao => Bem.DataAquisicao.ToString("dd/MM/yyyy");
    public string Valor => Formato.Moeda(Bem.Valor);
    public string Acumulada => Formato.Moeda(Bem.Acumulada);
    public string ValorContabil => Formato.Moeda(Bem.ValorContabil);
    public string Situacao => Bem.DataBaixa is { } b ? $"Baixado {b:dd/MM/yyyy}" : Bem.UltimaCompetencia is { } u ? $"Depreciado até {u:MM/yyyy}" : "Ativo";
}

/// <summary>Imobilizado › Cadastro de bens.</summary>
public partial class BensViewModel(IContexto contexto) : CadastroViewModel(contexto)
{
    private IReadOnlyList<BemImobilizado> _todos = [];

    public ObservableCollection<BemLinha> Lista { get; } = [];
    public ObservableCollection<GrupoBem> Grupos { get; } = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TemSelecionado))] private BemLinha? _selecionado;
    [ObservableProperty] private string _filtro = "";
    [ObservableProperty] private string _resumo = "";

    [ObservableProperty] private string _codigo = "";
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(RegraFiscal))] private GrupoBem? _grupo;
    [ObservableProperty] private string _plaqueta = "";
    [ObservableProperty] private string _localizacao = "";
    [ObservableProperty] private string _notaFiscal = "";
    [ObservableProperty] private string _fornecedor = "";
    [ObservableProperty] private DateTime? _dataAquisicao = DateTime.Today;
    [ObservableProperty] private DateTime? _inicioUso = DateTime.Today;
    [ObservableProperty] private string _valor = "";
    [ObservableProperty] private string _residual = "0,00";
    [ObservableProperty] private string _vidaUtil = "";
    [ObservableProperty] private OpcaoMetodo _metodo = OpcaoMetodo.Todos[0];
    [ObservableProperty] private int _turnos = 1;
    [ObservableProperty] private bool _proRataDia;
    [ObservableProperty] private string _depreciacaoAnterior = "0,00";
    [ObservableProperty] private string _contaBem = "";
    [ObservableProperty] private string _contaAcumulada = "";
    [ObservableProperty] private string _contaDespesa = "";
    [ObservableProperty] private DateTime? _dataBaixa;
    [ObservableProperty] private string _motivoBaixa = "";

    public static IReadOnlyList<int> OpcoesTurnos { get; } = [1, 2, 3];
    public bool TemSelecionado => Selecionado is not null;
    public bool PodeEditar => Contexto.Pode(Permissao.LancamentoIncluir);
    public override bool PodeManter => PodeEditar;
    protected override string NomeRegistro => "bem";
    protected override string? DescricaoSelecionado => Selecionado is { } s ? $"{s.Codigo} – {s.Descricao}" : null;
    protected override string DetalheExclusao => "\n\nBem com depreciação lançada não é excluído: registre a baixa.";

    public string RegraFiscal => Grupo is not { } g ? ""
        : !g.Deprecia ? $"{g.Descricao}: não se deprecia. {g.BaseLegal}"
        : $"RFB: {g.TaxaAnualFiscal.ToString("0.##", Formato.PtBr)}% ao ano ({g.VidaUtilFiscalMeses} meses). {g.BaseLegal}";

    public void AtualizarPermissoes()
    {
        OnPropertyChanged(nameof(PodeEditar));
        AtualizarBarra();
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var grupos = await app.Imobilizado.ListarGruposAsync();
        var bens = await app.Imobilizado.ListarBensAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        var grupoAtual = Grupo?.Id;
        Grupos.Clear();
        foreach (var x in grupos) Grupos.Add(x);
        Grupo = Grupos.FirstOrDefault(x => x.Id == grupoAtual);
        _todos = bens;
        Filtrar();
        var ativos = bens.Where(b => b.DataBaixa is null).ToList();
        Resumo = $"{ativos.Count} bem(ns) ativo(s) · custo {Formato.Moeda(ativos.Sum(b => b.Valor))} · depreciação acumulada {Formato.Moeda(ativos.Sum(b => b.Acumulada))}" +
                 $" · valor contábil {Formato.Moeda(ativos.Sum(b => b.ValorContabil))}";
    }

    partial void OnFiltroChanged(string value) => Filtrar();

    private void Filtrar()
    {
        var id = Selecionado?.Bem.Id;
        var f = Filtro.Trim();
        var nomes = Grupos.ToDictionary(x => x.Id, x => x.Descricao);
        Lista.Clear();
        foreach (var b in _todos.Where(b => f.Length == 0 || b.Codigo.ToString() == f || b.Descricao.Contains(f, StringComparison.CurrentCultureIgnoreCase)
                                            || (b.Plaqueta ?? "").Contains(f, StringComparison.CurrentCultureIgnoreCase)))
            Lista.Add(new BemLinha(b, nomes.GetValueOrDefault(b.GrupoId, "")));
        Selecionado = Lista.FirstOrDefault(l => l.Bem.Id == id);
    }

    partial void OnSelecionadoChanged(BemLinha? value)
    {
        if (EmConsulta) MostrarSelecionado();
        AtualizarBarra();
    }

    /// <summary>Ao escolher o grupo na inclusão: vida útil sugerida pela taxa fiscal e contas do grupo.</summary>
    partial void OnGrupoChanged(GrupoBem? value)
    {
        if (!EmInclusao || value is null) return;
        VidaUtil = value.Deprecia ? value.VidaUtilFiscalMeses.ToString() : "0";
        (ContaBem, ContaAcumulada, ContaDespesa) = (value.ContaBem ?? "", value.ContaAcumulada ?? "", value.ContaDespesa ?? "");
    }

    protected override void MostrarSelecionado()
    {
        var b = Selecionado?.Bem;
        Codigo = b?.Codigo.ToString() ?? "";
        (Descricao, Plaqueta, Localizacao, NotaFiscal, Fornecedor) = (b?.Descricao ?? "", b?.Plaqueta ?? "", b?.Localizacao ?? "", b?.NotaFiscal ?? "", b?.CodigoFornecedor ?? "");
        Grupo = b is null ? null : Grupos.FirstOrDefault(g => g.Id == b.GrupoId);
        DataAquisicao = b is null ? DateTime.Today : b.DataAquisicao.ToDateTime(TimeOnly.MinValue);
        InicioUso = b is null ? DateTime.Today : b.InicioUso.ToDateTime(TimeOnly.MinValue);
        (Valor, Residual, VidaUtil) = (b is null ? "" : Formato.Moeda(b.Valor), Formato.Moeda(b?.Residual ?? 0), b?.VidaUtilMeses.ToString() ?? "");
        Metodo = OpcaoMetodo.Todos.First(m => m.Metodo == (b?.Metodo ?? MetodoDepreciacao.Linear));
        (Turnos, DepreciacaoAnterior, ProRataDia) = (b?.Turnos ?? 1, Formato.Moeda(b?.DepreciacaoAnterior ?? 0), b?.ProRataDia ?? false);
        (ContaBem, ContaAcumulada, ContaDespesa) = (b?.ContaBem ?? "", b?.ContaAcumulada ?? "", b?.ContaDespesa ?? "");
        DataBaixa = b?.DataBaixa?.ToDateTime(TimeOnly.MinValue);
        MotivoBaixa = b?.MotivoBaixa ?? "";
    }

    protected override void PrepararInclusao()
    {
        Selecionado = null;
        MostrarSelecionado();
        Codigo = ServicoImobilizado.ProximoCodigo(_todos).ToString();
        Grupo = null;
    }

    protected override async Task<bool> GravarAsync(bool inclusao)
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            if (Grupo is null) { Erros.Add("Escolha o grupo do bem."); return; }
            if (Formato.LerValor(Valor) is not { } valor) { Erros.Add("Valor de aquisição inválido."); return; }
            var residual = Formato.LerValor(Residual) ?? 0;
            var anterior = Formato.LerValor(DepreciacaoAnterior) ?? 0;
            _ = int.TryParse(VidaUtil.Trim(), out var vida);
            _ = int.TryParse(Codigo.Trim(), out var codigo);
            var id = inclusao ? 0 : Selecionado?.Bem.Id ?? 0;
            var r = await app.Imobilizado.GravarBemAsync(empresa, new BemImobilizado(id, codigo, Descricao, Grupo.Id, Plaqueta, Localizacao, NotaFiscal,
                Fornecedor, Data(DataAquisicao, DateOnly.FromDateTime(DateTime.Today)), Data(InicioUso, DateOnly.FromDateTime(DateTime.Today)),
                valor, residual, vida, Metodo.Metodo, Turnos, anterior, ContaBem, ContaAcumulada, ContaDespesa,
                DataBaixa is { } bx ? DateOnly.FromDateTime(bx) : null, MotivoBaixa, ProRataDia: ProRataDia), usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            ok = true;
            Modo = ModoCadastro.Consulta;
            await CarregarAsync();
            Selecionado = Lista.FirstOrDefault(l => l.Bem.Id == r.Valor);
            Mensagem = inclusao ? $"Bem {Selecionado?.Codigo} incluído." : "Bem alterado.";
        }, Permissao.LancamentoIncluir);
        return ok;
    }

    protected override async Task<bool> ExcluirSelecionadoAsync()
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            if (Selecionado is null) return;
            var r = await app.Imobilizado.ExcluirBemAsync(empresa, Selecionado.Bem.Id, usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            ok = true;
            Selecionado = null;
            await CarregarAsync();
            Mensagem = "Bem excluído.";
        }, Permissao.LancamentoIncluir);
        return ok;
    }
}

public sealed record GrupoLinha(GrupoBem Grupo)
{
    public string Codigo => Grupo.Codigo;
    public string Descricao => Grupo.Descricao;
    public string Taxa => Grupo.Deprecia ? Grupo.TaxaAnualFiscal.ToString("0.##", Formato.PtBr) + "%" : "—";
    public string VidaUtil => Grupo.Deprecia ? $"{Grupo.VidaUtilFiscalMeses / 12m:0.#} anos" : "não deprecia";
    public string Contas => string.Join(" / ", new[] { Grupo.ContaBem, Grupo.ContaAcumulada, Grupo.ContaDespesa }.Where(c => c is not null));
    public string BaseLegal => Grupo.BaseLegal ?? "";
}

/// <summary>Imobilizado › Regras de depreciação: grupos de bens com a taxa fiscal (RFB) e as contas sugeridas.</summary>
public partial class GruposDepreciacaoViewModel(IContexto contexto) : CadastroViewModel(contexto)
{
    public ObservableCollection<GrupoLinha> Lista { get; } = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TemSelecionado))] private GrupoLinha? _selecionado;
    [ObservableProperty] private string _codigo = "";
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private string _taxa = "";
    [ObservableProperty] private bool _deprecia = true;
    [ObservableProperty] private bool _intangivel;
    [ObservableProperty] private string _contaBem = "";
    [ObservableProperty] private string _contaAcumulada = "";
    [ObservableProperty] private string _contaDespesa = "";
    [ObservableProperty] private string _baseLegal = "";

    public bool TemSelecionado => Selecionado is not null;
    public bool PodeEditar => Contexto.Pode(Permissao.LancamentoIncluir);
    public override bool PodeManter => PodeEditar;
    protected override string NomeRegistro => "grupo";
    protected override string? DescricaoSelecionado => Selecionado is { } s ? $"{s.Codigo} – {s.Descricao}" : null;

    public void AtualizarPermissoes()
    {
        OnPropertyChanged(nameof(PodeEditar));
        AtualizarBarra();
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app) return;
        var g = NovaGeracao();
        var lista = await app.Imobilizado.ListarGruposAsync();
        if (!EhAtual(g)) return;
        var id = Selecionado?.Grupo.Id;
        Lista.Clear();
        foreach (var x in lista) Lista.Add(new GrupoLinha(x));
        Selecionado = Lista.FirstOrDefault(l => l.Grupo.Id == id);
    }

    partial void OnSelecionadoChanged(GrupoLinha? value)
    {
        if (EmConsulta) MostrarSelecionado();
        AtualizarBarra();
    }

    protected override void MostrarSelecionado()
    {
        var g = Selecionado?.Grupo;
        (Codigo, Descricao, Taxa, Deprecia, Intangivel) = (g?.Codigo ?? "", g?.Descricao ?? "", g?.TaxaAnualFiscal.ToString("0.##", Formato.PtBr) ?? "",
                                                          g?.Deprecia ?? true, g?.Intangivel ?? false);
        (ContaBem, ContaAcumulada, ContaDespesa, BaseLegal) = (g?.ContaBem ?? "", g?.ContaAcumulada ?? "", g?.ContaDespesa ?? "", g?.BaseLegal ?? "");
    }

    protected override void PrepararInclusao()
    {
        Selecionado = null;
        MostrarSelecionado();
        Codigo = Vorticis.Domain.Comum.Sequencia.MenorLivre(Lista.Select(l => l.Codigo)).ToString("00");
    }

    protected override async Task<bool> GravarAsync(bool inclusao)
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            var taxa = Formato.LerValor(Taxa.Replace("%", "")) ?? 0;
            var id = inclusao ? 0 : Selecionado?.Grupo.Id ?? 0;
            var r = await app.Imobilizado.GravarGrupoAsync(empresa,
                new GrupoBem(id, Codigo, Descricao, taxa, Deprecia, Intangivel, ContaBem, ContaAcumulada, ContaDespesa, BaseLegal), usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            ok = true;
            Modo = ModoCadastro.Consulta;
            await CarregarAsync();
            Selecionado = Lista.FirstOrDefault(l => l.Grupo.Id == r.Valor);
            Mensagem = inclusao ? "Grupo incluído." : "Grupo alterado.";
        }, Permissao.LancamentoIncluir);
        return ok;
    }

    protected override async Task<bool> ExcluirSelecionadoAsync()
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            if (Selecionado is null) return;
            var r = await app.Imobilizado.ExcluirGrupoAsync(empresa, Selecionado.Grupo.Id, usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            ok = true;
            Selecionado = null;
            await CarregarAsync();
            Mensagem = "Grupo excluído.";
        }, Permissao.LancamentoIncluir);
        return ok;
    }
}

public sealed record DepreciacaoLinha(ItemDepreciacao Item)
{
    public int Codigo => Item.Bem.Codigo;
    public string Descricao => Item.Bem.Descricao;
    public string Grupo => Item.Grupo.Descricao;
    public string Custo => Formato.Moeda(Item.Bem.Valor);
    public string AcumuladoAnterior => Formato.Moeda(Item.Quota?.AcumuladoAnterior ?? Item.Bem.Acumulada);
    public string Contabil => Item.Quota is { } q ? Formato.Moeda(q.Contabil) : "";
    public string Fiscal => Item.Quota is { } q ? Formato.Moeda(q.Fiscal) : "";
    public string Diferenca => Item.Quota is { Diferenca: not 0 } q ? Formato.Moeda(q.Diferenca) : "";
    public string Saldo => Formato.Moeda(Item.Bem.Valor - (Item.Quota?.AcumuladoApos ?? Item.Bem.Acumulada));
    public string Situacao => Item.Situacao;
}

/// <summary>Imobilizado › Cálculo e lançamento da depreciação mensal.</summary>
public partial class DepreciacaoViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public static IReadOnlyList<string> Meses { get; } =
        Enumerable.Range(1, 12).Select(m => $"{m:00} - {Formato.PtBr.DateTimeFormat.GetMonthName(m)}").ToList();

    public ObservableCollection<DepreciacaoLinha> Linhas { get; } = [];
    [ObservableProperty] private int _mes = DateTime.Today.Month - 1;
    [ObservableProperty] private decimal? _ano = DateTime.Today.Year;
    [ObservableProperty] private string _resumo = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(LancarCommand))] private bool _podeLancar;

    private DateOnly Competencia => new((int)(Ano ?? DateTime.Today.Year), Mes + 1, 1);

    partial void OnMesChanged(int value) => Limpar();
    partial void OnAnoChanged(decimal? value) => Limpar();

    private void Limpar()
    {
        Linhas.Clear();
        Resumo = "";
        PodeLancar = false;
    }

    [RelayCommand]
    private Task CalcularAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        Limpar();
        var r = await app.Imobilizado.CalcularAsync(empresa, Competencia, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        foreach (var i in r.Valor) Linhas.Add(new DepreciacaoLinha(i));
        var quotas = r.Valor.Select(i => i.Quota).OfType<QuotaDepreciacao>().ToList();
        var contabil = quotas.Sum(q => q.Contabil);
        Resumo = r.Valor.Count == 0 ? "Nenhum bem cadastrado."
            : $"{Competencia:MM/yyyy}: depreciação contábil {Formato.Moeda(contabil)} · fiscal {Formato.Moeda(quotas.Sum(q => q.Fiscal))}" +
              $" · diferença (LALUR) {Formato.Moeda(quotas.Sum(q => q.Diferenca))}";
        PodeLancar = contabil > 0;
    });

    [RelayCommand(CanExecute = nameof(PodeLancar))]
    private Task LancarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (!await Contexto.ConfirmarAsync("Lançar depreciação",
                $"Lançar a depreciação de {Competencia:MM/yyyy} na contabilidade (último dia do mês)?", "Lançar", "Cancelar")) return;
        var r = await app.Imobilizado.LancarAsync(empresa, Competencia, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var msg = $"Depreciação de {Competencia:MM/yyyy} lançada: lançamento nº {r.Valor.Numero}.";
        await CalcularAsync();
        Mensagem = msg;
    }, Permissao.LancamentoIncluir);

    [RelayCommand]
    private Task EstornarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (!await Contexto.ConfirmarAsync("Estornar depreciação",
                $"Estornar a depreciação lançada em {Competencia:MM/yyyy}? Ela poderá ser calculada e lançada de novo.", "Estornar", "Cancelar")) return;
        var r = await app.Imobilizado.EstornarAsync(empresa, Competencia, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CalcularAsync();
        Mensagem = $"Depreciação de {Competencia:MM/yyyy} estornada.";
    }, Permissao.LancamentoEstornar);
}
