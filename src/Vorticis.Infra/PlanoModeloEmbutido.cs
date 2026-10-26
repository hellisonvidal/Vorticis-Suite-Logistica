using System.Text.Json;
using Vorticis.Application.Cadastros;
using Vorticis.Application.Contabil;
using Vorticis.Domain.Contabil;

namespace Vorticis.Infra;

/// <summary>
/// Modelos de plano de contas embutidos: o catálogo (Dados/planos_catalogo.json) e um arquivo de contas por modelo
/// (Dados/plano_*.json), todos no mesmo formato. Estrutura da Lei 6.404/76 (arts. 176 a 187) e do CPC 26: Ativo, Passivo,
/// Patrimônio Líquido, Receitas, Custos e Despesas, mais a conta de apuração do resultado.
/// </summary>
public sealed class PlanoModeloEmbutido : IFontePlanoModelo
{
    private sealed record Item(string Codigo, string Descricao, string CodNat, string Natureza);

    private sealed record EntradaCatalogo(string Id, string Nome, string Descricao, string Base, string[] Regimes, string Arquivo,
                                          string Apuracao, string Lucros, string Prejuizos);

    private static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web);

    private static Stream Recurso(string arquivo) =>
        typeof(PlanoModeloEmbutido).Assembly.GetManifestResourceStream("Dados." + arquivo)
        ?? throw new InvalidOperationException($"Recurso {arquivo} não encontrado.");

    private static IReadOnlyList<ContaModelo> Ler(string arquivo)
    {
        using var s = Recurso(arquivo);
        var itens = JsonSerializer.Deserialize<Item[]>(s, Opcoes)!;
        return itens.Select(i => new ContaModelo(i.Codigo, i.Descricao, i.CodNat, Enum.Parse<IndicadorDC>(i.Natureza))).ToArray();
    }

    private static readonly Lazy<IReadOnlyList<(ModeloPlano Modelo, string Arquivo)>> Catalogo = new(() =>
    {
        using var s = Recurso("planos_catalogo.json");
        return JsonSerializer.Deserialize<EntradaCatalogo[]>(s, Opcoes)!
            .Select(e => (new ModeloPlano(e.Id, e.Nome, e.Descricao, e.Base, e.Regimes.Select(Enum.Parse<RegimeTributario>).ToArray(),
                                          new ContasEncerramento(e.Apuracao, e.Lucros, e.Prejuizos), Ler(e.Arquivo).Count), e.Arquivo))
            .ToArray();
    });

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<ContaModelo>> Contas = new();

    public string ModeloPadrao => "basico";

    public IReadOnlyList<ModeloPlano> Listar() => Catalogo.Value.Select(c => c.Modelo).ToArray();

    public IReadOnlyList<ContaModelo> Obter(string? modeloId = null)
    {
        var arquivo = Catalogo.Value.FirstOrDefault(c => c.Modelo.Id == (modeloId ?? ModeloPadrao)).Arquivo;
        return arquivo is null ? [] : Contas.GetOrAdd(arquivo, Ler);
    }
}
