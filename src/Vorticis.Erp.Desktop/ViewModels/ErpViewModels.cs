using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra;

namespace Vorticis.Erp.Desktop.ViewModels;

public static class ErpTelas
{
    public const string Painel = "erp.painel";
    public const string PedidosVenda = "erp.pedidos.venda";
    public const string PedidosCompra = "erp.pedidos.compra";
    public const string Estoque = "erp.estoque";
    public const string Veiculos = "erp.veiculos";
    public const string Transporte = "erp.transporte";
    public const string MonitorNotas = "erp.monitor.notas";
    public const string MonitorCobrancas = "erp.monitor.cobrancas";
}

internal static class Ext
{
    public static ServicoErp Erp(this VorticisApp app) => ServicosErp.De(app);
    public static DateOnly Dia(DateTime? d) => d is { } v ? DateOnly.FromDateTime(v) : DateOnly.FromDateTime(DateTime.Today);
    public static DateTime Dt(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);
    public static string Q(decimal v) => v.ToString("#,##0.####", new System.Globalization.CultureInfo("pt-BR"));
}

public sealed record Opcao(long Id, string Texto)
{
    public override string ToString() => Texto;
}

// ---------------------------------------------------------------------------------------------- painel

public sealed record LinhaPainel(string Descricao, string Valor);

/// <summary>Relatórios › Painel do empresário: vendas, compras, contas, estoque e fluxo previsto, em linguagem direta.</summary>
public partial class PainelViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public ObservableCollection<LinhaPainel> Indicadores { get; } = [];
    public ObservableCollection<LinhaPainel> Clientes { get; } = [];
    public ObservableCollection<LinhaPainel> Produtos { get; } = [];
    public ObservableCollection<LinhaPainel> Fluxo { get; } = [];
    [ObservableProperty] private DateTime? _referencia = DateTime.Today;
    [ObservableProperty] private string _resumo = "";

    partial void OnReferenciaChanged(DateTime? value) => _ = CarregarAsync();

    public Task CarregarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Erp().PainelAsync(empresa, Ext.Dia(Referencia), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var p = r.Valor;
        string M(decimal v) => Formato.Moeda(v);
        Indicadores.Clear();
        foreach (var l in new LinhaPainel[]
        {
            new($"Vendas de {p.Mes:MM/yyyy}", M(p.Vendas)), new($"Compras de {p.Mes:MM/yyyy}", M(p.Compras)),
            new("A receber (em aberto)", M(p.ReceberAberto)), new("   dos quais vencidos", M(p.ReceberVencido)), new("   vencendo em 7 dias", M(p.Receber7Dias)),
            new("A pagar (em aberto)", M(p.PagarAberto)), new("   dos quais vencidos", M(p.PagarVencido)), new("   vencendo em 7 dias", M(p.Pagar7Dias)),
            new("Saldo previsto (receber − pagar)", M(p.ReceberAberto - p.PagarAberto)),
            new("Valor do estoque (custo médio)", M(p.ValorEstoque)), new("Produtos abaixo do mínimo", p.ProdutosAbaixoMinimo.ToString()),
            new("Pedidos de venda em aberto", p.PedidosVendaAbertos.ToString()), new("Pedidos de compra em aberto", p.PedidosCompraAbertos.ToString()),
        }) Indicadores.Add(l);
        Clientes.Clear();
        foreach (var (n, v) in p.MaioresClientes) Clientes.Add(new LinhaPainel(n, M(v)));
        Produtos.Clear();
        foreach (var (n, q, v) in p.ProdutosMaisVendidos) Produtos.Add(new LinhaPainel($"{n} ({Ext.Q(q)})", M(v)));
        Fluxo.Clear();
        foreach (var (s, rec, pag) in p.FluxoPrevisto) Fluxo.Add(new LinhaPainel($"Semana de {s:dd/MM}", $"+{M(rec)}  −{M(pag)}  = {M(rec - pag)}"));
        Resumo = p.Vendas >= p.Compras ? $"No mês, as vendas superam as compras em {M(p.Vendas - p.Compras)}." : $"No mês, as compras superam as vendas em {M(p.Compras - p.Vendas)}.";
    }, Permissao.ErpRelatorios);
}

