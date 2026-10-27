namespace Vorticis.Frota;

/// <summary>
/// Financiamento (Price ou SAC) ou consórcio de um bem (normalmente um veículo), com o credor (banco ou administradora), as contas da
/// contabilização e o rateio das despesas por placa. Valores: no financiamento, ValorBem é o preço do bem, Entrada o que foi pago à vista e
/// Encargos o IOF/tarifas somados ao financiado; no consórcio, ValorBem é a carta de crédito.
/// </summary>
public sealed record ContratoFinanceiro(long Id, string Tipo, string Numero, string Descricao, long? CredorId, DateOnly DataContrato, decimal ValorBem,
                                        decimal Entrada, decimal Encargos, decimal TaxaJuros, string Sistema, decimal TaxaAdministracao, decimal FundoReserva,
                                        decimal SeguroMensal, int Parcelas, DateOnly PrimeiroVencimento, DateOnly? DataContemplacao = null, long? BemId = null,
                                        bool Contabiliza = false, string? ContaBem = null, string? ContaPagamento = null, string? ContaCurtoPrazo = null,
                                        string? ContaLongoPrazo = null, string? ContaDespesa = null, string? ContaConsorcio = null, string Situacao = "ATIVO",
                                        string? Observacao = null, string? Credor = null, string? Bem = null)
{
    public bool Consorcio => Tipo == RegrasContratos.Consorcio;
    /// <summary>Valor financiado (principal da dívida): preço do bem − entrada + encargos financiados. No consórcio, a carta de crédito.</summary>
    public decimal ValorFinanciado => Consorcio ? ValorBem : ValorBem - Entrada + Encargos;
    /// <summary>No consórcio, a dívida (fundo comum das parcelas que faltam) só existe depois da contemplação.</summary>
    public bool TemDivida => !Consorcio || DataContemplacao is not null;
}

/// <summary>
/// Parcela: amortização (no consórcio, o fundo comum), juros (no consórcio, a taxa de administração), outros (fundo de reserva) e seguro;
/// Saldo é o saldo devedor depois dela.
/// </summary>
public sealed record ParcelaContrato(long Id, int Numero, DateOnly Vencimento, decimal Amortizacao, decimal Juros, decimal Outros, decimal Seguro, decimal Saldo,
                                     DateOnly? PagoEm = null, decimal? ValorPago = null, bool Contabilizada = false)
{
    public decimal Valor => Amortizacao + Juros + Outros + Seguro;
    public bool Paga => PagoEm is not null;
}

/// <summary>Parte das despesas do contrato que vai a um veículo (centro de custo da frota do veículo).</summary>
public sealed record RateioContrato(long VeiculoId, decimal Percentual, string? Placa = null);

/// <summary>Saldos já contabilizados do contrato: passivo no curto e no longo prazo e o ativo do consórcio (parcelas pagas a contemplar).</summary>
public sealed record SaldosContabeisContrato(decimal CurtoPrazo, decimal LongoPrazo, decimal AtivoConsorcio, bool ContratacaoContabilizada);

/// <summary>Divisão do saldo devedor (principal) entre curto e longo prazo numa data-base.</summary>
public sealed record DivisaoPrazos(decimal CurtoPrazo, decimal LongoPrazo)
{
    public decimal Total => CurtoPrazo + LongoPrazo;
}

/// <summary>Regras dos financiamentos e consórcios (sem banco): parcelas Price, SAC e de consórcio, validação e curto × longo prazo.</summary>
public static class RegrasContratos
{
    public const string Financiamento = "FINANCIAMENTO";
    public const string Consorcio = "CONSORCIO";
    public static readonly IReadOnlyList<string> Sistemas = ["PRICE", "SAC"];
    public static readonly IReadOnlyList<string> Situacoes = ["ATIVO", "QUITADO", "CANCELADO"];

