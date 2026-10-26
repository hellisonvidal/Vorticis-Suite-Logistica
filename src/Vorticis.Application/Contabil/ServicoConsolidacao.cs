using Vorticis.Application.Relatorios;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;

namespace Vorticis.Application.Contabil;

/// <summary>Agrupado: os valores das empresas somados conta a conta. Lado a lado: uma coluna por empresa e o total.</summary>
public enum FormaConsolidacao { Agrupado, LadoALado }

/// <summary>Empresa escolhida para o relatório do grupo (matriz ou filial).</summary>
public sealed record EmpresaRelatorio(long Id, string Nome);

/// <summary>
/// Relatórios contábeis de matriz e filiais: o mesmo relatório de cada empresa (com as permissões dela) juntado conta a conta pelo código
/// da conta. Com uma empresa só, é o relatório individual dela; com várias, agrupado (somado) ou lado a lado. As contas que existem só em
/// algumas empresas entram com zero nas outras e são avisadas no rodapé (o ideal é o mesmo plano de contas no grupo).
/// </summary>
public sealed class ServicoConsolidacao(ServicoRelatorios relatorios, ServicoDemonstracoes demonstracoes)
{
    /// <summary>Linha de uma empresa: código, descrição, nível, se é analítica e os valores a juntar.</summary>
    private sealed record Item(string Codigo, string Descricao, int Nivel, bool Analitica, decimal[] Valores);

    private static string Nomes(IReadOnlyList<EmpresaRelatorio> empresas) => string.Join(", ", empresas.Select(e => e.Nome));

    private static async Task<Resultado<List<List<Item>>>> ColetarAsync(IReadOnlyList<EmpresaRelatorio> empresas,
        Func<EmpresaRelatorio, Task<Resultado<List<Item>>>> ler)
    {
        if (empresas.Count == 0) return Resultado<List<List<Item>>>.Falha("Escolha ao menos uma empresa.");
        var todas = new List<List<Item>>();
        foreach (var e in empresas)
        {
            var r = await ler(e);
            if (!r.Sucesso) return Resultado<List<List<Item>>>.Falha(r.Erros.Select(x => $"{e.Nome}: {x}"));
            todas.Add(r.Valor);
        }
        return Resultado<List<List<Item>>>.Ok(todas);
    }

    /// <summary>
    /// Junta as linhas das empresas pelo código da conta. Agrupado: soma cada valor; lado a lado: o valor escolhido de cada empresa e o total.
    /// </summary>
    private static (List<LinhaRelatorio> Linhas, string? Aviso) Juntar(IReadOnlyList<EmpresaRelatorio> empresas, List<List<Item>> porEmpresa,
                                                                      FormaConsolidacao forma, int valorLadoALado, Func<decimal, object> exibir)
    {
        var codigos = porEmpresa.SelectMany(l => l).GroupBy(i => i.Codigo).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        var parciais = codigos.Where(g => porEmpresa.Count(l => l.Any(i => i.Codigo == g.Key)) < porEmpresa.Count).Select(g => g.Key).ToList();
        var linhas = new List<LinhaRelatorio>();
        foreach (var g in codigos)
        {
            var primeira = g.First();
            var recuo = Math.Max(0, primeira.Nivel - 1);
            if (forma == FormaConsolidacao.Agrupado || empresas.Count == 1)
            {
                var soma = Enumerable.Range(0, primeira.Valores.Length).Select(k => g.Sum(i => i.Valores[k])).Select(exibir);
                linhas.Add(new LinhaRelatorio([g.Key, primeira.Descricao, .. soma], !primeira.Analitica, recuo));
            }
            else
            {
                var cada = porEmpresa.Select(l => l.FirstOrDefault(i => i.Codigo == g.Key)?.Valores[valorLadoALado] ?? 0m).ToList();
                linhas.Add(new LinhaRelatorio([g.Key, primeira.Descricao, .. cada.Select(exibir), exibir(cada.Sum())], !primeira.Analitica, recuo));
            }
        }
        var aviso = parciais.Count == 0 || empresas.Count == 1 ? null
            : $"Contas que não existem em todas as empresas (entram com zero nas demais): {string.Join(", ", parciais.Take(15))}{(parciais.Count > 15 ? "..." : "")}.";
        return (linhas, aviso);
    }