// ---------------------------------------------------------------------------------------------- pedidos

public sealed record PedidoLinha(Pedido P)
{
    public int Numero => P.Numero;
    public string Data => P.Data.ToString("dd/MM/yyyy");
    public string Participante => P.ParticipanteNome ?? "";
    public string Total => Formato.Moeda(P.Total);
    public string Situacao => P.Situacao.Rotulo(P.Tipo);
    public string Concluido => P.ConcluidoEm is { } d ? d.ToString("dd/MM/yyyy") : "";
}

public sealed record ItemLinha(ItemPedido I)
{
    public string Produto => $"{I.ProdutoCodigo} - {I.ProdutoDescricao}";
    public string Quantidade => Ext.Q(I.Quantidade);
    public string Unitario => Formato.Moeda(I.ValorUnitario);
    public string Desconto => I.Desconto == 0 ? "" : Formato.Moeda(I.Desconto);
    public string Total => Formato.Moeda(I.Total);
}

public sealed record ProdutoOpcao(PosicaoEstoque P)
{
    public override string ToString() => $"{P.Codigo} - {P.Descricao} (saldo {Ext.Q(P.Saldo)})";
}

/// <summary>Vendas › Pedidos de venda e Compras › Pedidos de compra: digitação e conclusão (faturar / receber) do pedido.</summary>
public abstract partial class PedidosViewModel(IContexto contexto, TipoPedido tipo) : ModuloViewModel(contexto)
{
    public TipoPedido Tipo { get; } = tipo;
    public bool Venda => Tipo == TipoPedido.Venda;
    public string Titulo => Venda ? "Pedidos de venda" : "Pedidos de compra";
    public string RotuloParticipante => Venda ? "Cliente" : "Fornecedor";
    public string RotuloConcluir => Venda ? "Faturar pedido (gerar nota de saída)" : "Receber mercadorias (gerar nota de entrada)";
    public string AjudaConclusao => Venda
        ? "Faturar grava a nota fiscal de saída no Fiscal (com as parcelas a receber) e baixa o estoque. A emissão da NF-e na SEFAZ continua no seu emissor."
        : "Receber grava a nota de entrada do fornecedor no Fiscal (com as parcelas a pagar) e dá entrada no estoque pelo custo do pedido.";

