using Vorticis.Application.Relatorios;
using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Fiscal.Duplicatas;

namespace Vorticis.Fiscal.Desktop;

/// <summary>Gráficos do financeiro (Relatórios › Gráficos).</summary>
public static class GraficosFiscais
{
    private static readonly DefinicaoGrafico PagarReceber = new("pagarreceber", "Contas a pagar e a receber por mês", "Financeiro", async c =>
    {
        async Task<IReadOnlyList<Duplicata>> Listar(TipoDuplicata tipo) =>
            await c.App.Fiscal().Duplicatas.ConsultarAsync(c.Empresa, new FiltroDuplicatas(tipo, c.Inicio, c.Fim), c.Usuario) is { Sucesso: true } r
                ? [.. r.Valor.Where(d => d.Situacao != SituacaoDuplicata.Cancelada)] : [];
        return MontagemGraficos.PorMes("Contas a pagar e a receber por mês", "R$", FormaGrafico.Barras, c.Inicio, c.Fim, (Duplicata d) => d.Vencimento,
                                       ("A receber", await Listar(TipoDuplicata.Receber), d => d.Valor), ("A pagar", await Listar(TipoDuplicata.Pagar), d => d.Valor)) with
               { Nota = "Valor das duplicatas pelo mês de vencimento (abertas, parciais e liquidadas; sem as canceladas)." };
    });

    private static readonly DefinicaoGrafico SaldoAberto = new("saldoaberto", "Saldo em aberto por mês de vencimento", "Financeiro", async c =>
    {
        async Task<IReadOnlyList<Duplicata>> Listar(TipoDuplicata tipo) =>
            await c.App.Fiscal().Duplicatas.ConsultarAsync(c.Empresa, new FiltroDuplicatas(tipo, c.Inicio, c.Fim, SomenteAbertas: true), c.Usuario) is { Sucesso: true } r
                ? r.Valor : [];
        return MontagemGraficos.PorMes("Saldo em aberto por mês de vencimento", "R$", FormaGrafico.Linhas, c.Inicio, c.Fim, (Duplicata d) => d.Vencimento,
                                       ("A receber", await Listar(TipoDuplicata.Receber), d => d.Saldo), ("A pagar", await Listar(TipoDuplicata.Pagar), d => d.Saldo)) with
               { Nota = "O que ainda falta receber e pagar, pelo mês de vencimento." };
    });

    /// <summary>Área dos indicadores de BI do financeiro (Relatórios › BI).</summary>
    public const string AreaBi = "BI Financeiro";

    private static async Task<IReadOnlyList<Duplicata>> DuplicatasAsync(ContextoGrafico c, TipoDuplicata tipo, DateOnly? inicio, DateOnly? fim, bool abertas = false) =>
        await c.App.Fiscal().Duplicatas.ConsultarAsync(c.Empresa, new FiltroDuplicatas(tipo, inicio, fim, SomenteAbertas: abertas, Limite: 20000), c.Usuario) is { Sucesso: true } r
            ? [.. r.Valor.Where(d => d.Situacao != SituacaoDuplicata.Cancelada)] : [];

    /// <summary>Fluxo de caixa realizado: o que entrou e saiu de fato (baixas) em cada mês, e o saldo do mês.</summary>
    private static readonly DefinicaoGrafico FluxoCaixa = new("bi.fluxocaixa", "Fluxo de caixa realizado por mês", AreaBi, async c =>
    {
        async Task<IReadOnlyList<BaixaDuplicata>> Baixas(TipoDuplicata tipo)
        {
            var dups = await DuplicatasAsync(c, tipo, c.Inicio.AddYears(-3), c.Fim.AddYears(1));
            if (dups.Count == 0) return [];
            return await c.App.Fiscal().Duplicatas.BaixasAsync(c.Empresa, [.. dups.Select(d => d.Id)], c.Usuario) is { Sucesso: true } r ? r.Valor : [];
        }
        var (recebido, pago) = (await Baixas(TipoDuplicata.Receber), await Baixas(TipoDuplicata.Pagar));
        var g = MontagemGraficos.PorMes("Fluxo de caixa realizado por mês", "R$", FormaGrafico.Barras, c.Inicio, c.Fim, (BaixaDuplicata b) => b.Data,
                                        ("Recebido", recebido, b => b.Liquido), ("Pago", pago, b => b.Liquido));
        var saldo = new SerieGrafico("Saldo do mês", [.. g.Series[0].Valores.Zip(g.Series[1].Valores, (r, p) => r - p)]);
        return g with { Series = [.. g.Series, saldo], Nota = "Baixas das contas a receber e a pagar pela data do pagamento (com juros e multa, menos descontos)." };
    });

