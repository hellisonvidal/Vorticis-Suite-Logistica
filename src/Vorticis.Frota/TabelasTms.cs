using Vorticis.Application.Relatorios;
using Vorticis.Domain.Comum;

namespace Vorticis.Frota;

/// <summary>Relatórios do TMS: fretes por cliente e por rota (trecho), conhecimentos emitidos, prazo de entrega e faturas de frete.</summary>
public static class TabelasTms
{
    public const string TituloFretesCliente = "Fretes por cliente";
    public const string TituloFretesTrecho = "Fretes por trecho";
    public const string TituloConhecimentos = "Conhecimentos emitidos";
    public const string TituloPrazoEntrega = "Prazo de entrega";
    public const string TituloFaturas = "Faturas de frete";

    public static readonly IReadOnlyList<string> Titulos = [TituloFretesCliente, TituloFretesTrecho, TituloConhecimentos, TituloPrazoEntrega, TituloFaturas];

    private static LinhaRelatorio L(bool destaque, params object?[] v) => new(v, destaque);

    public static TabelaRelatorio Gerar(string titulo, IReadOnlyList<Cte> ctes, DateOnly hoje) => titulo switch
    {
        TituloConhecimentos => Conhecimentos(ctes),
        TituloPrazoEntrega => PrazoEntrega(ctes, hoje),
        _ => FretesPor(ctes, titulo),
    };

    /// <summary>Frete dos CT-e emitidos (sem os digitados e os cancelados) por tomador ou por trecho: quantidade, peso, valor da mercadoria, ICMS e frete.</summary>
    public static TabelaRelatorio FretesPor(IReadOnlyList<Cte> ctes, string titulo)
    {
        var validos = ctes.Where(c => RegrasTms.CteEmitido(c.Situacao)).ToList();
        string Chave(Cte c) => titulo == TituloFretesTrecho ? c.Trecho : c.Tomador ?? "";
        var linhas = new List<LinhaRelatorio>();
        foreach (var g in validos.GroupBy(Chave).OrderByDescending(g => g.Sum(c => c.ValorTotal)).ThenBy(g => g.Key))
            linhas.Add(L(false, g.Key, (long)g.Count(), g.Sum(c => c.PesoKg), g.Sum(c => c.ValorMercadoria), g.Sum(c => c.Frete.Icms), g.Sum(c => c.ValorTotal),
                         g.Sum(c => c.PesoKg) > 0 ? Math.Round(g.Sum(c => c.ValorTotal) / g.Sum(c => c.PesoKg), 4) : null));
        var peso = validos.Sum(c => c.PesoKg);
        linhas.Add(L(true, "TOTAL", (long)validos.Count, peso, validos.Sum(c => c.ValorMercadoria), validos.Sum(c => c.Frete.Icms), validos.Sum(c => c.ValorTotal),
                     peso > 0 ? Math.Round(validos.Sum(c => c.ValorTotal) / peso, 4) : null));
        return new TabelaRelatorio(titulo,
        [
            new(titulo == TituloFretesTrecho ? "Trecho" : "Cliente (tomador)"), new("CT-e", TipoColuna.Inteiro, 1.6), new("Peso (kg)", TipoColuna.Valor),
            new("Valor da mercadoria", TipoColuna.Valor), new("ICMS", TipoColuna.Valor), new("Frete", TipoColuna.Valor), new("Frete por kg", TipoColuna.Valor),
        ], linhas, "CT-e emitidos, em trânsito ou entregues no período (digitados e cancelados não entram).");
    }

    public static TabelaRelatorio Conhecimentos(IReadOnlyList<Cte> ctes)
    {
        var linhas = ctes.OrderBy(c => c.Emissao).ThenBy(c => c.Serie).ThenBy(c => c.Numero)
            .Select(c => L(c.Situacao == "CANCELADO", $"{c.Serie}-{c.Numero}", c.Emissao, c.Tomador, c.Trecho, c.PesoKg, c.Frete.Icms, c.ValorTotal,
                           RegrasTms.Rotulo(c.Situacao), c.FaturaNumero is { } f ? $"Fatura {f}" : "")).ToList();
        var validos = ctes.Where(c => c.Situacao != "CANCELADO").ToList();
        linhas.Add(L(true, "TOTAL", null, $"{validos.Count} CT-e (sem os cancelados)", "", validos.Sum(c => c.PesoKg), validos.Sum(c => c.Frete.Icms), validos.Sum(c => c.ValorTotal), "", ""));
        return new TabelaRelatorio(TituloConhecimentos,
        [
            new("CT-e", LarguraCm: 1.8), new("Emissão", TipoColuna.Data), new("Tomador"), new("Trecho"), new("Peso (kg)", TipoColuna.Valor), new("ICMS", TipoColuna.Valor),
            new("Frete", TipoColuna.Valor), new("Situação", LarguraCm: 2.2), new("Faturamento", LarguraCm: 2),
        ], linhas);
    }

