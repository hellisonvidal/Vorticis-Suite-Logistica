using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Frota.Desktop.ViewModels;

public sealed record MultaLinha(MultaTransito M, PrazoMulta? Prazo)
{
    public string Placa => M.Placa ?? "";
    public string Motorista => M.MotoristaNome ?? "";
    public string Auto => M.AutoInfracao;
    public string Data => Ext.Data(M.DataInfracao);
    public string Infracao => string.Join(" – ", new[] { M.Enquadramento, M.Descricao }.Where(t => !string.IsNullOrWhiteSpace(t)));
    public string Gravidade => RegrasMultas.Rotulo(M.Gravidade);
    public string Pontos => M.Pontos.ToString();
    public string Valor => Formato.Moeda(M.Valor);
    public string Situacao => RegrasMultas.Rotulo(M.Situacao);
    public string ProximoPrazo => Prazo is null ? "" : $"{Prazo.Assunto}: {Ext.Data(Prazo.Data)}";
    /// <summary>Alerta do monitor: VENCIDO, A vencer (dentro da antecedência dos avisos) ou vazio.</summary>
    public string Alerta => Prazo?.Gravidade switch
    {
        GravidadeAviso.Vencido => "VENCIDO – " + RegrasFrota.TextoVencimento(Prazo.Data, Ext.Hoje),
        GravidadeAviso.AVencer => "A vencer – " + RegrasFrota.TextoVencimento(Prazo.Data, Ext.Hoje),
        _ => "",
    };
}

public sealed record PontosLinha(PontosMotorista P)
{
    public string Motorista => P.Nome;
    public string Pontos => P.Pontos.ToString();
    public string Multas => P.Multas.ToString();
}

/// <summary>
/// Monitores › Monitor de multas de trânsito: lançamento das notificações recebidas, prazos de indicação do condutor, recurso e pagamento
/// com alerta, quem paga (empresa ou motorista) e a pontuação dos motoristas nos últimos 12 meses.
/// </summary>
public partial class MultasViewModel(IContexto contexto) : FrotaBase(contexto, Permissao.FrotaOperacao)
{
    public ObservableCollection<MultaLinha> Lista { get; } = [];
    public ObservableCollection<PontosLinha> Pontuacao { get; } = [];
    public static IReadOnlyList<OpcaoCodigo> Gravidades { get; } = OpcaoCodigo.De(RegrasMultas.Gravidades);
    public static IReadOnlyList<OpcaoCodigo> Situacoes { get; } = OpcaoCodigo.De(RegrasMultas.Situacoes);
    public static IReadOnlyList<OpcaoCodigo> Responsaveis { get; } = OpcaoCodigo.De(RegrasMultas.Responsaveis);
    public static IReadOnlyList<string> Filtros { get; } = ["Em aberto", "Com prazo vencido ou a vencer", "Todas"];

    private List<MultaTransito> _todas = [];
    private int _diasAviso = 30;
    private TabelaInfracoes? _tabela;

    [ObservableProperty] private string _filtro = Filtros[0];
    [ObservableProperty] private string _resumo = "";
    [ObservableProperty] private MultaLinha? _selecionado;
    [ObservableProperty] private Opcao? _veiculo;
    [ObservableProperty] private Opcao? _motorista;
    [ObservableProperty] private string _autoInfracao = "";
    [ObservableProperty] private string _orgao = "";
    [ObservableProperty] private DateTime? _dataInfracao = DateTime.Today;
    [ObservableProperty] private string _local = "";
    [ObservableProperty] private string _enquadramento = "";
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private OpcaoCodigo? _gravidade = Gravidades[1];
    [ObservableProperty] private string _pontos = "";
    [ObservableProperty] private string _valor = "";
    [ObservableProperty] private string _desconto = "";
    [ObservableProperty] private DateTime? _vencimento;
    [ObservableProperty] private DateTime? _prazoIndicacao;
    [ObservableProperty] private DateTime? _prazoRecurso;
    [ObservableProperty] private OpcaoCodigo? _responsavel = Responsaveis[0];
    [ObservableProperty] private OpcaoCodigo? _situacao = Situacoes[0];
    [ObservableProperty] private DateTime? _dataPagamento;
    [ObservableProperty] private string _valorPago = "";
    [ObservableProperty] private string _observacao = "";

    partial void OnFiltroChanged(string value) => Preencher();

    /// <summary>Ao escolher a gravidade de uma multa nova, sugere os pontos e o valor do CTB (core.parametro_legal) se ainda estão vazios.</summary>
    partial void OnGravidadeChanged(OpcaoCodigo? value)
    {
        if (Selecionado is not null || value is null || _tabela?.PorGravidade.TryGetValue(value.Valor, out var t) != true) return;
        Pontos = t.Pontos.ToString();
        Valor = Formato.Num(t.Valor);
    }

