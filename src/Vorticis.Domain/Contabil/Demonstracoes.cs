namespace Vorticis.Domain.Contabil;

public enum TipoLinhaDemonstracao { Grupo, Conta, Total }

/// <summary>Linha de demonstração. O valor é apresentado com sinal "natural" do grupo (ativo e receita positivos).</summary>
public sealed record LinhaDemonstracao(string Codigo, string Descricao, short Nivel, decimal Valor, TipoLinhaDemonstracao Tipo);

public sealed record BalancoPatrimonial(DateOnly Data, IReadOnlyList<LinhaDemonstracao> Ativo, IReadOnlyList<LinhaDemonstracao> PassivoPl,
                                        decimal TotalAtivo, decimal TotalPassivoPl, decimal ResultadoNaoEncerrado)
{
    public bool Fecha => TotalAtivo == TotalPassivoPl;
}

public sealed record DemonstracaoResultado(DateOnly Inicio, DateOnly Fim, IReadOnlyList<LinhaDemonstracao> Linhas, decimal Resultado);

/// <summary>
/// Monta Balanço Patrimonial e DRE a partir do plano de contas e do balancete (ITG 2000 / NBC TG 26), seguindo a
/// estrutura do plano da empresa até o nível pedido. COD_NAT: 01 Ativo; 02 Passivo; 03 PL; 04 Resultado.
/// O enquadramento nas linhas da Lei 6.404 (receita líquida, lucro bruto...) virá do mapeamento ao plano referencial.
/// </summary>
public static class Demonstracoes
{
    public static BalancoPatrimonial MontarBalanco(DateOnly data, IReadOnlyList<ContaPlano> plano, IReadOnlyList<LinhaBalancete> balancete, short nivelMaximo)
    {
        var saldos = balancete.ToDictionary(l => l.ContaId, l => l.SaldoFinal);
        decimal Saldo(ContaPlano c) => saldos.GetValueOrDefault(c.Id);

        // Resultado ainda não transferido ao PL (exercício não encerrado): soma das contas de resultado analíticas.
        // Saldo credor (negativo na convenção débito+) = lucro.
        var resultado = -plano.Where(c => c.Analitica && c.CodNat == "04").Sum(Saldo);

        List<LinhaDemonstracao> Linhas(Func<ContaPlano, bool> filtro, int sinal) => plano
            .Where(c => filtro(c) && c.Nivel <= nivelMaximo && Saldo(c) != 0)
            .OrderBy(c => c.Codigo, StringComparer.Ordinal)
            .Select(c => new LinhaDemonstracao(c.Codigo, c.Descricao, c.Nivel, sinal * Saldo(c),
                                                c.Analitica ? TipoLinhaDemonstracao.Conta : TipoLinhaDemonstracao.Grupo))
            .ToList();

        var ativo = Linhas(c => c.CodNat == "01", +1);
        var passivoPl = Linhas(c => c.CodNat is "02" or "03", -1);
        var totalAtivo = plano.Where(c => c.Analitica && c.CodNat == "01").Sum(Saldo);
        var totalPassivoPl = -plano.Where(c => c.Analitica && c.CodNat is "02" or "03").Sum(Saldo) + resultado;

        if (resultado != 0)
            passivoPl.Add(new LinhaDemonstracao("", resultado > 0 ? "Lucro do período (antes do encerramento)" : "Prejuízo do período (antes do encerramento)",
                                                2, resultado, TipoLinhaDemonstracao.Conta));
        ativo.Add(new LinhaDemonstracao("", "TOTAL DO ATIVO", 1, totalAtivo, TipoLinhaDemonstracao.Total));
        passivoPl.Add(new LinhaDemonstracao("", "TOTAL DO PASSIVO E PATRIMÔNIO LÍQUIDO", 1, totalPassivoPl, TipoLinhaDemonstracao.Total));
        return new BalancoPatrimonial(data, ativo, passivoPl, totalAtivo, totalPassivoPl, resultado);
    }

    /// <param name="movimentos">Movimento do período SEM os lançamentos de encerramento (senão as contas de resultado zeram).</param>
    public static DemonstracaoResultado MontarDre(DateOnly inicio, DateOnly fim, IReadOnlyList<ContaPlano> plano,
                                                  IReadOnlyList<LinhaBalancete> movimentos, short nivelMaximo)
    {
        var porConta = movimentos.ToDictionary(l => l.ContaId);
        decimal Valor(ContaPlano c) => porConta.TryGetValue(c.Id, out var l) ? l.Creditos - l.Debitos : 0;   // receita positiva

        var linhas = plano
            .Where(c => c.CodNat == "04" && c.Nivel <= nivelMaximo && Valor(c) != 0)
            .OrderBy(c => c.Codigo, StringComparer.Ordinal)
            .Select(c => new LinhaDemonstracao(c.Codigo, c.Descricao, c.Nivel, Valor(c),
                                                c.Analitica ? TipoLinhaDemonstracao.Conta : TipoLinhaDemonstracao.Grupo))
            .ToList();
        var resultado = plano.Where(c => c.Analitica && c.CodNat == "04").Sum(Valor);
        linhas.Add(new LinhaDemonstracao("", resultado >= 0 ? "LUCRO LÍQUIDO DO PERÍODO" : "PREJUÍZO LÍQUIDO DO PERÍODO", 1, resultado,
                                         TipoLinhaDemonstracao.Total));
        return new DemonstracaoResultado(inicio, fim, linhas, resultado);
    }
}