    /// <summary>Entregas no período: previsão, data da entrega e situação do prazo; atrasados primeiro.</summary>
    public static TabelaRelatorio PrazoEntrega(IReadOnlyList<Cte> ctes, DateOnly hoje)
    {
        var lista = ctes.Where(c => RegrasTms.CteEmitido(c.Situacao))
            .Select(c => (C: c, Prazo: RegrasTms.SituacaoPrazo(c.PrevisaoEntrega, c.Situacao, c.EntregueEm, hoje)))
            .OrderBy(x => x.Prazo switch { "Atrasado" => 0, "Entregue com atraso" => 1, "No prazo" => 2, _ => 3 }).ThenBy(x => x.C.PrevisaoEntrega).ToList();
        var linhas = lista.Select(x => L(x.Prazo is "Atrasado" or "Entregue com atraso", $"{x.C.Serie}-{x.C.Numero}", x.C.Emissao, x.C.Destinatario, x.C.Trecho,
                                         x.C.PrevisaoEntrega, x.C.EntregueEm is { } e ? DateOnly.FromDateTime(e) : null, x.C.Recebedor ?? "", x.Prazo)).ToList();
        int entregues = lista.Count(x => x.C.Situacao == "ENTREGUE"), noPrazo = lista.Count(x => x.Prazo == "Entregue no prazo");
        linhas.Add(L(true, "TOTAL", null, $"{lista.Count} CT-e · {entregues} entregue(s)", "", null, null, "",
                     entregues > 0 ? $"{Math.Round(100m * noPrazo / entregues, 1).ToString("N1", Formato.PtBr)}% no prazo" : ""));
        return new TabelaRelatorio(TituloPrazoEntrega,
        [
            new("CT-e", LarguraCm: 1.8), new("Emissão", TipoColuna.Data), new("Destinatário"), new("Trecho"), new("Previsão", TipoColuna.Data), new("Entregue em", TipoColuna.Data),
            new("Recebedor"), new("Prazo", LarguraCm: 3),
        ], linhas, "Previsão = emissão + prazo da tabela de frete. % no prazo considera só os entregues.");
    }

    public static TabelaRelatorio Faturas(IReadOnlyList<FaturaFrete> faturas, DateOnly hoje)
    {
        var linhas = faturas.OrderBy(f => f.Emissao).ThenBy(f => f.Numero).Select(f =>
        {
            var aberta = f.Situacao == "ABERTA";
            var saldo = aberta ? f.Saldo ?? 0 : 0;
            var situacao = !aberta ? "Cancelada" : saldo == 0 ? "Recebida" : f.Vencimento < hoje ? "Vencida" : "A receber";
            return L(situacao == "Vencida", (long)f.Numero, f.Emissao, f.Vencimento, f.Cliente, (long)f.Ctes, aberta ? f.ValorTotal : 0, saldo, situacao);
        }).ToList();
        var abertas = faturas.Where(f => f.Situacao == "ABERTA").ToList();
        linhas.Add(L(true, "TOTAL", null, null, null, $"{abertas.Count} fatura(s) válidas", (long)abertas.Sum(f => f.Ctes), abertas.Sum(f => f.ValorTotal), abertas.Sum(f => f.Saldo ?? 0), ""));
        return new TabelaRelatorio(TituloFaturas,
        [
            new("Nº", TipoColuna.Inteiro, 1.4), new("Emissão", TipoColuna.Data), new("Vencimento", TipoColuna.Data), new("Cliente"), new("CT-e", TipoColuna.Inteiro, 1.4),
            new("Valor", TipoColuna.Valor), new("Saldo a receber", TipoColuna.Valor), new("Situação", LarguraCm: 2.2),
        ], linhas, "O saldo vem da conta a receber (Financeiro › Contas a receber).");
    }
}
