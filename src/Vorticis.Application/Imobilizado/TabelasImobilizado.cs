using Vorticis.Application.Relatorios;

namespace Vorticis.Application.Imobilizado;

/// <summary>
/// Relatórios › Depreciação: relação de bens, valor depreciado, valor residual e resumo por grupo, na posição da última
/// depreciação lançada de cada bem (a depreciação anterior à implantação entra no acumulado).
/// </summary>
public static class TabelasImobilizado
{
    public const string TituloRelacao = "Relação de bens";
    public const string TituloDepreciado = "Valor depreciado";
    public const string TituloResidual = "Valor residual";
    public const string TituloPorGrupo = "Resumo da depreciação por grupo";

    private static LinhaRelatorio L(bool destaque, params object?[] valores) => new(valores, destaque);

    private static string Situacao(BemImobilizado b) => b.DataBaixa is { } d ? $"Baixado em {d:dd/MM/yyyy}" : "Em uso";

    private static IEnumerable<BemImobilizado> Filtrar(IEnumerable<BemImobilizado> bens, long? grupoId, bool incluirBaixados) =>
        bens.Where(b => (grupoId is null || b.GrupoId == grupoId) && (incluirBaixados || b.DataBaixa is null)).OrderBy(b => b.Codigo);

    private static string NomeGrupo(IReadOnlyDictionary<long, GrupoBem> grupos, long id) => grupos.TryGetValue(id, out var g) ? g.Descricao : "";

    public static TabelaRelatorio RelacaoBens(IReadOnlyList<BemImobilizado> bens, IReadOnlyList<GrupoBem> grupos, long? grupoId = null, bool incluirBaixados = true)
    {
        var porId = grupos.ToDictionary(g => g.Id);
        var lista = Filtrar(bens, grupoId, incluirBaixados).ToList();
        var linhas = lista.Select(b => L(false, b.Codigo, b.Descricao, NomeGrupo(porId, b.GrupoId), b.Plaqueta ?? "", b.Localizacao ?? "", b.NotaFiscal ?? "",
                                         b.DataAquisicao, b.InicioUso, b.Valor, Situacao(b))).ToList();
        linhas.Add(L(true, null, $"TOTAL ({lista.Count} bem(ns))", "", "", "", "", null, null, lista.Sum(b => b.Valor), ""));
        return new TabelaRelatorio(TituloRelacao,
        [
            new("Cód.", TipoColuna.Inteiro, 1.2), new("Descrição"), new("Grupo", LarguraCm: 3.5), new("Plaqueta", LarguraCm: 2), new("Localização", LarguraCm: 2.5),
            new("NF", LarguraCm: 1.6), new("Aquisição", TipoColuna.Data), new("Início do uso", TipoColuna.Data), new("Valor", TipoColuna.Valor), new("Situação", LarguraCm: 2.8),
        ], linhas);
    }

    public static TabelaRelatorio ValorDepreciado(IReadOnlyList<BemImobilizado> bens, IReadOnlyList<GrupoBem> grupos, long? grupoId = null, bool incluirBaixados = true)
    {
        var porId = grupos.ToDictionary(g => g.Id);
        var lista = Filtrar(bens, grupoId, incluirBaixados).ToList();
        static decimal Percentual(decimal parte, decimal todo) => todo == 0 ? 0 : Math.Round(parte / todo * 100m, 2);
        var linhas = lista.Select(b => L(false, b.Codigo, b.Descricao, NomeGrupo(porId, b.GrupoId), b.Valor, b.DepreciacaoAnterior, b.DepreciacaoLancada,
                                         b.Acumulada, Percentual(b.Acumulada, b.Valor), b.UltimaCompetencia is { } u ? u.ToString("MM/yyyy") : "")).ToList();
        decimal valor = lista.Sum(b => b.Valor), acumulada = lista.Sum(b => b.Acumulada);
        linhas.Add(L(true, null, "TOTAL", "", valor, lista.Sum(b => b.DepreciacaoAnterior), lista.Sum(b => b.DepreciacaoLancada), acumulada,
                     Percentual(acumulada, valor), ""));
        return new TabelaRelatorio(TituloDepreciado,
        [
            new("Cód.", TipoColuna.Inteiro, 1.2), new("Descrição"), new("Grupo", LarguraCm: 3.5), new("Valor do bem", TipoColuna.Valor),
            new("Anterior à implantação", TipoColuna.Valor), new("Lançada no sistema", TipoColuna.Valor), new("Depreciação acumulada", TipoColuna.Valor),
            new("% depreciado", TipoColuna.Valor, 1.8), new("Último mês", LarguraCm: 1.8),
        ], linhas);
    }

    public static TabelaRelatorio ValorResidual(IReadOnlyList<BemImobilizado> bens, IReadOnlyList<GrupoBem> grupos, long? grupoId = null, bool incluirBaixados = false)
    {
        var lista = Filtrar(bens, grupoId, incluirBaixados).ToList();
        static decimal ADepreciar(BemImobilizado b) => Math.Max(0, b.ValorContabil - b.Residual);
        var linhas = lista.Select(b => L(false, b.Codigo, b.Descricao, b.Valor, b.Acumulada, b.ValorContabil, b.Residual, ADepreciar(b), b.VidaUtilMeses)).ToList();
        linhas.Add(L(true, null, "TOTAL", lista.Sum(b => b.Valor), lista.Sum(b => b.Acumulada), lista.Sum(b => b.ValorContabil), lista.Sum(b => b.Residual),
                     lista.Sum(ADepreciar), null));
        return new TabelaRelatorio(TituloResidual,
        [
            new("Cód.", TipoColuna.Inteiro, 1.2), new("Descrição"), new("Valor do bem", TipoColuna.Valor), new("Depreciação acumulada", TipoColuna.Valor),
            new("Valor contábil líquido", TipoColuna.Valor), new("Valor residual", TipoColuna.Valor), new("Saldo a depreciar", TipoColuna.Valor),
            new("Vida útil (meses)", TipoColuna.Inteiro, 1.8),
        ], linhas);
    }

    public static TabelaRelatorio PorGrupo(IReadOnlyList<BemImobilizado> bens, IReadOnlyList<GrupoBem> grupos, bool incluirBaixados = false)
    {
        var porId = grupos.ToDictionary(g => g.Id);
        var lista = Filtrar(bens, null, incluirBaixados).ToList();
        var linhas = lista.GroupBy(b => b.GrupoId).OrderBy(g => porId.TryGetValue(g.Key, out var gr) ? gr.Codigo : "")
            .Select(g =>
            {
                var gr = porId.GetValueOrDefault(g.Key);
                return L(false, gr?.Codigo ?? "", gr?.Descricao ?? "", gr?.TaxaAnualFiscal ?? 0m, g.Count(), g.Sum(b => b.Valor), g.Sum(b => b.Acumulada),
                         g.Sum(b => b.ValorContabil));
            }).ToList();
        linhas.Add(L(true, "", "TOTAL", null, lista.Count, lista.Sum(b => b.Valor), lista.Sum(b => b.Acumulada), lista.Sum(b => b.ValorContabil)));
        return new TabelaRelatorio(TituloPorGrupo,
        [
            new("Grupo", LarguraCm: 1.6), new("Descrição"), new("Taxa anual (%)", TipoColuna.Valor, 2), new("Bens", TipoColuna.Inteiro, 1.4),
            new("Valor dos bens", TipoColuna.Valor), new("Depreciação acumulada", TipoColuna.Valor), new("Valor contábil líquido", TipoColuna.Valor),
        ], linhas);
    }
}
