using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Imobilizado;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra;

namespace Vorticis.Frota.Desktop.ViewModels;

public sealed record ContratoLinha(ContratoFinanceiro C)
{
    public string Numero => C.Numero;
    public string Descricao => C.Descricao;
    public string Credor => C.Credor ?? "";
    public string Data => Ext.Data(C.DataContrato);
    public string Valor => Formato.Moeda(C.ValorBem);
    public string Parcelas => C.Parcelas.ToString();
    public string Situacao => RegrasContratos.Rotulo(C.Situacao) + (C.Consorcio ? C.DataContemplacao is { } d ? $" – contemplado em {Ext.Data(d)}" : " – a contemplar" : "");
}

public sealed record ParcelaLinha(ParcelaContrato P)
{
    public string Numero => P.Numero.ToString();
    public string Vencimento => Ext.Data(P.Vencimento);
    public string Amortizacao => Formato.Num(P.Amortizacao);
    public string Juros => Formato.Num(P.Juros);
    public string Outros => P.Outros == 0 ? "" : Formato.Num(P.Outros);
    public string Seguro => P.Seguro == 0 ? "" : Formato.Num(P.Seguro);
    public string Valor => Formato.Num(P.Valor);
    public string Saldo => Formato.Num(P.Saldo);
    public string Pagamento => P.PagoEm is { } d ? $"{Ext.Data(d)} – {Formato.Moeda(P.ValorPago ?? 0)}" + (P.Contabilizada ? " (contabilizado)" : "") : "";
}

public sealed partial class RateioLinha(Opcao veiculo, string percentual) : ObservableObject
{
    [ObservableProperty] private Opcao? _veiculo = veiculo;
    [ObservableProperty] private string _percentual = percentual;
}

/// <summary>
/// Financeiro › Contas › Financiamentos e Consórcios: contrato, parcelas (Price, SAC ou consórcio), pagamento, rateio por placa,
/// bem do imobilizado e contabilização (contratação, pagamentos, contemplação e reclassificação do longo para o curto prazo).
/// </summary>
public partial class ContratosViewModel(IContexto contexto, string tipo) : FrotaBase(contexto, Permissao.FiscalDuplicatasManter)
{
    public string Tipo { get; } = tipo;
    public bool Consorcio => Tipo == RegrasContratos.Consorcio;
    public bool Financiamento => !Consorcio;
    public string Titulo => Consorcio ? "Consórcios" : "Financiamentos";
    public string RotuloNumero => Consorcio ? "Grupo/cota" : "Nº do contrato";
    public string RotuloCredor => Consorcio ? "Administradora" : "Banco/financeira";
    public string RotuloValor => Consorcio ? "Carta de crédito" : "Valor do bem";
    public string RotuloData => Consorcio ? "Data da adesão" : "Data do contrato";

    public ObservableCollection<ContratoLinha> Lista { get; } = [];
    public ObservableCollection<ParcelaLinha> ParcelasLista { get; } = [];
    public ObservableCollection<RateioLinha> Rateio { get; } = [];
    public ObservableCollection<Opcao> Credores { get; } = [];
    public ObservableCollection<GrupoBem> Grupos { get; } = [];
    public static IReadOnlyList<string> Sistemas { get; } = ["Price (parcelas iguais)", "SAC (amortização constante)"];
    public static IReadOnlyList<string> SituacoesContrato { get; } = ["Ativo", "Quitado", "Cancelado"];