    public ObservableCollection<PedidoLinha> Lista { get; } = [];
    public ObservableCollection<ItemLinha> Itens { get; } = [];
    public ObservableCollection<Opcao> Participantes { get; } = [];
    public ObservableCollection<ProdutoOpcao> ProdutosDisponiveis { get; } = [];

    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _fim = DateTime.Today;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(EmAberto))] private PedidoLinha? _selecionado;
    [ObservableProperty] private Opcao? _participante;
    [ObservableProperty] private DateTime? _data = DateTime.Today;
    [ObservableProperty] private decimal? _parcelas = 1;
    [ObservableProperty] private decimal? _intervalo = 30;
    [ObservableProperty] private DateTime? _primeiroVencimento;
    [ObservableProperty] private string _observacao = "";
    [ObservableProperty] private ProdutoOpcao? _produto;
    [ObservableProperty] private string _quantidade = "1";
    [ObservableProperty] private string _unitario = "";
    [ObservableProperty] private string _desconto = "";
    [ObservableProperty] private ItemLinha? _itemSelecionado;
    [ObservableProperty] private string _numeroNota = "";
    [ObservableProperty] private string _serieNota = "1";
    [ObservableProperty] private string _chaveNota = "";
    [ObservableProperty] private string _totalPedido = "";

    public bool EmAberto => Selecionado is null || Selecionado.P.Situacao == SituacaoPedido.Aberto;
    public bool PodeEditar => Contexto.Pode(Venda ? Permissao.ErpVendas : Permissao.ErpCompras);
    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeEditar));

    partial void OnInicioChanged(DateTime? value) => _ = CarregarAsync();
    partial void OnFimChanged(DateTime? value) => _ = CarregarAsync();
    partial void OnProdutoChanged(ProdutoOpcao? value)
    {
        if (value is null) return;
        var preco = Venda ? value.P.PrecoVenda : value.P.CustoMedio;
        Unitario = preco > 0 ? Formato.Num(preco) : "";
    }

    partial void OnSelecionadoChanged(PedidoLinha? value)
    {
        if (value?.P is not { } p) return;
        Participante = Participantes.FirstOrDefault(o => o.Id == p.ParticipanteId);
        (Data, Parcelas, Intervalo, PrimeiroVencimento, Observacao) = (Ext.Dt(p.Data), p.Parcelas, p.IntervaloDias, p.PrimeiroVencimento is { } v ? Ext.Dt(v) : null, p.Observacao ?? "");
        Itens.Clear();
        foreach (var i in p.Itens) Itens.Add(new ItemLinha(i));
        Totalizar();
    }

    private void Totalizar() => TotalPedido = $"Total do pedido: {Formato.Moeda(Itens.Sum(i => i.I.Total))}";

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        var lista = await app.Erp().ListarPedidosAsync(empresa, Tipo, Ext.Dia(Inicio), Ext.Dia(Fim), sessao.Id);
        var parts = await app.Erp().ParticipantesAsync(empresa, Tipo, sessao.Id);
        var prods = await app.Erp().ProdutosAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        Participantes.Clear();
        foreach (var (id, nome) in parts) Participantes.Add(new Opcao(id, nome));
        ProdutosDisponiveis.Clear();
        foreach (var p in prods) ProdutosDisponiveis.Add(new ProdutoOpcao(p));
        var sel = Selecionado?.P.Id;
        Lista.Clear();
        foreach (var p in lista) Lista.Add(new PedidoLinha(p));
        Selecionado = Lista.FirstOrDefault(l => l.P.Id == sel);
        if (Venda && string.IsNullOrEmpty(NumeroNota)) NumeroNota = (await app.Erp().ProximoNumeroNotaAsync(empresa)).ToString();
    }

    [RelayCommand]
    private void Novo()
    {
        Selecionado = null;
        (Participante, Data, Parcelas, Intervalo, PrimeiroVencimento, Observacao) = (null, DateTime.Today, 1, 30, null, "");
        Itens.Clear();
        Totalizar();
        Mensagem = $"Escolha o {RotuloParticipante.ToLowerInvariant()}, inclua os itens e clique em Gravar pedido.";
    }

    [RelayCommand]
    private void IncluirItem()
    {
        Erros.Clear();
        if (!EmAberto) { Erros.Add("O pedido não está em aberto."); return; }
        if (Produto is null) { Erros.Add("Escolha o produto."); return; }
        var q = Formato.LerValor(Quantidade) ?? 0;
        var u = Formato.LerValor(Unitario) ?? -1;
        var d = Formato.LerValor(Desconto) ?? 0;
        if (q <= 0) { Erros.Add("Informe a quantidade."); return; }
        if (u < 0) { Erros.Add("Informe o valor unitário."); return; }
        Itens.Add(new ItemLinha(new ItemPedido(Produto.P.ProdutoId, q, u, d, Produto.P.Descricao, Produto.P.Codigo)));
        (Quantidade, Desconto) = ("1", "");
        Totalizar();
    }

    [RelayCommand]
    private void RemoverItem()
    {
        if (ItemSelecionado is { } i && EmAberto) { Itens.Remove(i); Totalizar(); }
    }

    private Pedido Montar(long empresa) => new(Selecionado?.P.Id ?? 0, Tipo, 0, Ext.Dia(Data), Participante?.Id ?? 0, SituacaoPedido.Aberto, (int)(Parcelas ?? 1),
                                               (int)(Intervalo ?? 30), PrimeiroVencimento is { } v ? DateOnly.FromDateTime(v) : null,
                                               string.IsNullOrWhiteSpace(Observacao) ? null : Observacao, Itens.Select(i => i.I).ToList());

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Erp().GravarPedidoAsync(empresa, Montar(empresa), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Selecionado = Lista.FirstOrDefault(l => l.P.Id == r.Valor);
        Mensagem = $"Pedido nº {Selecionado?.Numero} gravado.";
    }, Venda ? Permissao.ErpVendas : Permissao.ErpCompras);

    [RelayCommand]
    private Task CancelarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado?.P is not { } p) { Erros.Add("Selecione o pedido na lista."); return; }
        if (!await Contexto.ConfirmarAsync("Cancelar pedido", $"Cancelar o pedido nº {p.Numero}?")) return;
        var r = await app.Erp().CancelarPedidoAsync(empresa, p, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = "Pedido cancelado.";
    }, Venda ? Permissao.ErpVendas : Permissao.ErpCompras);

    [RelayCommand]
    private Task ConcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado?.P is not { } p) { Erros.Add("Grave e selecione o pedido na lista."); return; }
        if (!await Contexto.ConfirmarAsync(Venda ? "Faturar pedido" : "Receber mercadorias",
                $"{(Venda ? "Faturar" : "Receber")} o pedido nº {p.Numero} ({Formato.Moeda(p.Total)}) com a nota nº {NumeroNota}, série {SerieNota}?")) return;
        var r = await app.Erp().ConcluirPedidoAsync(empresa, p, NumeroNota, SerieNota, ChaveNota, Ext.Dia(Data), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        (NumeroNota, ChaveNota) = ("", "");
        await CarregarAsync();
        Mensagem = Venda ? "Pedido faturado: a nota de saída e as contas a receber foram geradas e o estoque foi baixado."
                         : "Mercadorias recebidas: a nota de entrada e as contas a pagar foram geradas e o estoque foi atualizado.";
    }, Venda ? Permissao.ErpVendas : Permissao.ErpCompras);
}

