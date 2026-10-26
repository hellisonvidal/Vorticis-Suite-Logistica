using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;

namespace Vorticis.Application.Relatorios;

/// <summary>
/// DMPL e DLPA como tabelas de relatório. A tela, a exportação (PDF, Excel, CSV) e o Livro Contábil Completo
/// usam as mesmas tabelas, então o que vai ao livro é exatamente o que o usuário vê.
/// </summary>
public static class TabelasMutacoes
{
    public const string TituloDmpl = "DMPL — Demonstração das Mutações do Patrimônio Líquido";
    public const string TituloDlpa = "DLPA — Demonstração de Lucros ou Prejuízos Acumulados";

    /// <summary>DMPL (CPC 26): uma coluna por componente do PL e o total.</summary>
    public static TabelaRelatorio Dmpl(DemonstracaoMutacoes d, string? rodape = null)
    {
        var colunas = new List<ColunaRelatorio> { new("Descrição", LarguraCm: 7) };
        colunas.AddRange(d.Colunas.Select(c => new ColunaRelatorio(DemonstracaoMutacoes.NomeColuna(c), TipoColuna.Valor)));
        colunas.Add(new("Total", TipoColuna.Valor));
        var linhas = d.Dmpl.Select(l => new LinhaRelatorio(
            new object?[] { l.Descricao }.Concat(d.Colunas.Select(c => (object?)l.Valor(c))).Append(l.Total).ToList(), Destaque: l.Saldo)).ToList();
        return new TabelaRelatorio(TituloDmpl, colunas, linhas, rodape);
    }

    /// <summary>DLPA (Lei 6.404/76, art. 186): saldo inicial, ajustes, reversões, lucro líquido, destinações e saldo final.</summary>
    public static TabelaRelatorio Dlpa(DemonstracaoMutacoes d) =>
        new(TituloDlpa, [new("Descrição"), new("R$", TipoColuna.Valor)],
            d.Dlpa.Select(l => new LinhaRelatorio([l.Descricao, l.SemValor ? null : l.Valor], Destaque: l.Destaque)).ToList());
}
