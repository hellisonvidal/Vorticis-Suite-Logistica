using Vorticis.Domain.Comum;

namespace Vorticis.Erp;

public enum TipoPedido { Venda, Compra }
public enum SituacaoPedido { Aberto, Concluido, Cancelado }

public sealed record ItemPedido(long ProdutoId, decimal Quantidade, decimal ValorUnitario, decimal Desconto = 0, string? ProdutoDescricao = null, int ProdutoCodigo = 0)
{
    public decimal Total => Dinheiro.Arredondar(Quantidade * ValorUnitario) - Desconto;
}

public sealed record Pedido(long Id, TipoPedido Tipo, int Numero, DateOnly Data, long ParticipanteId, SituacaoPedido Situacao, int Parcelas, int IntervaloDias,
                            DateOnly? PrimeiroVencimento, string? Observacao, IReadOnlyList<ItemPedido> Itens, string? ParticipanteNome = null,
                            long? DocumentoId = null, DateOnly? ConcluidoEm = null)
{
    public decimal Total => Itens.Sum(i => i.Total);
}

public sealed record MovimentoEstoque(long Id, long ProdutoId, string Produto, DateOnly Data, string Tipo, decimal Quantidade, decimal CustoUnitario,
                                      int? PedidoNumero, string? Observacao);

public sealed record PosicaoEstoque(long ProdutoId, int Codigo, string Descricao, string Unidade, decimal Saldo, decimal CustoMedio, decimal Minimo, decimal PrecoVenda)
{
    public decimal ValorEstoque => Dinheiro.Arredondar(Math.Max(0, Saldo) * CustoMedio);
    public bool AbaixoDoMinimo => Minimo > 0 && Saldo < Minimo;
}

public sealed record Veiculo(long Id, string Placa, string? Renavam, string Tipo, string Uf, int TaraKg, int CapacidadeKg, bool Ativo = true);
public sealed record Motorista(long Id, string Nome, string Cpf, string? Cnh, DateOnly? CnhValidade, bool Ativo = true);

public sealed record DocumentoTransporte(long Id, string Tipo, int Numero, int Serie, DateOnly Emissao, string? Chave, long? RemetenteId, long? DestinatarioId,
                                         string UfInicio, string UfFim, long? VeiculoId, long? MotoristaId, decimal ValorFrete, decimal ValorCarga, decimal PesoKg,
                                         string? ChavesNfe, string Situacao, string? Observacao, string? RemetenteNome = null, string? DestinatarioNome = null,
                                         string? Placa = null, string? MotoristaNome = null);

/// <summary>Indicadores do painel do empresário (mês da data de referência).</summary>
public sealed record PainelEmpresario(DateOnly Mes, decimal Vendas, decimal Compras, decimal ReceberAberto, decimal ReceberVencido, decimal PagarAberto,
                                      decimal PagarVencido, decimal Receber7Dias, decimal Pagar7Dias, decimal ValorEstoque, int ProdutosAbaixoMinimo,
                                      int PedidosVendaAbertos, int PedidosCompraAbertos, IReadOnlyList<(string Nome, decimal Valor)> MaioresClientes,
                                      IReadOnlyList<(string Nome, decimal Quantidade, decimal Valor)> ProdutosMaisVendidos,
                                      IReadOnlyList<(DateOnly Semana, decimal Receber, decimal Pagar)> FluxoPrevisto);

/// <summary>Regras puras do ERP.</summary>
public static class RegrasErp
{
    /// <summary>Custo médio ponderado pelas entradas (ajustes positivos entram com o custo informado; saídas não alteram o custo).</summary>
    public static decimal CustoMedio(IEnumerable<(decimal Quantidade, decimal Custo)> entradas)
    {
        var l = entradas.Where(e => e.Quantidade > 0).ToList();
        var q = l.Sum(e => e.Quantidade);
        return q == 0 ? 0 : Math.Round(l.Sum(e => e.Quantidade * e.Custo) / q, 4);
    }

    /// <summary>Erros de um pedido antes de gravar.</summary>
    public static List<string> Validar(Pedido p)
    {
        var erros = new List<string>();
        if (p.ParticipanteId == 0) erros.Add(p.Tipo == TipoPedido.Venda ? "Escolha o cliente." : "Escolha o fornecedor.");
        if (p.Itens.Count == 0) erros.Add("Inclua ao menos um item.");
        if (p.Itens.Any(i => i.Quantidade <= 0)) erros.Add("A quantidade dos itens deve ser maior que zero.");
        if (p.Itens.Any(i => i.ValorUnitario < 0 || i.Desconto < 0)) erros.Add("Valores e descontos não podem ser negativos.");
        if (p.Itens.Any(i => i.Desconto > Dinheiro.Arredondar(i.Quantidade * i.ValorUnitario))) erros.Add("O desconto não pode passar do valor do item.");
        if (p.Parcelas is < 1 or > 120) erros.Add("Parcelas: de 1 a 120.");
        if (p.IntervaloDias is < 0 or > 365) erros.Add("Intervalo entre parcelas: de 0 a 365 dias.");
        return erros;
    }

    public static string Sql(this TipoPedido t) => t == TipoPedido.Venda ? "VENDA" : "COMPRA";
    public static SituacaoPedido SituacaoDe(string s) => s switch { "CONCLUIDO" => SituacaoPedido.Concluido, "CANCELADO" => SituacaoPedido.Cancelado, _ => SituacaoPedido.Aberto };
    public static string Rotulo(this SituacaoPedido s, TipoPedido t) => s switch
    {
        SituacaoPedido.Concluido => t == TipoPedido.Venda ? "Faturado" : "Recebido", SituacaoPedido.Cancelado => "Cancelado", _ => "Em aberto",
    };

    /// <summary>Placa no padrão antigo (ABC1234) ou Mercosul (ABC1D23), sem traço.</summary>
    public static string? PlacaValida(string? placa)
    {
        var p = new string((placa ?? "").ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
        return System.Text.RegularExpressions.Regex.IsMatch(p, "^[A-Z]{3}[0-9][A-Z0-9][0-9]{2}$") ? p : null;
    }
}