    /// <summary>Faixas de atraso das contas em aberto na data final do período.</summary>
    public static readonly IReadOnlyList<(string Nome, int De, int Ate)> FaixasAtraso =
        [("A vencer", int.MinValue, 0), ("1 a 30 dias", 1, 30), ("31 a 60 dias", 31, 60), ("61 a 90 dias", 61, 90), ("Acima de 90 dias", 91, int.MaxValue)];

    /// <summary>Saldo em aberto por faixa de atraso (aging) na data de referência.</summary>
    public static IReadOnlyList<decimal> PorFaixa(IEnumerable<Duplicata> abertas, DateOnly referencia) =>
        [.. FaixasAtraso.Select(f => abertas.Where(d => d.EmAberto && referencia.DayNumber - d.Vencimento.DayNumber is var dias && dias >= f.De && dias <= f.Ate).Sum(d => d.Saldo))];

    private static readonly DefinicaoGrafico Inadimplencia = new("bi.inadimplencia", "Contas em aberto por faixa de atraso", AreaBi, async c =>
    {
        var referencia = c.Fim;
        var receber = await DuplicatasAsync(c, TipoDuplicata.Receber, null, null, abertas: true);
        var pagar = await DuplicatasAsync(c, TipoDuplicata.Pagar, null, null, abertas: true);
        return new DadosGrafico("Contas em aberto por faixa de atraso", "R$", FormaGrafico.Barras, [.. FaixasAtraso.Select(f => f.Nome)],
            [new SerieGrafico("A receber", PorFaixa(receber, referencia)), new SerieGrafico("A pagar", PorFaixa(pagar, referencia))],
            $"Saldo das contas em aberto em {referencia:dd/MM/yyyy}, pelos dias de atraso do vencimento.");
    });

    private static readonly DefinicaoGrafico MaioresClientes = new("bi.clientes", "Maiores clientes (a receber no período)", AreaBi, async c =>
        MontagemGraficos.PorCategoria("Maiores clientes (a receber no período)", "R$", await DuplicatasAsync(c, TipoDuplicata.Receber, c.Inicio, c.Fim),
            d => d.ParticipanteNome ?? "Sem cliente", "A receber", d => d.Valor, 10, "Contas a receber com vencimento no período, por cliente."));

    private static readonly DefinicaoGrafico MaioresFornecedores = new("bi.fornecedores", "Maiores fornecedores (a pagar no período)", AreaBi, async c =>
        MontagemGraficos.PorCategoria("Maiores fornecedores (a pagar no período)", "R$", await DuplicatasAsync(c, TipoDuplicata.Pagar, c.Inicio, c.Fim),
            d => d.ParticipanteNome ?? "Sem fornecedor", "A pagar", d => d.Valor, 10, "Contas a pagar com vencimento no período, por fornecedor."));

    /// <summary>Projeção do caixa: soma acumulada de (a receber − a pagar) das contas em aberto, pelo mês de vencimento.</summary>
    private static readonly DefinicaoGrafico ProjecaoCaixa = new("bi.projecao", "Projeção de caixa (contas em aberto)", AreaBi, async c =>
    {
        var receber = await DuplicatasAsync(c, TipoDuplicata.Receber, c.Inicio, c.Fim, abertas: true);
        var pagar = await DuplicatasAsync(c, TipoDuplicata.Pagar, c.Inicio, c.Fim, abertas: true);
        var g = MontagemGraficos.PorMes("Projeção de caixa (contas em aberto)", "R$", FormaGrafico.Linhas, c.Inicio, c.Fim, (Duplicata d) => d.Vencimento,
                                        ("A receber", receber, d => d.Saldo), ("A pagar", pagar, d => d.Saldo));
        decimal acumulado = 0;
        var saldo = new SerieGrafico("Saldo acumulado", [.. g.Series[0].Valores.Zip(g.Series[1].Valores, (r, p) => acumulado += r - p)]);
        return g with { Series = [.. g.Series, saldo], Nota = "Contas ainda em aberto pelo mês de vencimento e o saldo que se acumula mês a mês." };
    });

    public static IReadOnlyList<DefinicaoGrafico> Bi { get; } = [FluxoCaixa, Inadimplencia, ProjecaoCaixa, MaioresClientes, MaioresFornecedores];

    public static IReadOnlyList<DefinicaoGrafico> Todos { get; } = [PagarReceber, SaldoAberto, .. Bi];
}
