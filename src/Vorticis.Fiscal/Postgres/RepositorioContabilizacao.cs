using Npgsql;
using NpgsqlTypes;
using Vorticis.Domain.Comum;
using Vorticis.Fiscal.Contabilizacao;
using Vorticis.Fiscal.Documentos;

namespace Vorticis.Fiscal.Postgres;

public sealed class RepositorioContabilizacao(NpgsqlDataSource ds) : IRepositorioContabilizacao
{
    public async Task<IReadOnlyList<RegraContabilizacao>> ListarRegrasAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT r.id, r.prioridade, r.tipo_operacao, r.cfop, r.participante_id, p.nome, d.codigo, c.codigo, r.historico, r.ativo
              FROM fiscal.regra_contabilizacao r
              JOIN contabil.plano_contas d ON d.id = r.conta_debito_id
              JOIN contabil.plano_contas c ON c.id = r.conta_credito_id
              LEFT JOIN core.participante p ON p.id = r.participante_id
             WHERE r.empresa_id = @e
             ORDER BY r.prioridade, r.id
            """).Com("e", empresaId);
        var res = new List<RegraContabilizacao>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new RegraContabilizacao(r.GetInt64(0), r.GetInt32(1), r.IsDBNull(2) ? null : ModelosExtensions.OperacaoDe(r.GetString(2).Trim()),
                r.Texto(3), r.LongOuNulo(4), r.Texto(5), r.GetString(6), r.GetString(7), r.GetString(8), r.GetBoolean(9)));
        return res;
    }

    public Task<long> GravarRegraAsync(long empresaId, RegraContabilizacao g, long contaDebitoId, long contaCreditoId, CancellationToken ct)
        => Sql.TraduzirErrosAsync(async () =>
        {
            await using var cmd = g.Id == 0
                ? ds.CreateCommand("""
                    INSERT INTO fiscal.regra_contabilizacao (empresa_id, prioridade, tipo_operacao, cfop, participante_id, conta_debito_id, conta_credito_id, historico, ativo)
                    VALUES (@e, @pri, @op, @cfop, @part, @cd, @cc, @hist, @ativo) RETURNING id
                    """)
                : ds.CreateCommand("""
                    UPDATE fiscal.regra_contabilizacao SET prioridade = @pri, tipo_operacao = @op, cfop = @cfop, participante_id = @part,
                           conta_debito_id = @cd, conta_credito_id = @cc, historico = @hist, ativo = @ativo
                     WHERE id = @id AND empresa_id = @e RETURNING id
                    """);
            cmd.Com("e", empresaId).Com("pri", g.Prioridade).Com("op", NpgsqlDbType.Char, g.Operacao?.Codigo()).ComTexto("cfop", g.Cfop)
               .Com("part", NpgsqlDbType.Bigint, g.ParticipanteId).Com("cd", contaDebitoId).Com("cc", contaCreditoId).Com("hist", g.Historico).Com("ativo", g.Ativo);
            if (g.Id != 0) cmd.Com("id", g.Id);
            return await cmd.ExecuteScalarAsync(ct) as long? ?? throw new ViolacaoRegraException("Regra não encontrada.");
        });

    public async Task ExcluirRegraAsync(long empresaId, long regraId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("DELETE FROM fiscal.regra_contabilizacao WHERE id = @i AND empresa_id = @e").Com("i", regraId).Com("e", empresaId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, ContaResolvida>> ResolverContasAsync(long empresaId, IEnumerable<string> codigos, CancellationToken ct)
    {
        var lista = codigos.Select(c => c.Trim()).Distinct().ToArray();
        await using var cmd = ds.CreateCommand("""
            SELECT id, codigo, codigo_reduzido::text, tipo = 'A', ativa FROM contabil.plano_contas
             WHERE empresa_id = @e AND (codigo = ANY(@c) OR codigo_reduzido::text = ANY(@c))
            """).Com("e", empresaId).Com("c", lista);
        var res = new Dictionary<string, ContaResolvida>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var conta = new ContaResolvida(r.GetInt64(0), r.GetString(1), r.GetBoolean(3), r.GetBoolean(4));
            res[conta.Codigo] = conta;
            if (r.Texto(2) is { } reduzido) res.TryAdd(reduzido, conta);
        }
        return res;
    }

    public async Task MarcarContabilizadosAsync(IReadOnlyList<(long DocumentoId, long LancamentoId)> marcas, long usuarioId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            UPDATE fiscal.documento d SET lancamento_id = t.l, contabilizado_em = now(), contabilizado_por = @u
              FROM unnest(@d, @l) AS t(d, l) WHERE d.id = t.d
            """).Com("u", usuarioId).Com("d", marcas.Select(m => m.DocumentoId).ToArray()).Com("l", marcas.Select(m => m.LancamentoId).ToArray());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DesmarcarAsync(IReadOnlyCollection<long> documentoIds, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("UPDATE fiscal.documento SET lancamento_id = NULL, contabilizado_em = NULL, contabilizado_por = NULL WHERE id = ANY(@d)")
            .Com("d", documentoIds.ToArray());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<DateOnly?> ObterDataLancamentoAsync(long lancamentoId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT data_lancamento FROM contabil.lancamento WHERE id = @l").Com("l", lancamentoId);
        return await cmd.ExecuteScalarAsync(ct) is DateOnly d ? d : null;
    }

    public async Task<bool> ContabilizacaoOnlineAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT contabilizacao_online FROM fiscal.configuracao WHERE empresa_id = @e").Com("e", empresaId);
        return await cmd.ExecuteScalarAsync(ct) is true;
    }

    public async Task GravarContabilizacaoOnlineAsync(long empresaId, bool online, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            INSERT INTO fiscal.configuracao (empresa_id, contabilizacao_online) VALUES (@e, @o)
            ON CONFLICT (empresa_id) DO UPDATE SET contabilizacao_online = @o, atualizado_em = now()
            """).Com("e", empresaId).Com("o", online);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> CentroCustoAtivoAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT e.usa_centro_custo AND COALESCE((SELECT c.usa_centro_custo FROM fiscal.configuracao c WHERE c.empresa_id = e.id), FALSE)
              FROM core.empresa e WHERE e.id = @e
            """).Com("e", empresaId);
        return await cmd.ExecuteScalarAsync(ct) is true;
    }
}
