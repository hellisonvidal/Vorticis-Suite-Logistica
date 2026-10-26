using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Desktop.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Documentos;
using Vorticis.Fiscal.Produtos;
using Vorticis.Fiscal.Relatorios;

namespace Vorticis.Fiscal.Desktop.ViewModels;

public sealed record ProdutoLinha(Produto Produto)
{
    public int Codigo => Produto.Codigo;
    public string Descricao => Produto.Descricao;
    public string Unidade => Produto.Unidade;
    public string Ncm => Produto.Ncm ?? "";
    public string Gtin => Produto.Gtin ?? "";
    public string Tipo => $"{Produto.TipoItem} - {TiposItem.Rotulo(Produto.TipoItem)}";
    public string Ativo => Produto.Ativo ? "Sim" : "Não";
}

/// <summary>Cadastros › Produtos: mercadorias com NCM, CEST, GTIN, origem, tipo do item (SPED) e padrões de CFOP, CST e alíquotas.</summary>
public partial class ProdutosViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public static IReadOnlyList<string> Tipos { get; } = TiposItem.Todos.Select(t => $"{t.Codigo} - {t.Descricao}").ToList();
    public static IReadOnlyList<string> Origens { get; } = OrigensMercadoria.Todas.Select(o => $"{o.Codigo} - {o.Descricao}").ToList();

    public ObservableCollection<ProdutoLinha> Produtos { get; } = [];

    [ObservableProperty] private ProdutoLinha? _selecionado;
    [ObservableProperty] private string _filtro = "";
    [ObservableProperty] private bool _somenteAtivos;
    [ObservableProperty] private string _codigo = "(novo)";
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private string _unidade = "UN";
    [ObservableProperty] private string _ncm = "";
    [ObservableProperty] private string _cest = "";
    [ObservableProperty] private string _gtin = "";
    [ObservableProperty] private int _origem;
    [ObservableProperty] private int _tipo;
    [ObservableProperty] private string _cfopEntrada = "";
    [ObservableProperty] private string _cfopSaida = "";
    [ObservableProperty] private string _cstEntrada = "";
    [ObservableProperty] private string _cstSaida = "";
    [ObservableProperty] private string _aliqIcms = "";
    [ObservableProperty] private string _aliqIpi = "";
    [ObservableProperty] private string _aliqPis = "";
    [ObservableProperty] private string _aliqCofins = "";
    [ObservableProperty] private bool _ativo = true;

    public bool PodeManter => Contexto.Pode(Permissao.FiscalDocumentosImportar);

    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeManter));

    private static string T(decimal v) => v == 0 ? "" : v.ToString("0.####", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

    public Task CarregarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Fiscal().Produtos.ListarAsync(empresa, SomenteAtivos, Filtro, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Produtos.Clear();
        foreach (var p in r.Valor) Produtos.Add(new ProdutoLinha(p));
        Mensagem = r.Valor.Count == 0 ? "Nenhum produto encontrado." : null;
    }, Permissao.FiscalDocumentosConsultar);

    [RelayCommand]
    private Task ConsultarAsync() => CarregarAsync();

    partial void OnSelecionadoChanged(ProdutoLinha? value)
    {
        if (value is null) return;
        var p = value.Produto;
        (Codigo, Descricao, Unidade, Ncm, Cest, Gtin) = (p.Codigo.ToString(), p.Descricao, p.Unidade, p.Ncm ?? "", p.Cest ?? "", p.Gtin ?? "");
        (Origem, Tipo) = (Math.Max(0, OrigensMercadoria.Todas.ToList().FindIndex(o => o.Codigo == p.Origem)), Math.Max(0, TiposItem.Todos.ToList().FindIndex(t => t.Codigo == p.TipoItem)));
        (CfopEntrada, CfopSaida, CstEntrada, CstSaida) = (p.CfopEntrada ?? "", p.CfopSaida ?? "", p.CstIcmsEntrada ?? "", p.CstIcmsSaida ?? "");
        (AliqIcms, AliqIpi, AliqPis, AliqCofins, Ativo) = (T(p.AliqIcms), T(p.AliqIpi), T(p.AliqPis), T(p.AliqCofins), p.Ativo);
    }

    [RelayCommand]
    private void Novo()
    {
        Selecionado = null;
        (Codigo, Descricao, Unidade, Ncm, Cest, Gtin, Origem, Tipo) = ("(novo)", "", "UN", "", "", "", 0, 0);
        (CfopEntrada, CfopSaida, CstEntrada, CstSaida, AliqIcms, AliqIpi, AliqPis, AliqCofins, Ativo) = ("", "", "", "", "", "", "", "", true);
        Erros.Clear();
        Mensagem = null;
    }

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        static decimal D(string t) => Numeros.LerValor(t) ?? 0m;
        var p = new Produto(Selecionado?.Produto.Id ?? 0, Selecionado?.Produto.Codigo ?? 0, Descricao, Unidade, Ncm, Cest, Gtin,
            OrigensMercadoria.Todas[Math.Clamp(Origem, 0, 8)].Codigo, TiposItem.Todos[Math.Clamp(Tipo, 0, TiposItem.Todos.Count - 1)].Codigo,
            CfopEntrada, CfopSaida, CstEntrada, CstSaida, D(AliqIcms), D(AliqIpi), D(AliqPis), D(AliqCofins), Ativo);
        var r = await app.Fiscal().Produtos.GravarAsync(empresa, p, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Selecionado = Produtos.FirstOrDefault(x => x.Produto.Id == r.Valor);
        Mensagem = "Produto gravado.";
    }, Permissao.FiscalDocumentosImportar);

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado is not { } s) { Erros.Add("Selecione um produto."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir produto", $"Excluir o produto {s.Codigo} - {s.Descricao}?", "Excluir", "Cancelar")) return;
        var r = await app.Fiscal().Produtos.ExcluirAsync(empresa, s.Produto.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Novo();
        Mensagem = "Produto excluído.";
    }, Permissao.FiscalDocumentosImportar);
}

