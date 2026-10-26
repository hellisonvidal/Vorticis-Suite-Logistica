using Npgsql;
using NpgsqlTypes;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;
using Vorticis.Fiscal.Duplicatas;

namespace Vorticis.Fiscal.Postgres;

public sealed class RepositorioDuplicatas(NpgsqlDataSource ds) : IRepositorioDuplicatas
{
    private const string Colunas = """
        d.id, d.documento_id, doc.numero, d.participante_id, p.codigo, p.nome, d.tipo, d.parcela, d.numero_documento, d.emissao, d.vencimento, d.valor,
        d.juros, d.multa, d.desconto, d.saldo, d.situacao, d.observacao, d.origem
        """;

    private const string Origem = """
        FROM fiscal.duplicata d LEFT JOIN fiscal.documento doc ON doc.id = d.documento_id LEFT JOIN core.participante p ON p.id = d.participante_id
        """;

    private static Duplicata Ler(NpgsqlDataReader r) =>
        new(r.GetInt64(0), r.LongOuNulo(1), r.Texto(2), r.LongOuNulo(3), r.Texto(4), r.Texto(5), DuplicatasExtensions.TipoDe(r.GetString(6)), r.GetString(7), r.GetString(8),
            r.GetFieldValue<DateOnly>(9), r.GetFieldValue<DateOnly>(10), r.GetDecimal(11), r.GetDecimal(12), r.GetDecimal(13), r.GetDecimal(14), r.GetDecimal(15),
            DuplicatasExtensions.SituacaoDe(r.GetString(16)), r.Texto(17), r.GetString(18));

    public async Task<IReadOnlyList<Duplicata>> ListarAsync(long empresaId, FiltroDuplicatas f, CancellationToken ct)
    {
        var where = new List<string> { "d.empresa_id = @e" };
        await using var cmd = ds.CreateCommand("").Com("e", empresaId);
        if (f.Tipo is { } t) { where.Add("d.tipo = @tipo"); cmd.Com("tipo", t.Codigo()); }
        if (f.VencimentoInicio is { } vi) { where.Add("d.vencimento >= @vi"); cmd.Com("vi", vi); }
        if (f.VencimentoFim is { } vf) { where.Add("d.vencimento <= @vf"); cmd.Com("vf", vf); }
        if (f.ParticipanteId is { } pid) { where.Add("d.participante_id = @pid"); cmd.Com("pid", pid); }
        if (!string.IsNullOrWhiteSpace(f.Participante)) { where.Add("(p.nome ILIKE @pn OR p.codigo = @pc OR p.cpf_cnpj = @pc)"); cmd.Com("pn", $"%{f.Participante.Trim()}%").Com("pc", f.Participante.Trim()); }
        if (f.Situacao is { } s) { where.Add("d.situacao = @sit"); cmd.Com("sit", s.Codigo()); }
        if (f.SomenteAbertas) where.Add("d.situacao IN ('ABERTA', 'PARCIAL')");
        if (f.SomenteVencidas) { where.Add("d.situacao IN ('ABERTA', 'PARCIAL') AND d.vencimento < @hoje"); cmd.Com("hoje", f.Hoje ?? DateOnly.FromDateTime(DateTime.Today)); }
        if (f.DocumentoId is { } did) { where.Add("d.documento_id = @did"); cmd.Com("did", did); }
        cmd.CommandText = $"SELECT {Colunas} {Origem} WHERE {string.Join(" AND ", where)} ORDER BY d.vencimento, d.id LIMIT {Math.Max(1, f.Limite)}";
        var res = new List<Duplicata>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(Ler(r));
        return res;
    }

