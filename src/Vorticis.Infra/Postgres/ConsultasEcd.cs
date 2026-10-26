using Npgsql;
using Vorticis.Application.Sped;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Sped;

namespace Vorticis.Infra.Postgres;

/// <summary>
/// Leitura dos dados da ECD direto das tabelas (lançamentos e partidas, não o cache de saldos): os saldos periódicos (I155)
/// são calculados das próprias partidas, então batem com os lançamentos (I250) por construção. Lançamentos estornados e os
/// estornos entram (ambos estão no Diário); rascunhos não.
/// </summary>
public sealed class ConsultasEcd(NpgsqlDataSource ds) : IConsultasEcd
{
    public async Task<DadosContabeisEcd?> CarregarAsync(long empresaId, int ano, CancellationToken ct)
    {
        await using var con = await ds.OpenConnectionAsync(ct);

        long exercicioId; DateOnly inicio, fim; string? codPlanRef;
        await using (var cmd = new NpgsqlCommand(
            "SELECT id, data_inicio, data_fim, cod_plan_ref FROM contabil.exercicio WHERE empresa_id = @e AND ano = @a", con)
            .Com("e", empresaId).Com("a", (short)ano))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) return null;
            (exercicioId, inicio, fim, codPlanRef) = (r.GetInt64(0), r.GetFieldValue<DateOnly>(1), r.GetFieldValue<DateOnly>(2), r.IsDBNull(3) ? null : r.GetString(3));
        }

        EmpresaEcd empresa;
        await using (var cmd = new NpgsqlCommand("""
            SELECT cnpj, razao_social, uf, cod_municipio_ibge, municipio, inscricao_estadual, inscricao_municipal, nire, grande_porte, data_inicio_ativ
              FROM core.empresa WHERE id = @e
            """, con).Com("e", empresaId))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) return null;
            string? T(int i) => r.IsDBNull(i) ? null : r.GetString(i);
            empresa = new EmpresaEcd(r.GetString(0).Trim(), r.GetString(1), r.GetString(2).Trim(), r.GetString(3).Trim(), T(4), T(5), T(6), T(7),
                                     r.GetBoolean(8), r.GetFieldValue<DateOnly>(9));
        }

        var plano = new List<ContaEcd>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT c.id, c.codigo, c.descricao, c.nivel, c.tipo = 'A', c.conta_pai_id, c.cod_nat, c.natureza_saldo::text, c.data_inclusao, ref.codigo
              FROM contabil.plano_contas c
              LEFT JOIN LATERAL (SELECT r.codigo FROM contabil.mapeamento_referencial m
                                   JOIN contabil.plano_referencial r ON r.id = m.plano_referencial_id
                                  WHERE m.conta_id = c.id AND m.centro_custo_id IS NULL AND m.ano_vigencia = @a AND r.cod_plan_ref = @p
                                  LIMIT 1) ref ON TRUE
             WHERE c.empresa_id = @e
             ORDER BY c.codigo
            """, con).Com("e", empresaId).Com("a", (short)ano).Com("p", NpgsqlTypes.NpgsqlDbType.Varchar, codPlanRef))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                plano.Add(new ContaEcd(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt16(3), r.GetBoolean(4), r.IsDBNull(5) ? null : r.GetInt64(5),
                                       r.GetString(6), Mapeamentos.DC(r.GetString(7)), r.GetFieldValue<DateOnly>(8), r.IsDBNull(9) ? null : r.GetString(9)));

        var centros = new List<CentroCustoEcd>();
        await using (var cmd = new NpgsqlCommand(
            "SELECT id, codigo, descricao, data_inclusao FROM contabil.centro_custo WHERE empresa_id = @e ORDER BY codigo", con).Com("e", empresaId))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                centros.Add(new CentroCustoEcd(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetFieldValue<DateOnly>(3)));

        var abertura = new List<SaldoAberturaEcd>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT p.conta_id, COALESCE(p.centro_custo_id, 0), SUM(CASE p.ind_dc WHEN 'D' THEN p.valor ELSE -p.valor END)
              FROM contabil.partida p JOIN contabil.lancamento l ON l.id = p.lancamento_id
             WHERE l.empresa_id = @e AND l.status <> 'RASCUNHO' AND l.data_lancamento < @ini
             GROUP BY 1, 2
            HAVING SUM(CASE p.ind_dc WHEN 'D' THEN p.valor ELSE -p.valor END) <> 0
            """, con).Com("e", empresaId).Com("ini", inicio))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                abertura.Add(new SaldoAberturaEcd(r.GetInt64(0), r.GetInt64(1), r.GetDecimal(2)));

        var lancamentos = new List<LancamentoEcd>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT l.id, l.numero, l.data_lancamento, l.ind_lcto::text, l.data_lcto_ext, l.origem = 'SALDO_INICIAL',
                   p.conta_id, p.centro_custo_id, p.ind_dc::text, p.valor, p.historico, p.num_arquivo
              FROM contabil.lancamento l JOIN contabil.partida p ON p.lancamento_id = l.id
             WHERE l.empresa_id = @e AND l.status <> 'RASCUNHO' AND l.data_lancamento BETWEEN @ini AND @fim
             ORDER BY l.data_lancamento, l.numero, p.sequencia
            """, con).Com("e", empresaId).Com("ini", inicio).Com("fim", fim))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            long? atual = null;
            List<PartidaEcd> partidas = [];
            (long Id, long Numero, DateOnly Data, IndicadorLancamento Ind, DateOnly? Ext, bool Saldo) cab = default;
            void Fechar()
            {
                if (atual is not null) lancamentos.Add(new LancamentoEcd(cab.Id, cab.Numero, cab.Data, cab.Ind, cab.Ext, partidas, cab.Saldo));
            }
            while (await r.ReadAsync(ct))
            {
                var id = r.GetInt64(0);
                if (id != atual)
                {
                    Fechar();
                    atual = id;
                    partidas = [];
                    cab = (id, r.GetInt64(1), r.GetFieldValue<DateOnly>(2), r.GetString(3) switch { "E" => IndicadorLancamento.Encerramento,
                           "X" => IndicadorLancamento.Extemporaneo, _ => IndicadorLancamento.Normal },
                           r.IsDBNull(4) ? null : r.GetFieldValue<DateOnly>(4), r.GetBoolean(5));
                }
                partidas.Add(new PartidaEcd(r.GetInt64(6), r.IsDBNull(7) ? null : r.GetInt64(7), Mapeamentos.DC(r.GetString(8)), r.GetDecimal(9),
                                            r.GetString(10), r.IsDBNull(11) ? null : r.GetString(11)));
            }
            Fechar();
        }

        var historicos = new List<HistoricoPadraoEcd>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT DISTINCT h.codigo, h.descricao
              FROM contabil.historico_padrao h
              JOIN contabil.partida p ON p.historico_padrao_id = h.id
              JOIN contabil.lancamento l ON l.id = p.lancamento_id
             WHERE l.empresa_id = @e AND l.status <> 'RASCUNHO' AND l.data_lancamento BETWEEN @ini AND @fim
             ORDER BY h.codigo
            """, con).Com("e", empresaId).Com("ini", inicio).Com("fim", fim))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                historicos.Add(new HistoricoPadraoEcd(r.GetString(0), r.GetString(1)));

        // Resultado do exercício anterior (sem os lançamentos de encerramento) por conta de resultado, para J150.VL_CTA_INI.
        Dictionary<long, decimal>? anterior = null;
        await using (var cmd = new NpgsqlCommand("""
            SELECT p.conta_id, SUM(CASE p.ind_dc WHEN 'D' THEN p.valor ELSE -p.valor END)
              FROM contabil.exercicio e
              JOIN contabil.lancamento l ON l.exercicio_id = e.id AND l.status <> 'RASCUNHO' AND l.ind_lcto <> 'E'
              JOIN contabil.partida p ON p.lancamento_id = l.id
              JOIN contabil.plano_contas c ON c.id = p.conta_id AND c.cod_nat = '04'
             WHERE e.empresa_id = @e AND e.ano = @a
             GROUP BY p.conta_id
            """, con).Com("e", empresaId).Com("a", (short)(ano - 1)))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                (anterior ??= [])[r.GetInt64(0)] = r.GetDecimal(1);

        var abertos = new List<DateOnly>();
        await using (var cmd = new NpgsqlCommand(
            "SELECT competencia FROM contabil.periodo WHERE exercicio_id = @x AND status = 'ABERTO' ORDER BY competencia", con).Com("x", exercicioId))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) abertos.Add(r.GetFieldValue<DateOnly>(0));

        // Número do livro: o mesmo da última geração deste ano (regerar não cria livro novo); senão o seguinte ao do ano anterior.
        int numeroOrdem;
        await using (var cmd = new NpgsqlCommand("""
            SELECT COALESCE((SELECT num_ordem FROM contabil.ecd_gerada WHERE empresa_id = @e AND ano = @a ORDER BY gerado_em DESC, id DESC LIMIT 1),
                            (SELECT MAX(num_ordem) + 1 FROM contabil.ecd_gerada WHERE empresa_id = @e AND ano < @a), 1)
            """, con).Com("e", empresaId).Com("a", (short)ano))
            numeroOrdem = (int)(await cmd.ExecuteScalarAsync(ct))!;

        return new DadosContabeisEcd(empresa, exercicioId, inicio, fim, codPlanRef, plano, centros, historicos, abertura, lancamentos, anterior, abertos, numeroOrdem);
    }

    public async Task<long> RegistrarAsync(long empresaId, int ano, DateOnly inicio, DateOnly fim, int numeroOrdem, string naturezaLivro, string hashSha1,
                                           int linhas, int assinaturas, long? usuarioId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            INSERT INTO contabil.ecd_gerada (empresa_id, ano, dt_ini, dt_fin, num_ordem, nat_livro, hash_sha1, linhas, assinaturas, gerado_por)
            VALUES (@e, @a, @ini, @fim, @n, @nat, @h, @l, @s, (SELECT id FROM core.usuario WHERE id = @u))
            RETURNING id
            """).Com("e", empresaId).Com("a", (short)ano).Com("ini", inicio).Com("fim", fim).Com("n", numeroOrdem)
                .Com("nat", naturezaLivro.Length > 80 ? naturezaLivro[..80] : naturezaLivro).Com("h", hashSha1).Com("l", linhas)
                .Com("s", (short)assinaturas).Com("u", NpgsqlTypes.NpgsqlDbType.Bigint, usuarioId);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<IReadOnlyList<EcdRegistrada>> ListarAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT id, ano, dt_ini, dt_fin, num_ordem, nat_livro, hash_sha1, linhas, assinaturas, gerado_em
              FROM contabil.ecd_gerada WHERE empresa_id = @e ORDER BY gerado_em DESC, id DESC
            """).Com("e", empresaId);
        var res = new List<EcdRegistrada>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new EcdRegistrada(r.GetInt64(0), r.GetInt16(1), r.GetFieldValue<DateOnly>(2), r.GetFieldValue<DateOnly>(3), r.GetInt32(4),
                                      r.GetString(5), r.GetString(6), r.GetInt32(7), r.GetInt16(8), r.GetFieldValue<DateTimeOffset>(9)));
        return res;
    }
}
