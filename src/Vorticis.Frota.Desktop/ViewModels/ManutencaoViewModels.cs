using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra;

namespace Vorticis.Frota.Desktop.ViewModels;

// ---------------------------------------------------------------------------------------------- peças e serviços

public sealed record ItemManutencaoLinha(ItemManutencao I)
{
    public int Codigo => I.Codigo;
    public string Tipo => RegrasFrota.RotuloManutencao(I.Tipo);
    public string Descricao => I.Descricao;
    public string Unidade => I.Unidade;
    public string Valor => I.ValorReferencia is { } v ? Formato.Moeda(v) : "";
    public string Situacao => I.Ativo ? "Ativo" : "Inativo";
}

/// <summary>Manutenção › Peças e serviços: catálogo com valor de referência, usado para orçar as ordens de serviço.</summary>
public partial class ItensManutencaoViewModel(IContexto contexto) : FrotaBase(contexto, Permissao.FrotaManutencao)
{
    public ObservableCollection<ItemManutencaoLinha> Lista { get; } = [];
    public static IReadOnlyList<OpcaoCodigo> Tipos { get; } = [.. RegrasFrota.TiposItem.Select(t => new OpcaoCodigo(t))];
    [ObservableProperty] private ItemManutencaoLinha? _selecionado;
    [ObservableProperty] private string _codigo = "";
    [ObservableProperty] private OpcaoCodigo? _tipo = Tipos[0];
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private string _unidade = "UN";
    [ObservableProperty] private string _valor = "";
    [ObservableProperty] private bool _ativo = true;

    partial void OnSelecionadoChanged(ItemManutencaoLinha? value)
    {
        if (value?.I is not { } i) return;
        (Codigo, Descricao, Unidade, Valor, Ativo) = (i.Codigo.ToString(), i.Descricao, i.Unidade, Ext.Num(i.ValorReferencia), i.Ativo);
        Tipo = Tipos.First(t => t.Valor == i.Tipo);
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        var itens = await app.Frota().ItensManutencaoAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        Lista.Clear();
        foreach (var i in itens) Lista.Add(new ItemManutencaoLinha(i));
        if (Selecionado is null && string.IsNullOrEmpty(Codigo)) Novo();
    }

    [RelayCommand]
    private void Novo()
    {
        Selecionado = null;
        Codigo = ((Lista.Count == 0 ? 0 : Lista.Max(l => l.I.Codigo)) + 1).ToString();
        (Tipo, Descricao, Unidade, Valor, Ativo) = (Tipos[0], "", "UN", "", true);
    }

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Frota().GravarItemManutencaoAsync(empresa, new ItemManutencao(Selecionado?.I.Id ?? 0, Ext.Int(Codigo) ?? 0, Tipo?.Valor ?? "", Descricao, Unidade,
                                                                                        Formato.LerValor(Valor), Ativo), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Selecionado = Lista.FirstOrDefault(l => l.I.Id == r.Valor);
        Mensagem = "Item gravado.";
    }, Permissao.FrotaManutencao);

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado?.I is not { } i) { Erros.Add("Selecione o item."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir item", $"Excluir \"{i.Descricao}\" do catálogo? As ordens que já o usam não mudam.")) return;
        var r = await app.Frota().ExcluirItemManutencaoAsync(empresa, i.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Selecionado = null;
        await CarregarAsync();
        Novo();
        Mensagem = "Item excluído.";
    }, Permissao.FrotaManutencao);
}

// ---------------------------------------------------------------------------------------------- ordens de serviço

public sealed record OrdemLinha(OrdemServico O)
{
    public int Numero => O.Numero;
    public string Data => Ext.Data(O.Data);
    public string Placa => RegrasFrota.FormatarPlaca(O.Placa ?? "");
    public string Tipo => RegrasFrota.Rotulo(O.Tipo);
    public string Descricao => O.Descricao;
    public string Oficina => O.Oficina ?? "";
    public string Total => Formato.Moeda(O.Total);
    public string Situacao => RegrasFrota.RotuloManutencao(O.Situacao);
    public string Previsao => Ext.Data(O.Previsao);
}