    private static List<ColunaRelatorio> Colunas(IReadOnlyList<EmpresaRelatorio> empresas, FormaConsolidacao forma, IEnumerable<ColunaRelatorio> agrupadas, ColunaRelatorio porEmpresa) =>
        forma == FormaConsolidacao.Agrupado || empresas.Count == 1
            ? [new("Conta", LarguraCm: 2.6), new("Descrição"), .. agrupadas]
            : [new("Conta", LarguraCm: 2.6), new("Descrição"), .. empresas.Select(e => porEmpresa with { Titulo = e.Nome }), porEmpresa with { Titulo = "Total" }];

    /// <summary>Balancete: agrupado mostra saldo anterior, débitos, créditos e saldo final somados; lado a lado, o saldo final de cada empresa.</summary>
    public async Task<Resultado<TabelaRelatorio>> BalanceteAsync(IReadOnlyList<EmpresaRelatorio> empresas, DateOnly inicio, DateOnly fim, FormaConsolidacao forma,
                                                                 long usuario, CancellationToken ct = default)
    {
        var dados = await ColetarAsync(empresas, async e =>
        {
            var r = await relatorios.BalanceteAsync(e.Id, inicio, fim, usuario, ct: ct);
            return r.Sucesso
                ? Resultado<List<Item>>.Ok([.. r.Valor.Select(l => new Item(l.Codigo, l.Descricao, l.Nivel, l.Analitica, [l.SaldoAnterior, l.Debitos, l.Creditos, l.SaldoFinal]))])
                : Resultado<List<Item>>.Falha(r.Erros);
        });
        if (!dados.Sucesso) return Resultado<TabelaRelatorio>.Falha(dados.Erros);
        // saldos com sinal: positivo devedor (D), negativo credor (C); débitos e créditos como números
        var (linhas, aviso) = Juntar(empresas, dados.Valor, forma, 3, v => v);
        var agrupado = forma == FormaConsolidacao.Agrupado || empresas.Count == 1;
        linhas = [.. linhas.Select(l => l with
        {
            Valores = agrupado
                ? [l.Valores[0], l.Valores[1], LinhaBalancete.Formatar((decimal)l.Valores[2]!), l.Valores[3], l.Valores[4], LinhaBalancete.Formatar((decimal)l.Valores[5]!)]
                : [l.Valores[0], l.Valores[1], .. l.Valores.Skip(2).Select(v => (object?)LinhaBalancete.Formatar((decimal)v!))],
        })];
        var colunas = Colunas(empresas, forma, [new("Saldo anterior", LarguraCm: 3.2), new("Débitos", TipoColuna.Valor), new("Créditos", TipoColuna.Valor), new("Saldo final", LarguraCm: 3.2)],
                              new ColunaRelatorio("", LarguraCm: 3.2));
        return Resultado<TabelaRelatorio>.Ok(new TabelaRelatorio(Titulo("Balancete de verificação", empresas, forma), colunas, linhas,
            string.Join(" ", new[] { $"Empresas: {Nomes(empresas)}. Saldos: D = devedor, C = credor.", aviso }.Where(x => x is not null))));
    }

