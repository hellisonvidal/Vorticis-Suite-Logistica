using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Frota.Desktop.ViewModels;

public sealed record CiotLinha(OperacaoCiot O)
{
    public string Numero => O.Numero.ToString();
    public string Ciot => O.Ciot ?? "";
    public string Contratado => O.Contratado ?? "";
    public string Placa => RegrasFrota.FormatarPlaca(O.Placa);
    public string Trecho => O.Trecho;
    public string Inicio => Ext.Data(O.Inicio);
    public string Frete => Formato.Moeda(O.ValorFrete);
    public string Retencoes => Formato.Moeda(O.Retencoes.Total);
    public string Saldo => Formato.Moeda(O.Saldo);
    public string Situacao => RegrasCiot.Rotulo(O.Situacao);
}

/// <summary>
/// Operação › CIOT: subcontratação de TAC (autônomo) ou ETC equiparada, com retenções do TAC (INSS, SEST/SENAT e IRRF), adiantamento e saldo,
/// CT-e da operação, o CIOT gerado pela IPEF, encerramento e o contrato de frete em PDF.
/// </summary>
public partial class CiotViewModel(IContexto contexto) : TmsBase(contexto)
{
    public ObservableCollection<CiotLinha> Lista { get; } = [];
    public ObservableCollection<CteMarcado> Ctes { get; } = [];
    public ObservableCollection<Opcao> ViagensOpcoes { get; } = [];
    public SeletorMunicipio Origem { get; } = new() { Uf = "SP" };
    public SeletorMunicipio Destino { get; } = new() { Uf = "SP" };
    public static IReadOnlyList<OpcaoCodigoCiot> Tipos { get; } = [.. RegrasCiot.Tipos.Select(t => new OpcaoCodigoCiot(t))];

    [ObservableProperty] private CiotLinha? _selecionado;
    [ObservableProperty] private OpcaoCodigoCiot? _tipo = Tipos[0];
    [ObservableProperty] private Opcao? _contratado;
    [ObservableProperty] private string _rntrc = "";
    [ObservableProperty] private string _placa = "";
    [ObservableProperty] private string _motoristaNome = "";
    [ObservableProperty] private string _motoristaCpf = "";
    [ObservableProperty] private Opcao? _viagem;
    [ObservableProperty] private DateTime? _dataInicio = DateTime.Today;
    [ObservableProperty] private DateTime? _dataFim = DateTime.Today;
    [ObservableProperty] private string _produto = "";
    [ObservableProperty] private string _ncm = "";
    [ObservableProperty] private string _pesoKg = "";
    [ObservableProperty] private string _valorFrete = "";
    [ObservableProperty] private string _valePedagio = "";
    [ObservableProperty] private string _adiantamento = "";
    [ObservableProperty] private string _dependentes = "";
    [ObservableProperty] private string _ipef = "";
    [ObservableProperty] private string _observacao = "";
    [ObservableProperty] private string _ciot = "";
    [ObservableProperty] private string _codigoVerificador = "";
    [ObservableProperty] private string _resumoValores = "";
    [ObservableProperty] private string _situacaoTexto = "Nova (digitada)";

    protected override void AoMudarPeriodo() => _ = CarregarAsync();

    partial void OnSelecionadoChanged(CiotLinha? value)
    {
        if (value?.O is not { } o) return;
        Tipo = Tipos.FirstOrDefault(t => t.Valor == o.Tipo);
        Contratado = Escolher(ParticipantesOpcoes, o.ContratadoId, () => o.Contratado ?? "");
        (Rntrc, Placa, MotoristaNome, MotoristaCpf) = (o.Rntrc, RegrasFrota.FormatarPlaca(o.Placa), o.MotoristaNome ?? "", o.MotoristaCpf ?? "");
        Viagem = Escolher(ViagensOpcoes, o.ViagemId, () => "(viagem)");
        Origem.Definir(o.Origem);
        Destino.Definir(o.Destino);
        (DataInicio, DataFim, Produto, Ncm, PesoKg) = (Ext.Dt(o.Inicio), Ext.Dt(o.Fim), o.Produto ?? "", o.Ncm ?? "", Ext.Num(o.PesoKg));
        (ValorFrete, ValePedagio, Adiantamento, Dependentes) = (Ext.Num(o.ValorFrete), Ext.Num(o.ValePedagio), Ext.Num(o.Adiantamento), o.Dependentes == 0 ? "" : o.Dependentes.ToString());
        (Ipef, Observacao, Ciot, CodigoVerificador) = (o.Ipef ?? "", o.Observacao ?? "", o.Ciot ?? "", o.CodigoVerificador ?? "");
        foreach (var c in Ctes) c.Marcado = o.Ctes.Contains(c.C.Id);
        Resumir(o);
        SituacaoTexto = RegrasCiot.Rotulo(o.Situacao);
    }

