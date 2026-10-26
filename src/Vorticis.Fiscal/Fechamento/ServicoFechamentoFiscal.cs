using Npgsql;
using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Postgres;

namespace Vorticis.Fiscal.Fechamento;

/// <summary>Situação de um mês no fechamento fiscal: quantas notas tem e quantas ainda não foram contabilizadas.</summary>
public sealed record MesFiscal(DateOnly Competencia, bool Fechado, DateTimeOffset? FechadoEm, string? FechadoPor, int Notas, int NaoContabilizadas);

/// <summary>
/// Apuração › Fechamento do período fiscal (empresas do regime normal: Lucro Presumido, Real ou Arbitrado). Com o mês fechado, as notas com
/// data de entrada/saída no mês ficam travadas no banco (incluir, alterar e excluir); contabilizar continua permitido. Reabrir destrava.
/// </summary>
public sealed class ServicoFechamentoFiscal(NpgsqlDataSource ds, Autorizador autorizador)
{
    /// <summary>Regime normal = não é Simples Nacional nem MEI (o fechamento é das empresas que apuram ICMS/IPI e PIS/COFINS pelo regime normal).</summary>
    public static bool RegimeNormal(string? regime) => regime is "LUCRO_PRESUMIDO" or "LUCRO_REAL" or "LUCRO_ARBITRADO" or "IMUNE_ISENTA";

    public async Task<Resultado<IReadOnlyList<MesFiscal>>> ListarAsync(long empresaId, int ano, long usuarioId, CancellationToken ct = default)
    {
        if (!(await autorizador.ObterAsync(usuarioId, empresaId, ct)).TemAcesso) return Resultado<IReadOnlyList<MesFiscal>>.Falha("Você não tem acesso a esta empresa.");
        await using var cmd = ds.CreateCommand("""
            SELECT m::date,
                   f.competencia IS NOT NULL, f.fechado_em, u.nome,
                   (SELECT count(*) FROM fiscal.documento d WHERE d.empresa_id = @e AND date_trunc('month', d.data_entrada_saida) = m)::int,
                   (SELECT count(*) FROM fiscal.documento d WHERE d.empresa_id = @e AND date_trunc('month', d.data_entrada_saida) = m
                                                               AND d.situacao = 'AUTORIZADA' AND d.lancamento_id IS NULL)::int
              FROM generate_series(make_date(@a, 1, 1), make_date(@a, 12, 1), interval '1 month') m
              LEFT JOIN fiscal.periodo_fechado f ON f.empresa_id = @e AND f.competencia = m::date
              LEFT JOIN core.usuario u ON u.id = f.fechado_por
             ORDER BY m
            """).Com("e", empresaId).Com("a", ano);
        var res = new List<MesFiscal>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new MesFiscal(r.GetFieldValue<DateOnly>(0), r.GetBoolean(1), r.IsDBNull(2) ? null : r.GetFieldValue<DateTimeOffset>(2),
                                  r.IsDBNull(3) ? null : r.GetString(3), r.GetInt32(4), r.GetInt32(5)));
        return Resultado<IReadOnlyList<MesFiscal>>.Ok(res);
    }

    public async Task<string?> RegimeAsync(long empresaId, DateOnly data, CancellationToken ct = default)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT regime::text FROM core.empresa_regime
             WHERE empresa_id = @e AND vigencia_inicio <= @d AND (vigencia_fim IS NULL OR vigencia_fim >= @d)
             ORDER BY vigencia_inicio DESC LIMIT 1
            """).Com("e", empresaId).Com("d", data);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    /// <summary>Fecha o mês. Devolve quantas notas do mês ainda não estavam contabilizadas (aviso; o fechamento não depende disso).</summary>
    public async Task<Resultado<int>> FecharAsync(long empresaId, DateOnly competencia, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<int>(usuarioId, empresaId, Permissao.FiscalApurar, ct) is { } negado) return negado;
        var mes = new DateOnly(competencia.Year, competencia.Month, 1);
        var regime = await RegimeAsync(empresaId, mes, ct);
        if (!RegimeNormal(regime))
            return Resultado<int>.Falha(regime is null
                ? "Informe o regime tributário da empresa (Cadastros › Empresas, aba Contabilidade) antes de fechar o período."
                : "O fechamento do período fiscal é para empresas do regime normal (Lucro Presumido ou Real). No Simples Nacional, o período é encerrado pela apuração do DAS.");
        await using var cmd = ds.CreateCommand("""
            INSERT INTO fiscal.periodo_fechado (empresa_id, competencia, fechado_por) VALUES (@e, @c, @u) ON CONFLICT DO NOTHING;
            SELECT count(*)::int FROM fiscal.documento WHERE empresa_id = @e AND date_trunc('month', data_entrada_saida) = @c
                                                         AND situacao = 'AUTORIZADA' AND lancamento_id IS NULL;
            """).Com("e", empresaId).Com("c", mes).Com("u", usuarioId);
        return Resultado<int>.Ok((int)(await cmd.ExecuteScalarAsync(ct) ?? 0));
    }

    public async Task<Resultado> ReabrirAsync(long empresaId, DateOnly competencia, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FiscalApurar, ct) is { } negado) return Resultado.Falha(negado);
        await using var cmd = ds.CreateCommand("DELETE FROM fiscal.periodo_fechado WHERE empresa_id = @e AND competencia = @c")
            .Com("e", empresaId).Com("c", new DateOnly(competencia.Year, competencia.Month, 1));
        return await cmd.ExecuteNonQueryAsync(ct) == 0 ? Resultado.Falha("O mês não está fechado.") : Resultado.Ok();
    }
}