    /// <summary>DRE: os valores de cada linha (sinal natural do grupo) e o resultado do período.</summary>
    public async Task<Resultado<TabelaRelatorio>> DreAsync(IReadOnlyList<EmpresaRelatorio> empresas, DateOnly inicio, DateOnly fim, short nivel, FormaConsolidacao forma,
                                                           long usuario, CancellationToken ct = default)
    {
        var resultados = new List<decimal>();
        var dados = await ColetarAsync(empresas, async e =>
        {
            var r = await demonstracoes.DreAsync(e.Id, inicio, fim, nivel, usuario, ct);
            if (!r.Sucesso) return Resultado<List<Item>>.Falha(r.Erros);
            resultados.Add(r.Valor.Resultado);
            return Resultado<List<Item>>.Ok([.. r.Valor.Linhas.Select(l => new Item(l.Codigo, l.Descricao, l.Nivel, l.Tipo == TipoLinhaDemonstracao.Conta, [l.Valor]))]);
        });
        if (!dados.Sucesso) return Resultado<TabelaRelatorio>.Falha(dados.Erros);
        var (linhas, aviso) = Juntar(empresas, dados.Valor, forma, 0, v => v);
        var agrupado = forma == FormaConsolidacao.Agrupado || empresas.Count == 1;
        linhas.Add(new LinhaRelatorio(agrupado ? ["", "RESULTADO DO PERÍODO", resultados.Sum()] : ["", "RESULTADO DO PERÍODO", .. resultados.Cast<object?>(), resultados.Sum()], true));
        var colunas = Colunas(empresas, forma, [new ColunaRelatorio("Valor", TipoColuna.Valor)], new ColunaRelatorio("", TipoColuna.Valor));
        return Resultado<TabelaRelatorio>.Ok(new TabelaRelatorio(Titulo("Demonstração do Resultado (DRE)", empresas, forma), colunas, linhas,
            string.Join(" ", new[] { $"Empresas: {Nomes(empresas)}. Período de {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}.", aviso }.Where(x => x is not null))));
    }

    /// <summary>Balanço Patrimonial na data: ativo e passivo + patrimônio líquido, em duas tabelas.</summary>
    public async Task<Resultado<IReadOnlyList<TabelaRelatorio>>> BalancoAsync(IReadOnlyList<EmpresaRelatorio> empresas, DateOnly data, short nivel, FormaConsolidacao forma,
                                                                              long usuario, CancellationToken ct = default)
    {
        var ativos = new List<List<Item>>();
        var passivos = new List<List<Item>>();
        var dados = await ColetarAsync(empresas, async e =>
        {
            var r = await demonstracoes.BalancoAsync(e.Id, data, nivel, usuario, ct);
            if (!r.Sucesso) return Resultado<List<Item>>.Falha(r.Erros);
            ativos.Add([.. r.Valor.Ativo.Select(l => new Item(l.Codigo, l.Descricao, l.Nivel, l.Tipo == TipoLinhaDemonstracao.Conta, [l.Valor]))]);
            passivos.Add([.. r.Valor.PassivoPl.Select(l => new Item(l.Codigo, l.Descricao, l.Nivel, l.Tipo == TipoLinhaDemonstracao.Conta, [l.Valor]))]);
            return Resultado<List<Item>>.Ok([]);
        });
        if (!dados.Sucesso) return Resultado<IReadOnlyList<TabelaRelatorio>>.Falha(dados.Erros);
        var colunas = Colunas(empresas, forma, [new ColunaRelatorio("Valor", TipoColuna.Valor)], new ColunaRelatorio("", TipoColuna.Valor));
        var (a, avisoA) = Juntar(empresas, ativos, forma, 0, v => v);
        var (p, avisoP) = Juntar(empresas, passivos, forma, 0, v => v);
        var titulo = Titulo($"Balanço Patrimonial em {data:dd/MM/yyyy}", empresas, forma);
        return Resultado<IReadOnlyList<TabelaRelatorio>>.Ok([new TabelaRelatorio(titulo + " – Ativo", colunas, a, avisoA),
                                                             new TabelaRelatorio(titulo + " – Passivo e patrimônio líquido", colunas, p, avisoP ?? $"Empresas: {Nomes(empresas)}.")]);
    }

    private static string Titulo(string relatorio, IReadOnlyList<EmpresaRelatorio> empresas, FormaConsolidacao forma) =>
        empresas.Count == 1 ? $"{relatorio} – {empresas[0].Nome}"
        : forma == FormaConsolidacao.Agrupado ? $"{relatorio} – agrupado ({empresas.Count} empresas)" : $"{relatorio} – por empresa ({empresas.Count} empresas)";
}