/// <summary>Item da ordem na tela (editável enquanto a ordem está aberta).</summary>
public sealed partial class ItemOrdemLinha : ObservableObject
{
    public long? ItemId { get; init; }
    [ObservableProperty] private string _tipo = "PECA";
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private decimal _quantidade = 1;
    [ObservableProperty] private decimal _valorUnitario;
    public string TipoTexto => RegrasFrota.RotuloManutencao(Tipo);
    public string QuantidadeTexto => Formato.Num(Quantidade);
    public string ValorTexto => Formato.Moeda(ValorUnitario);
    public string TotalTexto => Formato.Moeda(Math.Round(Quantidade * ValorUnitario, 2));
    public ItemOrdem Item => new(0, ItemId, Tipo, Descricao, Quantidade, ValorUnitario);
}

/// <summary>
/// Manutenção › Ordens de serviço: cabeçalho, peças e serviços, pedido de autorização, conclusão (renova o plano preventivo), cancelamento
/// e reabertura. As notas de compra ligadas aparecem embaixo.
/// </summary>
public partial class OrdensViewModel(IContexto contexto) : FrotaBase(contexto, Permissao.FrotaManutencao)
{
    private List<PlanoManutencao> _planos = [];
    private List<ItemManutencao> _catalogo = [];
    public ObservableCollection<OrdemLinha> Lista { get; } = [];
    public ObservableCollection<Opcao> PlanosOpcoes { get; } = [];
    public ObservableCollection<Opcao> CatalogoOpcoes { get; } = [];
    public ObservableCollection<ItemOrdemLinha> Itens { get; } = [];
    public static IReadOnlyList<OpcaoCodigo> Tipos { get; } = [new("CORRETIVA"), new("PREVENTIVA")];
    public static IReadOnlyList<OpcaoCodigo> TiposItem { get; } = [.. RegrasFrota.TiposItem.Select(t => new OpcaoCodigo(t))];
    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-2);
    [ObservableProperty] private DateTime? _fim = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1).AddDays(-1);
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Editavel), nameof(SituacaoTexto))] private OrdemLinha? _selecionada;
    [ObservableProperty] private Opcao? _veiculo;
    [ObservableProperty] private Opcao? _plano;
    [ObservableProperty] private OpcaoCodigo? _tipo = Tipos[0];
    [ObservableProperty] private DateTime? _data = DateTime.Today;
    [ObservableProperty] private DateTime? _previsao;
    [ObservableProperty] private string _km = "";
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private string _oficina = "";
    [ObservableProperty] private string _solicitante = "";
    [ObservableProperty] private string _pecas = "";
    [ObservableProperty] private string _maoObra = "";
    // novo item
    [ObservableProperty] private Opcao? _itemCatalogo;
    [ObservableProperty] private OpcaoCodigo? _itemTipo = TiposItem[0];
    [ObservableProperty] private string _itemDescricao = "";
    [ObservableProperty] private string _itemQuantidade = "1";
    [ObservableProperty] private string _itemValor = "";
    [ObservableProperty] private ItemOrdemLinha? _itemSelecionado;
    // conclusão
    [ObservableProperty] private DateTime? _conclusao = DateTime.Today;
    [ObservableProperty] private string _totais = "";
    [ObservableProperty] private string _totalOrdem = "";
    [ObservableProperty] private string _notasDaOrdem = "";

    /// <summary>Itens e valores só mudam na ordem nova, aberta ou reprovada.</summary>
    public bool Editavel => Selecionada is null || RegrasFrota.OrdemEditavel(Selecionada.O.Situacao);
    public bool ValoresManuais => Itens.Count == 0;
    public string SituacaoTexto => Selecionada is null ? "Nova ordem" : $"OS nº {Selecionada.Numero} – {Selecionada.Situacao}";

    partial void OnInicioChanged(DateTime? value) => _ = CarregarAsync();
    partial void OnFimChanged(DateTime? value) => _ = CarregarAsync();
    partial void OnVeiculoChanged(Opcao? value) => MontarPlanos(value?.Id, Plano?.Id);

    partial void OnPlanoChanged(Opcao? value)
    {
        if (value?.Id is null) return;
        Tipo = Tipos.First(t => t.Valor == "PREVENTIVA");
        if (string.IsNullOrWhiteSpace(Descricao)) Descricao = _planos.FirstOrDefault(p => p.Id == value.Id)?.Descricao ?? "";
    }

    /// <summary>Escolher do catálogo traz o tipo, a descrição e o valor de referência.</summary>
    partial void OnItemCatalogoChanged(Opcao? value)
    {
        if (_catalogo.FirstOrDefault(i => i.Id == value?.Id) is not { } i) return;
        ItemTipo = TiposItem.First(t => t.Valor == i.Tipo);
        ItemDescricao = i.Descricao;
        ItemValor = Ext.Num(i.ValorReferencia);
    }

    private void MontarPlanos(long? veiculo, long? manter)
    {
        PlanosOpcoes.Clear();
        PlanosOpcoes.Add(new Opcao(null, "(nenhum)"));
        foreach (var p in _planos.Where(p => p.VeiculoId == veiculo && (p.Ativo || p.Id == manter))) PlanosOpcoes.Add(new Opcao(p.Id, p.Descricao));
        Plano = PlanosOpcoes.FirstOrDefault(o => o.Id == manter) ?? PlanosOpcoes[0];
    }

    private void AtualizarTotais()
    {
        var (pecas, servicos) = Itens.Count > 0 ? RegrasFrota.Totais(Itens.Select(i => i.Item)) : (Formato.LerValor(Pecas) ?? 0, Formato.LerValor(MaoObra) ?? 0);
        if (Itens.Count > 0) (Pecas, MaoObra) = (Formato.Num(pecas), Formato.Num(servicos));
        TotalOrdem = $"Peças {Formato.Moeda(pecas)} + serviços {Formato.Moeda(servicos)} = {Formato.Moeda(pecas + servicos)}";
        OnPropertyChanged(nameof(ValoresManuais));
    }

    partial void OnSelecionadaChanged(OrdemLinha? value)
    {
        if (value?.O is not { } o) return;
        Veiculo = Escolher(VeiculosOpcoes, o.VeiculoId, () => RegrasFrota.FormatarPlaca(o.Placa ?? ""));
        MontarPlanos(o.VeiculoId, o.PlanoId);
        Tipo = Tipos.FirstOrDefault(t => t.Valor == o.Tipo);
        (Data, Previsao, Km, Descricao, Oficina, Solicitante) = (Ext.Dt(o.Data), Ext.DtOpc(o.Previsao), Ext.Num(o.Km), o.Descricao, o.Oficina ?? "", o.Solicitante ?? "");
        (Pecas, MaoObra, Conclusao) = (Formato.Num(o.ValorPecas), Formato.Num(o.ValorMaoObra), Ext.DtOpc(o.Conclusao) ?? DateTime.Today);
        _ = CarregarItensAsync(o.Id);
    }

    private async Task CarregarItensAsync(long ordem)
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var itens = await app.Frota().ItensOrdemAsync(empresa, ordem, sessao.Id);
        var notas = (await app.Frota().NotasCompraAsync(empresa, DateOnly.MinValue, DateOnly.MaxValue, sessao.Id)).Where(n => n.OrdemId == ordem).ToList();
        if (Selecionada?.O.Id != ordem) return;
        Itens.Clear();
        foreach (var i in itens) Itens.Add(new ItemOrdemLinha { ItemId = i.ItemId, Tipo = i.Tipo, Descricao = i.Descricao, Quantidade = i.Quantidade, ValorUnitario = i.ValorUnitario });
        AtualizarTotais();
        NotasDaOrdem = notas.Count == 0 ? "Nenhuma nota de compra ligada a esta ordem."
            : $"Notas de compra: {string.Join(", ", notas.Select(n => $"{n.Numero} ({n.Fornecedor}) {Formato.Moeda(n.Total)}"))} – total {Formato.Moeda(notas.Sum(n => n.Total))}";
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        await CarregarCombosAsync(app, empresa, sessao.Id);
        _planos = await app.Frota().PlanosAsync(empresa, sessao.Id);
        _catalogo = await app.Frota().ItensManutencaoAsync(empresa, sessao.Id);
        var itens = await app.Frota().OrdensAsync(empresa, Ext.Dia(Inicio), Ext.Dia(Fim), sessao.Id);
        if (!EhAtual(g)) return;
        CatalogoOpcoes.Clear();
        CatalogoOpcoes.Add(new Opcao(null, "(digitar)"));
        foreach (var i in _catalogo.Where(i => i.Ativo)) CatalogoOpcoes.Add(new Opcao(i.Id, $"{i.Codigo} – {i.Descricao}"));
        var selecionada = Selecionada?.O.Id;
        Lista.Clear();
        foreach (var o in itens) Lista.Add(new OrdemLinha(o));
        var andamento = itens.Where(o => o.Situacao is not ("CONCLUIDA" or "CANCELADA")).ToList();
        Totais = $"{itens.Count} ordem(ns) · {andamento.Count} em andamento · {itens.Count(o => o.Situacao == "AGUARDANDO")} aguardando autorização · " +
                 $"total {Formato.Moeda(itens.Where(o => o.Situacao != "CANCELADA").Sum(o => o.Total))}";
        Veiculo ??= VeiculosOpcoes.FirstOrDefault();
        ItemCatalogo ??= CatalogoOpcoes[0];
        if (selecionada is { } id) Selecionada = Lista.FirstOrDefault(l => l.O.Id == id);
        else MontarPlanos(Veiculo?.Id, Plano?.Id);
    }

    [RelayCommand]
    private void Nova()
    {
        Selecionada = null;
        (Tipo, Data, Previsao, Km, Descricao, Oficina, Solicitante, Pecas, MaoObra) = (Tipos[0], DateTime.Today, null, "", "", "", "", "", "");
        Itens.Clear();
        AtualizarTotais();
        NotasDaOrdem = "";
        Veiculo = VeiculosOpcoes.FirstOrDefault();
        MontarPlanos(Veiculo?.Id, null);
    }

    [RelayCommand]
    private void AdicionarItem()
    {
        Erros.Clear();
        var item = new ItemOrdem(0, ItemCatalogo?.Id, ItemTipo?.Valor ?? "", ItemDescricao, Formato.LerValor(ItemQuantidade) ?? 0, Formato.LerValor(ItemValor) ?? 0);
        if (RegrasFrota.Validar(item) is { Count: > 0 } erros) { foreach (var e in erros) Erros.Add(e); return; }
        Itens.Add(new ItemOrdemLinha { ItemId = item.ItemId, Tipo = item.Tipo, Descricao = item.Descricao.Trim(), Quantidade = item.Quantidade, ValorUnitario = item.ValorUnitario });
        (ItemCatalogo, ItemDescricao, ItemQuantidade, ItemValor) = (CatalogoOpcoes.FirstOrDefault(), "", "1", "");
        AtualizarTotais();
    }

    [RelayCommand]
    private void RemoverItem()
    {
        if (ItemSelecionado is { } i) Itens.Remove(i);
        AtualizarTotais();
    }

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Veiculo?.Id is not { } v) { Erros.Add("Escolha o veículo."); return; }
        var o = new OrdemServico(Selecionada?.O.Id ?? 0, Selecionada?.O.Numero ?? 0, v, Plano?.Id, Tipo?.Valor ?? "CORRETIVA", Ext.Dia(Data), Formato.LerValor(Km),
                                 Descricao, Oficina, Formato.LerValor(Pecas) ?? 0, Formato.LerValor(MaoObra) ?? 0, Solicitante: Solicitante, Previsao: Ext.DiaOpc(Previsao));
        var r = await app.Frota().GravarOrdemAsync(empresa, o, [.. Itens.Select(i => i.Item)], usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Selecionada = null;
        await CarregarAsync();
        Selecionada = Lista.FirstOrDefault(l => l.O.Numero == r.Valor);
        Mensagem = $"Ordem de serviço nº {r.Valor} gravada. Para executar o serviço, clique em Pedir autorização.";
    }, Permissao.FrotaManutencao);

    private Task AcaoAsync(AcaoOrdem acao, string confirmacao) => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada?.O is not { } o) { Erros.Add("Selecione a ordem de serviço (grave-a antes)."); return; }
        if (!await Contexto.ConfirmarAsync($"OS nº {o.Numero}", confirmacao)) return;
        var r = await app.Frota().MudarSituacaoAsync(empresa, o.Id, acao, usuario, conclusao: Ext.DiaOpc(Conclusao), km: Formato.LerValor(Km));
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = $"OS nº {o.Numero}: {RegrasFrota.RotuloManutencao(r.Valor!).ToLowerInvariant()}." +
                   (acao == AcaoOrdem.SolicitarAutorizacao && r.Valor == "APROVADA" ? " Autorizada automaticamente (dentro do limite dos Parâmetros)." : "") +
                   (acao == AcaoOrdem.Concluir && o.PlanoId is not null ? " O plano de manutenção foi renovado." : "");
    }, Permissao.FrotaManutencao);

    [RelayCommand] private Task PedirAutorizacaoAsync() => AcaoAsync(AcaoOrdem.SolicitarAutorizacao, "Enviar esta ordem para autorização?");
    [RelayCommand] private Task ConcluirAsync() => AcaoAsync(AcaoOrdem.Concluir, $"Concluir a ordem em {Conclusao:dd/MM/yyyy}?");
    [RelayCommand] private Task CancelarAsync() => AcaoAsync(AcaoOrdem.Cancelar, "Cancelar esta ordem de serviço?");
    [RelayCommand] private Task ReabrirAsync() => AcaoAsync(AcaoOrdem.Reabrir, "Reabrir esta ordem? Ela volta a precisar de autorização.");

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada?.O is not { } o) { Erros.Add("Selecione a ordem de serviço."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir ordem de serviço", $"Excluir a ordem nº {o.Numero}?")) return;
        var r = await app.Frota().ExcluirOrdemAsync(empresa, o.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Nova();
        await CarregarAsync();
        Mensagem = "Ordem de serviço excluída.";
    }, Permissao.FrotaManutencao);
}

