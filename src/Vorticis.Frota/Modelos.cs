using System.Text.RegularExpressions;

namespace Vorticis.Frota;

/// <summary>Configuração da frota; LimiteAutorizacao: ordens de serviço até este total são autorizadas automaticamente (0 = todas passam por autorização).</summary>
public sealed record ConfiguracaoFrota(int DiasAviso = 30, decimal ToleranciaConsumo = 15m, decimal LimiteAutorizacao = 0m,
                                       decimal FatorRodoviario = 1.25m, decimal VelocidadeMedia = 60m,
                                       string? ContaDiariaDespesa = null, string? ContaDiariaCredito = null)
{
    /// <summary>As duas contas da contabilização das diárias estão informadas.</summary>
    public bool ContabilizaDiarias => !string.IsNullOrWhiteSpace(ContaDiariaDespesa) && !string.IsNullOrWhiteSpace(ContaDiariaCredito);
}

/// <summary>Veículo da frota (inclusive reboques e semirreboques, que não abastecem).</summary>
public sealed record Veiculo(long Id, int Codigo, string Placa, string? Renavam, string Tipo, string? Marca, string? Modelo, int? AnoFabricacao, int? AnoModelo,
                             string Combustivel, decimal? CapacidadeKg, decimal KmAtual, decimal? MediaEsperada, bool Ativo = true, string? Observacao = null, long? FrotaId = null)
{
    public string Descricao => $"{RegrasFrota.FormatarPlaca(Placa)} – {string.Join(" ", new[] { Marca, Modelo }.Where(x => !string.IsNullOrWhiteSpace(x)))}".TrimEnd(' ', '–');
    public bool Abastece => Combustivel != "NENHUM";
}

public sealed record Motorista(long Id, int Codigo, string Nome, string Cpf, string? Cnh, string? CnhCategoria, DateOnly? CnhValidade, string? Telefone, bool Ativo = true);

public sealed record Abastecimento(long Id, long VeiculoId, long? MotoristaId, DateOnly Data, decimal Km, decimal Litros, decimal ValorTotal, string Combustivel,
                                   string? Posto, bool TanqueCheio = true, string? Observacao = null, string? Placa = null, string? MotoristaNome = null)
{
    public decimal PrecoLitro => Litros == 0 ? 0 : Math.Round(ValorTotal / Litros, 3);
}

public sealed record PlanoManutencao(long Id, long VeiculoId, string Descricao, decimal? IntervaloKm, int? IntervaloDias, decimal? UltimaKm, DateOnly? UltimaData,
                                     bool Ativo = true, string? Placa = null, decimal KmAtual = 0);

public sealed record OrdemServico(long Id, int Numero, long VeiculoId, long? PlanoId, string Tipo, DateOnly Data, decimal? Km, string Descricao, string? Oficina,
                                  decimal ValorPecas, decimal ValorMaoObra, string Situacao = "ABERTA", string? Placa = null, string? Solicitante = null,
                                  DateOnly? Previsao = null, DateOnly? Conclusao = null)
{
    public decimal Total => ValorPecas + ValorMaoObra;
}

public sealed record Viagem(long Id, int Numero, long VeiculoId, long? MotoristaId, string? Cliente, string Origem, string Destino, DateOnly Saida, DateOnly? Chegada,
                            decimal KmSaida, decimal? KmChegada, decimal ValorFrete, decimal Pedagio, decimal Diarias, decimal OutrasDespesas,
                            string Situacao = "EM_ANDAMENTO", string? Placa = null, string? MotoristaNome = null)
{
    public decimal? KmRodado => KmChegada is { } c ? c - KmSaida : null;
    public decimal Despesas => Pedagio + Diarias + OutrasDespesas;
}

public sealed record DocumentoFrota(long Id, long? VeiculoId, long? MotoristaId, string Tipo, string? Numero, DateOnly Vencimento, decimal? Valor, string? Observacao,
                                    string? Dono = null);

/// <summary>Aviso do painel da frota: vencimentos, manutenções devidas e consumo fora do esperado.</summary>
public enum GravidadeAviso { Vencido, AVencer, Atencao }

public sealed record AvisoFrota(GravidadeAviso Gravidade, string Assunto, string Descricao, DateOnly? Data = null);

