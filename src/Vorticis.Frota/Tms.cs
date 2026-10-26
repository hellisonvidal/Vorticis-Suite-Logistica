using Vorticis.Domain.Comum;

namespace Vorticis.Frota;

/// <summary>
/// Tabela de frete: frete-peso (R$ por kg do peso taxado, com frete mínimo; peso taxado é o maior entre o peso real e o cubado = m³ × fator de cubagem),
/// ad valorem e GRIS (% sobre o valor da mercadoria), pedágio por fração de 100 kg, taxas de coleta, entrega e outras, alíquota de ICMS informada pelo
/// usuário (a transportadora define conforme a operação) e prazo de entrega em dias. Vale para uma rota ou para as UFs de origem e destino.
/// </summary>
public sealed record TabelaFrete(long Id, int Codigo, string Descricao, long? RotaId, string? UfOrigem, string? UfDestino, decimal ValorKg, decimal FreteMinimo,
                                 decimal FatorCubagem, decimal AdValoremPct, decimal GrisPct, decimal PedagioFracao, decimal TaxaColeta, decimal TaxaEntrega,
                                 decimal OutrasTaxas, decimal AliquotaIcms, int PrazoDias, bool Ativa = true, string? RotaNome = null)
{
    public string Abrangencia => RotaNome ?? (UfOrigem is null && UfDestino is null ? "Todas" : $"{UfOrigem ?? "*"} → {UfDestino ?? "*"}");
    public override string ToString() => $"{Codigo} – {Descricao}";
}

/// <summary>Componentes do frete calculado pela tabela. Total = base do ICMS (o ICMS entra "por dentro" do valor da prestação).</summary>
public sealed record ComponentesFrete(decimal PesoTaxado, decimal FretePeso, decimal AdValorem, decimal Gris, decimal Pedagio, decimal Taxas, decimal AliquotaIcms,
                                      decimal Icms, decimal Total)
{
    public decimal SemIcms => FretePeso + AdValorem + Gris + Pedagio + Taxas;
}

public sealed record Cotacao(long Id, int Numero, DateOnly Data, DateOnly Validade, long ClienteId, long TabelaId, string Origem, string Destino, decimal PesoKg,
                             decimal VolumeM3, decimal ValorMercadoria, bool Coleta, bool Entrega, decimal ValorTotal, string Situacao, string? Observacao,
                             string? Cliente = null, string? Tabela = null);

/// <summary>Conhecimento de transporte (CT-e). Situação: DIGITADO → EMITIDO (com a chave de acesso) → EM_TRANSITO → ENTREGUE; ou CANCELADO.</summary>
public sealed record Cte(long Id, int Serie, int Numero, DateOnly Emissao, string? Cfop, long RemetenteId, long DestinatarioId, long TomadorId,
                         ParadaRota Origem, ParadaRota Destino, long? TabelaId, long? CotacaoId, long? ColetaId, long? ViagemId, long? VeiculoId, long? MotoristaId,
                         string? Produto, int Volumes, decimal PesoKg, decimal VolumeM3, decimal ValorMercadoria, string? ChavesNfe, ComponentesFrete Frete,
                         DateOnly? PrevisaoEntrega, string? Chave, string? Protocolo, string Situacao, DateTime? EntregueEm, string? Recebedor, long? FaturaId,
                         string? Observacao, string? Remetente = null, string? Destinatario = null, string? Tomador = null, string? Placa = null,
                         string? MotoristaNome = null, int? FaturaNumero = null)
{
    public decimal ValorTotal => Frete.Total;
    public string Trecho => $"{Origem.Municipio}/{Origem.Uf} → {Destino.Municipio}/{Destino.Uf}";
    public override string ToString() => $"{Serie}-{Numero} · {Tomador ?? Destinatario} · {Formato.Moeda(ValorTotal)}";
}

public sealed record Manifesto(long Id, int Serie, int Numero, DateOnly Emissao, string UfInicio, string UfFim, string? UfsPercurso, long VeiculoId, long MotoristaId,
                               long? ViagemId, string? Chave, string Situacao, DateOnly? EncerradoEm, string? Observacao, IReadOnlyList<long> Ctes,
                               string? Placa = null, string? MotoristaNome = null, decimal PesoKg = 0, decimal ValorMercadoria = 0, decimal ValorFrete = 0);