    public async Task<Duplicata?> ObterAsync(long empresaId, long id, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand($"SELECT {Colunas} {Origem} WHERE d.id = @i AND d.empresa_id = @e").Com("i", id).Com("e", empresaId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Ler(r) : null;
    }

    private const string ColunasBaixa = "b.id, b.duplicata_id, b.data, b.valor_principal, b.juros, b.multa, b.desconto, b.conta_disponivel, b.lancamento_id, b.partida_extrato_id, b.conciliacao_id";

    private static BaixaDuplicata LerBaixa(NpgsqlDataReader r) =>
        new(r.GetInt64(0), r.GetInt64(1), r.GetFieldValue<DateOnly>(2), r.GetDecimal(3), r.GetDecimal(4), r.GetDecimal(5), r.GetDecimal(6), r.Texto(7), r.LongOuNulo(8),
            r.LongOuNulo(9), r.LongOuNulo(10));

    public async Task<IReadOnlyList<BaixaDuplicata>> ListarBaixasAsync(long empresaId, IReadOnlyCollection<long> duplicataIds, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand($"""
            SELECT {ColunasBaixa} FROM fiscal.duplicata_baixa b JOIN fiscal.duplicata d ON d.id = b.duplicata_id
             WHERE d.empresa_id = @e AND b.duplicata_id = ANY(@d) ORDER BY b.data, b.id
            """).Com("e", empresaId).Com("d", duplicataIds.ToArray());
        var res = new List<BaixaDuplicata>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(LerBaixa(r));
        return res;
    }

    public async Task<BaixaDuplicata?> ObterBaixaAsync(long empresaId, long baixaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand($"""
            SELECT {ColunasBaixa} FROM fiscal.duplicata_baixa b JOIN fiscal.duplicata d ON d.id = b.duplicata_id WHERE b.id = @b AND d.empresa_id = @e
            """).Com("b", baixaId).Com("e", empresaId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? LerBaixa(r) : null;
    }

    public Task<long> GravarAsync(long empresaId, DadosDuplicata d, long usuarioId, CancellationToken ct)
        => Sql.TraduzirErrosAsync(async () =>
        {
            await using var cmd = d.Id == 0
                ? ds.CreateCommand("""
                    INSERT INTO fiscal.duplicata (empresa_id, participante_id, tipo, parcela, numero_documento, emissao, vencimento, valor, saldo, observacao, origem, criado_por)
                    VALUES (@e, @part, @tipo, @parc, @num, @emi, @venc, @valor, @valor, @obs, 'MANUAL', @usu) RETURNING id
                    """).Com("tipo", d.Tipo.Codigo()).Com("usu", usuarioId)
                : ds.CreateCommand("""
                    UPDATE fiscal.duplicata SET participante_id = @part, parcela = @parc, numero_documento = @num, emissao = @emi, vencimento = @venc, valor = @valor,
                           observacao = @obs, atualizado_em = now()
                     WHERE id = @id AND empresa_id = @e RETURNING id
                    """).Com("id", d.Id);
            cmd.Com("e", empresaId).Com("part", NpgsqlDbType.Bigint, d.ParticipanteId == 0 ? null : d.ParticipanteId).Com("parc", d.Parcela).Com("num", d.NumeroDocumento)
               .Com("emi", d.Emissao).Com("venc", d.Vencimento).Com("valor", d.Valor).ComTexto("obs", d.Observacao);
            return await cmd.ExecuteScalarAsync(ct) as long? ?? throw new ViolacaoRegraException("Duplicata não encontrada.");
        });

    public async Task ExcluirAsync(long empresaId, long id, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("DELETE FROM fiscal.duplicata WHERE id = @i AND empresa_id = @e").Com("i", id).Com("e", empresaId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public Task CancelarAsync(long empresaId, long id, CancellationToken ct)
        => Sql.TraduzirErrosAsync(async () =>
        {
            await using var cmd = ds.CreateCommand("UPDATE fiscal.duplicata SET situacao = 'CANCELADA', atualizado_em = now() WHERE id = @i AND empresa_id = @e").Com("i", id).Com("e", empresaId);
            await cmd.ExecuteNonQueryAsync(ct);
            return 0;
        });

    public Task<long> InserirBaixaAsync(long empresaId, NovaBaixa b, long? lancamentoId, long? partidaExtratoId, long usuarioId, CancellationToken ct)
        => Sql.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await Sql.DefinirUsuarioAsync(con, tx, usuarioId, ct);
            decimal saldo;
            string situacao;
            await using (var q = new NpgsqlCommand("SELECT saldo, situacao FROM fiscal.duplicata WHERE id = @d AND empresa_id = @e FOR UPDATE", con, tx).Com("d", b.DuplicataId).Com("e", empresaId))
            await using (var r = await q.ExecuteReaderAsync(ct))
            {
                if (!await r.ReadAsync(ct)) throw new ViolacaoRegraException("Duplicata não encontrada.");
                (saldo, situacao) = (r.GetDecimal(0), r.GetString(1));
            }
            if (situacao is "CANCELADA" or "LIQUIDADA") throw new ViolacaoRegraException($"Duplicata {(situacao == "LIQUIDADA" ? "já liquidada" : "cancelada")}: não há o que baixar.");
            if (b.Principal > saldo) throw new ViolacaoRegraException($"O valor da baixa ({Formato.Moeda(b.Principal)}) é maior que o saldo ({Formato.Moeda(saldo)}).");
            await using var ins = new NpgsqlCommand("""
                INSERT INTO fiscal.duplicata_baixa (duplicata_id, data, valor_principal, juros, multa, desconto, conta_disponivel, lancamento_id, partida_extrato_id, criado_por)
                VALUES (@d, @data, @p, @j, @m, @ds, @conta, @l, @pe, @u) RETURNING id
                """, con, tx).Com("d", b.DuplicataId).Com("data", b.Data).Com("p", b.Principal).Com("j", b.Juros).Com("m", b.Multa).Com("ds", b.Desconto)
                             .ComTexto("conta", b.ContaDisponivel).Com("l", NpgsqlDbType.Bigint, lancamentoId).Com("pe", NpgsqlDbType.Bigint, partidaExtratoId).Com("u", usuarioId);
            var id = (long)(await ins.ExecuteScalarAsync(ct))!;
            await tx.CommitAsync(ct);
            return id;
        });

    public async Task DefinirLancamentoBaixaAsync(long empresaId, long baixaId, long? lancamentoId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("UPDATE fiscal.duplicata_baixa SET lancamento_id = @l WHERE id = @b AND duplicata_id IN (SELECT id FROM fiscal.duplicata WHERE empresa_id = @e)")
            .Com("l", NpgsqlDbType.Bigint, lancamentoId).Com("b", baixaId).Com("e", empresaId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DefinirConciliacaoAsync(long baixaId, long conciliacaoId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("UPDATE fiscal.duplicata_baixa SET conciliacao_id = @c WHERE id = @b").Com("c", conciliacaoId).Com("b", baixaId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ExcluirBaixaAsync(long empresaId, long baixaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            DELETE FROM fiscal.duplicata_baixa b USING fiscal.duplicata d WHERE b.id = @b AND d.id = b.duplicata_id AND d.empresa_id = @e
            """).Com("b", baixaId).Com("e", empresaId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ------------------------------------------------------------------ configuração

    internal const string ColunasConfiguracao = """
        duplicata_a_vista, duplicata_a_vista_dias, conta_fornecedores, conta_clientes, conta_juros_pagos, conta_multa_paga, conta_desconto_obtido,
        conta_juros_recebidos, conta_multa_recebida, conta_desconto_concedido, historico_pagamento, historico_recebimento
        """;

    internal static ConfiguracaoDuplicatas LerConfiguracao(NpgsqlDataReader r) =>
        new(r.GetBoolean(0), r.GetInt16(1), r.Texto(2), r.Texto(3), r.Texto(4), r.Texto(5), r.Texto(6), r.Texto(7), r.Texto(8), r.Texto(9), r.GetString(10), r.GetString(11));

    public async Task<ConfiguracaoDuplicatas> ObterConfiguracaoAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand($"SELECT {ColunasConfiguracao} FROM fiscal.configuracao WHERE empresa_id = @e").Com("e", empresaId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? LerConfiguracao(r) : new ConfiguracaoDuplicatas();
    }

    public Task GravarConfiguracaoAsync(long empresaId, ConfiguracaoDuplicatas c, CancellationToken ct)
        => Sql.TraduzirErrosAsync(async () =>
        {
            await using var cmd = ds.CreateCommand("""
                INSERT INTO fiscal.configuracao (empresa_id, duplicata_a_vista, duplicata_a_vista_dias, conta_fornecedores, conta_clientes, conta_juros_pagos, conta_multa_paga,
                    conta_desconto_obtido, conta_juros_recebidos, conta_multa_recebida, conta_desconto_concedido, historico_pagamento, historico_recebimento)
                VALUES (@e, @av, @dias, @cf, @cc, @jp, @mp, @dob, @jr, @mr, @dc, @hp, @hr)
                ON CONFLICT (empresa_id) DO UPDATE SET duplicata_a_vista = @av, duplicata_a_vista_dias = @dias, conta_fornecedores = @cf, conta_clientes = @cc,
                    conta_juros_pagos = @jp, conta_multa_paga = @mp, conta_desconto_obtido = @dob, conta_juros_recebidos = @jr, conta_multa_recebida = @mr,
                    conta_desconto_concedido = @dc, historico_pagamento = @hp, historico_recebimento = @hr, atualizado_em = now()
                """).Com("e", empresaId).Com("av", c.GerarAVista).Com("dias", (short)c.AVistaDias).ComTexto("cf", c.ContaFornecedores).ComTexto("cc", c.ContaClientes)
                    .ComTexto("jp", c.ContaJurosPagos).ComTexto("mp", c.ContaMultaPaga).ComTexto("dob", c.ContaDescontoObtido).ComTexto("jr", c.ContaJurosRecebidos)
                    .ComTexto("mr", c.ContaMultaRecebida).ComTexto("dc", c.ContaDescontoConcedido).Com("hp", c.HistoricoPagamento).Com("hr", c.HistoricoRecebimento);
            await cmd.ExecuteNonQueryAsync(ct);
            return 0;
        });

    /// <summary>Dos códigos informados, os de contas analíticas e ativas da empresa.</summary>
    public async Task<IReadOnlySet<string>> ContasAnaliticasAsync(long empresaId, IEnumerable<string> codigos, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT codigo FROM contabil.plano_contas WHERE empresa_id = @e AND tipo = 'A' AND ativa AND codigo = ANY(@c)")
            .Com("e", empresaId).Com("c", codigos.ToArray());
        var res = new HashSet<string>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(r.GetString(0));
        return res;
    }

    public async Task<(long Id, string Codigo, string Nome)?> ObterParticipanteAsync(long empresaId, long participanteId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT id, codigo, nome FROM core.participante WHERE id = @p AND empresa_id = @e").Com("p", participanteId).Com("e", empresaId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? (r.GetInt64(0), r.GetString(1).Trim(), r.GetString(2)) : null;
    }

    public async Task<IReadOnlySet<long>> DocumentosComDuplicataAsync(long empresaId, IEnumerable<long> documentoIds, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT DISTINCT documento_id FROM fiscal.duplicata WHERE empresa_id = @e AND documento_id = ANY(@d) AND situacao <> 'CANCELADA'")
            .Com("e", empresaId).Com("d", documentoIds.ToArray());
        var res = new HashSet<long>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(r.GetInt64(0));
        return res;
    }

    public async Task<IReadOnlySet<long>> DocumentosComBaixaAsync(long empresaId, IEnumerable<long> documentoIds, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT DISTINCT d.documento_id FROM fiscal.duplicata d JOIN fiscal.duplicata_baixa b ON b.duplicata_id = d.id
             WHERE d.empresa_id = @e AND d.documento_id = ANY(@d)
            """).Com("e", empresaId).Com("d", documentoIds.ToArray());
        var res = new HashSet<long>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(r.GetInt64(0));
        return res;
    }

    // ------------------------------------------------------------------ extrato bancário (OFX importado no contábil)

    private const string SelectMovimento = """
        SELECT pb.id, l.id, l.data_lancamento, pb.ind_dc::text, pb.valor, pb.historico, pb.num_arquivo, cb.codigo, pc.id, cc.codigo,
               EXISTS (SELECT 1 FROM contabil.conciliacao_partida x WHERE x.partida_id = pc.id)
          FROM contabil.lancamento l
          JOIN contabil.partida pb ON pb.lancamento_id = l.id
          JOIN contabil.plano_contas cb ON cb.id = pb.conta_id
          JOIN contabil.partida pc ON pc.lancamento_id = l.id AND pc.id <> pb.id
          JOIN contabil.plano_contas cc ON cc.id = pc.conta_id
         WHERE l.empresa_id = @e AND l.status = 'EFETIVADO'
           AND EXISTS (SELECT 1 FROM contabil.lancamento_importado li WHERE li.lancamento_id = l.id AND li.chave LIKE 'OFX:%')
           AND (SELECT count(*) FROM contabil.partida z WHERE z.lancamento_id = l.id) = 2
           AND NOT EXISTS (SELECT 1 FROM fiscal.duplicata_baixa b WHERE b.partida_extrato_id = pb.id)
        """;

    private static MovimentoExtrato LerMovimento(NpgsqlDataReader r) =>
        new(r.GetInt64(0), r.GetInt64(1), r.GetFieldValue<DateOnly>(2), r.GetString(3) == "D" ? IndicadorDC.D : IndicadorDC.C, r.GetDecimal(4), r.GetString(5), r.Texto(6),
            r.GetString(7), r.GetInt64(8), r.GetString(9), r.GetBoolean(10));

    /// <summary>
    /// Movimentos do extrato OFX já importado cuja partida está na conta do banco informada (código completo ou reduzido), ainda sem duplicata baixada.
    /// </summary>
    public async Task<IReadOnlyList<MovimentoExtrato>> ListarMovimentosExtratoAsync(long empresaId, string contaBanco, DateOnly inicio, DateOnly fim, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand(SelectMovimento + """
               AND l.data_lancamento BETWEEN @i AND @f AND (cb.codigo = @conta OR cb.codigo_reduzido::text = @conta)
             ORDER BY l.data_lancamento, pb.id
            """).Com("e", empresaId).Com("i", inicio).Com("f", fim).Com("conta", contaBanco.Trim());
        var res = new List<MovimentoExtrato>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(LerMovimento(r));
        return res;
    }

    public async Task<MovimentoExtrato?> ObterMovimentoAsync(long empresaId, long partidaBancoId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand(SelectMovimento + " AND pb.id = @p").Com("e", empresaId).Com("p", partidaBancoId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? LerMovimento(r) : null;
    }

    /// <summary>Partida do lançamento na conta (código) informada.</summary>
    public async Task<long?> PartidaDaContaAsync(long lancamentoId, string contaCodigo, IndicadorDC dc, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT p.id FROM contabil.partida p JOIN contabil.plano_contas c ON c.id = p.conta_id
             WHERE p.lancamento_id = @l AND c.codigo = @c AND p.ind_dc = @dc::contabil.indicador_dc ORDER BY p.sequencia LIMIT 1
            """).Com("l", lancamentoId).Com("c", contaCodigo).Com("dc", dc.ToString());
        return await cmd.ExecuteScalarAsync(ct) as long?;
    }

    // ------------------------------------------------------------------ geração a partir das notas (na transação da nota)

    /// <summary>Opções de geração à vista da empresa, lidas na transação da nota.</summary>
    internal static async Task<ConfiguracaoDuplicatas> ConfiguracaoAsync(NpgsqlConnection con, NpgsqlTransaction tx, long empresaId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand($"SELECT {ColunasConfiguracao} FROM fiscal.configuracao WHERE empresa_id = @e", con, tx).Com("e", empresaId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? LerConfiguracao(r) : new ConfiguracaoDuplicatas();
    }

    /// <summary>
    /// Parcelas da nota: as informadas (cobrança do XML ou digitadas) ou, se não houver, uma parcela à vista (vencimento = emissão + dias da configuração),
    /// quando a configuração permite. Sem valor, nada.
    /// </summary>
    internal static IReadOnlyList<ParcelaDuplicata> ParcelasOuAVista(IReadOnlyList<ParcelaDuplicata>? informadas, ConfiguracaoDuplicatas config, decimal total, DateOnly emissao)
    {
        if (informadas is { Count: > 0 }) return informadas;
        if (!config.GerarAVista || total <= 0) return [];
        return [new ParcelaDuplicata("001", emissao.AddDays(config.AVistaDias), total)];
    }

    /// <summary>Grava as parcelas da nota (nota de entrada = a pagar; saída = a receber). Números repetidos ganham sufixo para manter a unicidade.</summary>
    internal static async Task InserirDoDocumentoAsync(NpgsqlConnection con, NpgsqlTransaction tx, long empresaId, long documentoId, long? participanteId, bool entrada,
                                                       string numeroDocumento, DateOnly emissao, IReadOnlyList<ParcelaDuplicata> parcelas, long? usuarioId, CancellationToken ct)
    {
        var usados = new HashSet<string>();
        var n = 0;
        foreach (var p in parcelas)
        {
            n++;
            if (p.Valor <= 0) continue;
            var numero = string.IsNullOrWhiteSpace(p.Numero) ? n.ToString("000") : p.Numero.Trim();
            if (numero.Length > 20) numero = numero[..20];
            for (var k = 2; !usados.Add(numero); k++) numero = $"{(string.IsNullOrWhiteSpace(p.Numero) ? n.ToString("000") : p.Numero.Trim())}-{k}";
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO fiscal.duplicata (empresa_id, documento_id, participante_id, tipo, parcela, numero_documento, emissao, vencimento, valor, saldo, origem, criado_por)
                VALUES (@e, @d, @part, @tipo, @parc, @num, @emi, @venc, @valor, @valor, 'NOTA', @u)
                """, con, tx).Com("e", empresaId).Com("d", documentoId).Com("part", NpgsqlDbType.Bigint, participanteId)
                             .Com("tipo", entrada ? "PAGAR" : "RECEBER").Com("parc", numero).Com("num", numeroDocumento.Length > 30 ? numeroDocumento[..30] : numeroDocumento)
                             .Com("emi", emissao).Com("venc", p.Vencimento < emissao ? emissao : p.Vencimento).Com("valor", p.Valor).Com("u", NpgsqlDbType.Bigint, usuarioId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