/// <summary>Situação de um plano de manutenção preventiva frente ao km atual do veículo e à data de hoje.</summary>
public sealed record SituacaoPreventiva(PlanoManutencao Plano, decimal? ProximaKm, DateOnly? ProximaData, GravidadeAviso? Gravidade)
{
    public string Texto => Gravidade switch { GravidadeAviso.Vencido => "VENCIDA", GravidadeAviso.AVencer => "A VENCER", _ => "Em dia" };
}

/// <summary>Média de consumo calculada entre dois abastecimentos de tanque cheio.</summary>
public sealed record MediaConsumo(long VeiculoId, DateOnly Data, decimal KmRodado, decimal Litros, decimal Media, long? MotoristaId = null);

/// <summary>Filtros opcionais dos relatórios novos da Frota (vazio = todos).</summary>
public sealed record FiltroRelatorioFrota(long? VeiculoId = null, long? MotoristaId = null);

/// <summary>Regras puras da frota (sem banco): placa, RENAVAM, consumo, preventivas e vencimentos.</summary>
public static partial class RegrasFrota
{
    public static readonly IReadOnlyList<string> TiposVeiculo = ["CAVALO", "TRUCK", "TOCO", "CARRETA", "BITREM", "VAN", "UTILITARIO", "CARRO", "MOTO", "REBOQUE", "SEMIRREBOQUE", "OUTRO"];
    public static readonly IReadOnlyList<string> CombustiveisVeiculo = ["DIESEL_S10", "DIESEL", "GASOLINA", "ETANOL", "FLEX", "GNV", "ELETRICO", "NENHUM"];
    public static readonly IReadOnlyList<string> CombustiveisAbastecimento = ["DIESEL_S10", "DIESEL", "GASOLINA", "ETANOL", "GNV", "ARLA32"];
    public static readonly IReadOnlyList<string> CategoriasCnh = ["A", "B", "C", "D", "E", "AB", "AC", "AD", "AE"];
    public static readonly IReadOnlyList<string> TiposDocumento = ["LICENCIAMENTO", "IPVA", "SEGURO", "ANTT_RNTRC", "TACOGRAFO", "AET", "EXAME_TOXICO", "MOPP", "OUTRO"];

    public static string Rotulo(string codigo) => codigo switch
    {
        "DIESEL_S10" => "Diesel S10", "DIESEL" => "Diesel S500", "GASOLINA" => "Gasolina", "ETANOL" => "Etanol", "FLEX" => "Flex", "GNV" => "GNV",
        "ELETRICO" => "Elétrico", "NENHUM" => "Não abastece", "ARLA32" => "Arla 32",
        "UTILITARIO" => "Utilitário", "SEMIRREBOQUE" => "Semirreboque", "BITREM" => "Bitrem",
        "LICENCIAMENTO" => "Licenciamento (CRLV)", "ANTT_RNTRC" => "ANTT / RNTRC", "TACOGRAFO" => "Aferição do tacógrafo", "AET" => "AET (autorização especial)",
        "EXAME_TOXICO" => "Exame toxicológico", "MOPP" => "Curso MOPP", "SEGURO" => "Seguro", "IPVA" => "IPVA",
        "EM_ANDAMENTO" => "Em andamento", "CONCLUIDA" => "Concluída", "CANCELADA" => "Cancelada", "ABERTA" => "Aberta",
        "PREVENTIVA" => "Preventiva", "CORRETIVA" => "Corretiva",
        "PROPRIA" => "Própria", "ALIMENTACAO" => "Alimentação", "REFEICAO" => "Refeição", "LANCADA" => "Lançada", "EM_COLETA" => "Em coleta",
        "RODOVIARIA" => "Rodoviária (online)", "ESTIMADA" => "Estimada (linha reta)",
        _ => codigo.Length > 1 ? codigo[..1] + codigo[1..].ToLowerInvariant() : codigo,
    };

    [GeneratedRegex("^[A-Z]{3}[0-9][A-Z0-9][0-9]{2}$")]
    private static partial Regex PadraoPlaca();

    /// <summary>Placa sem traço, em maiúsculas (padrão antigo ABC1234 ou Mercosul ABC1D23).</summary>
    public static string NormalizarPlaca(string? placa) => new string([.. (placa ?? "").ToUpperInvariant().Where(char.IsAsciiLetterOrDigit)]);

    public static bool PlacaValida(string? placa) => PadraoPlaca().IsMatch(NormalizarPlaca(placa));

    /// <summary>ABC-1234 no padrão antigo; Mercosul sem traço (ABC1D23).</summary>
    public static string FormatarPlaca(string placa) => placa.Length == 7 && char.IsAsciiDigit(placa[4]) ? $"{placa[..3]}-{placa[3..]}" : placa;

