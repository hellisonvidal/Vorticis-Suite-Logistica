using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Contabilizacao;
using Vorticis.Fiscal.Documentos;

namespace Vorticis.Fiscal.Desktop.ViewModels;

public sealed record RegraLinha(RegraContabilizacao Regra)
{
    public int Prioridade => Regra.Prioridade;
    public string Operacao => Regra.Operacao?.Rotulo() ?? "Qualquer";
    public string Cfop => Regra.Cfop ?? "Qualquer";
    public string Participante => Regra.ParticipanteNome ?? "Qualquer";
    public string Debito => Regra.ContaDebito;
    public string Credito => Regra.ContaCredito;
    public string Historico => Regra.Historico;
    public string Ativa => Regra.Ativo ? "Sim" : "Não";
}

/// <summary>
/// Documentos fiscais › Regras de contabilização: por CFOP (código completo ou prefixo, ou NFSE para serviços), sentido e, se quiser, participante,
/// indicam a conta de débito e a de crédito. Vale a regra mais específica; no empate, a de menor prioridade.
/// </summary>
public partial class RegrasContabilizacaoViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public static IReadOnlyList<string> Operacoes { get; } = ["Qualquer", "Entrada", "Saída"];
    public IReadOnlyList<VariavelHistorico> Variaveis => VariaveisHistorico.Todas;
    public ObservableCollection<RegraLinha> Regras { get; } = [];

    [ObservableProperty] private RegraLinha? _selecionada;
    [ObservableProperty] private int _prioridade = 100;
    [ObservableProperty] private int _operacao;
    [ObservableProperty] private string _cfop = "";
    [ObservableProperty] private string _participante = "";
    [ObservableProperty] private string _debito = "";
    [ObservableProperty] private string _credito = "";
    [ObservableProperty] private string _historico = RegraContabilizacao.HistoricoPadrao;
    [ObservableProperty] private bool _ativa = true;

    public bool PodeManter => Contexto.Pode(Permissao.FiscalContabilizar);

    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeManter));

    public Task CarregarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Fiscal().Contabilizacao.ListarRegrasAsync(empresa, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Regras.Clear();
        foreach (var x in r.Valor) Regras.Add(new RegraLinha(x));
    });

    partial void OnSelecionadaChanged(RegraLinha? value)
    {
        if (value is null) return;
        var r = value.Regra;
        (Prioridade, Operacao, Cfop, Participante, Debito, Credito, Historico, Ativa) =
            (r.Prioridade, r.Operacao is null ? 0 : r.Operacao == TipoOperacao.Entrada ? 1 : 2, r.Cfop ?? "", r.ParticipanteNome ?? "", r.ContaDebito, r.ContaCredito, r.Historico, r.Ativo);
    }

    [RelayCommand]
    private void Nova()
    {
        Selecionada = null;
        (Prioridade, Operacao, Cfop, Participante, Debito, Credito, Historico, Ativa) = (100, 0, "", "", "", "", RegraContabilizacao.HistoricoPadrao, true);
        Erros.Clear();
        Mensagem = null;
    }

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        long? participanteId = null;
        string? participanteNome = null;
        if (!string.IsNullOrWhiteSpace(Participante))
        {
            var texto = Participante.Trim();
            var lista = await app.Participantes.ListarAsync(empresa, usuario);
            var p = lista.FirstOrDefault(x => x.Codigo == texto) ?? lista.FirstOrDefault(x => string.Equals(x.Nome, texto, StringComparison.CurrentCultureIgnoreCase))
                    ?? lista.FirstOrDefault(x => x.CpfCnpj == new string(texto.Where(char.IsAsciiDigit).ToArray()));
            if (p is null) { Erros.Add($"Participante \"{texto}\" não encontrado (informe o código, o nome ou o CPF/CNPJ cadastrado)."); return; }
            (participanteId, participanteNome) = (p.Id, p.Nome);
        }
        var regra = new RegraContabilizacao(Selecionada?.Regra.Id ?? 0, Prioridade, Operacao switch { 1 => TipoOperacao.Entrada, 2 => TipoOperacao.Saida, _ => null },
            Cfop, participanteId, participanteNome, Debito, Credito, Historico, Ativa);
        var r = await app.Fiscal().Contabilizacao.GravarRegraAsync(empresa, regra, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Selecionada = Regras.FirstOrDefault(x => x.Regra.Id == r.Valor);
        Mensagem = "Regra gravada.";
    }, Permissao.FiscalContabilizar);

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada is not { } s) { Erros.Add("Selecione uma regra."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir regra", $"Excluir a regra do CFOP {s.Cfop}?", "Excluir", "Cancelar")) return;
        var r = await app.Fiscal().Contabilizacao.ExcluirRegraAsync(empresa, s.Regra.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Nova();
        Mensagem = "Regra excluída.";
    }, Permissao.FiscalContabilizar);
}