/// <summary>Relatórios › Produtos cadastrados.</summary>
public partial class RelatorioProdutosViewModel(IContexto contexto) : RelatorioFiscalViewModel(contexto)
{
    [ObservableProperty] private string _filtro = "";
    [ObservableProperty] private bool _somenteAtivos = true;

    protected override string TituloRelatorio => "Produtos cadastrados";
    protected override string Parametros => (SomenteAtivos ? "Somente ativos" : "Ativos e inativos") + (string.IsNullOrWhiteSpace(Filtro) ? "" : $" · Filtro: {Filtro.Trim()}");
    protected override string NomeArquivo => "produtos_cadastrados";

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Fiscal().Produtos.RelatorioProdutosAsync(empresa, SomenteAtivos, Filtro, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); Limpar(); return; }
        Mostrar(TabelasFiscais.Produtos(r.Valor));
        Mensagem = r.Valor.Count == 0 ? "Nenhum produto cadastrado." : "Escolha o formato em Exportar: PDF, Excel (.xlsx) ou CSV.";
    }, Permissao.FiscalRelatorios);
}

/// <summary>Relatórios › Itens das notas fiscais, com filtros de período, entrada/saída, participante, produto, CFOP e NCM.</summary>
public partial class RelatorioItensNotasViewModel(IContexto contexto) : RelatorioFiscalViewModel(contexto)
{
    private (DateOnly Ini, DateOnly Fim) _periodo;
    private string _descricaoFiltros = "";

    public static IReadOnlyList<string> Operacoes { get; } = ["Entradas e saídas", "Somente entradas", "Somente saídas"];

    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _fim = DateTime.Today;
    [ObservableProperty] private int _operacao;
    [ObservableProperty] private string _participante = "";
    [ObservableProperty] private string _produto = "";
    [ObservableProperty] private string _cfop = "";
    [ObservableProperty] private string _ncm = "";

    protected override string TituloRelatorio => "Itens das notas fiscais";
    protected override string Parametros => $"Período: {_periodo.Ini:dd/MM/yyyy} a {_periodo.Fim:dd/MM/yyyy} · {Operacoes[Operacao]}{_descricaoFiltros}";
    protected override string NomeArquivo => $"itens_notas_{_periodo.Ini:yyyyMMdd}_{_periodo.Fim:yyyyMMdd}";

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var ini = Data(Inicio, new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1));
        var fim = Data(Fim, ini);
        long? participanteId = null, produtoId = null;
        var desc = "";
        if (!string.IsNullOrWhiteSpace(Participante))
        {
            var p = await Resolvedores.ParticipanteAsync(app, empresa, usuario, Participante);
            if (p is null) { Erros.Add($"Participante \"{Participante.Trim()}\" não encontrado."); Limpar(); return; }
            (participanteId, desc) = (p.Value.Id, desc + $" · Participante: {p.Value.Nome}");
        }
        if (!string.IsNullOrWhiteSpace(Produto))
        {
            var p = await Resolvedores.ProdutoAsync(app, empresa, usuario, Produto, false);
            if (p is null) { Erros.Add($"Produto \"{Produto.Trim()}\" não encontrado ou ambíguo."); Limpar(); return; }
            (produtoId, desc) = (p.Id, desc + $" · Produto: {p.Codigo} - {p.Descricao}");
        }
        if (!string.IsNullOrWhiteSpace(Cfop)) desc += $" · CFOP: {Cfop.Trim()}";
        if (!string.IsNullOrWhiteSpace(Ncm)) desc += $" · NCM: {Ncm.Trim()}";
        var filtro = new FiltroItensNotas(ini, fim, Operacao switch { 1 => TipoOperacao.Entrada, 2 => TipoOperacao.Saida, _ => null }, participanteId, produtoId, Cfop, Ncm);
        var r = await app.Fiscal().Produtos.RelatorioItensAsync(empresa, filtro, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); Limpar(); return; }
        (_periodo, _descricaoFiltros) = ((ini, fim), desc);
        Mostrar(TabelasFiscais.ItensNotas(r.Valor));
        Mensagem = r.Valor.Count == 0 ? "Nenhum item encontrado." : "Escolha o formato em Exportar: PDF, Excel (.xlsx) ou CSV.";
    }, Permissao.FiscalRelatorios);
}
