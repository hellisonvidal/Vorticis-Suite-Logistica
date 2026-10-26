using Vorticis.Fiscal.Documentos;
using Vorticis.Fiscal.Duplicatas;

namespace Vorticis.Erp;

/// <summary>Nota no monitor: situação, contabilização, cobrança e o alerta que pede atenção (vazio = em ordem).</summary>
public sealed record NotaMonitorada(DocumentoFiscal Documento, int Duplicatas, decimal SaldoAberto, decimal SaldoVencido, string Alerta)
{
    public bool TemAlerta => Alerta.Length > 0;
}

public sealed record ResumoMonitorNotas(int Total, int Entradas, int Saidas, int Canceladas, int NaoContabilizadas, int SemCobranca, int ComCobrancaVencida,
                                        decimal ValorEntradas, decimal ValorSaidas);

/// <summary>Cobrança (duplicata em aberto) no monitor, com a faixa de vencimento.</summary>
public sealed record CobrancaMonitorada(Duplicata Duplicata, string Faixa, int Dias)
{
    public bool Vencida => Dias < 0;
}

public sealed record ResumoMonitorCobrancas(decimal Aberto, decimal Vencido, decimal VenceHoje, decimal Proximos7Dias, decimal Depois, int Clientes, int Titulos,
                                            IReadOnlyList<(string Cliente, decimal Vencido)> MaioresAtrasos);

/// <summary>
/// Monitores do ERP: notas fiscais do período (o que está cancelado, sem contabilização, sem cobrança ou com cobrança vencida) e
/// cobranças em aberto (vencidas, vencendo hoje, nos próximos 7 dias e depois). Funções puras sobre os dados do Fiscal.
/// </summary>
public static class Monitores
{
    public static (IReadOnlyList<NotaMonitorada> Notas, ResumoMonitorNotas Resumo) Notas(IReadOnlyList<DocumentoFiscal> documentos,
                                                                                          IReadOnlyList<Duplicata> duplicatas, DateOnly hoje)
    {
        var porDoc = duplicatas.Where(d => d.DocumentoId is not null && d.Situacao != SituacaoDuplicata.Cancelada).ToLookup(d => d.DocumentoId!.Value);
        var notas = documentos.OrderByDescending(d => d.DataLivro).ThenByDescending(d => d.Numero).Select(d =>
        {
            var dups = porDoc[d.Id].ToList();
            var aberto = dups.Where(x => x.EmAberto).Sum(x => x.Saldo);
            var vencido = dups.Where(x => x.Vencida(hoje)).Sum(x => x.Saldo);
            var alerta = d.Situacao != SituacaoDocumento.Autorizada ? $"Nota {d.Situacao.ToString().ToLowerInvariant()}"
                : vencido > 0 ? "Cobrança vencida"
                : !d.Contabilizado ? "Não contabilizada"
                : dups.Count == 0 && d.Totais.Total > 0 ? "Sem duplicatas"
                : "";
            return new NotaMonitorada(d, dups.Count, aberto, vencido, alerta);
        }).ToList();
        var autorizadas = notas.Where(n => n.Documento.Situacao == SituacaoDocumento.Autorizada).ToList();
        var resumo = new ResumoMonitorNotas(notas.Count, notas.Count(n => n.Documento.Operacao == TipoOperacao.Entrada), notas.Count(n => n.Documento.Operacao == TipoOperacao.Saida),
            notas.Count - autorizadas.Count, autorizadas.Count(n => !n.Documento.Contabilizado), autorizadas.Count(n => n.Duplicatas == 0 && n.Documento.Totais.Total > 0),
            autorizadas.Count(n => n.SaldoVencido > 0),
            autorizadas.Where(n => n.Documento.Operacao == TipoOperacao.Entrada).Sum(n => n.Documento.Totais.Total),
            autorizadas.Where(n => n.Documento.Operacao == TipoOperacao.Saida).Sum(n => n.Documento.Totais.Total));
        return (notas, resumo);
    }

    public static string Faixa(int dias) => dias switch
    {
        < -90 => "Vencida há mais de 90 dias",
        < -30 => "Vencida de 31 a 90 dias",
        < 0 => "Vencida até 30 dias",
        0 => "Vence hoje",
        <= 7 => "Vence em até 7 dias",
        _ => "A vencer",
    };

    public static (IReadOnlyList<CobrancaMonitorada> Cobrancas, ResumoMonitorCobrancas Resumo) Cobrancas(IReadOnlyList<Duplicata> duplicatas, DateOnly hoje)
    {
        var abertas = duplicatas.Where(d => d.Tipo == TipoDuplicata.Receber && d.EmAberto)
            .Select(d => { var dias = d.Vencimento.DayNumber - hoje.DayNumber; return new CobrancaMonitorada(d, Faixa(dias), dias); })
            .OrderBy(c => c.Duplicata.Vencimento).ThenBy(c => c.Duplicata.ParticipanteNome).ToList();
        var atrasos = abertas.Where(c => c.Vencida).GroupBy(c => c.Duplicata.ParticipanteNome ?? "(sem cliente)")
            .Select(g => (g.Key, g.Sum(c => c.Duplicata.Saldo))).OrderByDescending(x => x.Item2).Take(5).ToList();
        var resumo = new ResumoMonitorCobrancas(abertas.Sum(c => c.Duplicata.Saldo), abertas.Where(c => c.Dias < 0).Sum(c => c.Duplicata.Saldo),
            abertas.Where(c => c.Dias == 0).Sum(c => c.Duplicata.Saldo), abertas.Where(c => c.Dias is > 0 and <= 7).Sum(c => c.Duplicata.Saldo),
            abertas.Where(c => c.Dias > 7).Sum(c => c.Duplicata.Saldo), abertas.Select(c => c.Duplicata.ParticipanteId).Distinct().Count(), abertas.Count, atrasos);
        return (abertas, resumo);
    }
}