public partial class NotaPendenteLinha(PropostaContabilizacao proposta) : ObservableObject
{
    public PropostaContabilizacao Proposta { get; } = proposta;
    [ObservableProperty] private bool _marcada = proposta.Pronta;
    public long Id => Proposta.Documento.Id;
    public string Data => Proposta.Documento.DataLivro.ToString("dd/MM/yyyy");
    public string Documento => $"{Proposta.Documento.Modelo.Rotulo()} {Proposta.Documento.Numero}";
    public string Operacao => Proposta.Documento.Operacao.Rotulo();
    public string Participante => Proposta.Documento.NomeParticipante ?? "";
    public string Valor => Numeros.Moeda(Proposta.Documento.Totais.Total);
    public string Situacao => Proposta.Pronta ? "Pronta" : string.Join(" ", Proposta.Erros);
    public string Lancamento => Proposta.Lancamento is { } l ? string.Join(" · ", l.Partidas.Where(p => p.DC == IndicadorDC.D).Select(p => $"D {p.CodigoConta}").Concat(l.Partidas.Where(p => p.DC == IndicadorDC.C).Select(p => $"C {p.CodigoConta}"))) : "";
}

public partial class NotaContabilizadaLinha(DocumentoFiscal documento) : ObservableObject
{
    public DocumentoFiscal Documento { get; } = documento;
    [ObservableProperty] private bool _marcada;
    public long Id => Documento.Id;
    public string Data => Documento.DataLivro.ToString("dd/MM/yyyy");
    public string Descricao => $"{Documento.Modelo.Rotulo()} {Documento.Numero}";
    public string Participante => Documento.NomeParticipante ?? "";
    public string Valor => Numeros.Moeda(Documento.Totais.Total);
}

/// <summary>Documentos fiscais › Contabilizar notas fiscais: pré-visualiza os lançamentos pelas regras, contabiliza e desfaz (estorna).</summary>
public partial class ContabilizarNotasViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public static IReadOnlyList<string> Operacoes { get; } = ["Entradas e saídas", "Somente entradas", "Somente saídas"];

    public ObservableCollection<NotaPendenteLinha> Pendentes { get; } = [];
    public ObservableCollection<NotaContabilizadaLinha> Contabilizadas { get; } = [];

    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _fim = DateTime.Today;
    [ObservableProperty] private int _operacao;
    [ObservableProperty] private string _resumo = "";

    public bool PodeContabilizar => Contexto.Pode(Permissao.FiscalContabilizar);

    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeContabilizar));

    private (DateOnly Ini, DateOnly Fim, TipoOperacao? Op) Filtro()
    {
        var ini = Data(Inicio, new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1));
        return (ini, Data(Fim, ini), Operacao switch { 1 => TipoOperacao.Entrada, 2 => TipoOperacao.Saida, _ => null });
    }

    [RelayCommand]
    private Task PreVisualizarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var (ini, fim, op) = Filtro();
        var r = await app.Fiscal().Contabilizacao.PrevisaoAsync(empresa, ini, fim, op, null, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Pendentes.Clear();
        foreach (var p in r.Valor) Pendentes.Add(new NotaPendenteLinha(p));
        var docs = await app.Fiscal().Documentos.ConsultarAsync(empresa, new FiltroDocumentos(ini, fim, op, Contabilizado: true), usuario);
        Contabilizadas.Clear();
        if (docs.Sucesso) foreach (var d in docs.Valor) Contabilizadas.Add(new NotaContabilizadaLinha(d));
        var prontas = r.Valor.Count(p => p.Pronta);
        Resumo = $"{r.Valor.Count} nota(s) pendente(s): {prontas} pronta(s), {r.Valor.Count - prontas} com pendência (sem regra, cancelada ou conta inválida) · {Contabilizadas.Count} já contabilizada(s) no período";
    }, Permissao.FiscalContabilizar);

    [RelayCommand]
    private Task ContabilizarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var marcadas = Pendentes.Where(p => p.Marcada && p.Proposta.Pronta).Select(p => p.Id).ToHashSet();
        if (marcadas.Count == 0) { Erros.Add("Marque ao menos uma nota pronta."); return; }
        if (!await Contexto.ConfirmarAsync("Contabilizar notas", $"Gerar os lançamentos contábeis de {marcadas.Count} nota(s)?", "Contabilizar", "Cancelar")) return;
        var (ini, fim, op) = Filtro();
        var r = await app.Fiscal().Contabilizacao.ContabilizarAsync(empresa, ini, fim, op, marcadas, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await PreVisualizarAsync();
        foreach (var f in r.Valor.Falhas) foreach (var e in f.Erros) Erros.Add($"Nota {f.Documento}: {e}");
        Mensagem = r.Valor.Resumo;
    }, Permissao.FiscalContabilizar);

    [RelayCommand]
    private Task DesfazerAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var marcadas = Contabilizadas.Where(c => c.Marcada).Select(c => c.Id).ToList();
        if (marcadas.Count == 0) { Erros.Add("Marque ao menos uma nota contabilizada."); return; }
        if (!await Contexto.ConfirmarAsync("Desfazer contabilização", $"Estornar os lançamentos de {marcadas.Count} nota(s)? Elas voltam a ficar pendentes.", "Desfazer", "Cancelar")) return;
        var r = await app.Fiscal().Contabilizacao.DesfazerAsync(empresa, marcadas, null, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await PreVisualizarAsync();
        foreach (var f in r.Valor.Falhas) foreach (var e in f.Erros) Erros.Add($"Nota {f.Documento}: {e}");
        Mensagem = $"{r.Valor.Contabilizados} contabilização(ões) desfeita(s).";
    }, Permissao.FiscalContabilizar);
}
