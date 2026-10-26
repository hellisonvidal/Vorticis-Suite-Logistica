using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Documentos;

namespace Vorticis.Fiscal.Desktop.ViewModels;

public sealed record NaturezaLinha(NaturezaEntrada N)
{
    public int Codigo => N.Codigo;
    public string Descricao => N.Descricao;
    public string CfopSaida => N.CfopSaida.Length == 4 ? N.CfopSaida : N.CfopSaida + "… (todos que começam assim)";
    public string CfopEntrada => N.CfopEntrada;
    public string Situacao => N.Ativa ? "Ativa" : "Inativa";
}

/// <summary>
/// Cadastros › Naturezas de entrada: diretrizes de troca do CFOP de saída do emitente (o que vem no XML) pelo CFOP de entrada da escrituração.
/// Aplicadas sozinhas na importação de XML de entrada e pelo botão "Aplicar naturezas" na aba Escrituração da nota.
/// </summary>
public partial class NaturezasViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public ObservableCollection<NaturezaLinha> Lista { get; } = [];
    [ObservableProperty] private NaturezaLinha? _selecionada;
    [ObservableProperty] private string _codigo = "";
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private string _cfopSaida = "";
    [ObservableProperty] private string _cfopEntrada = "";
    [ObservableProperty] private bool _ativa = true;
    [ObservableProperty] private string _teste = "";
    [ObservableProperty] private string _resultadoTeste = "";

    public bool PodeManter => Contexto.Pode(Permissao.FiscalDocumentosImportar);
    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeManter));

    partial void OnSelecionadaChanged(NaturezaLinha? value)
    {
        if (value?.N is not { } n) return;
        (Codigo, Descricao, CfopSaida, CfopEntrada, Ativa) = (n.Codigo.ToString(), n.Descricao, n.CfopSaida, n.CfopEntrada, n.Ativa);
    }

    /// <summary>Mostra qual natureza vale para o CFOP de saída digitado (a mais específica vence).</summary>
    partial void OnTesteChanged(string value)
    {
        var cfop = new string([.. value.Where(char.IsAsciiDigit)]);
        ResultadoTeste = cfop.Length != 4 ? ""
            : RegrasNaturezas.Encontrar(Lista.Select(l => l.N), cfop) is { } n ? $"CFOP {cfop} entra como {n.CfopEntrada} ({n.Descricao})."
            : $"Nenhuma natureza para o CFOP {cfop}: o item fica com o CFOP do XML até você ajustar a escrituração.";
    }

    public Task CarregarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var itens = await app.Fiscal().Naturezas.ListarAsync(empresa, usuario);
        Lista.Clear();
        foreach (var n in itens) Lista.Add(new NaturezaLinha(n));
        if (Selecionada is null && Codigo == "") Codigo = ((itens.Count == 0 ? 0 : itens.Max(n => n.Codigo)) + 1).ToString();
        OnTesteChanged(Teste);
    });

    [RelayCommand]
    private void Nova()
    {
        Selecionada = null;
        (Codigo, Descricao, CfopSaida, CfopEntrada, Ativa) = (((Lista.Count == 0 ? 0 : Lista.Max(n => n.Codigo)) + 1).ToString(), "", "", "", true);
    }

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var n = new NaturezaEntrada(Selecionada?.N.Id ?? 0, int.TryParse(Codigo.Trim(), out var c) ? c : 0, Descricao, CfopSaida, CfopEntrada, Ativa);
        var r = await app.Fiscal().Naturezas.GravarAsync(empresa, n, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Selecionada = Lista.FirstOrDefault(l => l.N.Id == r.Valor);
        Mensagem = "Natureza gravada.";
    }, Permissao.FiscalDocumentosImportar);

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada?.N is not { } n) { Erros.Add("Selecione a natureza."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir natureza", $"Excluir a natureza {n.Codigo} – {n.Descricao}? As notas já importadas não mudam.")) return;
        var r = await app.Fiscal().Naturezas.ExcluirAsync(empresa, n.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Nova();
        await CarregarAsync();
        Mensagem = "Natureza excluída.";
    }, Permissao.FiscalDocumentosImportar);

    [RelayCommand]
    private Task IncluirSugeridasAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Fiscal().Naturezas.IncluirSugeridasAsync(empresa, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = r.Valor == 0 ? "As naturezas sugeridas já estão cadastradas." : $"{r.Valor} natureza(s) sugerida(s) incluída(s). Confira se valem para a operação da empresa.";
    }, Permissao.FiscalDocumentosImportar);
}
