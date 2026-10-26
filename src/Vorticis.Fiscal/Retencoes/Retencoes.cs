using System.Text.Json;
using Vorticis.Domain.Comum;
using Vorticis.Fiscal.Documentos;

namespace Vorticis.Fiscal.Retencoes;

/// <summary>SOFRIDA: retida por clientes sobre as notas da empresa (crédito a compensar). EFETUADA: retida pela empresa de fornecedores (a recolher).</summary>
public enum SentidoRetencao { Sofrida, Efetuada }

public enum TributoRetido { IRRF, PIS, COFINS, CSLL, INSS, ISS }

public sealed record Retencao(
    long Id, SentidoRetencao Sentido, TributoRetido Tributo, DateOnly Competencia, long? DocumentoId, string? DocumentoNumero, long? ParticipanteId,
    string? ParticipanteNome, decimal BaseCalculo, decimal Aliquota, decimal Valor, string? CodigoReceita, string Origem, string? Descricao);

/// <summary>Retenção a gravar (ainda sem id).</summary>
public sealed record NovaRetencao(
    SentidoRetencao Sentido, TributoRetido Tributo, DateOnly Competencia, long? DocumentoId, long? ParticipanteId, decimal BaseCalculo, decimal Aliquota,
    decimal Valor, string? CodigoReceita, string Origem, string? Descricao);

/// <summary>Códigos de receita, alíquotas de referência e vencimentos (core.parametro_legal, chave RETENCOES_GUIAS).</summary>
public sealed record ParametrosGuias(
    string CodigoCsrf, string CodigoInss, string CodigoIrrfServicos, int DiaVencimentoIss, decimal MinimoDarf, IReadOnlyDictionary<string, string> Darf)
{
    public string Codigo(string chave) => Darf.TryGetValue(chave, out var c) ? c : throw new KeyNotFoundException($"Código de receita {chave} não parametrizado.");

    public static ParametrosGuias Carregar(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        return new ParametrosGuias(r.GetProperty("csrf").GetProperty("codigo_receita").GetString()!, r.GetProperty("inss").GetProperty("codigo_receita").GetString()!,
            r.GetProperty("irrf").GetProperty("servicos_pj").GetString()!, r.GetProperty("iss").GetProperty("dia_vencimento").GetInt32(),
            r.GetProperty("csrf").GetProperty("minimo").GetDecimal(), r.GetProperty("darf").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!));
    }
}

/// <summary>Extrai as retenções informadas nas notas (NF-e retTrib; NFS-e tribFed/tribMun). Pura.</summary>
public static class GeradorRetencoes
{
    public static IReadOnlyList<NovaRetencao> DeDocumento(DocumentoFiscal d, ParametrosGuias p)
    {
        if (d.Situacao != SituacaoDocumento.Autorizada) return [];
        var sentido = d.Operacao == TipoOperacao.Saida ? SentidoRetencao.Sofrida : SentidoRetencao.Efetuada;
        var comp = new DateOnly(d.DataLivro.Year, d.DataLivro.Month, 1);
        var baseCalculo = d.Totais.Total;
        var t = d.Totais;
        var lista = new List<NovaRetencao>();
        void Add(TributoRetido tributo, decimal valor, string? codigo)
        {
            if (valor <= 0) return;
            lista.Add(new NovaRetencao(sentido, tributo, comp, d.Id, d.ParticipanteId, baseCalculo, baseCalculo > 0 ? Math.Round(valor / baseCalculo * 100m, 4) : 0,
                valor, sentido == SentidoRetencao.Efetuada ? codigo : null, "NOTA", $"{d.Modelo.Rotulo()} {d.Numero}"));
        }
        Add(TributoRetido.IRRF, t.Irrf, p.CodigoIrrfServicos);
        Add(TributoRetido.PIS, t.RetPis, p.CodigoCsrf);
        Add(TributoRetido.COFINS, t.RetCofins, p.CodigoCsrf);
        Add(TributoRetido.CSLL, t.RetCsll, p.CodigoCsrf);
        Add(TributoRetido.INSS, t.RetPrev, p.CodigoInss);
        if (t.IssRetido) Add(TributoRetido.ISS, t.Iss, null);
        return lista;
    }
}