public sealed class PedidosVendaViewModel(IContexto contexto) : PedidosViewModel(contexto, TipoPedido.Venda);
public sealed class PedidosCompraViewModel(IContexto contexto) : PedidosViewModel(contexto, TipoPedido.Compra);

// ---------------------------------------------------------------------------------------------- estoque

public sealed record PosicaoLinha(PosicaoEstoque P)
{
    public int Codigo => P.Codigo;
    public string Descricao => P.Descricao;
    public string Unidade => P.Unidade;
    public string Saldo => Ext.Q(P.Saldo);
    public string Minimo => P.Minimo == 0 ? "" : Ext.Q(P.Minimo);
    public string CustoMedio => Formato.Moeda(P.CustoMedio);
    public string Valor => Formato.Moeda(P.ValorEstoque);
    public string Preco => P.PrecoVenda == 0 ? "" : Formato.Moeda(P.PrecoVenda);
    public string Alerta => P.AbaixoDoMinimo ? "REPOR" : "";
}

public sealed record MovimentoLinha(MovimentoEstoque M)
{
    public string Data => M.Data.ToString("dd/MM/yyyy");
    public string Tipo => M.Tipo switch { "ENTRADA" => "Entrada", "SAIDA" => "Saída", _ => "Ajuste" };
    public string Quantidade => Ext.Q(M.Quantidade);
    public string Custo => M.CustoUnitario == 0 ? "" : Formato.Moeda(M.CustoUnitario);
    public string Origem => M.PedidoNumero is { } n ? $"Pedido {n}" : M.Observacao ?? "";
}

