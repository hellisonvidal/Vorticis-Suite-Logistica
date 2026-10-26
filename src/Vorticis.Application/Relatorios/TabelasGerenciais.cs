using System.Globalization;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;

namespace Vorticis.Application.Relatorios;

/// <summary>Relatórios › Gerenciais como tabelas de relatório: a tela e as exportações (PDF, Excel, CSV) usam as mesmas tabelas.</summary>
public static class TabelasGerenciais
{
    public const string TituloCentroCusto = "Contabilização por centro de custo";
    public const string TituloOrcadoRealizado = "Orçado x Realizado";
    public const string TituloCustos = "Relação de custos e despesas";
    public const string TituloReceitaCentro = "Receita por centro de custo";

    private static readonly CultureInfo PtBr = Formato.PtBr;
    private static bool Destaca(TipoLinhaGerencial t) => t is TipoLinhaGerencial.Grupo or TipoLinhaGerencial.Total;
    private static string DC(decimal saldo) => saldo == 0 ? "" : saldo > 0 ? "D" : "C";

    public static TabelaRelatorio CentroCusto(IReadOnlyList<LinhaCentroConta> linhas)
    {
        var detalhe = linhas.Any(l => l.Tipo == TipoLinhaGerencial.Detalhe);
        var colunas = new List<ColunaRelatorio> { new("Código", LarguraCm: 3.2), new("Descrição"), new("Débitos", TipoColuna.Valor), new("Créditos", TipoColuna.Valor),
                                                  new("Saldo", TipoColuna.Valor), new("D/C", LarguraCm: 1.1) };
        if (detalhe) { colunas.Add(new("Data", TipoColuna.Data)); colunas.Add(new("Lançamento", TipoColuna.Inteiro, 2)); }
        var rows = linhas.Select(l =>
        {
            var v = new List<object?> { l.Codigo, l.Descricao, l.Debitos, l.Creditos, Math.Abs(l.Saldo), DC(l.Saldo) };
            if (detalhe) { v.Add(l.Data); v.Add(l.Numero); }
            return new LinhaRelatorio(v, Destaca(l.Tipo), l.Recuo);
        }).ToList();
        return new(TituloCentroCusto, colunas, rows, "Saldo = débitos − créditos (D = devedor, C = credor). Os centros sintéticos somam os analíticos abaixo deles; sem filtro de centro, as partidas sem centro de custo aparecem em \"Sem centro de custo\". Lançamentos de encerramento do exercício não entram.");
    }

    public static TabelaRelatorio Custos(IReadOnlyList<LinhaCusto> linhas, bool comparar)
    {
        var colunas = new List<ColunaRelatorio> { new("Código", LarguraCm: 3.2), new("Descrição"), new("Valor", TipoColuna.Valor), new("Part. %", TipoColuna.Valor, 2) };
        if (comparar)
        {
            colunas.Add(new("Período comparado", TipoColuna.Valor));
            colunas.Add(new("Variação", TipoColuna.Valor));
            colunas.Add(new("Var. %", TipoColuna.Valor, 2));
        }
        var rows = linhas.Select(l =>
        {
            var v = new List<object?> { l.Codigo, l.Descricao, l.Valor, l.Participacao };
            if (comparar) { v.Add(l.Anterior); v.Add(l.Variacao); v.Add(l.VariacaoPercentual); }
            return new LinhaRelatorio(v, Destaca(l.Tipo), l.Recuo);
        }).ToList();
        return new(TituloCustos, colunas, rows, "Contas de resultado de natureza devedora (custos, despesas e deduções da receita). Part. % = participação no total de custos e despesas do período.");
    }

    public static TabelaRelatorio ReceitaCentro(ReceitaPorCentro r)
    {
        var colunas = new List<ColunaRelatorio> { new("Código", LarguraCm: 2.4), new("Centro de custo") };
        colunas.AddRange(r.Meses.Select(m => new ColunaRelatorio(m.ToString("MM/yyyy", PtBr), TipoColuna.Valor, 2.2)));
        colunas.AddRange([new("Receita", TipoColuna.Valor, 2.4), new("% do total", TipoColuna.Valor, 1.8), new("Custos e despesas", TipoColuna.Valor, 2.4),
                          new("Margem", TipoColuna.Valor, 2.4), new("Margem %", TipoColuna.Valor, 1.8)]);
        var rows = r.Linhas.Select(l =>
        {
            var v = new List<object?> { l.Codigo, l.Descricao };
            v.AddRange(l.PorMes.Select(x => (object?)x));
            v.AddRange([l.Receita, l.Participacao, l.CustosDespesas, l.Margem, l.MargemPercentual]);
            return new LinhaRelatorio(v, Destaca(l.Tipo), l.Recuo);
        }).ToList();
        return new(TituloReceitaCentro, colunas, rows, "Margem = receita − custos − despesas. Margem % = margem ÷ receita. Os centros sintéticos somam os analíticos abaixo deles.");
    }

    public static TabelaRelatorio OrcadoRealizado(IReadOnlyList<LinhaOrcadoRealizado> linhas, bool mensal)
    {
        var colunas = new List<ColunaRelatorio> { new("Conta", LarguraCm: 2.6), new("Descrição"), new("Centro de custo", LarguraCm: 3.6) };
        if (mensal) colunas.Add(new("Mês", TipoColuna.Inteiro, 1.1));
        colunas.AddRange([new("Orçado", TipoColuna.Valor, 2.3), new("Realizado", TipoColuna.Valor, 2.3), new("Diferença", TipoColuna.Valor, 2.3), new("%", TipoColuna.Valor, 1.6)]);
        if (mensal)
            colunas.AddRange([new("Acum. orçado", TipoColuna.Valor, 2.3), new("Acum. realizado", TipoColuna.Valor, 2.3), new("Acum. diferença", TipoColuna.Valor, 2.3), new("Acum. %", TipoColuna.Valor, 1.6)]);
        var rows = linhas.Select(l =>
        {
            var v = new List<object?> { l.Codigo, l.Descricao, l.Centro };
            if (mensal) v.Add(l.Mes);
            v.AddRange([l.Orcado, l.Realizado, l.Diferenca, l.Percentual]);
            if (mensal) v.AddRange([l.AcumOrcado, l.AcumRealizado, l.AcumDiferenca, l.AcumPercentual]);
            return new LinhaRelatorio(v, Destaca(l.Tipo));
        }).ToList();
        return new(TituloOrcadoRealizado, colunas, rows, "Valores na natureza da conta. Diferença = realizado − orçado; % = realizado ÷ orçado.");
    }
}