    /// <summary>RENAVAM: 11 dígitos; o último confere os 10 primeiros (pesos 3,2,9,8,7,6,5,4,3,2; (soma × 10) mod 11, 10 vira 0).</summary>
    public static bool RenavamValido(string? renavam)
    {
        var d = new string([.. (renavam ?? "").Where(char.IsAsciiDigit)]);
        if (d.Length != 11) return false;
        int[] pesos = [3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        var soma = pesos.Select((p, i) => p * (d[i] - '0')).Sum();
        var dv = soma * 10 % 11;
        return (dv == 10 ? 0 : dv) == d[10] - '0';
    }

    public static IReadOnlyList<string> Validar(Veiculo v)
    {
        var erros = new List<string>();
        if (v.Codigo <= 0) erros.Add("Informe o código do veículo.");
        if (!PlacaValida(v.Placa)) erros.Add("Placa inválida: use o padrão antigo (ABC-1234) ou o Mercosul (ABC1D23).");
        if (!string.IsNullOrWhiteSpace(v.Renavam) && !RenavamValido(v.Renavam)) erros.Add("RENAVAM inválido (11 dígitos; confira o dígito verificador).");
        if (!TiposVeiculo.Contains(v.Tipo)) erros.Add("Escolha o tipo do veículo.");
        if (!CombustiveisVeiculo.Contains(v.Combustivel)) erros.Add("Escolha o combustível.");
        if (v.AnoModelo is { } am && v.AnoFabricacao is { } af && (am < af || am > af + 1)) erros.Add("O ano do modelo deve ser o de fabricação ou o seguinte.");
        if (v.KmAtual < 0) erros.Add("A quilometragem não pode ser negativa.");
        if (v.MediaEsperada is <= 0) erros.Add("A média esperada (km/l) deve ser maior que zero.");
        return erros;
    }

    public static IReadOnlyList<string> Validar(Motorista m)
    {
        var erros = new List<string>();
        if (m.Codigo <= 0) erros.Add("Informe o código do motorista.");
        if (string.IsNullOrWhiteSpace(m.Nome)) erros.Add("Informe o nome do motorista.");
        if (!Vorticis.Domain.Cadastros.Cpf.Valido(m.Cpf)) erros.Add("CPF inválido.");
        if (!string.IsNullOrWhiteSpace(m.Cnh) && new string([.. m.Cnh.Where(char.IsAsciiDigit)]).Length is < 9 or > 11) erros.Add("Número da CNH inválido (9 a 11 dígitos).");
        if (m.CnhCategoria is { } c && !CategoriasCnh.Contains(c)) erros.Add("Categoria da CNH inválida.");
        return erros;
    }

    /// <summary>Confere o abastecimento contra o veículo: km não pode voltar e o combustível deve combinar (Arla vale para diesel).</summary>
    public static IReadOnlyList<string> Validar(Abastecimento a, Veiculo v, decimal? kmAnterior)
    {
        var erros = new List<string>();
        if (!v.Abastece) erros.Add($"O veículo {FormatarPlaca(v.Placa)} ({Rotulo(v.Tipo)}) não abastece.");
        if (a.Litros <= 0) erros.Add("Informe os litros abastecidos.");
        if (a.ValorTotal <= 0) erros.Add("Informe o valor total do abastecimento.");
        if (a.Km < 0) erros.Add("Informe a quilometragem do hodômetro.");
        if (kmAnterior is { } k && a.Km < k) erros.Add(string.Create(Vorticis.Domain.Comum.Formato.PtBr, $"A quilometragem ({a.Km:N0}) é menor que a do abastecimento anterior ({k:N0})."));
        if (!CombustiveisAbastecimento.Contains(a.Combustivel)) erros.Add("Escolha o combustível abastecido.");
        else if (!CombinaCombustivel(v.Combustivel, a.Combustivel)) erros.Add($"Combustível {Rotulo(a.Combustivel)} não combina com o veículo ({Rotulo(v.Combustivel)}).");
        return erros;
    }

    public static bool CombinaCombustivel(string veiculo, string abastecido) => veiculo switch
    {
        "DIESEL_S10" or "DIESEL" => abastecido is "DIESEL_S10" or "DIESEL" or "ARLA32",
        "FLEX" => abastecido is "GASOLINA" or "ETANOL",
        "GASOLINA" => abastecido == "GASOLINA",
        "ETANOL" => abastecido == "ETANOL",
        "GNV" => abastecido is "GNV" or "GASOLINA" or "ETANOL",
        _ => false,
    };

    /// <summary>
    /// Médias km/l pelo método do tanque cheio: entre dois abastecimentos com tanque cheio, a média é o km rodado dividido pelos
    /// litros colocados depois do primeiro (inclusive os parciais e o do segundo). Arla 32 não entra.
    /// </summary>
    public static IReadOnlyList<MediaConsumo> Medias(IEnumerable<Abastecimento> abastecimentos)
    {
        var medias = new List<MediaConsumo>();
        foreach (var g in abastecimentos.Where(a => a.Combustivel != "ARLA32").GroupBy(a => a.VeiculoId))
        {
            Abastecimento? cheio = null;
            decimal litros = 0;
            foreach (var a in g.OrderBy(a => a.Km).ThenBy(a => a.Data))
            {
                if (cheio is null) { if (a.TanqueCheio) cheio = a; continue; }
                litros += a.Litros;
                if (!a.TanqueCheio) continue;
                var km = a.Km - cheio.Km;
                if (km > 0 && litros > 0) medias.Add(new MediaConsumo(g.Key, a.Data, km, litros, Math.Round(km / litros, 2), a.MotoristaId));
                (cheio, litros) = (a, 0);
            }
        }
        return medias;
    }

    /// <summary>Consumo abaixo do esperado além da tolerância (%) pede atenção (vazamento, desvio de combustível, manutenção).</summary>
    public static bool ConsumoForaDoEsperado(decimal media, decimal? esperada, decimal toleranciaPercentual)
        => esperada is { } e && e > 0 && media < e * (1 - toleranciaPercentual / 100m);

    /// <summary>Próxima preventiva por km e/ou data; vencida quando passou de qualquer um; a vencer quando está a 10% do intervalo de km ou dentro da antecedência.</summary>
    public static SituacaoPreventiva Situacao(PlanoManutencao p, decimal kmAtual, DateOnly hoje, int diasAviso)
    {
        decimal? proxKm = p.IntervaloKm is { } ik ? (p.UltimaKm ?? 0) + ik : null;
        DateOnly? proxData = p.IntervaloDias is { } dias && p.UltimaData is { } ud ? ud.AddDays(dias) : null;
        GravidadeAviso? g = null;
        if ((proxKm is { } k && kmAtual >= k) || (proxData is { } d && hoje >= d)) g = GravidadeAviso.Vencido;
        else if ((proxKm is { } k2 && kmAtual >= k2 - p.IntervaloKm!.Value * 0.1m) || (proxData is { } d2 && hoje.AddDays(diasAviso) >= d2)) g = GravidadeAviso.AVencer;
        return new SituacaoPreventiva(p, proxKm, proxData, g);
    }

    /// <summary>Vencido, a vencer (dentro da antecedência) ou em dia.</summary>
    public static GravidadeAviso? SituacaoVencimento(DateOnly vencimento, DateOnly hoje, int diasAviso)
        => vencimento < hoje ? GravidadeAviso.Vencido : vencimento <= hoje.AddDays(diasAviso) ? GravidadeAviso.AVencer : null;

    public static string TextoVencimento(DateOnly vencimento, DateOnly hoje)
    {
        var dias = vencimento.DayNumber - hoje.DayNumber;
        return dias < 0 ? $"vencido há {-dias} dia(s)" : dias == 0 ? "vence hoje" : $"vence em {dias} dia(s)";
    }

    public static IReadOnlyList<string> Validar(Viagem v)
    {
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(v.Origem) || string.IsNullOrWhiteSpace(v.Destino)) erros.Add("Informe a origem e o destino.");
        if (v.KmSaida < 0) erros.Add("Informe o km de saída.");
        if (v.KmChegada is { } kc && kc < v.KmSaida) erros.Add("O km de chegada não pode ser menor que o de saída.");
        if (v.Chegada is { } c && c < v.Saida) erros.Add("A chegada não pode ser antes da saída.");
        if (v.Situacao == "CONCLUIDA" && (v.Chegada is null || v.KmChegada is null)) erros.Add("Para concluir a viagem, informe a data e o km de chegada.");
        if (v.ValorFrete < 0 || v.Pedagio < 0 || v.Diarias < 0 || v.OutrasDespesas < 0) erros.Add("Valores não podem ser negativos.");
        return erros;
    }
}