/// <summary>Estoque › Posição e movimentos: saldo e custo médio por produto, preço de venda, mínimo, ajustes e movimentos.</summary>
public partial class EstoqueViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public ObservableCollection<PosicaoLinha> Posicao { get; } = [];
    public ObservableCollection<MovimentoLinha> Movimentos { get; } = [];
    [ObservableProperty] private PosicaoLinha? _selecionado;
    [ObservableProperty] private string _preco = "";
    [ObservableProperty] private string _minimo = "";
    [ObservableProperty] private DateTime? _dataAjuste = DateTime.Today;
    [ObservableProperty] private string _quantidadeAjuste = "";
    [ObservableProperty] private string _custoAjuste = "";
    [ObservableProperty] private string _motivoAjuste = "";
    [ObservableProperty] private string _totais = "";

    public bool PodeEditar => Contexto.Pode(Permissao.ErpEstoque);
    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeEditar));

    partial void OnSelecionadoChanged(PosicaoLinha? value)
    {
        if (value is null) return;
        (Preco, Minimo, CustoAjuste) = (Formato.Num(value.P.PrecoVenda), Ext.Q(value.P.Minimo), Formato.Num(value.P.CustoMedio));
        _ = CarregarMovimentosAsync();
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        var itens = await app.Erp().ProdutosAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        var sel = Selecionado?.P.ProdutoId;
        Posicao.Clear();
        foreach (var p in itens) Posicao.Add(new PosicaoLinha(p));
        Selecionado = Posicao.FirstOrDefault(p => p.P.ProdutoId == sel);
        Totais = $"{itens.Count} produto(s) · valor do estoque {Formato.Moeda(itens.Sum(p => p.ValorEstoque))} · {itens.Count(p => p.AbaixoDoMinimo)} abaixo do mínimo";
    }

    private async Task CarregarMovimentosAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa || Selecionado is null) return;
        var m = await app.Erp().MovimentosAsync(empresa, Selecionado.P.ProdutoId, new DateOnly(2000, 1, 1), DateOnly.FromDateTime(DateTime.Today).AddYears(1), sessao.Id);
        Movimentos.Clear();
        foreach (var x in m) Movimentos.Add(new MovimentoLinha(x));
    }

    [RelayCommand]
    private Task GravarParametrosAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado is null) { Erros.Add("Selecione o produto."); return; }
        var r = await app.Erp().GravarParametroProdutoAsync(empresa, Selecionado.P.ProdutoId, Formato.LerValor(Preco) ?? 0, Formato.LerValor(Minimo) ?? 0, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = "Preço de venda e estoque mínimo gravados.";
    }, Permissao.ErpEstoque);

    [RelayCommand]
    private Task AjustarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado is null) { Erros.Add("Selecione o produto."); return; }
        var r = await app.Erp().AjustarEstoqueAsync(empresa, Selecionado.P.ProdutoId, Ext.Dia(DataAjuste), Formato.LerValor(QuantidadeAjuste) ?? 0,
                                                    Formato.LerValor(CustoAjuste) ?? 0, string.IsNullOrWhiteSpace(MotivoAjuste) ? "Ajuste de inventário" : MotivoAjuste, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        (QuantidadeAjuste, MotivoAjuste) = ("", "");
        await CarregarAsync();
        Mensagem = "Ajuste registrado.";
    }, Permissao.ErpEstoque);
}

// ---------------------------------------------------------------------------------------------- veículos e motoristas