// ---------------------------------------------------------------------------------------------- autorização de serviços

public sealed record AutorizacaoLinha(Autorizacao A)
{
    public string Momento => A.Momento.ToString("dd/MM/yyyy HH:mm");
    public int Ordem => A.NumeroOrdem;
    public string Placa => RegrasFrota.FormatarPlaca(A.Placa ?? "");
    public string Decisao => A.Decisao == "APROVADA" ? (A.Automatica ? "Autorizada (automática)" : "Autorizada") : "Reprovada";
    public string Valor => Formato.Moeda(A.Valor);
    public string Usuario => A.Automatica ? "Sistema" : A.Usuario ?? "";
    public string Observacao => A.Observacao ?? "";
}

/// <summary>Manutenção › Autorização de serviços: ordens aguardando autorização, com peças e serviços, para aprovar ou reprovar (com motivo).</summary>
public partial class AutorizacaoViewModel(IContexto contexto) : FrotaBase(contexto, Permissao.FrotaAprovar)
{
    public ObservableCollection<OrdemLinha> Pendentes { get; } = [];
    public ObservableCollection<ItemOrdemLinha> Itens { get; } = [];
    public ObservableCollection<AutorizacaoLinha> Historico { get; } = [];
    [ObservableProperty] private OrdemLinha? _selecionada;
    [ObservableProperty] private string _observacao = "";
    [ObservableProperty] private string _detalhe = "";
    [ObservableProperty] private string _resumo = "";

