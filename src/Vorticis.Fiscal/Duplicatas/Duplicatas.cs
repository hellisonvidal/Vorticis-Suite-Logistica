using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;

namespace Vorticis.Fiscal.Duplicatas;

public enum TipoDuplicata { Pagar, Receber }

public enum SituacaoDuplicata { Aberta, Parcial, Liquidada, Cancelada }

public static class DuplicatasExtensions
{
    public static string Codigo(this TipoDuplicata t) => t == TipoDuplicata.Pagar ? "PAGAR" : "RECEBER";
    public static TipoDuplicata TipoDe(string codigo) => codigo.Trim() == "PAGAR" ? TipoDuplicata.Pagar : TipoDuplicata.Receber;
    public static string Rotulo(this TipoDuplicata t) => t == TipoDuplicata.Pagar ? "A pagar" : "A receber";

    public static string Codigo(this SituacaoDuplicata s) => s switch
    {
        SituacaoDuplicata.Parcial => "PARCIAL", SituacaoDuplicata.Liquidada => "LIQUIDADA", SituacaoDuplicata.Cancelada => "CANCELADA", _ => "ABERTA",
    };

    public static SituacaoDuplicata SituacaoDe(string codigo) => codigo.Trim() switch
    {
        "PARCIAL" => SituacaoDuplicata.Parcial, "LIQUIDADA" => SituacaoDuplicata.Liquidada, "CANCELADA" => SituacaoDuplicata.Cancelada, _ => SituacaoDuplicata.Aberta,
    };

    public static string Rotulo(this SituacaoDuplicata s) => s switch
    {
        SituacaoDuplicata.Parcial => "Parcial", SituacaoDuplicata.Liquidada => "Liquidada", SituacaoDuplicata.Cancelada => "Cancelada", _ => "Aberta",
    };
}

/// <summary>Parcela de cobrança lida do XML (&lt;cobr&gt;&lt;dup&gt;) ou gerada/digitada na nota.</summary>
public sealed record ParcelaDuplicata(string Numero, DateOnly Vencimento, decimal Valor);

/// <summary>Duplicata (parcela a pagar ou a receber). Juros, multa e desconto são os totais das baixas.</summary>
public sealed record Duplicata(
    long Id, long? DocumentoId, string? DocumentoNumero, long? ParticipanteId, string? ParticipanteCodigo, string? ParticipanteNome, TipoDuplicata Tipo,
    string Parcela, string NumeroDocumento, DateOnly Emissao, DateOnly Vencimento, decimal Valor, decimal Juros, decimal Multa, decimal Desconto,
    decimal Saldo, SituacaoDuplicata Situacao, string? Observacao, string Origem)
{
    public bool EmAberto => Situacao is SituacaoDuplicata.Aberta or SituacaoDuplicata.Parcial;

    public bool Vencida(DateOnly hoje) => EmAberto && Vencimento < hoje;

    /// <summary>Dias de atraso na data-base (zero se não vencida ou não aberta).</summary>
    public int DiasAtraso(DateOnly hoje) => Vencida(hoje) ? hoje.DayNumber - Vencimento.DayNumber : 0;

    /// <summary>Documento + parcela como aparece nas listas e no histórico ("123/001").</summary>
    public string Titulo => string.IsNullOrWhiteSpace(Parcela) ? NumeroDocumento : $"{NumeroDocumento}/{Parcela}";
}

public sealed record FiltroDuplicatas(
    TipoDuplicata? Tipo = null, DateOnly? VencimentoInicio = null, DateOnly? VencimentoFim = null, long? ParticipanteId = null, string? Participante = null,
    SituacaoDuplicata? Situacao = null, bool SomenteAbertas = false, bool SomenteVencidas = false, DateOnly? Hoje = null, long? DocumentoId = null, int Limite = 5000);

/// <summary>Duplicata a incluir ou alterar (lançamento manual).</summary>
public sealed record DadosDuplicata(
    long Id, TipoDuplicata Tipo, long ParticipanteId, string NumeroDocumento, string Parcela, DateOnly Emissao, DateOnly Vencimento, decimal Valor, string? Observacao);

public sealed record BaixaDuplicata(
    long Id, long DuplicataId, DateOnly Data, decimal Principal, decimal Juros, decimal Multa, decimal Desconto, string? ContaDisponivel, long? LancamentoId,
    long? PartidaExtratoId, long? ConciliacaoId)
{
    /// <summary>Dinheiro que entrou ou saiu: principal + juros + multa - desconto.</summary>
    public decimal Liquido => Principal + Juros + Multa - Desconto;
}

