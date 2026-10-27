using System.Text.Json;
using Vorticis.Domain.Comum;
using Vorticis.Infra.Postgres;
using P = Vorticis.Domain.Seguranca.Permissao;

namespace Vorticis.Frota;

/// <summary>Multas de trânsito: lançamento (FROTA_OPERACAO), consulta, prazos para o monitor e a tabela de pontos e valores do CTB.</summary>
public sealed partial class ServicoFrota
{
    public async Task<List<MultaTransito>> MultasAsync(long empresa, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await ListarMultasAsync(empresa, ct) : [];

    private Task<List<MultaTransito>> ListarMultasAsync(long empresa, CancellationToken ct) => _db.ListarAsync("""
        SELECT u.id, u.veiculo_id, u.motorista_id, u.auto_infracao, u.orgao, u.data_infracao, u.local_infracao, u.enquadramento, u.descricao, u.gravidade,
               u.pontos, u.valor, u.desconto, u.vencimento, u.prazo_indicacao, u.prazo_recurso, u.responsavel, u.situacao, u.data_pagamento, u.valor_pago,
               u.observacao, v.placa, m.nome
          FROM frota.multa u JOIN frota.veiculo v ON v.id = u.veiculo_id LEFT JOIN frota.motorista m ON m.id = u.motorista_id
         WHERE u.empresa_id = @e ORDER BY u.data_infracao DESC, u.id DESC
        """, r => new MultaTransito(r.GetInt64(0), r.GetInt64(1), r.Id(2), r.GetString(3), r.Texto(4), r.GetFieldValue<DateOnly>(5), r.Texto(6), r.Texto(7),
                                    r.Texto(8), r.GetString(9), r.GetInt16(10), r.GetDecimal(11), r.GetDecimal(12), r.Data(13), r.Data(14), r.Data(15),
                                    r.GetString(16), r.GetString(17), r.Data(18), r.Valor(19), r.Texto(20), RegrasFrota.FormatarPlaca(r.GetString(21)), r.Texto(22)),
        ct, ("e", empresa));

    public async Task<Resultado<long>> GravarMultaAsync(long empresa, MultaTransito m, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaOperacao, ct) is { } n) return Resultado<long>.Falha(n);
        m = m with { AutoInfracao = (m.AutoInfracao ?? "").Trim().ToUpperInvariant(), Orgao = N(m.Orgao), Local = N(m.Local), Enquadramento = N(m.Enquadramento),
                     Descricao = N(m.Descricao), Observacao = N(m.Observacao) };
        if (RegrasMultas.Validar(m) is { Count: > 0 } erros) return Resultado<long>.Falha(erros);
        (string, object?)[] p =
        [
            ("e", empresa), ("id", m.Id), ("v", m.VeiculoId), ("m", m.MotoristaId), ("a", m.AutoInfracao), ("o", m.Orgao), ("d", m.DataInfracao), ("l", m.Local),
            ("enq", m.Enquadramento), ("desc", m.Descricao), ("g", m.Gravidade), ("pt", (short)m.Pontos), ("valor", m.Valor), ("dsc", m.Desconto),
            ("venc", m.Vencimento), ("pi", m.PrazoIndicacao), ("pr", m.PrazoRecurso), ("resp", m.Responsavel), ("s", m.Situacao), ("dp", m.DataPagamento),
            ("vp", m.ValorPago), ("obs", m.Observacao),
        ];
        var r = await Tentar(async () => m.Id == 0
            ? await _db.EscalarAsync<long>("""
                INSERT INTO frota.multa (empresa_id, veiculo_id, motorista_id, auto_infracao, orgao, data_infracao, local_infracao, enquadramento, descricao,
                                         gravidade, pontos, valor, desconto, vencimento, prazo_indicacao, prazo_recurso, responsavel, situacao, data_pagamento,
                                         valor_pago, observacao)
                VALUES (@e, @v, @m, @a, @o, @d, @l, @enq, @desc, @g, @pt, @valor, @dsc, @venc, @pi, @pr, @resp, @s, @dp, @vp, @obs) RETURNING id
                """, ct, p)
            : await _db.EscalarAsync<long?>("""
                UPDATE frota.multa SET veiculo_id = @v, motorista_id = @m, auto_infracao = @a, orgao = @o, data_infracao = @d, local_infracao = @l,
                       enquadramento = @enq, descricao = @desc, gravidade = @g, pontos = @pt, valor = @valor, desconto = @dsc, vencimento = @venc,
                       prazo_indicacao = @pi, prazo_recurso = @pr, responsavel = @resp, situacao = @s, data_pagamento = @dp, valor_pago = @vp, observacao = @obs
                 WHERE id = @id AND empresa_id = @e RETURNING id
                """, ct, p) ?? throw new ViolacaoRegraException("Multa não encontrada."));
        // o único cadastro único da multa é o auto de infração
        return !r.Sucesso && r.Erros.Any(e => e.StartsWith("Já existe um cadastro", StringComparison.Ordinal))
            ? Resultado<long>.Falha($"O auto de infração {m.AutoInfracao} já está lançado.")
            : r;
    }

    public async Task<Resultado> ExcluirMultaAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaOperacao, ct) is { } n) return Resultado.Falha(n);
        return await Tentar(() => _db.ExecutarAsync("DELETE FROM frota.multa WHERE id = @id AND empresa_id = @e", ct, ("id", id), ("e", empresa)));
    }

    /// <summary>Pontos, valores e descontos do CTB vigentes na data (core.parametro_legal, chave CTB_INFRACOES); null se não cadastrados.</summary>
    public async Task<TabelaInfracoes?> TabelaInfracoesAsync(DateOnly data, CancellationToken ct = default)
    {
        var json = (await _db.ListarAsync("""
            SELECT valor::text FROM core.parametro_legal
             WHERE chave = 'CTB_INFRACOES' AND vigencia_inicio <= @d AND (vigencia_fim IS NULL OR vigencia_fim >= @d)
             ORDER BY vigencia_inicio DESC LIMIT 1
            """, r => r.GetString(0), ct, ("d", data))).FirstOrDefault();
        if (json is null) return null;
        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;
        var porGravidade = RegrasMultas.Gravidades.Where(g => raiz.TryGetProperty(g, out _))
            .ToDictionary(g => g, g => (raiz.GetProperty(g).GetProperty("pontos").GetInt32(), raiz.GetProperty(g).GetProperty("valor").GetDecimal()));
        decimal Num(string nome) => raiz.TryGetProperty(nome, out var v) ? v.GetDecimal() : 0;
        return new TabelaInfracoes(porGravidade, Num("desconto_vencimento"), Num("desconto_sne"));
    }
}
