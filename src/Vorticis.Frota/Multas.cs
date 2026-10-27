namespace Vorticis.Frota;

/// <summary>
/// Multa de trânsito recebida (notificação de autuação ou de penalidade): veículo, condutor, auto de infração, enquadramento, gravidade,
/// pontos, valor, prazos e quem paga (a empresa ou o motorista, com desconto no acerto).
/// </summary>
public sealed record MultaTransito(long Id, long VeiculoId, long? MotoristaId, string AutoInfracao, string? Orgao, DateOnly DataInfracao, string? Local,
                                   string? Enquadramento, string? Descricao, string Gravidade, int Pontos, decimal Valor, decimal Desconto, DateOnly? Vencimento,
                                   DateOnly? PrazoIndicacao, DateOnly? PrazoRecurso, string Responsavel = "EMPRESA", string Situacao = "NOTIFICADA",
                                   DateOnly? DataPagamento = null, decimal? ValorPago = null, string? Observacao = null, string? Placa = null, string? MotoristaNome = null)
{
    /// <summary>Valor a pagar com o desconto (20% até o vencimento, 40% com o SNE).</summary>
    public decimal ValorComDesconto => Math.Round(Valor * (100 - Desconto) / 100, 2, MidpointRounding.AwayFromZero);
    public bool EmAberto => Situacao is not ("PAGA" or "CANCELADA");
}

/// <summary>Pontos e valor de uma gravidade e os descontos do CTB (core.parametro_legal, chave CTB_INFRACOES).</summary>
public sealed record TabelaInfracoes(IReadOnlyDictionary<string, (int Pontos, decimal Valor)> PorGravidade, decimal DescontoVencimento, decimal DescontoSne);

/// <summary>Prazo da multa que vence primeiro e ainda está pendente (indicação do condutor, recurso ou pagamento).</summary>
public sealed record PrazoMulta(string Assunto, DateOnly Data, GravidadeAviso? Gravidade);

/// <summary>Pontos dos motoristas nos últimos 12 meses (pela data da infração; multas canceladas não contam).</summary>
public sealed record PontosMotorista(long MotoristaId, string Nome, int Pontos, int Multas);

/// <summary>Regras das multas (sem banco): validação, prazo pendente e pontuação dos condutores.</summary>
public static class RegrasMultas
{
    public static readonly IReadOnlyList<string> Gravidades = ["LEVE", "MEDIA", "GRAVE", "GRAVISSIMA"];
    public static readonly IReadOnlyList<string> Situacoes = ["NOTIFICADA", "CONDUTOR_INDICADO", "EM_RECURSO", "PAGA", "CANCELADA"];
    public static readonly IReadOnlyList<string> Responsaveis = ["EMPRESA", "MOTORISTA"];

    public static string Rotulo(string codigo) => codigo switch
    {
        "LEVE" => "Leve", "MEDIA" => "Média", "GRAVE" => "Grave", "GRAVISSIMA" => "Gravíssima",
        "NOTIFICADA" => "Notificada", "CONDUTOR_INDICADO" => "Condutor indicado", "EM_RECURSO" => "Em recurso", "PAGA" => "Paga", "CANCELADA" => "Cancelada",
        "EMPRESA" => "Empresa", "MOTORISTA" => "Motorista (desconto)",
        _ => codigo,
    };

    public static IReadOnlyList<string> Validar(MultaTransito m)
    {
        var erros = new List<string>();
        if (m.VeiculoId <= 0) erros.Add("Escolha o veículo.");
        if (string.IsNullOrWhiteSpace(m.AutoInfracao)) erros.Add("Informe o número do auto de infração.");
        if (!Gravidades.Contains(m.Gravidade)) erros.Add("Escolha a gravidade da infração.");
        if (!Situacoes.Contains(m.Situacao)) erros.Add("Escolha a situação da multa.");
        if (!Responsaveis.Contains(m.Responsavel)) erros.Add("Escolha quem paga a multa.");
        if (m.Valor < 0) erros.Add("O valor da multa não pode ser negativo.");
        if (m.Pontos is < 0 or > 99) erros.Add("Pontos: de 0 a 99.");
        if (m.Desconto is < 0 or > 100) erros.Add("Desconto: de 0 a 100%.");
        if (m.Situacao == "PAGA" && m.DataPagamento is null) erros.Add("Multa paga: informe a data do pagamento.");
        if (m.Situacao == "CONDUTOR_INDICADO" && m.MotoristaId is null) erros.Add("Condutor indicado: escolha o motorista.");
        if (m.Responsavel == "MOTORISTA" && m.MotoristaId is null) erros.Add("Multa descontada do motorista: escolha o motorista.");
        if (m.DataPagamento is { } p && p < m.DataInfracao) erros.Add("A data do pagamento não pode ser anterior à infração.");
        return erros;
    }

    /// <summary>
    /// O prazo pendente mais próximo de uma multa em aberto: a indicação do condutor (enquanto não indicado), o recurso (enquanto não recorrida
    /// nem paga) e o vencimento do boleto. Vencido, a vencer (dentro da antecedência dos avisos) ou em dia (gravidade nula).
    /// </summary>
    public static PrazoMulta? ProximoPrazo(MultaTransito m, DateOnly hoje, int diasAviso)
    {
        if (!m.EmAberto) return null;
        var prazos = new List<(string, DateOnly)>();
        if (m.PrazoIndicacao is { } i && m.Situacao == "NOTIFICADA") prazos.Add(("Indicação do condutor", i));
        if (m.PrazoRecurso is { } r && m.Situacao is "NOTIFICADA" or "CONDUTOR_INDICADO") prazos.Add(("Defesa/recurso", r));
        if (m.Vencimento is { } v) prazos.Add(("Pagamento", v));
        if (prazos.Count == 0) return null;
        // primeiro um prazo ainda não vencido (o que importa agora); se todos venceram, o último que venceu
        var (assunto, data) = prazos.Where(p => p.Item2 >= hoje).OrderBy(p => p.Item2).Cast<(string, DateOnly)?>().FirstOrDefault()
                              ?? prazos.OrderByDescending(p => p.Item2).First();
        return new PrazoMulta(assunto, data, RegrasFrota.SituacaoVencimento(data, hoje, diasAviso));
    }

    /// <summary>Pontos por motorista nas infrações dos 12 meses até hoje (multas canceladas não contam), do maior para o menor.</summary>
    public static IReadOnlyList<PontosMotorista> PontosDosMotoristas(IEnumerable<MultaTransito> multas, DateOnly hoje) =>
    [
        .. multas.Where(m => m.MotoristaId is not null && m.Situacao != "CANCELADA" && m.DataInfracao > hoje.AddMonths(-12) && m.DataInfracao <= hoje)
                 .GroupBy(m => m.MotoristaId!.Value)
                 .Select(g => new PontosMotorista(g.Key, g.First().MotoristaNome ?? "", g.Sum(m => m.Pontos), g.Count()))
                 .OrderByDescending(p => p.Pontos).ThenBy(p => p.Nome),
    ];
}
