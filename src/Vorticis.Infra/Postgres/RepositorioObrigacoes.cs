using Npgsql;
using Vorticis.Application.Obrigacoes;
using Vorticis.Domain.Obrigacoes;

namespace Vorticis.Infra.Postgres;

public sealed class RepositorioObrigacoes(NpgsqlDataSource ds) : IRepositorioObrigacoes
{
    public async Task<PerfilObrigacoesEmpresa> ObterPerfilAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT (SELECT regime::text FROM core.empresa_regime r WHERE r.empresa_id = @e ORDER BY vigencia_inicio DESC LIMIT 1),
                   EXISTS (SELECT 1 FROM folha.funcionario f WHERE f.empresa_id = @e AND f.situacao <> 'DESLIGADO')
            """).Com("e", empresaId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        var regime = r.IsDBNull(0) ? null : r.GetString(0);
        return new PerfilObrigacoesEmpresa(regime switch
        {
            "MEI" => RegimesObrigacao.Mei,
            "SIMPLES_NACIONAL" => RegimesObrigacao.Simples,
            "LUCRO_PRESUMIDO" => RegimesObrigacao.Presumido,
            "LUCRO_REAL" => RegimesObrigacao.Real,
            _ => RegimesObrigacao.Outros,
        }, r.GetBoolean(1));
    }

    private static readonly Dictionary<TipoRegra, string> RegraSql = new() { [TipoRegra.DiaFixo] = "DIA_FIXO", [TipoRegra.DiaUtil] = "DIA_UTIL", [TipoRegra.UltimoDiaUtil] = "ULTIMO_DIA_UTIL" };

    public async Task<IReadOnlyList<ObrigacaoPersonalizada>> ListarPersonalizadasAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT id, nome, orgao, esfera, periodicidade, regra, dia, meses_apos, mes_vencimento, anos_apos, ajuste, observacao, ativa
              FROM core.obrigacao_personalizada WHERE empresa_id = @e ORDER BY nome
            """).Com("e", empresaId);
        var res = new List<ObrigacaoPersonalizada>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new ObrigacaoPersonalizada(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3),
                Enum.Parse<Periodicidade>(r.GetString(4), true), RegraSql.First(p => p.Value == r.GetString(5)).Key, r.GetInt32(6), r.GetInt32(7),
                r.GetInt32(8), r.GetInt32(9), Enum.Parse<AjusteNaoUtil>(r.GetString(10), true), r.IsDBNull(11) ? null : r.GetString(11), r.GetBoolean(12)));
        return res;
    }

    public Task<long> SalvarPersonalizadaAsync(long empresaId, ObrigacaoPersonalizada o, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var cmd = ds.CreateCommand(o.Id == 0
                ? """
                  INSERT INTO core.obrigacao_personalizada (empresa_id, nome, orgao, esfera, periodicidade, regra, dia, meses_apos, mes_vencimento, anos_apos, ajuste, observacao, ativa)
                  VALUES (@e, @n, @org, @esf, @per, @reg, @dia, @ma, @mv, @aa, @aj, @obs, @at) RETURNING id
                  """
                : """
                  UPDATE core.obrigacao_personalizada SET nome = @n, orgao = @org, esfera = @esf, periodicidade = @per, regra = @reg, dia = @dia,
                         meses_apos = @ma, mes_vencimento = @mv, anos_apos = @aa, ajuste = @aj, observacao = @obs, ativa = @at
                   WHERE id = @id AND empresa_id = @e RETURNING id
                  """)
                .Com("e", empresaId).Com("id", o.Id).Com("n", o.Nome).Com("org", o.Orgao).Com("esf", o.Esfera.ToUpperInvariant())
                .Com("per", o.Periodicidade.ToString().ToUpperInvariant()).Com("reg", RegraSql[o.Regra]).Com("dia", o.Dia).Com("ma", o.MesesApos)
                .Com("mv", o.MesVencimento).Com("aa", o.AnosApos).Com("aj", o.Ajuste.ToString().ToUpperInvariant()).ComTexto("obs", o.Observacao).Com("at", o.Ativa);
            return await cmd.ExecuteScalarAsync(ct) is long id ? id : throw new Domain.Comum.ViolacaoRegraException("Obrigação não encontrada.");
        });

    public async Task ExcluirPersonalizadaAsync(long empresaId, long id, CancellationToken ct)
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        await using (var c1 = new NpgsqlCommand("DELETE FROM core.obrigacao_status WHERE empresa_id = @e AND obrigacao = @c", con, tx))
        {
            c1.Parameters.AddWithValue("e", empresaId);
            c1.Parameters.AddWithValue("c", $"P{id}");
            await c1.ExecuteNonQueryAsync(ct);
        }
        await using (var c2 = new NpgsqlCommand("DELETE FROM core.obrigacao_personalizada WHERE empresa_id = @e AND id = @id", con, tx))
        {
            c2.Parameters.AddWithValue("e", empresaId);
            c2.Parameters.AddWithValue("id", id);
            await c2.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<BaixaObrigacao>> ListarBaixasAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT obrigacao, competencia, cumprida_em, observacao FROM core.obrigacao_status WHERE empresa_id = @e").Com("e", empresaId);
        var res = new List<BaixaObrigacao>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new BaixaObrigacao(r.GetString(0), r.GetString(1), DateOnly.FromDateTime(r.GetDateTime(2)), r.IsDBNull(3) ? null : r.GetString(3)));
        return res;
    }

    public async Task SalvarBaixaAsync(long empresaId, BaixaObrigacao b, long usuarioId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            INSERT INTO core.obrigacao_status (empresa_id, obrigacao, competencia, cumprida_em, observacao, usuario_id)
            VALUES (@e, @o, @c, @d, @obs, @u)
            ON CONFLICT (empresa_id, obrigacao, competencia) DO UPDATE
               SET cumprida_em = EXCLUDED.cumprida_em, observacao = EXCLUDED.observacao, usuario_id = EXCLUDED.usuario_id, atualizado_em = now()
            """).Com("e", empresaId).Com("o", b.Obrigacao).Com("c", b.Competencia).Com("d", b.CumpridaEm).ComTexto("obs", b.Observacao).Com("u", NpgsqlTypes.NpgsqlDbType.Bigint, usuarioId > 0 ? usuarioId : null);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RemoverBaixaAsync(long empresaId, string obrigacao, string competencia, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("DELETE FROM core.obrigacao_status WHERE empresa_id = @e AND obrigacao = @o AND competencia = @c")
            .Com("e", empresaId).Com("o", obrigacao).Com("c", competencia);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