/// <summary>Pedido de baixa (total ou parcial). Os acréscimos e o desconto não alteram o saldo da duplicata, só o principal.</summary>
public sealed record NovaBaixa(long DuplicataId, DateOnly Data, decimal Principal, decimal Juros, decimal Multa, decimal Desconto, string ContaDisponivel);

public sealed record FalhaDuplicata(string Duplicata, IReadOnlyList<string> Erros);

public sealed record ResultadoBaixaLote(int Baixadas, decimal Valor, IReadOnlyList<FalhaDuplicata> Falhas)
{
    public string Resumo => $"{Baixadas} duplicata(s) baixada(s), total {Formato.Moeda(Valor)}" + (Falhas.Count > 0 ? $"; {Falhas.Count} com pendência." : ".");
}

/// <summary>Opções do Fiscal para duplicatas: geração à vista e contas contábeis das baixas (código da conta analítica).</summary>
public sealed record ConfiguracaoDuplicatas(
    bool GerarAVista = true, int AVistaDias = 0, string? ContaFornecedores = null, string? ContaClientes = null, string? ContaJurosPagos = null,
    string? ContaMultaPaga = null, string? ContaDescontoObtido = null, string? ContaJurosRecebidos = null, string? ContaMultaRecebida = null,
    string? ContaDescontoConcedido = null, string HistoricoPagamento = "Pagamento NF {DOCUMENTO} - {PARTICIPANTE}",
    string HistoricoRecebimento = "Recebimento NF {DOCUMENTO} - {PARTICIPANTE}")
{
    public string? ContaParticipante(TipoDuplicata t) => t == TipoDuplicata.Pagar ? ContaFornecedores : ContaClientes;
}

/// <summary>Movimento do extrato bancário (OFX já importado no contábil) ainda não conciliado com duplicata.</summary>
public sealed record MovimentoExtrato(
    long PartidaBancoId, long LancamentoId, DateOnly Data, IndicadorDC DC, decimal Valor, string Historico, string? Documento, string ContaBanco,
    long PartidaContraId, string ContaContra, bool ContraConciliada)
{
    /// <summary>Saída de dinheiro (crédito na conta do banco) casa com duplicata a pagar; entrada (débito), com a receber.</summary>
    public TipoDuplicata Tipo => DC == IndicadorDC.C ? TipoDuplicata.Pagar : TipoDuplicata.Receber;
}

public sealed record SugestaoConciliacao(Duplicata Duplicata, MovimentoExtrato Movimento, int Pontos, string Motivo);

/// <summary>Primeira parcela, quantidade e intervalo para gerar parcelas iguais.</summary>
public static class GeradorParcelas
{
    /// <summary>
    /// <paramref name="quantidade"/> parcelas iguais (a última absorve a diferença do arredondamento), a primeira em <paramref name="primeiroVencimento"/> e as
    /// seguintes a cada <paramref name="intervaloDias"/> dias; numeradas 001, 002...
    /// </summary>
    public static IReadOnlyList<ParcelaDuplicata> Gerar(decimal total, int quantidade, DateOnly primeiroVencimento, int intervaloDias)
    {
        if (quantidade < 1 || total <= 0) return [];
        var valor = Dinheiro.Arredondar(total / quantidade);
        var lista = new List<ParcelaDuplicata>(quantidade);
        for (var i = 0; i < quantidade; i++)
        {
            var v = i == quantidade - 1 ? Dinheiro.Arredondar(total - valor * (quantidade - 1)) : valor;
            lista.Add(new ParcelaDuplicata((i + 1).ToString("000"), primeiroVencimento.AddDays(intervaloDias * i), v));
        }
        return lista;
    }
}

public enum FaixaVencimento { AVencer, De1a30, De31a60, De61a90, Acima90 }

public static class Aging
{
    public static readonly IReadOnlyList<string> Rotulos = ["A vencer", "Vencidas 1 a 30 dias", "Vencidas 31 a 60 dias", "Vencidas 61 a 90 dias", "Vencidas há mais de 90 dias"];

    public static FaixaVencimento Faixa(int diasAtraso) => diasAtraso switch
    {
        <= 0 => FaixaVencimento.AVencer, <= 30 => FaixaVencimento.De1a30, <= 60 => FaixaVencimento.De31a60, <= 90 => FaixaVencimento.De61a90, _ => FaixaVencimento.Acima90,
    };

