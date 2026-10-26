using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Imobilizado;
using Vorticis.Domain.Imobilizado;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Documentos;

namespace Vorticis.Fiscal.Desktop.ViewModels;

/// <summary>Bens a cadastrar vindos de uma nota de entrada com CFOP de imobilizado.</summary>
public sealed record PendenciaImobilizado(string NotaFiscal, string? CodigoFornecedor, string Fornecedor, DateOnly Aquisicao, DateOnly InicioUso,
                                          IReadOnlyList<ItemFiscal> Itens)
{
    private static readonly ConditionalWeakTable<IContexto, PendenciaImobilizado> Caixa = new();

    /// <summary>Deixa a pendência para a tela "Bens do imobilizado da nota" da mesma janela, que a lê ao abrir.</summary>
    public static void Entregar(IContexto contexto, PendenciaImobilizado p) => Caixa.AddOrUpdate(contexto, p);

    public static PendenciaImobilizado? Retirar(IContexto contexto) => Caixa.TryGetValue(contexto, out var p) && Caixa.Remove(contexto) ? p : null;
}

/// <summary>Linha editável: um bem por item da nota, com o grupo (regra de depreciação) e as contas sugeridas pelo grupo.</summary>
public sealed partial class BemDaNotaLinha : ObservableObject
{
    public BemDaNotaLinha(ItemFiscal item, GrupoBem? grupo)
    {
        Item = item;
        _descricao = item.Descricao ?? $"Item {item.Numero}";
        _valor = item.ValorContabil > 0 ? item.ValorContabil : item.ValorProduto;
        Grupo = grupo;
    }

    public ItemFiscal Item { get; }
    public string Cfop => Item.Cfop;
    [ObservableProperty] private bool _cadastrar = true;
    [ObservableProperty] private string _descricao;
    [ObservableProperty] private decimal _valor;
    [ObservableProperty] private GrupoBem? _grupo;
    [ObservableProperty] private decimal? _vidaUtil;
    [ObservableProperty] private string _contaBem = "";
    [ObservableProperty] private string _contaAcumulada = "";
    [ObservableProperty] private string _contaDespesa = "";
    [ObservableProperty] private string _plaqueta = "";
    [ObservableProperty] private string _localizacao = "";
    [ObservableProperty] private string _situacao = "";

    /// <summary>Trocar o grupo traz a vida útil fiscal e as contas sugeridas dele.</summary>
    partial void OnGrupoChanged(GrupoBem? value)
    {
        if (value is null) return;
        VidaUtil = value.Deprecia ? value.VidaUtilFiscalMeses : 0;
        (ContaBem, ContaAcumulada, ContaDespesa) = (value.ContaBem ?? "", value.ContaAcumulada ?? "", value.ContaDespesa ?? "");
    }
}

/// <summary>
/// Lançamentos › Bens do imobilizado da nota: aberta ao gravar uma nota de entrada com CFOP de ativo imobilizado (1.551, 2.551,
/// 3.551, 1.406, 2.406). Cada item vira um bem no cadastro do Contábil (Imobilizado › Cadastro de bens), com a nota, o fornecedor,
/// a data de aquisição e o valor já preenchidos.
/// </summary>
public partial class BensDaNotaViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public ObservableCollection<BemDaNotaLinha> Linhas { get; } = [];
    public ObservableCollection<GrupoBem> Grupos { get; } = [];

    [ObservableProperty] private string _cabecalho = "Nenhuma nota com bens do imobilizado pendente. Grave uma nota de entrada com CFOP 1.551, 2.551, 3.551, 1.406 ou 2.406.";
    [ObservableProperty] private DateTime? _aquisicao;
    [ObservableProperty] private DateTime? _inicioUso;
    private PendenciaImobilizado? _pendencia;

    public bool TemLinhas => Linhas.Count > 0;

    public void AtualizarPermissoes() => OnPropertyChanged(nameof(TemLinhas));

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app) return;
        if (Grupos.Count == 0)
            foreach (var g in (await app.Imobilizado.ListarGruposAsync()).OrderBy(g => g.Codigo, StringComparer.Ordinal)) Grupos.Add(g);
        if (PendenciaImobilizado.Retirar(Contexto) is not { } p) return;
        _pendencia = p;
        (Aquisicao, InicioUso) = (p.Aquisicao.ToDateTime(TimeOnly.MinValue), p.InicioUso.ToDateTime(TimeOnly.MinValue));
        Cabecalho = $"Nota {p.NotaFiscal} de {p.Fornecedor}: {p.Itens.Count} item(ns) com CFOP de ativo imobilizado. Escolha o grupo de cada bem, confira as contas e clique em Cadastrar no imobilizado.";
        Linhas.Clear();
        foreach (var i in p.Itens)
        {
            var linha = new BemDaNotaLinha(i, null);
            Linhas.Add(linha);
        }
        OnPropertyChanged(nameof(TemLinhas));
        Erros.Clear();
        Mensagem = null;
    }

    [RelayCommand]
    private Task CadastrarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (_pendencia is not { } p || Linhas.Count == 0) { Erros.Add("Não há bens pendentes."); return; }
        var aquisicao = Data(Aquisicao, p.Aquisicao);
        var inicio = Data(InicioUso, aquisicao);
        var gravados = 0;
        foreach (var l in Linhas.Where(l => l.Cadastrar && l.Situacao != "Cadastrado").ToList())
        {
            if (l.Grupo is not { } g) { l.Situacao = "Escolha o grupo"; continue; }
            var bem = new BemImobilizado(0, 0, l.Descricao, g.Id, l.Plaqueta, l.Localizacao, p.NotaFiscal, p.CodigoFornecedor, aquisicao, inicio,
                l.Valor, 0, (int)(l.VidaUtil ?? 0), MetodoDepreciacao.Linear, 1, 0, l.ContaBem, l.ContaAcumulada, l.ContaDespesa, null, null);
            var r = await app.Imobilizado.GravarBemAsync(empresa, bem, usuario);
            if (r.Sucesso) { l.Situacao = "Cadastrado"; gravados++; }
            else l.Situacao = string.Join(" ", r.Erros);
        }
        var faltam = Linhas.Count(l => l.Cadastrar && l.Situacao != "Cadastrado");
        Mensagem = faltam == 0
            ? $"{gravados} bem(ns) cadastrado(s) no imobilizado (Contábil › Imobilizado › Cadastro de bens)."
            : $"{gravados} bem(ns) cadastrado(s); {faltam} com pendência (veja a coluna Situação).";
    }, Permissao.LancamentoIncluir);
}