public sealed record Ocorrencia(long Id, long CteId, DateTime DataHora, string Tipo, string? Descricao, string? Recebedor, string? Usuario = null);

public sealed record FaturaFrete(long Id, int Numero, DateOnly Emissao, DateOnly Vencimento, long ClienteId, decimal ValorTotal, long? DuplicataId, string Situacao,
                                 string? Observacao, string? Cliente = null, decimal? Saldo = null, int Ctes = 0);

/// <summary>Regras puras do TMS: cálculo do frete, chave de acesso, situações e prazos.</summary>
public static class RegrasTms
{
    public static readonly IReadOnlyList<string> SituacoesCte = ["DIGITADO", "EMITIDO", "EM_TRANSITO", "ENTREGUE", "CANCELADO"];
    public static readonly IReadOnlyList<string> SituacoesCotacao = ["ABERTA", "APROVADA", "RECUSADA", "CONVERTIDA"];
    public static readonly IReadOnlyList<string> SituacoesManifesto = ["ABERTO", "ENCERRADO", "CANCELADO"];
    public static readonly IReadOnlyList<string> TiposOcorrencia = ["SAIDA", "CHEGADA", "SAIU_PARA_ENTREGA", "ENTREGUE", "TENTATIVA", "AVARIA", "DEVOLUCAO"];
    public const string ModeloCte = "57";
    public const string ModeloMdfe = "58";
    public const string ModeloNfe = "55";

    public static string Rotulo(string codigo) => codigo switch
    {
        "DIGITADO" => "Digitado", "EMITIDO" => "Emitido", "EM_TRANSITO" => "Em trânsito", "ENTREGUE" => "Entregue", "CANCELADO" => "Cancelado",
        "ABERTA" => "Aberta", "APROVADA" => "Aprovada", "RECUSADA" => "Recusada", "CONVERTIDA" => "Convertida em CT-e", "CANCELADA" => "Cancelada",
        "ABERTO" => "Aberto", "ENCERRADO" => "Encerrado",
        "SAIDA" => "Saída do veículo", "CHEGADA" => "Chegada na filial/destino", "SAIU_PARA_ENTREGA" => "Saiu para entrega", "TENTATIVA" => "Tentativa sem sucesso",
        "AVARIA" => "Avaria", "DEVOLUCAO" => "Devolução",
        _ => codigo,
    };

    private static decimal R2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Frete pela tabela. Peso taxado = maior entre o peso real e o cubado (m³ × fator de cubagem). Frete-peso = peso taxado × R$/kg, nunca abaixo do mínimo.
    /// Pedágio por fração de 100 kg (101 kg = 2 frações). Ad valorem e GRIS sobre o valor da mercadoria. Com ICMS, o total é "por dentro":
    /// total = (soma dos componentes) ÷ (1 − alíquota), e o ICMS = total × alíquota. A alíquota informada (se houver) substitui a da tabela.
    /// </summary>
    public static ComponentesFrete Calcular(TabelaFrete t, decimal pesoKg, decimal volumeM3, decimal valorMercadoria, bool coleta, bool entrega, decimal? aliquotaIcms = null)
    {
        var pesoTaxado = Math.Max(Math.Max(pesoKg, 0), R2(Math.Max(volumeM3, 0) * t.FatorCubagem));
        var fretePeso = Math.Max(R2(pesoTaxado * t.ValorKg), t.FreteMinimo);
        var adValorem = R2(Math.Max(valorMercadoria, 0) * t.AdValoremPct / 100m);
        var gris = R2(Math.Max(valorMercadoria, 0) * t.GrisPct / 100m);
        var pedagio = pesoTaxado > 0 ? Math.Ceiling(pesoTaxado / 100m) * t.PedagioFracao : 0;
        var taxas = (coleta ? t.TaxaColeta : 0) + (entrega ? t.TaxaEntrega : 0) + t.OutrasTaxas;
        return ComIcms(fretePeso, adValorem, gris, pedagio, taxas, aliquotaIcms ?? t.AliquotaIcms, pesoTaxado);
    }

