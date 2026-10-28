namespace Vorticis.Frota;

/// <summary>
/// Operação de transporte com CIOT: a transportadora subcontrata um TAC (transportador autônomo, CPF) ou uma ETC equiparada (CNPJ) e registra a
/// operação numa IPEF (Repom, Pamcard, e-Frete...), que devolve o CIOT. Situação: DIGITADO (sem CIOT) → DECLARADO (com o CIOT) → ENCERRADO; ou CANCELADO.
/// </summary>
public sealed record OperacaoCiot(long Id, int Numero, string? Ciot, string? CodigoVerificador, string? Ipef, string Tipo, long ContratadoId, string Rntrc,
                                  string Placa, string? MotoristaNome, string? MotoristaCpf, long? ViagemId, ParadaRota Origem, ParadaRota Destino,
                                  DateOnly Inicio, DateOnly Fim, string? Produto, string? Ncm, decimal PesoKg, decimal ValorFrete, decimal ValePedagio,
                                  decimal Adiantamento, int Dependentes, RetencoesTac Retencoes, string Situacao, string? Observacao,
                                  IReadOnlyList<long> Ctes, string? Contratado = null, string? ContratadoDocumento = null)
{
    public decimal ValorLiquido => ValorFrete - Retencoes.Total;
    /// <summary>Saldo a pagar ao contratado no fim da viagem: o líquido menos o adiantamento.</summary>
    public decimal Saldo => ValorLiquido - Adiantamento;
    public string Trecho => $"{Origem.Municipio}/{Origem.Uf} → {Destino.Municipio}/{Destino.Uf}";
}

/// <summary>Retenções do frete pago ao TAC: INSS (11% sobre 20% do frete, até o teto), SEST/SENAT (2,5% sobre a mesma base) e IRRF.</summary>
public sealed record RetencoesTac(decimal Inss, decimal SestSenat, decimal Irrf)
{
    public static readonly RetencoesTac Nenhuma = new(0, 0, 0);
    public decimal Total => Inss + SestSenat + Irrf;
}

/// <summary>Faixa da tabela progressiva do IR: até o valor (null = acima), alíquota e parcela a deduzir.</summary>
public sealed record FaixaIr(decimal? Ate, decimal Aliquota, decimal Deduzir);

/// <summary>Redução do IR da Lei 15.270/2025: até o limite de isenção o imposto zera (até a redução máxima); até o limite superior, constante − coeficiente × rendimento.</summary>
public sealed record ReducaoIr(decimal LimiteIsencao, decimal ReducaoMaxima, decimal LimiteSuperior, decimal Constante, decimal Coeficiente);

/// <summary>Parâmetros legais das retenções do TAC (core.parametro_legal: TAC_RETENCOES, FOLHA_INSS_EMPREGADO e FOLHA_IRRF_MENSAL).</summary>
public sealed record ParametrosTac(decimal BaseInss, decimal AliquotaInss, decimal SestSenat, decimal BaseIrrf, decimal TetoInss,
                                  IReadOnlyList<FaixaIr> FaixasIr, decimal DeducaoDependente, ReducaoIr? Reducao);

/// <summary>Regras do CIOT (sem banco): validação, CIOT, RNTRC e as retenções do TAC.</summary>
public static class RegrasCiot
{
    public static readonly IReadOnlyList<string> Tipos = ["PADRAO", "TAC_AGREGADO"];
    public static readonly IReadOnlyList<string> Situacoes = ["DIGITADO", "DECLARADO", "ENCERRADO", "CANCELADO"];

    public static string Rotulo(string codigo) => codigo switch
    {
        "PADRAO" => "Padrão (uma viagem)", "TAC_AGREGADO" => "TAC agregado (período)",
        "DIGITADO" => "Digitado", "DECLARADO" => "Declarado (com CIOT)", "ENCERRADO" => "Encerrado", "CANCELADO" => "Cancelado",
        _ => codigo,
    };