/// <summary>Cadastros › Veículos e motoristas (usados nos manifestos e conhecimentos).</summary>
public partial class VeiculosViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public ObservableCollection<Veiculo> Veiculos { get; } = [];
    public ObservableCollection<Motorista> Motoristas { get; } = [];
    public static IReadOnlyList<string> Tipos { get; } = ["CAMINHAO", "CARRETA", "VAN", "UTILITARIO", "CARRO", "MOTO", "OUTRO"];

    [ObservableProperty] private Veiculo? _veiculoSelecionado;
    [ObservableProperty] private string _placa = "";
    [ObservableProperty] private string _renavam = "";
    [ObservableProperty] private string _tipo = "CAMINHAO";
    [ObservableProperty] private string _uf = "";
    [ObservableProperty] private decimal? _tara = 0;
    [ObservableProperty] private decimal? _capacidade = 0;
    [ObservableProperty] private Motorista? _motoristaSelecionado;
    [ObservableProperty] private string _nome = "";
    [ObservableProperty] private string _cpf = "";
    [ObservableProperty] private string _cnh = "";
    [ObservableProperty] private DateTime? _cnhValidade;

    public bool PodeEditar => Contexto.Pode(Permissao.ErpTransporte);
    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeEditar));

    partial void OnVeiculoSelecionadoChanged(Veiculo? value)
    {
        if (value is not { } v) return;
        (Placa, Renavam, Tipo, Uf, Tara, Capacidade) = (v.Placa, v.Renavam ?? "", v.Tipo, v.Uf, v.TaraKg, v.CapacidadeKg);
    }

    partial void OnMotoristaSelecionadoChanged(Motorista? value)
    {
        if (value is not { } m) return;
        (Nome, Cpf, Cnh, CnhValidade) = (m.Nome, m.Cpf, m.Cnh ?? "", m.CnhValidade is { } d ? Ext.Dt(d) : null);
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var v = await app.Erp().VeiculosAsync(empresa, sessao.Id);
        var m = await app.Erp().MotoristasAsync(empresa, sessao.Id);
        Veiculos.Clear();
        foreach (var x in v) Veiculos.Add(x);
        Motoristas.Clear();
        foreach (var x in m) Motoristas.Add(x);
    }

    [RelayCommand]
    private void NovoVeiculo() { VeiculoSelecionado = null; (Placa, Renavam, Tipo, Uf, Tara, Capacidade) = ("", "", "CAMINHAO", "", 0, 0); }

    [RelayCommand]
    private void NovoMotorista() { MotoristaSelecionado = null; (Nome, Cpf, Cnh, CnhValidade) = ("", "", "", null); }

    [RelayCommand]
    private Task GravarVeiculoAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Erp().GravarVeiculoAsync(empresa, new Veiculo(VeiculoSelecionado?.Id ?? 0, Placa, string.IsNullOrWhiteSpace(Renavam) ? null : Renavam,
                                                                        Tipo, Uf, (int)(Tara ?? 0), (int)(Capacidade ?? 0)), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = "Veículo gravado.";
    }, Permissao.ErpTransporte);

    [RelayCommand]
    private Task GravarMotoristaAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Erp().GravarMotoristaAsync(empresa, new Motorista(MotoristaSelecionado?.Id ?? 0, Nome, Cpf, string.IsNullOrWhiteSpace(Cnh) ? null : Cnh,
                                                                            CnhValidade is { } d ? DateOnly.FromDateTime(d) : null), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = "Motorista gravado.";
    }, Permissao.ErpTransporte);
}

// ---------------------------------------------------------------------------------------------- CT-e / MDF-e

public sealed record TransporteLinha(DocumentoTransporte D)
{
    public string Tipo => D.Tipo == "CTE" ? "CT-e" : "MDF-e";
    public string Numero => $"{D.Numero}/{D.Serie}";
    public string Emissao => D.Emissao.ToString("dd/MM/yyyy");
    public string Percurso => $"{D.UfInicio} → {D.UfFim}";
    public string Partes => D.Tipo == "CTE" ? $"{D.RemetenteNome} → {D.DestinatarioNome}" : D.MotoristaNome ?? "";
    public string Veiculo => D.Placa ?? "";
    public string Frete => Formato.Moeda(D.ValorFrete);
    public string Situacao => D.Situacao switch { "AUTORIZADO" => "Autorizado", "ENCERRADO" => "Encerrado", "CANCELADO" => "Cancelado", _ => "Digitado" };
}