    /// <summary>Monta os componentes (digitados ou calculados) com o ICMS por dentro.</summary>
    public static ComponentesFrete ComIcms(decimal fretePeso, decimal adValorem, decimal gris, decimal pedagio, decimal taxas, decimal aliquota, decimal pesoTaxado = 0)
    {
        var soma = fretePeso + adValorem + gris + pedagio + taxas;
        if (aliquota <= 0 || aliquota >= 100) return new ComponentesFrete(pesoTaxado, fretePeso, adValorem, gris, pedagio, taxas, 0, 0, R2(soma));
        var total = R2(soma / (1 - aliquota / 100m));
        return new ComponentesFrete(pesoTaxado, fretePeso, adValorem, gris, pedagio, taxas, aliquota, total - soma, total);
    }

    /// <summary>A tabela atende o trecho: pela rota escolhida ou pelas UFs (UF em branco na tabela vale para qualquer uma).</summary>
    public static bool Atende(TabelaFrete t, string? ufOrigem, string? ufDestino, long? rotaId = null)
        => t.Ativa && (t.RotaId is { } r ? r == rotaId
                                          : (t.UfOrigem is null || string.Equals(t.UfOrigem, ufOrigem, StringComparison.OrdinalIgnoreCase))
                                            && (t.UfDestino is null || string.Equals(t.UfDestino, ufDestino, StringComparison.OrdinalIgnoreCase)));

    public static string Digitos(string? s) => new([.. (s ?? "").Where(char.IsAsciiDigit)]);

    /// <summary>Dígito verificador da chave de acesso (módulo 11, pesos 2 a 9 da direita para a esquerda; resto 0 ou 1 dá 0).</summary>
    public static int DigitoChave(string chave43)
    {
        var soma = 0;
        var peso = 2;
        for (var i = chave43.Length - 1; i >= 0; i--)
        {
            soma += (chave43[i] - '0') * peso;
            peso = peso == 9 ? 2 : peso + 1;
        }
        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }

    /// <summary>Chave de acesso de 44 dígitos com o dígito verificador certo e, se informado, do modelo esperado (55 NF-e, 57 CT-e, 58 MDF-e; posições 21 e 22).</summary>
    public static bool ChaveValida(string? chave, string? modelo = null)
    {
        var d = Digitos(chave);
        return d.Length == 44 && DigitoChave(d[..43]) == d[43] - '0' && (modelo is null || d.Substring(20, 2) == modelo);
    }

