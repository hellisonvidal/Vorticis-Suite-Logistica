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

    public static IReadOnlyList<DefinicaoGrafico> Todos { get; } = [PagarReceber, SaldoAberto];
}