    [ObservableProperty] private ContratoLinha? _selecionado;
    [ObservableProperty] private ParcelaLinha? _parcelaSelecionada;
    [ObservableProperty] private string _numero = "";
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private Opcao? _credor;
    [ObservableProperty] private DateTime? _dataContrato = DateTime.Today;
    [ObservableProperty] private string _valorBem = "";
    [ObservableProperty] private string _entrada = "";
    [ObservableProperty] private string _encargos = "";
    [ObservableProperty] private string _taxaJuros = "";
    [ObservableProperty] private string _sistema = Sistemas[0];
    [ObservableProperty] private string _taxaAdministracao = "";
    [ObservableProperty] private string _fundoReserva = "";
    [ObservableProperty] private string _seguroMensal = "";
    [ObservableProperty] private string _quantidadeParcelas = "";
    [ObservableProperty] private DateTime? _primeiroVencimento = DateTime.Today.AddMonths(1);
    [ObservableProperty] private string _situacao = SituacoesContrato[0];
    [ObservableProperty] private string _observacao = "";
    [ObservableProperty] private bool _contabiliza;
    [ObservableProperty] private string _contaBem = "";
    [ObservableProperty] private string _contaPagamento = "";
    [ObservableProperty] private string _contaCurtoPrazo = "";
    [ObservableProperty] private string _contaLongoPrazo = "";
    [ObservableProperty] private string _contaDespesa = "";
    [ObservableProperty] private string _contaConsorcio = "";
    [ObservableProperty] private string _bem = "";
    [ObservableProperty] private GrupoBem? _grupo;
    [ObservableProperty] private string _resumo = "";
    [ObservableProperty] private string _saldosContabeis = "";
    [ObservableProperty] private DateTime? _dataPagamento = DateTime.Today;
    [ObservableProperty] private string _valorPago = "";
    [ObservableProperty] private DateTime? _dataOperacao = DateTime.Today;

    partial void OnParcelaSelecionadaChanged(ParcelaLinha? value)
    {
        if (value?.P is not { } p) return;
        ValorPago = Formato.Num(p.ValorPago ?? p.Valor);
        DataPagamento = Ext.Dt(p.PagoEm ?? (p.Vencimento < Ext.Hoje ? p.Vencimento : Ext.Hoje));
    }

    partial void OnSelecionadoChanged(ContratoLinha? value)
    {
        if (value?.C is not { } c) return;
        (Numero, Descricao, DataContrato, ValorBem) = (c.Numero, c.Descricao, Ext.Dt(c.DataContrato), Formato.Num(c.ValorBem));
        Credor = Escolher(Credores, c.CredorId, () => c.Credor ?? "");
        (Entrada, Encargos, TaxaJuros) = (Opc(c.Entrada), Opc(c.Encargos), Opc(c.TaxaJuros));
        Sistema = c.Sistema == "SAC" ? Sistemas[1] : Sistemas[0];
        (TaxaAdministracao, FundoReserva, SeguroMensal) = (Opc(c.TaxaAdministracao), Opc(c.FundoReserva), Opc(c.SeguroMensal));
        (QuantidadeParcelas, PrimeiroVencimento) = (c.Parcelas.ToString(), Ext.Dt(c.PrimeiroVencimento));
        Situacao = RegrasContratos.Rotulo(c.Situacao);
        Observacao = c.Observacao ?? "";
        (Contabiliza, ContaBem, ContaPagamento, ContaCurtoPrazo, ContaLongoPrazo, ContaDespesa, ContaConsorcio) =
            (c.Contabiliza, c.ContaBem ?? "", c.ContaPagamento ?? "", c.ContaCurtoPrazo ?? "", c.ContaLongoPrazo ?? "", c.ContaDespesa ?? "", c.ContaConsorcio ?? "");
        Bem = c.Bem ?? "(nenhum)";
        _ = CarregarDetalhesAsync(c);
    }