    /// <summary>Chaves das NF-e transportadas, uma por linha (ou separadas por vírgula/espaço), só os dígitos.</summary>
    public static IReadOnlyList<string> ChavesNfe(string? texto)
        => [.. (texto ?? "").Split(['\n', '\r', ',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).Select(Digitos).Where(c => c.Length > 0).Distinct()];

    public static IReadOnlyList<string> Validar(TabelaFrete t)
    {
        var erros = new List<string>();
        if (t.Codigo <= 0) erros.Add("Informe o código da tabela.");
        if (string.IsNullOrWhiteSpace(t.Descricao)) erros.Add("Informe a descrição da tabela.");
        if (t.ValorKg <= 0 && t.FreteMinimo <= 0) erros.Add("Informe o valor por kg ou o frete mínimo.");
        if (new[] { t.ValorKg, t.FreteMinimo, t.FatorCubagem, t.PedagioFracao, t.TaxaColeta, t.TaxaEntrega, t.OutrasTaxas }.Any(v => v < 0) || t.PrazoDias < 0)
            erros.Add("Valores e prazo não podem ser negativos.");
        if (t.AdValoremPct is < 0 or > 100 || t.GrisPct is < 0 or > 100) erros.Add("Ad valorem e GRIS devem estar entre 0% e 100%.");
        if (t.AliquotaIcms is < 0 or >= 100) erros.Add("Alíquota de ICMS inválida.");
        foreach (var uf in new[] { t.UfOrigem, t.UfDestino }.Where(u => u is not null))
            if (!Vorticis.Domain.Cadastros.Uf.Valida(uf!)) erros.Add($"UF inválida: {uf}.");
        return erros;
    }

    public static IReadOnlyList<string> Validar(Cte c)
    {
        var erros = new List<string>();
        if (c.RemetenteId <= 0) erros.Add("Escolha o remetente.");
        if (c.DestinatarioId <= 0) erros.Add("Escolha o destinatário.");
        if (c.TomadorId <= 0) erros.Add("Escolha o tomador do serviço (quem paga o frete).");
        foreach (var (p, nome) in new[] { (c.Origem, "origem"), (c.Destino, "destino") })
            if (BaseMunicipios.Obter(p.MunicipioIbge) is not { } m || !string.Equals(m.Uf, p.Uf, StringComparison.OrdinalIgnoreCase))
                erros.Add($"Informe o município de {nome} da prestação.");
        if (c.Serie is < 0 or > 999) erros.Add("Série inválida (0 a 999).");
        if (c.Cfop is { Length: > 0 } cfop && (cfop.Length != 4 || !cfop.All(char.IsAsciiDigit) || cfop[0] is not ('5' or '6' or '7')))
            erros.Add("CFOP inválido: use 4 dígitos de prestação de saída (5, 6 ou 7).");
        if (c.Volumes < 0 || c.PesoKg < 0 || c.VolumeM3 < 0 || c.ValorMercadoria < 0) erros.Add("Volumes, peso, cubagem e valor não podem ser negativos.");
        if (c.Frete.Total <= 0) erros.Add("O valor do frete deve ser maior que zero (calcule pela tabela ou informe os componentes).");
        foreach (var chave in ChavesNfe(c.ChavesNfe))
            if (!ChaveValida(chave, ModeloNfe)) erros.Add($"Chave de NF-e inválida: {chave}.");
        if (c.PrevisaoEntrega is { } prev && prev < c.Emissao) erros.Add("A previsão de entrega não pode ser antes da emissão.");
        return erros;
    }

    public static IReadOnlyList<string> Validar(Manifesto m)
    {
        var erros = new List<string>();
        if (m.VeiculoId <= 0) erros.Add("Escolha o veículo.");
        if (m.MotoristaId <= 0) erros.Add("Escolha o motorista.");
        if (!Vorticis.Domain.Cadastros.Uf.Valida(m.UfInicio) || !Vorticis.Domain.Cadastros.Uf.Valida(m.UfFim)) erros.Add("Informe as UFs de início e fim do percurso.");
        foreach (var uf in (m.UfsPercurso ?? "").Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries))
            if (!Vorticis.Domain.Cadastros.Uf.Valida(uf)) erros.Add($"UF de percurso inválida: {uf}.");
        if (m.Ctes.Count == 0) erros.Add("Inclua ao menos um CT-e no manifesto.");
        if (m.Chave is { Length: > 0 } ch && !ChaveValida(ch, ModeloMdfe)) erros.Add("Chave de acesso do MDF-e inválida (44 dígitos, modelo 58).");
        return erros;
    }

    /// <summary>Só o CT-e digitado (ainda não emitido) pode ter os dados alterados.</summary>
    public static bool CteEditavel(string situacao) => situacao == "DIGITADO";

    /// <summary>CT-e que pode entrar num manifesto ou numa fatura (já emitido e não cancelado).</summary>
    public static bool CteEmitido(string situacao) => situacao is "EMITIDO" or "EM_TRANSITO" or "ENTREGUE";

    /// <summary>Situação do CT-e depois de uma ocorrência; null se a ocorrência não é aceita na situação atual.</summary>
    public static string? SituacaoAposOcorrencia(string atual, string tipo) => atual switch
    {
        "EMITIDO" or "EM_TRANSITO" => tipo switch
        {
            "ENTREGUE" => "ENTREGUE",
            "SAIDA" or "CHEGADA" or "SAIU_PARA_ENTREGA" => "EM_TRANSITO",
            _ => atual,
        },
        _ => null,
    };

    /// <summary>Prazo da entrega: "Entregue no prazo", "Entregue com atraso", "Atrasado", "No prazo" ou "Sem previsão".</summary>
    public static string SituacaoPrazo(DateOnly? previsao, string situacao, DateTime? entregueEm, DateOnly hoje)
    {
        if (situacao == "CANCELADO") return "Cancelado";
        if (previsao is not { } p) return "Sem previsão";
        if (situacao == "ENTREGUE" && entregueEm is { } e) return DateOnly.FromDateTime(e) <= p ? "Entregue no prazo" : "Entregue com atraso";
        return hoje > p ? "Atrasado" : "No prazo";
    }

    public static DateOnly? Previsao(DateOnly emissao, int prazoDias) => prazoDias > 0 ? emissao.AddDays(prazoDias) : null;
}