    /// <summary>Saldo em aberto por participante e faixa de vencimento na data-base, só duplicatas abertas ou parciais; ordenado pelo nome.</summary>
    public static IReadOnlyList<LinhaAging> Posicao(IEnumerable<Duplicata> duplicatas, DateOnly dataBase) =>
        duplicatas.Where(d => d.EmAberto && d.Saldo > 0)
            .GroupBy(d => d.ParticipanteNome ?? "(sem participante)")
            .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(g =>
            {
                var faixas = new decimal[5];
                foreach (var d in g) faixas[(int)Faixa(d.DiasAtraso(dataBase))] += d.Saldo;
                return new LinhaAging(g.Key, faixas);
            }).ToList();
}

public sealed record LinhaAging(string Participante, IReadOnlyList<decimal> Faixas)
{
    public decimal Total => Faixas.Sum();
}

/// <summary>Sugestão de conciliação: casa duplicatas em aberto com movimentos do extrato por valor, data próxima e/ou documento. Puro, sem banco.</summary>
public static class ConciliadorDuplicatas
{
    /// <summary>Documento comparado sem espaços, zeros à esquerda e diferença de maiúsculas ("NF 000123" = "nf123").</summary>
    public static string Normalizar(string? documento)
    {
        if (string.IsNullOrWhiteSpace(documento)) return "";
        var t = new string(documento.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        var letras = new string(t.TakeWhile(char.IsLetter).ToArray());
        return letras + t[letras.Length..].TrimStart('0');
    }

    private static bool MencionaDocumento(MovimentoExtrato m, Duplicata d)
    {
        var alvo = Normalizar(d.NumeroDocumento);
        if (alvo.Length == 0) return false;
        if (Normalizar(m.Documento) == alvo) return true;
        // o número aparece como palavra do histórico do extrato (ex.: "PAGTO NF 1234 FORNECEDOR")
        return m.Historico.Split([' ', '-', '/', ':', ';', ',', '.'], StringSplitOptions.RemoveEmptyEntries).Any(p => Normalizar(p) == alvo);
    }

    /// <summary>
    /// Para cada duplicata em aberto, o melhor movimento de mesmo sentido e valor igual ao saldo, a até <paramref name="toleranciaDias"/> do vencimento ou com o
    /// documento citado. Cada movimento e cada duplicata aparecem no máximo uma vez (maior pontuação primeiro). Pontos: documento 60, mais 40 menos 5 por dia de
    /// diferença na data.
    /// </summary>
    public static IReadOnlyList<SugestaoConciliacao> Sugerir(IEnumerable<Duplicata> duplicatas, IEnumerable<MovimentoExtrato> movimentos, int toleranciaDias)
    {
        var candidatas = new List<SugestaoConciliacao>();
        var movs = movimentos.Where(m => !m.ContraConciliada).ToList();
        foreach (var d in duplicatas.Where(d => d.EmAberto && d.Saldo > 0))
            foreach (var m in movs.Where(m => m.Tipo == d.Tipo && m.Valor == d.Saldo))
            {
                var dias = Math.Abs(m.Data.DayNumber - d.Vencimento.DayNumber);
                var doc = MencionaDocumento(m, d);
                if (dias > toleranciaDias && !doc) continue;
                var pontos = (doc ? 60 : 0) + Math.Max(0, 40 - 5 * dias);
                var motivo = doc ? (dias == 0 ? "valor, documento e data iguais" : $"valor e documento iguais, data a {dias} dia(s) do vencimento")
                                 : dias == 0 ? "valor e data iguais" : $"valor igual, data a {dias} dia(s) do vencimento";
                candidatas.Add(new SugestaoConciliacao(d, m, pontos, motivo));
            }
        var usadasD = new HashSet<long>();
        var usadosM = new HashSet<long>();
        var res = new List<SugestaoConciliacao>();
        foreach (var s in candidatas.OrderByDescending(s => s.Pontos).ThenBy(s => s.Duplicata.Vencimento).ThenBy(s => s.Duplicata.Id))
            if (!usadasD.Contains(s.Duplicata.Id) && !usadosM.Contains(s.Movimento.PartidaBancoId))
            {
                usadasD.Add(s.Duplicata.Id);
                usadosM.Add(s.Movimento.PartidaBancoId);
                res.Add(s);
            }
        return res;
    }
}