    partial void OnSelecionadaChanged(OrdemLinha? value) => _ = CarregarItensAsync(value?.O);

    private async Task CarregarItensAsync(OrdemServico? o)
    {
        Itens.Clear();
        Detalhe = "";
        if (o is null || Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var itens = await app.Frota().ItensOrdemAsync(empresa, o.Id, sessao.Id);
        if (Selecionada?.O.Id != o.Id) return;
        foreach (var i in itens) Itens.Add(new ItemOrdemLinha { ItemId = i.ItemId, Tipo = i.Tipo, Descricao = i.Descricao, Quantidade = i.Quantidade, ValorUnitario = i.ValorUnitario });
        Detalhe = $"OS nº {o.Numero} – {RegrasFrota.FormatarPlaca(o.Placa ?? "")} – {o.Descricao}" + (o.Oficina is { } of ? $" – oficina {of}" : "") +
                  (o.Solicitante is { } s ? $" – solicitada por {s}" : "") + $"\nPeças {Formato.Moeda(o.ValorPecas)} + serviços {Formato.Moeda(o.ValorMaoObra)} = {Formato.Moeda(o.Total)}";
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var ordens = await app.Frota().OrdensAsync(empresa, hoje, hoje, sessao.Id);
        var historico = await app.Frota().AutorizacoesAsync(empresa, hoje.AddDays(-30), hoje, sessao.Id);
        if (!EhAtual(g)) return;
        Pendentes.Clear();
        foreach (var o in ordens.Where(o => o.Situacao == "AGUARDANDO").OrderBy(o => o.Data)) Pendentes.Add(new OrdemLinha(o));
        Historico.Clear();
        foreach (var a in historico.OrderByDescending(a => a.Momento)) Historico.Add(new AutorizacaoLinha(a));
        Resumo = Pendentes.Count == 0 ? "Nenhuma ordem aguardando autorização."
            : $"{Pendentes.Count} ordem(ns) aguardando autorização · {Formato.Moeda(Pendentes.Sum(p => p.O.Total))}";
        Selecionada = Pendentes.FirstOrDefault();
    }

    private Task DecidirAsync(AcaoOrdem acao) => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada?.O is not { } o) { Erros.Add("Selecione a ordem a autorizar."); return; }
        var r = await app.Frota().MudarSituacaoAsync(empresa, o.Id, acao, usuario, Observacao);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Observacao = "";
        await CarregarAsync();
        Mensagem = $"OS nº {o.Numero} {(acao == AcaoOrdem.Aprovar ? "autorizada" : "reprovada")}.";
    }, Permissao.FrotaAprovar);

    [RelayCommand] private Task AprovarAsync() => DecidirAsync(AcaoOrdem.Aprovar);
    [RelayCommand] private Task ReprovarAsync() => DecidirAsync(AcaoOrdem.Reprovar);
    [RelayCommand] private Task AtualizarAsync() => CarregarAsync();
}