    private void Resumir(OperacaoCiot o) =>
        ResumoValores = $"Frete {M(o.ValorFrete)} − retenções {M(o.Retencoes.Total)} (INSS {M(o.Retencoes.Inss)}, SEST/SENAT {M(o.Retencoes.SestSenat)}, "
                        + $"IRRF {M(o.Retencoes.Irrf)}) = líquido {M(o.ValorLiquido)} · adiantamento {M(o.Adiantamento)} · SALDO {M(o.Saldo)}"
                        + (o.ValePedagio > 0 ? $" · vale-pedágio {M(o.ValePedagio)} (fora do frete)" : "");

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        await CarregarParticipantesAsync(app, empresa, sessao.Id);
        var viagens = await app.Frota().ViagensAsync(empresa, Ext.Hoje.AddDays(-90), Ext.Hoje.AddDays(30), sessao.Id);
        var ctes = await app.Frota().CtesAsync(empresa, Ext.Hoje.AddDays(-90), Ext.Hoje.AddDays(30), null, sessao.Id);
        var itens = await app.Frota().CiotsAsync(empresa, Ext.Dia(Inicio), Ext.Dia(Fim), sessao.Id);
        if (!EhAtual(g)) return;
        ViagensOpcoes.Clear();
        ViagensOpcoes.Add(new Opcao(null, "(nenhuma)"));
        foreach (var v in viagens.Where(v => v.Situacao != "CANCELADA")) ViagensOpcoes.Add(new Opcao(v.Id, $"Nº {v.Numero} – {v.Origem} → {v.Destino}"));
        Ctes.Clear();
        foreach (var c in ctes.Where(c => c.Situacao != "CANCELADO")) Ctes.Add(new CteMarcado(c, false));
        var atual = Selecionado?.O.Id;
        Lista.Clear();
        foreach (var o in itens) Lista.Add(new CiotLinha(o));
        var validas = itens.Where(o => o.Situacao != "CANCELADO").ToList();
        Totais = $"{itens.Count} operação(ões) · {itens.Count(o => o.Situacao == "DIGITADO")} sem CIOT · fretes {M(validas.Sum(o => o.ValorFrete))} · "
                 + $"retenções {M(validas.Sum(o => o.Retencoes.Total))} · saldo a pagar {M(validas.Where(o => o.Situacao != "ENCERRADO").Sum(o => o.Saldo))}";
        if (atual is not null) Selecionado = Lista.FirstOrDefault(l => l.O.Id == atual);
        else if (Contratado is null) Novo();
    }

    [RelayCommand]
    private void Novo()
    {
        Selecionado = null;
        (Tipo, Contratado, Viagem) = (Tipos[0], ParticipantesOpcoes.FirstOrDefault(), ViagensOpcoes.FirstOrDefault());
        (Rntrc, Placa, MotoristaNome, MotoristaCpf, Produto, Ncm, PesoKg) = ("", "", "", "", "", "", "");
        (DataInicio, DataFim, ValorFrete, ValePedagio, Adiantamento, Dependentes) = (DateTime.Today, DateTime.Today, "", "", "", "");
        (Ipef, Observacao, Ciot, CodigoVerificador, ResumoValores, SituacaoTexto) = ("", "", "", "", "", "Nova (digitada)");
        foreach (var c in Ctes) c.Marcado = false;
    }

    private OperacaoCiot Montar() =>
        new(Selecionado?.O.Id ?? 0, Selecionado?.O.Numero ?? 0, Ciot, CodigoVerificador, Ipef, Tipo?.Valor ?? "", Contratado?.Id ?? 0, Rntrc, Placa, MotoristaNome,
            MotoristaCpf, Viagem?.Id, Origem.Parada, Destino.Parada, Ext.Dia(DataInicio), Ext.Dia(DataFim), Produto, Ncm, Formato.LerValor(PesoKg) ?? 0,
            Formato.LerValor(ValorFrete) ?? 0, Formato.LerValor(ValePedagio) ?? 0, Formato.LerValor(Adiantamento) ?? 0, Ext.Int(Dependentes) ?? 0,
            RetencoesTac.Nenhuma, "DIGITADO", Observacao, [.. Ctes.Where(c => c.Marcado).Select(c => c.C.Id)]);

    /// <summary>Mostra as retenções e o saldo antes de gravar (o serviço recalcula ao gravar).</summary>
    [RelayCommand]
    private Task CalcularAsync() => ExecutarAsync(async (app, empresa, _) =>
    {
        var o = Montar();
        var r = await app.Frota().CalcularRetencoesAsync(empresa, o);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Resumir(o with { Retencoes = r.Valor });
        Mensagem = r.Valor.Total == 0 ? "Sem retenções (contratado com CNPJ ou frete abaixo das bases)." : "Retenções calculadas pelas tabelas vigentes.";
    });

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Frota().GravarCiotAsync(empresa, Montar(), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await RecarregarAsync(r.Valor.Id, $"Operação nº {r.Valor.Numero} gravada. Registre-a na IPEF e informe o CIOT recebido em Declarar CIOT.");
    }, Permissao.FrotaTms);

    private async Task RecarregarAsync(long id, string mensagem)
    {
        Selecionado = null;
        await CarregarAsync();
        Selecionado = Lista.FirstOrDefault(l => l.O.Id == id);
        Mensagem = mensagem;
    }

    private Task ComOperacaoAsync(Func<Vorticis.Infra.VorticisApp, long, long, OperacaoCiot, Task> acao) => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado?.O is not { } o) { Erros.Add("Selecione (ou grave) a operação."); return; }
        await acao(app, empresa, usuario, o);
    }, Permissao.FrotaTms);

    [RelayCommand]
    private Task DeclararAsync() => ComOperacaoAsync(async (app, empresa, usuario, o) =>
    {
        var r = await app.Frota().DeclararCiotAsync(empresa, o.Id, Ciot, CodigoVerificador, Ipef, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await RecarregarAsync(o.Id, "CIOT registrado: informe-o também no MDF-e da viagem.");
    });

    [RelayCommand]
    private Task EncerrarAsync() => ComOperacaoAsync(async (app, empresa, usuario, o) =>
    {
        if (!await Contexto.ConfirmarAsync("Encerrar operação", $"Encerrar a operação nº {o.Numero} (viagem concluída e encerrada na IPEF)?")) return;
        var r = await app.Frota().MudarSituacaoCiotAsync(empresa, o.Id, "ENCERRADO", usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await RecarregarAsync(o.Id, "Operação encerrada.");
    });

    [RelayCommand]
    private Task CancelarAsync() => ComOperacaoAsync(async (app, empresa, usuario, o) =>
    {
        if (!await Contexto.ConfirmarAsync("Cancelar operação", $"Cancelar a operação nº {o.Numero}? Cancele também na IPEF, se já foi declarada.")) return;
        var r = await app.Frota().MudarSituacaoCiotAsync(empresa, o.Id, "CANCELADO", usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await RecarregarAsync(o.Id, "Operação cancelada.");
    });

    [RelayCommand]
    private Task ExcluirAsync() => ComOperacaoAsync(async (app, empresa, usuario, o) =>
    {
        if (!await Contexto.ConfirmarAsync("Excluir operação", $"Excluir a operação nº {o.Numero}?")) return;
        var r = await app.Frota().ExcluirCiotAsync(empresa, o.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Novo();
        await CarregarAsync();
        Mensagem = "Operação excluída.";
    });

    [RelayCommand]
    private void Imprimir()
    {
        Erros.Clear();
        Mensagem = null;
        if (Selecionado?.O is not { } o) { Erros.Add("Selecione a operação."); return; }
        SalvarPdf($"contrato_frete_{o.Numero}.pdf", () => ServicoFrota.ContratoFretePdf(o, Contexto.DescricaoEmpresa, Contexto.Sessao?.Nome ?? ""), "Contrato de frete");
    }
}

/// <summary>Tipo da operação com o rótulo da tela.</summary>
public sealed record OpcaoCodigoCiot(string Valor)
{
    public override string ToString() => RegrasCiot.Rotulo(Valor);
}