    private static decimal R(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
    private static string Digitos(string? s) => new([.. (s ?? "").Where(char.IsAsciiDigit)]);

    /// <summary>TAC é pessoa física (CPF, 11 dígitos): só ele tem retenções; a ETC equiparada (CNPJ) recebe o frete cheio.</summary>
    public static bool EhTac(string? documento) => Digitos(documento).Length == 11;

    public static IReadOnlyList<string> Validar(OperacaoCiot o)
    {
        var erros = new List<string>();
        if (o.ContratadoId <= 0) erros.Add("Escolha o contratado (o transportador autônomo ou a empresa equiparada).");
        if (Digitos(o.Rntrc).Length is < 8 or > 9) erros.Add("Informe o RNTRC do contratado (8 ou 9 dígitos).");
        if (!RegrasFrota.PlacaValida(o.Placa)) erros.Add("Placa do veículo inválida.");
        if (o.MotoristaCpf is { Length: > 0 } cpf && !Vorticis.Domain.Cadastros.Cpf.Valido(cpf)) erros.Add("CPF do motorista inválido.");
        if (string.IsNullOrWhiteSpace(o.Origem.MunicipioIbge) || string.IsNullOrWhiteSpace(o.Destino.MunicipioIbge)) erros.Add("Escolha o município de origem e o de destino.");
        if (o.Fim < o.Inicio) erros.Add("O fim da operação não pode ser antes do início.");
        if (o.ValorFrete <= 0) erros.Add("Informe o valor do frete contratado.");
        if (o.ValePedagio < 0 || o.Adiantamento < 0 || o.PesoKg < 0) erros.Add("Valores não podem ser negativos.");
        if (o.Adiantamento > o.ValorLiquido) erros.Add("O adiantamento não pode passar do valor líquido do frete.");
        if (o.Ciot is { Length: > 0 } c && Digitos(c).Length != 12) erros.Add("O CIOT tem 12 dígitos.");
        if (o.Situacao is "DECLARADO" or "ENCERRADO" && string.IsNullOrWhiteSpace(o.Ciot)) erros.Add("Informe o CIOT gerado pela IPEF.");
        if (o.Ncm is { Length: > 0 } ncm && Digitos(ncm).Length != 8) erros.Add("O NCM tem 8 dígitos.");
        if (!Tipos.Contains(o.Tipo)) erros.Add("Escolha o tipo da operação.");
        return erros;
    }

    /// <summary>Retenções sobre um valor de frete (do mês), sem considerar o que já foi retido.</summary>
    public static RetencoesTac Calcular(decimal frete, int dependentes, ParametrosTac p)
    {
        if (frete <= 0) return RetencoesTac.Nenhuma;
        var baseInss = frete * p.BaseInss / 100;
        var inss = R(Math.Min(baseInss, p.TetoInss) * p.AliquotaInss / 100);
        var sest = R(baseInss * p.SestSenat / 100);
        var rendimento = frete * p.BaseIrrf / 100;
        var baseIr = Math.Max(0, rendimento - inss - dependentes * p.DeducaoDependente);
        var faixa = p.FaixasIr.FirstOrDefault(f => f.Ate is null || baseIr <= f.Ate) ?? p.FaixasIr[^1];
        var imposto = Math.Max(0, baseIr * faixa.Aliquota / 100 - faixa.Deduzir);
        if (p.Reducao is { } red && imposto > 0)
        {
            var reducao = rendimento <= red.LimiteIsencao ? Math.Min(imposto, red.ReducaoMaxima)
                        : rendimento <= red.LimiteSuperior ? Math.Max(0, red.Constante - red.Coeficiente * rendimento) : 0;
            imposto = Math.Max(0, imposto - reducao);
        }
        return new RetencoesTac(inss, sest, R(imposto));
    }

    /// <summary>
    /// Retenções deste frete considerando os outros fretes do mesmo TAC no mês (o teto do INSS e a tabela do IR são mensais):
    /// o calculado sobre o total do mês menos o calculado sobre os anteriores. ETC (CNPJ): nenhuma.
    /// </summary>
    public static RetencoesTac Retencoes(string? documentoContratado, decimal frete, decimal fretesAnterioresNoMes, int dependentes, ParametrosTac p)
    {
        if (!EhTac(documentoContratado)) return RetencoesTac.Nenhuma;
        var total = Calcular(fretesAnterioresNoMes + frete, dependentes, p);
        var antes = Calcular(fretesAnterioresNoMes, dependentes, p);
        return new RetencoesTac(Math.Max(0, total.Inss - antes.Inss), total.SestSenat - antes.SestSenat, Math.Max(0, total.Irrf - antes.Irrf));
    }
}