    /// <summary>
    /// Curto prazo (passivo circulante): as parcelas que vencem até 12 meses depois da data-base, como pede a Lei 6.404/76, art. 180
    /// (no balanço de 31/12, as que vencem no exercício seguinte); as demais ficam no longo prazo (não circulante).
    /// </summary>
    public const int MesesCurtoPrazo = 12;

    public static string Rotulo(string codigo) => codigo switch
    {
        Financiamento => "Financiamento", Consorcio => "Consórcio", "PRICE" => "Price (parcelas iguais)", "SAC" => "SAC (amortização constante)",
        "ATIVO" => "Ativo", "QUITADO" => "Quitado", "CANCELADO" => "Cancelado",
        _ => codigo,
    };

    private static decimal R(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    public static IReadOnlyList<string> Validar(ContratoFinanceiro c, IReadOnlyList<RateioContrato> rateio)
    {
        var erros = new List<string>();
        if (c.Tipo is not (Financiamento or Consorcio)) erros.Add("Tipo de contrato inválido.");
        if (string.IsNullOrWhiteSpace(c.Numero)) erros.Add(c.Consorcio ? "Informe o número do grupo/cota." : "Informe o número do contrato.");
        if (string.IsNullOrWhiteSpace(c.Descricao)) erros.Add("Informe a descrição (ex.: o bem financiado).");
        if (c.ValorBem <= 0) erros.Add(c.Consorcio ? "Informe o valor da carta de crédito." : "Informe o valor do bem.");
        if (c.Parcelas is < 1 or > 600) erros.Add("Número de parcelas: de 1 a 600.");
        if (c.PrimeiroVencimento < c.DataContrato) erros.Add("O primeiro vencimento não pode ser anterior à data do contrato.");
        if (!c.Consorcio)
        {
            if (c.Entrada < 0 || c.Entrada >= c.ValorBem) erros.Add("A entrada deve ser menor que o valor do bem.");
            if (c.Encargos < 0) erros.Add("Os encargos não podem ser negativos.");
            if (c.TaxaJuros is < 0 or > 100) erros.Add("Taxa de juros: de 0 a 100% ao mês.");
            if (!Sistemas.Contains(c.Sistema)) erros.Add("Escolha o sistema de amortização (Price ou SAC).");
        }
        else
        {
            if (c.TaxaAdministracao is < 0 or > 100 || c.FundoReserva is < 0 or > 100) erros.Add("Taxa de administração e fundo de reserva: de 0 a 100%.");
            if (c.SeguroMensal < 0) erros.Add("O seguro não pode ser negativo.");
            if (c.DataContemplacao is { } d && d < c.DataContrato) erros.Add("A contemplação não pode ser anterior à adesão.");
        }
        if (!Situacoes.Contains(c.Situacao)) erros.Add("Situação inválida.");
        if (rateio.Count > 0 && rateio.Sum(r => r.Percentual) != 100) erros.Add("O rateio por placa deve somar 100%.");
        if (rateio.Any(r => r.Percentual <= 0)) erros.Add("Cada placa do rateio precisa de um percentual maior que zero.");
        if (rateio.GroupBy(r => r.VeiculoId).Any(g => g.Count() > 1)) erros.Add("A mesma placa aparece duas vezes no rateio.");
        if (c.Contabiliza)
        {
            bool Falta(string? conta) => string.IsNullOrWhiteSpace(conta);
            if (Falta(c.ContaPagamento) || Falta(c.ContaCurtoPrazo) || Falta(c.ContaLongoPrazo) || Falta(c.ContaDespesa))
                erros.Add("Para contabilizar, informe as contas de pagamento (banco/caixa), curto prazo, longo prazo e despesa.");
            if (Falta(c.ContaBem) && c.BemId is null) erros.Add("Para contabilizar, informe a conta do bem (imobilizado) ou ligue o contrato a um bem do imobilizado.");
            if (c.Consorcio && Falta(c.ContaConsorcio)) erros.Add("Para contabilizar o consórcio, informe a conta do consórcio (ativo).");
        }
        return erros;
    }

    /// <summary>
    /// As parcelas do contrato. Financiamento Price: parcelas iguais (juros sobre o saldo); SAC: amortização constante e juros sobre o saldo;
    /// sem juros, só a amortização. Consórcio: fundo comum (carta ÷ parcelas), taxa de administração e fundo de reserva (percentuais sobre a
    /// carta, divididos pelas parcelas) e o seguro mensal. Vencimentos mensais a partir do primeiro; a última parcela acerta os centavos.
    /// </summary>
    public static IReadOnlyList<ParcelaContrato> Calcular(ContratoFinanceiro c)
    {
        var n = c.Parcelas;
        var lista = new List<ParcelaContrato>(n);
        var saldo = c.ValorFinanciado;
        DateOnly Venc(int k) => c.PrimeiroVencimento.AddMonths(k);
        if (c.Consorcio)
        {
            var fundo = R(c.ValorBem / n);
            var adm = R(c.ValorBem * c.TaxaAdministracao / 100 / n);
            var reserva = R(c.ValorBem * c.FundoReserva / 100 / n);
            var admTotal = R(c.ValorBem * c.TaxaAdministracao / 100);
            var reservaTotal = R(c.ValorBem * c.FundoReserva / 100);
            for (var k = 0; k < n; k++)
            {
                var ultima = k == n - 1;
                var f = ultima ? saldo : fundo;
                saldo -= f;
                lista.Add(new ParcelaContrato(0, k + 1, Venc(k), f, ultima ? admTotal - adm * (n - 1) : adm, ultima ? reservaTotal - reserva * (n - 1) : reserva,
                                              c.SeguroMensal, saldo));
            }
            return lista;
        }
        var i = c.TaxaJuros / 100;
        var pmt = i == 0 ? R(saldo / n) : R(saldo * i / (1 - (decimal)Math.Pow((double)(1 + i), -n)));
        var amortSac = R(saldo / n);
        for (var k = 0; k < n; k++)
        {
            var juros = R(saldo * i);
            var amort = k == n - 1 ? saldo : c.Sistema == "SAC" || i == 0 ? amortSac : Math.Min(saldo, pmt - juros);
            saldo -= amort;
            lista.Add(new ParcelaContrato(0, k + 1, Venc(k), amort, juros, 0, 0, saldo));
        }
        return lista;
    }

    /// <summary>
    /// Saldo devedor (principal das parcelas em aberto) dividido em curto e longo prazo na data-base: curto prazo as que vencem até
    /// <see cref="MesesCurtoPrazo"/> meses depois (inclusive as atrasadas). No consórcio, só existe dívida depois da contemplação.
    /// </summary>
    public static DivisaoPrazos Dividir(ContratoFinanceiro c, IEnumerable<ParcelaContrato> parcelas, DateOnly dataBase)
    {
        if (!c.TemDivida) return new DivisaoPrazos(0, 0);
        var abertas = parcelas.Where(p => !p.Paga).ToList();
        var limite = dataBase.AddMonths(MesesCurtoPrazo);
        return new DivisaoPrazos(abertas.Where(p => p.Vencimento <= limite).Sum(p => p.Amortizacao), abertas.Where(p => p.Vencimento > limite).Sum(p => p.Amortizacao));
    }

    /// <summary>Divide um valor pelos percentuais do rateio; o último veículo fica com a diferença dos centavos. Sem rateio, um item só (sem veículo).</summary>
    public static IReadOnlyList<(long? VeiculoId, decimal Valor)> Ratear(decimal valor, IReadOnlyList<RateioContrato> rateio)
    {
        if (rateio.Count == 0 || valor == 0) return [(null, valor)];
        var partes = rateio.Select(r => ((long?)r.VeiculoId, R(valor * r.Percentual / 100))).ToList();
        var diferenca = valor - partes.Sum(p => p.Item2);
        partes[^1] = (partes[^1].Item1, partes[^1].Item2 + diferenca);
        return [.. partes.Where(p => p.Item2 != 0)];
    }
}