    private static string Opc(decimal v) => v == 0 ? "" : Formato.Num(v);

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        await CarregarCombosAsync(app, empresa, sessao.Id);
        var participantes = await app.Frota().ParticipantesAsync(empresa, sessao.Id);
        var grupos = await app.Frota().GruposImobilizadoAsync();
        var contratos = await app.Frota().ContratosAsync(empresa, Tipo, sessao.Id);
        if (!EhAtual(g)) return;
        Credores.Clear();
        Credores.Add(new Opcao(null, "(escolha)"));
        foreach (var p in participantes) Credores.Add(new Opcao(p.Id, p.ToString()));
        Credor ??= Credores[0];
        Grupos.Clear();
        foreach (var gr in grupos.Where(x => !x.Intangivel)) Grupos.Add(gr);
        Grupo ??= Grupos.FirstOrDefault(x => x.Codigo == "06") ?? Grupos.FirstOrDefault();
        var atual = Selecionado?.C.Id;
        Lista.Clear();
        foreach (var c in contratos) Lista.Add(new ContratoLinha(c));
        if (atual is not null) Selecionado = Lista.FirstOrDefault(l => l.C.Id == atual);
        if (Rateio.Count == 0 && Selecionado is null) Rateio.Add(new RateioLinha(VeiculosOpcoes[0], "100"));
    }

    private async Task CarregarDetalhesAsync(ContratoFinanceiro c)
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var parcelas = await app.Frota().ParcelasContratoAsync(empresa, c.Id, sessao.Id);
        var rateio = await app.Frota().RateioContratoAsync(empresa, c.Id, sessao.Id);
        var saldos = await app.Frota().SaldosContabeisAsync(c.Id);
        if (Selecionado?.C.Id != c.Id) return;
        ParcelasLista.Clear();
        foreach (var p in parcelas) ParcelasLista.Add(new ParcelaLinha(p));
        Rateio.Clear();
        foreach (var r in rateio) Rateio.Add(new RateioLinha(Escolher(VeiculosOpcoes, r.VeiculoId, () => r.Placa ?? "")!, Formato.Num(r.Percentual)));
        var prazos = RegrasContratos.Dividir(c, parcelas, Ext.Hoje);
        var pagas = parcelas.Count(p => p.Paga);
        Resumo = $"{pagas} de {parcelas.Count} parcela(s) paga(s); " + (c.TemDivida
            ? $"saldo devedor {Formato.Moeda(prazos.Total)}: curto prazo {Formato.Moeda(prazos.CurtoPrazo)}, longo prazo {Formato.Moeda(prazos.LongoPrazo)} (hoje)."
            : $"a pagar {Formato.Moeda(parcelas.Where(p => !p.Paga).Sum(p => p.Valor))} até a contemplação (sem dívida no passivo).");
        SaldosContabeis = !c.Contabiliza ? "Contrato sem contabilização."
            : !saldos.ContratacaoContabilizada && !c.Consorcio ? "Contratação ainda não contabilizada."
            : $"Contabilizado: curto prazo {Formato.Moeda(saldos.CurtoPrazo)}, longo prazo {Formato.Moeda(saldos.LongoPrazo)}"
              + (c.Consorcio ? $", ativo do consórcio {Formato.Moeda(saldos.AtivoConsorcio)}" : "") + ".";
    }

    [RelayCommand]
    private void Novo()
    {
        Selecionado = null;
        (Numero, Descricao, DataContrato, ValorBem, Entrada, Encargos, TaxaJuros) = ("", "", DateTime.Today, "", "", "", "");
        (Sistema, TaxaAdministracao, FundoReserva, SeguroMensal, QuantidadeParcelas) = (Sistemas[0], "", "", "", "");
        (PrimeiroVencimento, Situacao, Observacao, Bem, Resumo, SaldosContabeis) = (DateTime.Today.AddMonths(1), SituacoesContrato[0], "", "(nenhum)", "", "");
        Credor = Credores.FirstOrDefault();
        ParcelasLista.Clear();
        Rateio.Clear();
        Rateio.Add(new RateioLinha(VeiculosOpcoes.FirstOrDefault() ?? new Opcao(null, "(escolha)"), "100"));
    }

    [RelayCommand] private void IncluirPlaca() => Rateio.Add(new RateioLinha(VeiculosOpcoes.FirstOrDefault() ?? new Opcao(null, "(escolha)"), ""));
    [RelayCommand] private void RetirarPlaca(RateioLinha? linha) { if (linha is not null) Rateio.Remove(linha); }

    private ContratoFinanceiro Montar() =>
        new(Selecionado?.C.Id ?? 0, Tipo, Numero, Descricao, Credor?.Id, Ext.Dia(DataContrato), Formato.LerValor(ValorBem) ?? 0, Formato.LerValor(Entrada) ?? 0,
            Formato.LerValor(Encargos) ?? 0, Formato.LerValor(TaxaJuros) ?? 0, Sistema == Sistemas[1] ? "SAC" : "PRICE", Formato.LerValor(TaxaAdministracao) ?? 0,
            Formato.LerValor(FundoReserva) ?? 0, Formato.LerValor(SeguroMensal) ?? 0, Ext.Int(QuantidadeParcelas) ?? 0, Ext.Dia(PrimeiroVencimento),
            Selecionado?.C.DataContemplacao, Selecionado?.C.BemId, Contabiliza, ContaBem, ContaPagamento, ContaCurtoPrazo, ContaLongoPrazo, ContaDespesa, ContaConsorcio,
            Situacao switch { "Quitado" => "QUITADO", "Cancelado" => "CANCELADO", _ => "ATIVO" }, Observacao);

    private List<RateioContrato> MontarRateio() =>
        [.. Rateio.Where(r => r.Veiculo?.Id is not null).Select(r => new RateioContrato(r.Veiculo!.Id!.Value, Formato.LerValor(r.Percentual) ?? 0))];

    /// <summary>Mostra as parcelas calculadas, sem gravar.</summary>
    [RelayCommand]
    private void Simular()
    {
        Erros.Clear();
        var c = Montar();
        if (RegrasContratos.Validar(c with { Contabiliza = false }, []) is { Count: > 0 } erros) { foreach (var e in erros) Erros.Add(e); return; }
        ParcelasLista.Clear();
        var parcelas = RegrasContratos.Calcular(c);
        foreach (var p in parcelas) ParcelasLista.Add(new ParcelaLinha(p));
        Mensagem = $"Simulação: {parcelas.Count} parcela(s), total {Formato.Moeda(parcelas.Sum(p => p.Valor))}"
                   + (c.Consorcio ? "" : $", juros {Formato.Moeda(parcelas.Sum(p => p.Juros))}") + ". Grave para guardar.";
    }

    private async Task RecarregarAsync(long id, string mensagem)
    {
        await CarregarAsync();
        Selecionado = Lista.FirstOrDefault(l => l.C.Id == id);
        Mensagem = mensagem;
    }

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Frota().GravarContratoAsync(empresa, Montar(), MontarRateio(), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await RecarregarAsync(r.Valor, Consorcio ? "Consórcio gravado." : "Financiamento gravado.");
    }, Permissao.FiscalDuplicatasManter);

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado?.C is not { } c) { Erros.Add("Selecione o contrato."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir contrato", $"Excluir {RegrasContratos.Rotulo(c.Tipo).ToLowerInvariant()} nº {c.Numero} e as parcelas?")) return;
        var r = await app.Frota().ExcluirContratoAsync(empresa, c.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Novo();
        await CarregarAsync();
        Mensagem = "Contrato excluído.";
    }, Permissao.FiscalDuplicatasManter);

    private Task ComContratoAsync(Func<VorticisApp, long, long, ContratoFinanceiro, Task> acao, Permissao? exige = null) => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado?.C is not { } c) { Erros.Add("Selecione (ou grave) o contrato."); return; }
        await acao(app, empresa, usuario, c);
    }, exige);

    [RelayCommand]
    private Task PagarAsync() => ComContratoAsync(async (app, empresa, usuario, c) =>
    {
        if (ParcelaSelecionada?.P is not { } p) { Erros.Add("Selecione a parcela na lista de parcelas."); return; }
        var r = await app.Frota().PagarParcelaAsync(empresa, c.Id, p.Numero, Ext.Dia(DataPagamento), Formato.LerValor(ValorPago) ?? 0, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await RecarregarAsync(c.Id, $"Parcela {p.Numero} paga" + (c.Contabiliza ? " e contabilizada." : "."));
    }, Permissao.FiscalDuplicatasBaixar);

    [RelayCommand]
    private Task EstornarPagamentoAsync() => ComContratoAsync(async (app, empresa, usuario, c) =>
    {
        if (ParcelaSelecionada?.P is not { Paga: true } p) { Erros.Add("Selecione uma parcela paga."); return; }
        if (!await Contexto.ConfirmarAsync("Estornar pagamento", $"Estornar o pagamento da parcela {p.Numero}" + (p.Contabilizada ? " e o lançamento contábil?" : "?"))) return;
        var r = await app.Frota().EstornarPagamentoAsync(empresa, c.Id, p.Numero, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await RecarregarAsync(c.Id, $"Pagamento da parcela {p.Numero} estornado.");
    }, Permissao.FiscalDuplicatasBaixar);

    [RelayCommand]
    private Task ContabilizarContratacaoAsync() => ComContratoAsync(async (app, empresa, usuario, c) =>
    {
        var r = await app.Frota().ContabilizarContratacaoAsync(empresa, c.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await RecarregarAsync(c.Id, "Contratação contabilizada.");
    });

    [RelayCommand]
    private Task DesfazerContratacaoAsync() => ComContratoAsync(async (app, empresa, usuario, c) =>
    {
        if (!await Contexto.ConfirmarAsync("Desfazer contabilização", "Estornar o lançamento da contratação?")) return;
        var r = await app.Frota().DesfazerContratacaoAsync(empresa, c.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await RecarregarAsync(c.Id, "Contabilização da contratação desfeita.");
    });

    [RelayCommand]
    private Task RegistrarContemplacaoAsync() => ComContratoAsync(async (app, empresa, usuario, c) =>
    {
        var data = Ext.Dia(DataOperacao);
        if (!await Contexto.ConfirmarAsync("Registrar contemplação", $"Registrar a contemplação em {Ext.Data(data)}"
                                                                     + (c.Contabiliza ? " e contabilizar a entrada do bem?" : "?"))) return;
        var r = await app.Frota().RegistrarContemplacaoAsync(empresa, c.Id, data, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await RecarregarAsync(c.Id, "Contemplação registrada.");
    }, Permissao.FiscalDuplicatasManter);

    [RelayCommand]
    private Task ReclassificarAsync() => ComContratoAsync(async (app, empresa, usuario, c) =>
    {
        var r = await app.Frota().ReclassificarAsync(empresa, c.Id, Ext.Dia(DataOperacao), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await RecarregarAsync(c.Id, r.Valor > 0 ? $"{Formato.Moeda(r.Valor)} passaram do longo para o curto prazo." : $"{Formato.Moeda(-r.Valor)} voltaram para o longo prazo.");
    });

    [RelayCommand]
    private Task GerarBemAsync() => ComContratoAsync(async (app, empresa, usuario, c) =>
    {
        if (Grupo is null) { Erros.Add("Escolha o grupo do bem."); return; }
        var r = await app.Frota().GerarBemAsync(empresa, c.Id, Grupo.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await RecarregarAsync(c.Id, "Bem cadastrado no imobilizado (confira em Imobilizado › Cadastro de bens).");
    }, Permissao.FiscalDuplicatasManter);
}

public sealed record PosicaoLinha(PosicaoContrato P)
{
    public string Tipo => RegrasContratos.Rotulo(P.Contrato.Tipo);
    public string Numero => P.Contrato.Numero;
    public string Descricao => P.Contrato.Descricao;
    public string Credor => P.Contrato.Credor ?? "";
    public string Bem => P.Contrato.Bem ?? (P.Contrato.Consorcio && P.Contrato.DataContemplacao is null ? "(a contemplar)" : "(sem bem no imobilizado)");
    public string Placas => P.Placas;
    public string Valor => Formato.Moeda(P.Contrato.ValorBem);
    public string Parcelas => $"{P.Pagas}/{P.Contrato.Parcelas}";
    public string Saldo => Formato.Moeda(P.SaldoDevedor);
    public string Curto => Formato.Moeda(P.Prazos.CurtoPrazo);
    public string Longo => Formato.Moeda(P.Prazos.LongoPrazo);
    public string Situacao => RegrasContratos.Rotulo(P.Contrato.Situacao);
}

/// <summary>Financeiro › Imobilizado › Bens financiados e consorciados: o bem de cada contrato, as placas e o saldo devedor em curto e longo prazo.</summary>
public partial class BensContratosViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public ObservableCollection<PosicaoLinha> Lista { get; } = [];
    [ObservableProperty] private DateTime? _dataBase = DateTime.Today;
    [ObservableProperty] private string _totais = "";

    [RelayCommand]
    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        var itens = await app.Frota().PosicaoContratosAsync(empresa, Ext.Dia(DataBase), sessao.Id);
        if (!EhAtual(g)) return;
        Lista.Clear();
        foreach (var p in itens) Lista.Add(new PosicaoLinha(p));
        var comDivida = itens.Where(p => p.Contrato.TemDivida && p.Contrato.Situacao == "ATIVO").ToList();
        Totais = $"{itens.Count} contrato(s). Dívida em {Ext.Data(Ext.Dia(DataBase))}: curto prazo {Formato.Moeda(comDivida.Sum(p => p.Prazos.CurtoPrazo))}, "
                 + $"longo prazo {Formato.Moeda(comDivida.Sum(p => p.Prazos.LongoPrazo))}; bens {Formato.Moeda(itens.Sum(p => p.Contrato.ValorBem))}.";
    }
}
