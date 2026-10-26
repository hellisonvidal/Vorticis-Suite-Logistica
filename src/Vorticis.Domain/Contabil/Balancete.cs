using Vorticis.Domain.Comum;

namespace Vorticis.Domain.Contabil;

public sealed record ContaPlano(long Id, string Codigo, string Descricao, short Nivel, bool Analitica, long? ContaPaiId, IndicadorDC NaturezaSaldo,
                               string? CodNat = null);

/// <summary>Movimento de uma conta analítica no intervalo. Saldos com sinal: débito positivo.</summary>
public sealed record MovimentoConta(long ContaId, decimal SaldoAnterior, decimal Debitos, decimal Creditos);

/// <summary>Saldo e movimento de um participante (cliente/fornecedor) dentro de uma conta analítica — detalhamento do balancete.</summary>
public sealed record MovimentoParticipante(long ContaId, string CodigoParticipante, string NomeParticipante, decimal SaldoAnterior,
                                           decimal Debitos, decimal Creditos)
{
    public decimal SaldoFinal => SaldoAnterior + Debitos - Creditos;
}

public sealed record LinhaBalancete(
    long ContaId,
    string Codigo,
    string Descricao,
    short Nivel,
    bool Analitica,
    decimal SaldoAnterior,
    decimal Debitos,
    decimal Creditos,
    decimal SaldoFinal,
    bool SaldoInvertido)
{
    public static string Formatar(decimal saldoComSinal) =>
        saldoComSinal == 0 ? "0,00" : $"{Formato.Moeda(Math.Abs(saldoComSinal))} {(saldoComSinal > 0 ? 'D' : 'C')}";
}

public static class Balancete
{
    /// <summary>
    /// Totaliza os movimentos analíticos nas contas sintéticas ancestrais e devolve o balancete em
    /// ordem de código. Contas sem saldo nem movimento são omitidas, salvo <paramref name="incluirZeradas"/>.
    /// </summary>
    public static IReadOnlyList<LinhaBalancete> Montar(
        IReadOnlyList<ContaPlano> plano, IEnumerable<MovimentoConta> movimentos, bool incluirZeradas = false)
    {
        var porId = plano.ToDictionary(c => c.Id);
        var acum = plano.ToDictionary(c => c.Id, _ => (ant: 0m, deb: 0m, cred: 0m));

        foreach (var m in movimentos)
        {
            long? id = m.ContaId;
            int guarda = 0;
            while (id is long atual && porId.TryGetValue(atual, out var conta))
            {
                var a = acum[atual];
                acum[atual] = (a.ant + m.SaldoAnterior, a.deb + m.Debitos, a.cred + m.Creditos);
                id = conta.ContaPaiId;
                if (++guarda > 64) throw new InvalidOperationException("Ciclo detectado na hierarquia do plano de contas.");
            }
        }

        return plano
            .OrderBy(c => c.Codigo, StringComparer.Ordinal)
            .Select(c =>
            {
                var (ant, deb, cred) = acum[c.Id];
                var final = ant + deb - cred;
                var invertido = final != 0 && (final > 0) != (c.NaturezaSaldo == IndicadorDC.D);
                return new LinhaBalancete(c.Id, c.Codigo, c.Descricao, c.Nivel, c.Analitica, ant, deb, cred, final, invertido);
            })
            .Where(l => incluirZeradas || l.SaldoAnterior != 0 || l.Debitos != 0 || l.Creditos != 0)
            .ToArray();
    }
}
