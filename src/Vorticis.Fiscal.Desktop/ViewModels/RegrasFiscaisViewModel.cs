using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Documentos;

namespace Vorticis.Fiscal.Desktop.ViewModels;

public sealed record RegraFiscalLinha(RegraFiscal R)
{
    public int Codigo => R.Codigo;
    public string Descricao => R.Descricao;
    public string Operacao => R.Operacao == TipoOperacao.Entrada ? "Entrada" : "Saída";
    public string CfopOrigem => R.CfopOrigem.Length == 4 ? R.CfopOrigem : R.CfopOrigem + "… (todos que começam assim)";
    public string CfopDestino => R.CfopDestino;
    public string Conta => R.ContaContabil ?? "";
    public string Csts => string.Join("  ", new[] { ("ICMS", R.CstIcms), ("PIS", R.CstPis), ("COFINS", R.CstCofins) }.Where(c => c.Item2 is not null).Select(c => $"{c.Item1} {c.Item2}"));
    public string Situacao => R.Ativa ? "Ativa" : "Inativa";
}

/// <summary>
/// Cadastros › Regras fiscais: de/para do CFOP que vem no XML para o CFOP da escrituração, nas notas de entrada (5102 → 1102) e de saída
/// (5102 → 5405, por exemplo), com os CSTs opcionais. Aplicadas sozinhas na importação de XML e pelo botão "Aplicar regras fiscais" da nota.
/// </summary>
public partial class RegrasFiscaisViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public static IReadOnlyList<string> Operacoes { get; } = ["Entrada", "Saída"];

    public ObservableCollection<RegraFiscalLinha> Lista { get; } = [];
    [ObservableProperty] private RegraFiscalLinha? _selecionada;
    [ObservableProperty] private string _codigo = "";
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(RotuloOrigem), nameof(RotuloDestino), nameof(ExemploDestino), nameof(RotuloConta))] private int _operacao;
    [ObservableProperty] private string _cfopOrigem = "";
    [ObservableProperty] private string _cfopDestino = "";
    [ObservableProperty] private string _cstIcms = "";
    [ObservableProperty] private string _cstPis = "";
    [ObservableProperty] private string _cstCofins = "";
    [ObservableProperty] private bool _ativa = true;
    /// <summary>Conta contábil da contabilização: na entrada, a conta debitada (estoque, despesa, custo); na saída, a conta creditada (receita).</summary>
    [ObservableProperty] private string _contaContabil = "";
    [ObservableProperty] private string _nomeContaContabil = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ExemploTeste))] private int _operacaoTeste;

    // CSTs escolhidos na lista com as legendas (o código vai para os campos de texto, que são os gravados)
    public static IReadOnlyList<OpcaoCst> OpcoesIcms => TabelasCst.Icms;
    public static IReadOnlyList<OpcaoCst> OpcoesPisCofins => TabelasCst.PisCofins;
    public OpcaoCst CstIcmsOpcao { get => TabelasCst.Opcao(TabelasCst.Icms, CstIcms); set => CstIcms = value?.Codigo ?? ""; }
    public OpcaoCst CstPisOpcao { get => TabelasCst.Opcao(TabelasCst.PisCofins, CstPis); set => CstPis = value?.Codigo ?? ""; }
    public OpcaoCst CstCofinsOpcao { get => TabelasCst.Opcao(TabelasCst.PisCofins, CstCofins); set => CstCofins = value?.Codigo ?? ""; }
    partial void OnCstIcmsChanged(string value) => OnPropertyChanged(nameof(CstIcmsOpcao));
    partial void OnCstPisChanged(string value) => OnPropertyChanged(nameof(CstPisOpcao));
    partial void OnCstCofinsChanged(string value) => OnPropertyChanged(nameof(CstCofinsOpcao));
    public string RotuloConta => Operacao == 1 ? "Conta contábil (receita, crédito)" : "Conta contábil (débito: estoque, despesa ou custo)";
    [ObservableProperty] private string _teste = "";
    [ObservableProperty] private string _resultadoTeste = "";

    private TipoOperacao Tipo => Operacao == 1 ? TipoOperacao.Saida : TipoOperacao.Entrada;
    public string RotuloOrigem => Operacao == 1 ? "CFOP do XML (saída)" : "CFOP do XML (do fornecedor)";
    public string RotuloDestino => Operacao == 1 ? "CFOP da escrituração (saída)" : "CFOP da escrituração (entrada)";
    public string ExemploDestino => Operacao == 1 ? "5102" : "1102";
    public string ExemploTeste => "5102";

    public bool PodeManter => Contexto.Pode(Permissao.FiscalDocumentosImportar);
    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeManter));

    partial void OnSelecionadaChanged(RegraFiscalLinha? value)
    {
        if (value?.R is not { } r) return;
        (Codigo, Descricao, Operacao, CfopOrigem, CfopDestino, Ativa) =
            (r.Codigo.ToString(), r.Descricao, r.Operacao == TipoOperacao.Saida ? 1 : 0, r.CfopOrigem, r.CfopDestino, r.Ativa);
        (CstIcms, CstPis, CstCofins) = (r.CstIcms ?? "", r.CstPis ?? "", r.CstCofins ?? "");
        (ContaContabil, NomeContaContabil) = (r.ContaContabil ?? "", "");
    }

    partial void OnOperacaoTesteChanged(int value) => OnTesteChanged(Teste);

    /// <summary>Mostra qual regra vale para o CFOP do XML digitado, na operação escolhida (a mais específica vence).</summary>
    partial void OnTesteChanged(string value)
    {
        var cfop = RegrasFiscais.Digitos(value);
        var op = OperacaoTeste == 1 ? TipoOperacao.Saida : TipoOperacao.Entrada;
        ResultadoTeste = cfop.Length != 4 ? ""
            : RegrasFiscais.Encontrar(Lista.Select(l => l.R), op, cfop) is { } r ? $"CFOP {cfop} é escriturado como {r.CfopDestino} ({r.Descricao})."
            : $"Nenhuma regra para o CFOP {cfop}: o item fica com o CFOP do XML até você ajustar a escrituração.";
    }

    private int ProximoCodigo => (Lista.Count == 0 ? 0 : Lista.Max(n => n.Codigo)) + 1;

    public Task CarregarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var itens = await app.Fiscal().RegrasFiscais.ListarAsync(empresa, usuario);
        Lista.Clear();
        foreach (var r in itens) Lista.Add(new RegraFiscalLinha(r));
        if (Selecionada is null && Codigo == "") Codigo = ProximoCodigo.ToString();
        OnTesteChanged(Teste);
    });

    /// <summary>Consulta o plano de contas (com inclusão de conta nova) e preenche a conta contábil da regra.</summary>
    public async Task EscolherContaAsync(Avalonia.Controls.Window dono)
    {
        if (await Vorticis.Desktop.Comum.Views.SeletorContaContabil.EscolherAsync(dono, Contexto, ContaContabil) is { } c)
            (ContaContabil, NomeContaContabil) = (c.Codigo, c.Descricao);
    }

    [RelayCommand]
    private void Nova()
    {
        Selecionada = null;
        (Codigo, Descricao, CfopOrigem, CfopDestino, CstIcms, CstPis, CstCofins, Ativa) = (ProximoCodigo.ToString(), "", "", "", "", "", "", true);
        (ContaContabil, NomeContaContabil) = ("", "");
    }

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = new RegraFiscal(Selecionada?.R.Id ?? 0, int.TryParse(Codigo.Trim(), out var c) ? c : 0, Descricao, Tipo, CfopOrigem, CfopDestino, CstIcms, CstPis, CstCofins, Ativa, ContaContabil);
        var g = await app.Fiscal().RegrasFiscais.GravarAsync(empresa, r, usuario);
        if (!g.Sucesso) { MostrarResultado(g, ""); return; }
        await CarregarAsync();
        Selecionada = Lista.FirstOrDefault(l => l.R.Id == g.Valor);
        Mensagem = "Regra fiscal gravada.";
    }, Permissao.FiscalDocumentosImportar);

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada?.R is not { } r) { Erros.Add("Selecione a regra."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir regra fiscal", $"Excluir a regra {r.Codigo} – {r.Descricao}? As notas já importadas não mudam.")) return;
        var x = await app.Fiscal().RegrasFiscais.ExcluirAsync(empresa, r.Id, usuario);
        if (!x.Sucesso) { MostrarResultado(x, ""); return; }
        Nova();
        await CarregarAsync();
        Mensagem = "Regra fiscal excluída.";
    }, Permissao.FiscalDocumentosImportar);

    [RelayCommand]
    private Task IncluirSugeridasAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Fiscal().RegrasFiscais.IncluirSugeridasAsync(empresa, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = r.Valor == 0 ? "As regras sugeridas já estão cadastradas." : $"{r.Valor} regra(s) sugerida(s) incluída(s). Confira se valem para a operação da empresa.";
    }, Permissao.FiscalDocumentosImportar);
}