    partial void OnSelecionadoChanged(MultaLinha? value)
    {
        if (value?.M is not { } m) return;
        Veiculo = Escolher(VeiculosOpcoes, m.VeiculoId, () => m.Placa ?? "");
        Motorista = Escolher(MotoristasOpcoes, m.MotoristaId, () => m.MotoristaNome ?? "");
        (AutoInfracao, Orgao, DataInfracao, Local, Enquadramento, Descricao) = (m.AutoInfracao, m.Orgao ?? "", Ext.Dt(m.DataInfracao), m.Local ?? "", m.Enquadramento ?? "", m.Descricao ?? "");
        Gravidade = Gravidades.FirstOrDefault(g => g.Valor == m.Gravidade);
        (Pontos, Valor, Desconto) = (m.Pontos.ToString(), Formato.Num(m.Valor), m.Desconto == 0 ? "" : Formato.Num(m.Desconto));
        (Vencimento, PrazoIndicacao, PrazoRecurso) = (Ext.DtOpc(m.Vencimento), Ext.DtOpc(m.PrazoIndicacao), Ext.DtOpc(m.PrazoRecurso));
        Responsavel = Responsaveis.FirstOrDefault(r => r.Valor == m.Responsavel);
        Situacao = Situacoes.FirstOrDefault(s => s.Valor == m.Situacao);
        (DataPagamento, ValorPago, Observacao) = (Ext.DtOpc(m.DataPagamento), Ext.Num(m.ValorPago), m.Observacao ?? "");
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        await CarregarCombosAsync(app, empresa, sessao.Id, "(escolha)");
        var cfg = await app.Frota().ConfiguracaoAsync(empresa);
        var multas = await app.Frota().MultasAsync(empresa, sessao.Id);
        var tabela = await app.Frota().TabelaInfracoesAsync(Ext.Hoje);
        if (!EhAtual(g)) return;
        (_todas, _diasAviso, _tabela) = (multas, cfg.DiasAviso, tabela);
        Preencher();
        Veiculo ??= VeiculosOpcoes.FirstOrDefault();
        Motorista ??= MotoristasOpcoes.FirstOrDefault();
    }

    private void Preencher()
    {
        var hoje = Ext.Hoje;
        var linhas = _todas.Select(m => new MultaLinha(m, RegrasMultas.ProximoPrazo(m, hoje, _diasAviso))).ToList();
        Lista.Clear();
        foreach (var l in linhas.Where(l => Filtro switch
                 {
                     "Todas" => true,
                     "Com prazo vencido ou a vencer" => l.Prazo?.Gravidade is not null,
                     _ => l.M.EmAberto,
                 }))
            Lista.Add(l);
        Pontuacao.Clear();
        foreach (var p in RegrasMultas.PontosDosMotoristas(_todas, hoje)) Pontuacao.Add(new PontosLinha(p));
        var abertas = linhas.Where(l => l.M.EmAberto).ToList();
        var vencidas = linhas.Count(l => l.Prazo?.Gravidade == GravidadeAviso.Vencido);
        var aVencer = linhas.Count(l => l.Prazo?.Gravidade == GravidadeAviso.AVencer);
        Resumo = abertas.Count == 0 ? "Nenhuma multa em aberto."
            : $"{abertas.Count} multa(s) em aberto, {Formato.Moeda(abertas.Sum(l => l.M.ValorComDesconto))} a pagar"
              + (vencidas > 0 ? $"; {vencidas} com prazo vencido" : "") + (aVencer > 0 ? $"; {aVencer} com prazo a vencer" : "") + ".";
    }

    [RelayCommand]
    private void Novo()
    {
        Selecionado = null;
        (Veiculo, Motorista) = (VeiculosOpcoes.FirstOrDefault(), MotoristasOpcoes.FirstOrDefault());
        (AutoInfracao, Orgao, DataInfracao, Local, Enquadramento, Descricao) = ("", "", DateTime.Today, "", "", "");
        (Pontos, Valor, Desconto, Vencimento, PrazoIndicacao, PrazoRecurso) = ("", "", "", null, null, null);
        (Responsavel, Situacao, DataPagamento, ValorPago, Observacao) = (Responsaveis[0], Situacoes[0], null, "", "");
        Gravidade = null;
        Gravidade = Gravidades[1];
    }

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var m = new MultaTransito(Selecionado?.M.Id ?? 0, Veiculo?.Id ?? 0, Motorista?.Id, AutoInfracao, Orgao, Ext.Dia(DataInfracao), Local, Enquadramento, Descricao,
                                  Gravidade?.Valor ?? "", Ext.Int(Pontos) ?? 0, Formato.LerValor(Valor) ?? 0, Formato.LerValor(Desconto) ?? 0, Ext.DiaOpc(Vencimento),
                                  Ext.DiaOpc(PrazoIndicacao), Ext.DiaOpc(PrazoRecurso), Responsavel?.Valor ?? "", Situacao?.Valor ?? "", Ext.DiaOpc(DataPagamento),
                                  Formato.LerValor(ValorPago), Observacao);
        var r = await app.Frota().GravarMultaAsync(empresa, m, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        if (Filtro != "Todas" && !m.EmAberto) Filtro = "Todas";      // a multa paga ou cancelada continua à vista
        await CarregarAsync();
        Selecionado = Lista.FirstOrDefault(l => l.M.Id == r.Valor);
        Mensagem = "Multa gravada.";
    }, Permissao.FrotaOperacao);

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado?.M is not { } m) { Erros.Add("Selecione a multa."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir multa", $"Excluir a multa do auto {m.AutoInfracao} ({m.Placa})?")) return;
        var r = await app.Frota().ExcluirMultaAsync(empresa, m.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Novo();
        await CarregarAsync();
        Mensagem = "Multa excluída.";
    }, Permissao.FrotaOperacao);
}
