using Npgsql;
using Vorticis.Application.Contabil;
using Vorticis.Domain.Contabil;

namespace Vorticis.Infra.Postgres;

public sealed class RepositorioPlanoContas(NpgsqlDataSource ds) : IRepositorioPlanoContas
{
    public async Task<IReadOnlyList<ContaDetalhe>> ListarAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT c.id, c.codigo, c.descricao, c.tipo = 'A', c.nivel, c.conta_pai_id, c.cod_nat, c.natureza_saldo::text,
                   c.exige_centro_custo, c.exige_participante, c.data_inclusao, c.ativa,
                   EXISTS (SELECT 1 FROM contabil.partida p WHERE p.conta_id = c.id),
                   EXISTS (SELECT 1 FROM contabil.plano_contas f WHERE f.conta_pai_id = c.id),
                   COALESCE((SELECT SUM(s.total_debito - s.total_credito) FROM contabil.saldo_mensal s WHERE s.conta_id = c.id), 0),
                   c.codigo_reduzido, c.grupo_participantes_id
              FROM contabil.plano_contas c WHERE c.empresa_id = @e ORDER BY c.codigo
            """).Com("e", empresaId);
        var res = new List<ContaDetalhe>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new ContaDetalhe(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetBoolean(3), r.GetInt16(4),
                r.IsDBNull(5) ? null : r.GetInt64(5), r.GetString(6), Mapeamentos.DC(r.GetString(7)), r.GetBoolean(8), r.GetBoolean(9),
                r.GetFieldValue<DateOnly>(10), r.GetBoolean(11), r.GetBoolean(12), r.GetBoolean(13), r.GetDecimal(14),
                r.IsDBNull(15) ? null : r.GetInt32(15), r.IsDBNull(16) ? null : r.GetInt64(16)));
        return res;
    }

    public Task<long> IncluirAsync(long empresaId, NovaConta c, long? contaPaiId, short nivel, bool analitica, long? paiParaSintetica,
                                   long usuarioId, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await Mapeamentos.DefinirUsuarioAsync(con, tx, usuarioId, ct);
            if (paiParaSintetica is long pai)
                await using (var cmd = new NpgsqlCommand("""
                    UPDATE contabil.plano_contas SET tipo = 'S'
                     WHERE id = @id AND tipo = 'A' AND NOT EXISTS (SELECT 1 FROM contabil.partida WHERE conta_id = @id)
                    """, con, tx).Com("id", pai))
                    if (await cmd.ExecuteNonQueryAsync(ct) != 1)
                        throw new Domain.Comum.ViolacaoRegraException("A conta superior recebeu lançamentos e não pode virar sintética.");

            long id;
            await using (var cmd = new NpgsqlCommand("""
                INSERT INTO contabil.plano_contas (empresa_id, codigo, descricao, tipo, nivel, conta_pai_id, cod_nat, natureza_saldo,
                                                   exige_centro_custo, exige_participante, data_inclusao, codigo_reduzido, grupo_participantes_id)
                VALUES (@e, @c, @d, @t::contabil.tipo_conta, @n, @pai, @nat, @sal::contabil.indicador_dc, @cc, @part, @inc, @red, @grupo) RETURNING id
                """, con, tx)
                .Com("e", empresaId).Com("c", c.Codigo).Com("d", c.Descricao).Com("t", analitica ? "A" : "S").Com("n", nivel)
                .Com("pai", NpgsqlTypes.NpgsqlDbType.Bigint, contaPaiId).Com("nat", c.CodNat).Com("sal", c.NaturezaSaldo.ToString())
                .Com("cc", c.ExigeCentroCusto).Com("part", c.ExigeParticipante).Com("inc", c.DataInclusao)
                .Com("red", NpgsqlTypes.NpgsqlDbType.Integer, c.CodigoReduzido)
                .Com("grupo", NpgsqlTypes.NpgsqlDbType.Bigint, c.GrupoParticipantesId))
                id = (long)(await cmd.ExecuteScalarAsync(ct))!;
            await tx.CommitAsync(ct);
            return id;
        });

    public Task AlterarAsync(long contaId, string descricao, IndicadorDC naturezaSaldo, bool exigeCentroCusto, bool exigeParticipante, bool ativa,
                             long? grupoParticipantesId, long usuarioId, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await Mapeamentos.DefinirUsuarioAsync(con, tx, usuarioId, ct);
            await using (var cmd = new NpgsqlCommand("""
                UPDATE contabil.plano_contas
                   SET descricao = @d, natureza_saldo = @s::contabil.indicador_dc, exige_centro_custo = @cc,
                       exige_participante = @p, ativa = @a, grupo_participantes_id = @g
                 WHERE id = @id
                """, con, tx).Com("d", descricao).Com("s", naturezaSaldo.ToString()).Com("cc", exigeCentroCusto)
                             .Com("p", exigeParticipante).Com("a", ativa).Com("g", NpgsqlTypes.NpgsqlDbType.Bigint, grupoParticipantesId).Com("id", contaId))
                await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return 0;
        });

    public Task AlterarReduzidoAsync(long contaId, int reduzido, long usuarioId, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await Mapeamentos.DefinirUsuarioAsync(con, tx, usuarioId, ct);
            await using (var cmd = new NpgsqlCommand("UPDATE contabil.plano_contas SET codigo_reduzido = @r WHERE id = @id", con, tx)
                             .Com("r", reduzido).Com("id", contaId))
                await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return 0;
        });

    public Task ExcluirAsync(long contaId, long usuarioId, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await Mapeamentos.DefinirUsuarioAsync(con, tx, usuarioId, ct);
            await using (var cmd = new NpgsqlCommand("""
                DELETE FROM contabil.mapeamento_referencial WHERE conta_id = @id;
                DELETE FROM contabil.saldo_mensal WHERE conta_id = @id AND total_debito = 0 AND total_credito = 0;
                DELETE FROM contabil.plano_contas WHERE id = @id;
                """, con, tx).Com("id", contaId))
                await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return 0;
        });

    public Task SubstituirAsync(long empresaId, IReadOnlyList<Application.Cadastros.ContaModelo> plano, long usuarioId, CancellationToken ct,
                                Application.Cadastros.ModeloPlano? modelo = null)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await Mapeamentos.DefinirUsuarioAsync(con, tx, usuarioId, ct);
            DateOnly inicio;
            await using (var cmd = new NpgsqlCommand("""
                SELECT EXISTS (SELECT 1 FROM contabil.partida p JOIN contabil.plano_contas c ON c.id = p.conta_id WHERE c.empresa_id = @e),
                       (SELECT min(data_inicio) FROM contabil.exercicio WHERE empresa_id = @e)
                """, con, tx).Com("e", empresaId))
            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                await r.ReadAsync(ct);
                if (r.GetBoolean(0)) throw new Domain.Comum.ViolacaoRegraException("o plano atual já tem lançamentos.");
                inicio = r.IsDBNull(1) ? DateOnly.FromDateTime(DateTime.Today) : r.GetFieldValue<DateOnly>(1);
            }
            await using (var cmd = new NpgsqlCommand("""
                DELETE FROM contabil.mapeamento_referencial WHERE conta_id IN (SELECT id FROM contabil.plano_contas WHERE empresa_id = @e);
                DELETE FROM contabil.saldo_mensal WHERE conta_id IN (SELECT id FROM contabil.plano_contas WHERE empresa_id = @e);
                DELETE FROM contabil.plano_contas WHERE empresa_id = @e;
                UPDATE core.participante SET controle_conta = FALSE WHERE empresa_id = @e;
                """, con, tx).Com("e", empresaId))
                await cmd.ExecuteNonQueryAsync(ct);
            await RepositorioEmpresa.InserirPlanoAsync(con, tx, empresaId, plano, inicio, ct);
            // com modelo, a empresa passa a usar as contas de encerramento dele; sem (plano importado), as configuradas continuam valendo
            await using (var cmd = new NpgsqlCommand("""
                UPDATE core.empresa
                   SET plano_modelo = @m,
                       conta_apuracao = COALESCE(@ap, conta_apuracao), conta_lucros = COALESCE(@lu, conta_lucros),
                       conta_prejuizos = COALESCE(@pr, conta_prejuizos)
                 WHERE id = @e
                """, con, tx).Com("e", empresaId).ComTexto("m", modelo?.Id).ComTexto("ap", modelo?.Contas.Apuracao)
                             .ComTexto("lu", modelo?.Contas.LucrosAcumulados).ComTexto("pr", modelo?.Contas.PrejuizosAcumulados))
                await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return 0;
        });

    public async Task<Application.Cadastros.ConfiguracaoContabil> ObterConfiguracaoAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT e.plano_modelo, e.mascara_plano, e.inicio_escrituracao, e.usa_centro_custo, e.grande_porte,
                   e.conta_apuracao, e.conta_lucros, e.conta_prejuizos, r.apuracao_irpj, COALESCE(r.regime_caixa, FALSE),
                   e.mascara_centro_custo, e.exige_documento, e.data_bloqueio, e.usa_estorno, e.usa_gerencial
              FROM core.empresa e
              LEFT JOIN LATERAL (SELECT apuracao_irpj, regime_caixa FROM core.empresa_regime
                                  WHERE empresa_id = e.id ORDER BY vigencia_inicio DESC LIMIT 1) r ON TRUE
             WHERE e.id = @e
            """).Com("e", empresaId);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct)) return new();
        string? T(int i) => rd.IsDBNull(i) ? null : rd.GetString(i);
        return new Application.Cadastros.ConfiguracaoContabil(T(0), T(1), rd.IsDBNull(2) ? null : rd.GetFieldValue<DateOnly>(2), rd.GetBoolean(3),
                                                              rd.GetBoolean(4), T(5), T(6), T(7), T(8), rd.GetBoolean(9), T(10), rd.GetBoolean(11),
                                                              rd.IsDBNull(12) ? null : rd.GetFieldValue<DateOnly>(12), rd.GetBoolean(13),
                                                              rd.GetBoolean(14));
    }

    public Task SalvarConfiguracaoAsync(long empresaId, Application.Cadastros.ConfiguracaoContabil c, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await Mapeamentos.DefinirUsuarioAsync(con, tx, null, ct);
            // apuração do IRPJ e regime de caixa pertencem ao regime vigente (o mais recente)
            await using (var cmd = new NpgsqlCommand("""
                UPDATE core.empresa
                   SET mascara_plano = @mascara, inicio_escrituracao = @inicio, usa_centro_custo = @cc, grande_porte = @porte,
                       conta_apuracao = @ap, conta_lucros = @lu, conta_prejuizos = @pr,
                       exige_documento = @exdoc, data_bloqueio = @bloq, usa_estorno = @estorno, usa_gerencial = @gerencial
                 WHERE id = @e;
                UPDATE core.empresa_regime SET apuracao_irpj = @irpj, regime_caixa = @caixa
                 WHERE id = (SELECT id FROM core.empresa_regime WHERE empresa_id = @e ORDER BY vigencia_inicio DESC LIMIT 1);
                """, con, tx)
                .Com("e", empresaId).ComTexto("mascara", c.MascaraPlano).Com("inicio", NpgsqlTypes.NpgsqlDbType.Date, c.InicioEscrituracao)
                .Com("cc", c.UsaCentroCusto).Com("porte", c.GrandePorte).ComTexto("ap", c.ContaApuracao).ComTexto("lu", c.ContaLucros)
                .ComTexto("pr", c.ContaPrejuizos).ComTexto("irpj", c.ApuracaoIrpj).Com("caixa", c.RegimeCaixa)
                .Com("exdoc", c.ExigeDocumento).Com("bloq", NpgsqlTypes.NpgsqlDbType.Date, c.DataBloqueio).Com("estorno", c.UsaEstorno).Com("gerencial", c.UsaGerencial))
                await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return 0;
        });

    public Task RecodificarAsync(long empresaId, IReadOnlyDictionary<long, string> codigos, long usuarioId, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await Mapeamentos.DefinirUsuarioAsync(con, tx, usuarioId, ct);
            var ids = codigos.Keys.ToArray();
            // Em duas etapas para não colidir com códigos que ainda serão trocados (1.1.1 ↔ 1.1.01).
            await using (var cmd = new NpgsqlCommand(
                "UPDATE contabil.plano_contas SET codigo = '#' || id WHERE empresa_id = @e AND id = ANY(@ids)", con, tx)
                .Com("e", empresaId).Com("ids", ids))
                await cmd.ExecuteNonQueryAsync(ct);
            await using (var cmd = new NpgsqlCommand("""
                UPDATE contabil.plano_contas c SET codigo = n.codigo
                  FROM unnest(@ids, @cods) AS n(id, codigo) WHERE c.id = n.id AND c.empresa_id = @e
                """, con, tx).Com("e", empresaId).Com("ids", ids).Com("cods", ids.Select(i => codigos[i]).ToArray()))
                await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return 0;
        });

    public async Task<IReadOnlyList<CentroCustoDetalhe>> ListarCentrosAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT c.id, c.codigo, c.descricao, c.centro_pai_id, c.ativo,
                   EXISTS (SELECT 1 FROM contabil.partida p WHERE p.centro_custo_id = c.id),
                   c.tipo = 'A', c.nivel, EXISTS (SELECT 1 FROM contabil.centro_custo f WHERE f.centro_pai_id = c.id), c.frota_id
              FROM contabil.centro_custo c WHERE c.empresa_id = @e ORDER BY c.codigo
            """).Com("e", empresaId);
        var res = new List<CentroCustoDetalhe>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new CentroCustoDetalhe(r.GetInt64(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetInt64(3),
                                           r.GetBoolean(4), r.GetBoolean(5), r.GetBoolean(6), r.GetInt16(7), r.GetBoolean(8), r.IsDBNull(9) ? null : r.GetInt64(9)));
        return res;
    }

    public Task<long> IncluirCentroAsync(long empresaId, string codigo, string descricao, long? paiId, DateOnly dataInclusao, short nivel, bool analitico,
                                         long? paiParaSintetico, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            if (paiParaSintetico is { } pai)
                await using (var cmd = new NpgsqlCommand("UPDATE contabil.centro_custo SET tipo = 'S' WHERE id = @id", con, tx).Com("id", pai))
                    await cmd.ExecuteNonQueryAsync(ct);
            long id;
            await using (var cmd = new NpgsqlCommand("""
                INSERT INTO contabil.centro_custo (empresa_id, codigo, descricao, centro_pai_id, data_inclusao, nivel, tipo)
                VALUES (@e, @c, @d, @p, @i, @n, @t) RETURNING id
                """, con, tx).Com("e", empresaId).Com("c", codigo).Com("d", descricao).Com("p", NpgsqlTypes.NpgsqlDbType.Bigint, paiId)
                            .Com("i", dataInclusao).Com("n", nivel).Com("t", analitico ? "A" : "S"))
                id = (long)(await cmd.ExecuteScalarAsync(ct))!;
            await tx.CommitAsync(ct);
            return id;
        });

    public Task AlterarCentroAsync(long centroId, string descricao, bool ativo, bool analitico, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var cmd = ds.CreateCommand("UPDATE contabil.centro_custo SET descricao = @d, ativo = @a, tipo = @t WHERE id = @id")
                                    .Com("d", descricao).Com("a", ativo).Com("t", analitico ? "A" : "S").Com("id", centroId);
            return await cmd.ExecuteNonQueryAsync(ct);
        });

    public async Task<IReadOnlyList<FrotaDoCentro>> ListarFrotasAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT id, codigo || ' – ' || descricao FROM frota.frota WHERE empresa_id = @e AND ativa ORDER BY codigo").Com("e", empresaId);
        var res = new List<FrotaDoCentro>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(new FrotaDoCentro(r.GetInt64(0), r.GetString(1)));
        return res;
    }

    public Task DefinirFrotaCentroAsync(long centroId, long? frotaId, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var cmd = ds.CreateCommand("UPDATE contabil.centro_custo SET frota_id = @f WHERE id = @id").Com("f", NpgsqlTypes.NpgsqlDbType.Bigint, frotaId).Com("id", centroId);
            return await cmd.ExecuteNonQueryAsync(ct);
        });

    public async Task<string?> ObterMascaraCentrosAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT mascara_centro_custo FROM core.empresa WHERE id = @e").Com("e", empresaId);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    public Task DefinirMascaraCentrosAsync(long empresaId, string? mascara, IReadOnlyDictionary<long, string>? codigos, long usuarioId, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await Mapeamentos.DefinirUsuarioAsync(con, tx, usuarioId, ct);
            await using (var cmd = new NpgsqlCommand("UPDATE core.empresa SET mascara_centro_custo = @m WHERE id = @e", con, tx)
                .Com("e", empresaId).ComTexto("m", mascara))
                await cmd.ExecuteNonQueryAsync(ct);
            if (codigos is { Count: > 0 })
            {
                var ids = codigos.Keys.ToArray();
                // Em duas etapas para não colidir com códigos que ainda serão trocados (1.1 ↔ 1.01).
                await using (var cmd = new NpgsqlCommand(
                    "UPDATE contabil.centro_custo SET codigo = '#' || id WHERE empresa_id = @e AND id = ANY(@ids)", con, tx)
                    .Com("e", empresaId).Com("ids", ids))
                    await cmd.ExecuteNonQueryAsync(ct);
                await using (var cmd = new NpgsqlCommand("""
                    UPDATE contabil.centro_custo c SET codigo = n.codigo, nivel = array_length(string_to_array(n.codigo, '.'), 1)
                      FROM unnest(@ids, @cods) AS n(id, codigo) WHERE c.id = n.id AND c.empresa_id = @e
                    """, con, tx).Com("e", empresaId).Com("ids", ids).Com("cods", ids.Select(i => codigos[i]).ToArray()))
                    await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            return 0;
        });

    public Task ExcluirCentroAsync(long centroId, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var cmd = ds.CreateCommand("DELETE FROM contabil.centro_custo WHERE id = @id").Com("id", centroId);
            return await cmd.ExecuteNonQueryAsync(ct);
        });
}