/// <summary>Transporte › Conhecimentos e manifestos: registro e controle dos CT-e e MDF-e (emitidos no emissor da SEFAZ).</summary>
public partial class TransporteViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public ObservableCollection<TransporteLinha> Lista { get; } = [];
    public ObservableCollection<Opcao> Participantes { get; } = [];
    public ObservableCollection<Opcao> VeiculosOpcoes { get; } = [];
    public ObservableCollection<Opcao> MotoristasOpcoes { get; } = [];
    public static IReadOnlyList<string> Tipos { get; } = ["CTE", "MDFE"];
    public static IReadOnlyList<string> Situacoes { get; } = ["DIGITADO", "AUTORIZADO", "ENCERRADO", "CANCELADO"];

    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _fim = DateTime.Today;
    [ObservableProperty] private TransporteLinha? _selecionado;
    [ObservableProperty] private string _tipo = "CTE";
    [ObservableProperty] private string _numero = "";
    [ObservableProperty] private string _serie = "1";
    [ObservableProperty] private DateTime? _emissao = DateTime.Today;
    [ObservableProperty] private string _chave = "";
    [ObservableProperty] private Opcao? _remetente;
    [ObservableProperty] private Opcao? _destinatario;
    [ObservableProperty] private string _ufInicio = "";
    [ObservableProperty] private string _ufFim = "";
    [ObservableProperty] private Opcao? _veiculo;
    [ObservableProperty] private Opcao? _motorista;
    [ObservableProperty] private string _frete = "";
    [ObservableProperty] private string _valorCarga = "";
    [ObservableProperty] private string _peso = "";
    [ObservableProperty] private string _chavesNfe = "";
    [ObservableProperty] private string _situacao = "DIGITADO";

    public bool PodeEditar => Contexto.Pode(Permissao.ErpTransporte);
    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeEditar));
    partial void OnInicioChanged(DateTime? value) => _ = CarregarAsync();
    partial void OnFimChanged(DateTime? value) => _ = CarregarAsync();

    partial void OnSelecionadoChanged(TransporteLinha? value)
    {
        if (value?.D is not { } d) return;
        (Tipo, Numero, Serie, Emissao, Chave, UfInicio, UfFim, Situacao) = (d.Tipo, d.Numero.ToString(), d.Serie.ToString(), Ext.Dt(d.Emissao), d.Chave ?? "", d.UfInicio, d.UfFim, d.Situacao);
        (Remetente, Destinatario) = (Participantes.FirstOrDefault(p => p.Id == d.RemetenteId), Participantes.FirstOrDefault(p => p.Id == d.DestinatarioId));
        (Veiculo, Motorista) = (VeiculosOpcoes.FirstOrDefault(v => v.Id == d.VeiculoId), MotoristasOpcoes.FirstOrDefault(m => m.Id == d.MotoristaId));
        (Frete, ValorCarga, Peso, ChavesNfe) = (Formato.Num(d.ValorFrete), Formato.Num(d.ValorCarga), Ext.Q(d.PesoKg), d.ChavesNfe ?? "");
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        var lista = await app.Erp().DocumentosTransporteAsync(empresa, Ext.Dia(Inicio), Ext.Dia(Fim), sessao.Id);
        var c = await app.Erp().ParticipantesAsync(empresa, TipoPedido.Venda, sessao.Id);
        var f = await app.Erp().ParticipantesAsync(empresa, TipoPedido.Compra, sessao.Id);
        var v = await app.Erp().VeiculosAsync(empresa, sessao.Id);
        var m = await app.Erp().MotoristasAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        Participantes.Clear();
        foreach (var (id, nome) in c.Concat(f).DistinctBy(x => x.Id).OrderBy(x => x.Nome)) Participantes.Add(new Opcao(id, nome));
        VeiculosOpcoes.Clear();
        foreach (var x in v.Where(x => x.Ativo)) VeiculosOpcoes.Add(new Opcao(x.Id, $"{x.Placa} ({x.Tipo})"));
        MotoristasOpcoes.Clear();
        foreach (var x in m.Where(x => x.Ativo)) MotoristasOpcoes.Add(new Opcao(x.Id, x.Nome));
        Lista.Clear();
        foreach (var d in lista) Lista.Add(new TransporteLinha(d));
    }

    [RelayCommand]
    private void Novo()
    {
        Selecionado = null;
        (Numero, Chave, Remetente, Destinatario, Veiculo, Motorista, Frete, ValorCarga, Peso, ChavesNfe, Situacao) = ("", "", null, null, null, null, "", "", "", "", "DIGITADO");
        Emissao = DateTime.Today;
    }

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var d = new DocumentoTransporte(Selecionado?.D.Id ?? 0, Tipo, int.TryParse(Numero.Trim(), out var n) ? n : 0, short.TryParse(Serie.Trim(), out var s) ? s : 1,
                                        Ext.Dia(Emissao), string.IsNullOrWhiteSpace(Chave) ? null : new string(Chave.Where(char.IsAsciiDigit).ToArray()), Remetente?.Id,
                                        Destinatario?.Id, UfInicio, UfFim, Veiculo?.Id, Motorista?.Id, Formato.LerValor(Frete) ?? 0, Formato.LerValor(ValorCarga) ?? 0,
                                        Formato.LerValor(Peso) ?? 0, string.IsNullOrWhiteSpace(ChavesNfe) ? null : ChavesNfe, Situacao, null);
        var r = await app.Erp().GravarDocumentoTransporteAsync(empresa, d, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = "Documento de transporte gravado.";
    }, Permissao.ErpTransporte);
}