// ---------------------------------------------------------------------------------------------- notas de compra

public sealed record NotaLinha(NotaCompra N)
{
    public string Emissao => Ext.Data(N.Emissao);
    public string Numero => $"{N.Numero}/{N.Serie}";
    public string Fornecedor => N.Fornecedor;
    public string Ordem => N.NumeroOrdem is { } o ? $"{o} ({RegrasFrota.FormatarPlaca(N.Placa ?? "")})" : "";
    public string Total => Formato.Moeda(N.Total);
}

/// <summary>Manutenção › Notas de compra: notas fiscais de peças e serviços, ligadas à ordem de serviço que atenderam.</summary>
public partial class NotasCompraViewModel(IContexto contexto) : FrotaBase(contexto, Permissao.FrotaManutencao)
{
    private List<OrdemServico> _ordens = [];
    public ObservableCollection<NotaLinha> Lista { get; } = [];
    public ObservableCollection<Opcao> OrdensOpcoes { get; } = [];
    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _fim = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1).AddDays(-1);
    [ObservableProperty] private NotaLinha? _selecionada;
    [ObservableProperty] private Opcao? _ordem;
    [ObservableProperty] private string _fornecedor = "";
    [ObservableProperty] private string _cnpj = "";
    [ObservableProperty] private string _numero = "";
    [ObservableProperty] private string _serie = "1";
    [ObservableProperty] private DateTime? _emissao = DateTime.Today;
    [ObservableProperty] private string _chave = "";
    [ObservableProperty] private string _valorPecas = "";
    [ObservableProperty] private string _valorServicos = "";
    [ObservableProperty] private string _observacao = "";
    [ObservableProperty] private string _totais = "";
    [ObservableProperty] private string _conferencia = "";

    partial void OnInicioChanged(DateTime? value) => _ = CarregarAsync();
    partial void OnFimChanged(DateTime? value) => _ = CarregarAsync();

    /// <summary>Ao escolher a ordem: traz a oficina como fornecedor e mostra o valor da ordem para conferir com a nota.</summary>
    partial void OnOrdemChanged(Opcao? value)
    {
        if (_ordens.FirstOrDefault(o => o.Id == value?.Id) is not { } o) { Conferencia = ""; return; }
        if (string.IsNullOrWhiteSpace(Fornecedor) && o.Oficina is { } of) Fornecedor = of;
        Conferencia = $"OS nº {o.Numero}: peças {Formato.Moeda(o.ValorPecas)}, serviços {Formato.Moeda(o.ValorMaoObra)}, total {Formato.Moeda(o.Total)} ({RegrasFrota.RotuloManutencao(o.Situacao)})";
    }

    partial void OnSelecionadaChanged(NotaLinha? value)
    {
        if (value?.N is not { } n) return;
        (Fornecedor, Cnpj, Numero, Serie, Emissao, Chave) = (n.Fornecedor, n.Cnpj ?? "", n.Numero, n.Serie, Ext.Dt(n.Emissao), n.Chave ?? "");
        (ValorPecas, ValorServicos, Observacao) = (Formato.Num(n.ValorPecas), Formato.Num(n.ValorServicos), n.Observacao ?? "");
        Ordem = Escolher(OrdensOpcoes, n.OrdemId, () => $"OS nº {n.NumeroOrdem}");
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        _ordens = await app.Frota().OrdensAsync(empresa, hoje.AddMonths(-6), hoje, sessao.Id);
        var notas = await app.Frota().NotasCompraAsync(empresa, Ext.Dia(Inicio), Ext.Dia(Fim), sessao.Id);
        if (!EhAtual(g)) return;
        var manter = Ordem?.Id;
        OrdensOpcoes.Clear();
        OrdensOpcoes.Add(new Opcao(null, "(sem ordem de serviço)"));
        foreach (var o in _ordens.Where(o => o.Situacao != "CANCELADA").OrderByDescending(o => o.Numero))
            OrdensOpcoes.Add(new Opcao(o.Id, $"OS nº {o.Numero} – {RegrasFrota.FormatarPlaca(o.Placa ?? "")} – {o.Descricao}"));
        Ordem = OrdensOpcoes.FirstOrDefault(o => o.Id == manter) ?? OrdensOpcoes[0];
        Lista.Clear();
        foreach (var n in notas) Lista.Add(new NotaLinha(n));
        Totais = $"{notas.Count} nota(s) · peças {Formato.Moeda(notas.Sum(n => n.ValorPecas))} · serviços {Formato.Moeda(notas.Sum(n => n.ValorServicos))} · " +
                 $"total {Formato.Moeda(notas.Sum(n => n.Total))}" + (notas.Count(n => n.OrdemId is null) is > 0 and var s ? $" · {s} sem ordem de serviço" : "");
    }

    [RelayCommand]
    private void Nova()
    {
        Selecionada = null;
        (Fornecedor, Cnpj, Numero, Serie, Emissao, Chave, ValorPecas, ValorServicos, Observacao) = ("", "", "", "1", DateTime.Today, "", "", "", "");
        Ordem = OrdensOpcoes.FirstOrDefault();
    }

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var n = new NotaCompra(Selecionada?.N.Id ?? 0, Ordem?.Id, Fornecedor, Cnpj, Numero, Serie, Ext.Dia(Emissao), Chave, Formato.LerValor(ValorPecas) ?? 0,
                               Formato.LerValor(ValorServicos) ?? 0, Observacao);
        var r = await app.Frota().GravarNotaCompraAsync(empresa, n, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Selecionada = Lista.FirstOrDefault(l => l.N.Id == r.Valor);
        var os = _ordens.FirstOrDefault(o => o.Id == n.OrdemId);
        Mensagem = os is not null && n.Total > os.Total
            ? $"Nota gravada. Atenção: o valor da nota ({Formato.Moeda(n.Total)}) passa o da OS nº {os.Numero} ({Formato.Moeda(os.Total)})."
            : "Nota gravada.";
    }, Permissao.FrotaManutencao);

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada?.N is not { } n) { Erros.Add("Selecione a nota."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir nota de compra", $"Excluir a nota {n.Numero} de {n.Fornecedor}?")) return;
        var r = await app.Frota().ExcluirNotaCompraAsync(empresa, n.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Nova();
        await CarregarAsync();
        Mensagem = "Nota excluída.";
    }, Permissao.FrotaManutencao);
}
