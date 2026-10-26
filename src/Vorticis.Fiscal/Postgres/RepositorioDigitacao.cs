using Npgsql;
using NpgsqlTypes;
using Vorticis.Domain.Comum;
using Vorticis.Fiscal.Documentos;
using Vorticis.Fiscal.Produtos;

namespace Vorticis.Fiscal.Postgres;

public sealed class RepositorioDigitacao(NpgsqlDataSource ds) : IRepositorioDigitacao
{
    public async Task<ConfiguracaoFiscal> ObterConfiguracaoAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT COALESCE((SELECT c.usa_centro_custo FROM fiscal.configuracao c WHERE c.empresa_id = e.id), FALSE), e.usa_centro_custo
              FROM core.empresa e WHERE e.id = @e
            """).Com("e", empresaId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new ConfiguracaoFiscal(r.GetBoolean(0), r.GetBoolean(1)) : new ConfiguracaoFiscal(false, false);
    }

    public async Task GravarConfiguracaoAsync(long empresaId, bool usaCentroCusto, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            INSERT INTO fiscal.configuracao (empresa_id, usa_centro_custo) VALUES (@e, @u)
            ON CONFLICT (empresa_id) DO UPDATE SET usa_centro_custo = @u, atualizado_em = now()
            """).Com("e", empresaId).Com("u", usaCentroCusto);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<ParticipanteNota?> ObterParticipanteAsync(long empresaId, long participanteId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT id, nome, cpf_cnpj, uf FROM core.participante WHERE id = @p AND empresa_id = @e").Com("p", participanteId).Com("e", empresaId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new ParticipanteNota(r.GetInt64(0), r.GetString(1), r.Texto(2), r.Texto(3)) : null;
    }

    public async Task<IReadOnlyDictionary<long, Produto>> ObterProdutosAsync(long empresaId, IEnumerable<long> ids, CancellationToken ct)
    {
        var lista = ids.ToArray();
        var res = new Dictionary<long, Produto>();
        if (lista.Length == 0) return res;
        await using var cmd = ds.CreateCommand("""
            SELECT id, codigo, descricao, unidade, ncm, cest, gtin, origem, tipo_item FROM fiscal.produto WHERE empresa_id = @e AND id = ANY(@i)
            """).Com("e", empresaId).Com("i", lista);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res[r.GetInt64(0)] = new Produto(r.GetInt64(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.Texto(4), r.Texto(5), r.Texto(6), r.GetString(7).Trim(),
                r.GetString(8).Trim(), null, null, null, null, 0, 0, 0, 0);
        return res;
    }

    public async Task<IReadOnlySet<long>> CentrosValidosAsync(long empresaId, IEnumerable<long> ids, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT id FROM contabil.centro_custo WHERE empresa_id = @e AND ativo AND id = ANY(@i)").Com("e", empresaId).Com("i", ids.ToArray());
        var res = new HashSet<long>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(r.GetInt64(0));
        return res;
    }

    public Task<long> GravarAsync(long empresaId, DocumentoDigitado d, EmpresaFiscal empresa, ParticipanteNota part, long usuarioId, CancellationToken ct)
        => Sql.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await Sql.DefinirUsuarioAsync(con, tx, usuarioId, ct);
            await using (var trava = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext('fiscal_importacao'), @e::int)", con, tx).Com("e", empresaId))
                await trava.ExecuteNonQueryAsync(ct);

            if (d.Id != 0)
            {
                string origem;
                bool contabilizado;
                await using (var q = new NpgsqlCommand("""
                    SELECT d.origem, COALESCE(l.status = 'EFETIVADO', FALSE)
                      FROM fiscal.documento d LEFT JOIN contabil.lancamento l ON l.id = d.lancamento_id
                     WHERE d.id = @d AND d.empresa_id = @e FOR UPDATE OF d
                    """, con, tx).Com("d", d.Id).Com("e", empresaId))
                await using (var r = await q.ExecuteReaderAsync(ct))
                {
                    if (!await r.ReadAsync(ct)) throw new ViolacaoRegraException("Documento não encontrado.");
                    (origem, contabilizado) = (r.GetString(0), r.GetBoolean(1));
                }
                if (origem != "DIGITADO") throw new ViolacaoRegraException("Somente notas digitadas podem ser alteradas; documentos importados de XML não são editáveis.");
                if (contabilizado) throw new ViolacaoRegraException("A nota está contabilizada: desfaça a contabilização para alterá-la.");
            }

            await using (var dup = new NpgsqlCommand("""
                SELECT 1 FROM fiscal.documento WHERE empresa_id = @e AND tipo_operacao = @op AND modelo = @mod AND serie = @serie AND numero = @num
                   AND COALESCE(participante_id, 0) = @part AND origem = 'DIGITADO' AND id <> @id
                """, con, tx).Com("e", empresaId).Com("op", d.Operacao.Codigo()).Com("mod", d.Modelo.Codigo()).Com("serie", d.Serie).Com("num", d.Numero)
                             .Com("part", part.Id).Com("id", d.Id))
                if (await dup.ExecuteScalarAsync(ct) is not null)
                    throw new ViolacaoRegraException($"Já existe a nota {d.Modelo.Rotulo()} série {d.Serie} número {d.Numero} deste participante.");
            if (d.Chave is not null)
                await using (var dupChave = new NpgsqlCommand("SELECT 1 FROM fiscal.documento WHERE empresa_id = @e AND chave_acesso = @c AND id <> @id", con, tx)
                                 .Com("e", empresaId).Com("c", d.Chave).Com("id", d.Id))
                    if (await dupChave.ExecuteScalarAsync(ct) is not null)
                        throw new ViolacaoRegraException("Já existe um documento com esta chave de acesso.");

            // A outra parte é o participante; a empresa é o destinatário (entrada) ou o emitente (saída).
            var doc = part.CpfCnpj is { } pd ? new string(pd.Where(char.IsAsciiDigit).ToArray()) : null;
            var entrada = d.Operacao == TipoOperacao.Entrada;
            var (emiDoc, emiNome, emiUf, desDoc, desNome, desUf) = entrada
                ? (doc, part.Nome, part.Uf, empresa.Cnpj, empresa.RazaoSocial, (string?)null)
                : (empresa.Cnpj, empresa.RazaoSocial, (string?)null, doc, part.Nome, part.Uf);
            var t = d.Totais;

            long id = d.Id;
            var colunas = d.Id == 0
                ? """
                  INSERT INTO fiscal.documento (empresa_id, modelo, chave_acesso, tipo_operacao, numero, serie, data_emissao, data_entrada_saida, natureza_operacao,
                      situacao, emitente_documento, emitente_nome, emitente_uf, destinatario_documento, destinatario_nome, destinatario_uf, participante_id,
                      valor_produtos, valor_desconto, valor_frete, valor_seguro, valor_outras, valor_total, vl_bc_icms, vl_icms, vl_bc_icms_st, vl_icms_st,
                      vl_ipi, vl_pis, vl_cofins, vl_iss, iss_retido, vl_ret_pis, vl_ret_cofins, vl_ret_csll, vl_irrf, vl_ret_prev, origem, centro_custo_id, importado_por, observacoes, conta_debito, conta_credito,
                      forma_pagamento_id)
                  VALUES (@e, @mod, @chave, @op, @num, @serie, @emi, @es, @nat, 'AUTORIZADA', @ed, @en, @eu, @dd, @dn, @du, @part,
                      @vprod, @vdesc, @vfrete, @vseg, @voutras, @vtotal, @bcicms, @icms, @bcst, @st, @ipi, @pis, @cofins, @iss, @issret,
                      @rpis, @rcofins, @rcsll, @irrf, @rprev, 'DIGITADO', @cc, @usu, @obs, @cdeb, @ccre,
                      (SELECT id FROM core.forma_pagamento WHERE id = @fp AND empresa_id = @e))
                  RETURNING id
                  """
                : """
                  UPDATE fiscal.documento SET modelo = @mod, chave_acesso = @chave, tipo_operacao = @op, numero = @num, serie = @serie, data_emissao = @emi,
                      data_entrada_saida = @es, natureza_operacao = @nat, emitente_documento = @ed, emitente_nome = @en, emitente_uf = @eu,
                      destinatario_documento = @dd, destinatario_nome = @dn, destinatario_uf = @du, participante_id = @part,
                      valor_produtos = @vprod, valor_desconto = @vdesc, valor_frete = @vfrete, valor_seguro = @vseg, valor_outras = @voutras, valor_total = @vtotal,
                      vl_bc_icms = @bcicms, vl_icms = @icms, vl_bc_icms_st = @bcst, vl_icms_st = @st, vl_ipi = @ipi, vl_pis = @pis, vl_cofins = @cofins,
                      vl_iss = @iss, iss_retido = @issret, vl_ret_pis = @rpis, vl_ret_cofins = @rcofins, vl_ret_csll = @rcsll, vl_irrf = @irrf, vl_ret_prev = @rprev,
                      centro_custo_id = @cc, observacoes = @obs, conta_debito = @cdeb, conta_credito = @ccre,
                      forma_pagamento_id = (SELECT id FROM core.forma_pagamento WHERE id = @fp AND empresa_id = @e), atualizado_em = now()
                   WHERE id = @id AND empresa_id = @e
                  RETURNING id
                  """;
            await using (var cmd = new NpgsqlCommand(colunas, con, tx))
            {
                cmd.Com("e", empresaId).Com("mod", d.Modelo.Codigo()).ComTexto("chave", d.Chave).Com("op", d.Operacao.Codigo()).Com("num", d.Numero).Com("serie", d.Serie)
                   .Com("emi", d.Emissao).Com("es", d.DataLivro).ComTexto("nat", d.Natureza is { Length: > 200 } n ? n[..200] : d.Natureza)
                   .ComTexto("ed", emiDoc).ComTexto("en", emiNome).ComTexto("eu", emiUf).ComTexto("dd", desDoc).ComTexto("dn", desNome).ComTexto("du", desUf)
                   .Com("part", NpgsqlDbType.Bigint, part.Id)
                   .Com("vprod", t.Produtos).Com("vdesc", t.Desconto).Com("vfrete", t.Frete).Com("vseg", t.Seguro).Com("voutras", t.Outras).Com("vtotal", t.Total)
                   .Com("bcicms", t.BcIcms).Com("icms", t.Icms).Com("bcst", t.BcIcmsSt).Com("st", t.IcmsSt).Com("ipi", t.Ipi).Com("pis", t.Pis)
                   .Com("cofins", t.Cofins).Com("iss", t.Iss).Com("issret", t.IssRetido)
                   .Com("rpis", t.RetPis).Com("rcofins", t.RetCofins).Com("rcsll", t.RetCsll).Com("irrf", t.Irrf).Com("rprev", t.RetPrev)
                   .Com("cc", NpgsqlDbType.Bigint, d.CentroCustoId)
                   .ComTexto("obs", d.Observacoes).ComTexto("cdeb", d.ContaDebito).ComTexto("ccre", d.ContaCredito)
                   .Com("fp", NpgsqlDbType.Bigint, d.FormaPagamentoId);
                if (d.Id == 0) cmd.Com("usu", NpgsqlDbType.Bigint, usuarioId); else cmd.Com("id", d.Id);
                id = await cmd.ExecuteScalarAsync(ct) as long? ?? throw new ViolacaoRegraException("Documento não encontrado.");
            }
            if (d.Id != 0)
                await using (var del = new NpgsqlCommand("DELETE FROM fiscal.documento_item WHERE documento_id = @d", con, tx).Com("d", id))
                    await del.ExecuteNonQueryAsync(ct);

            foreach (var i in d.Itens)
            {
                await using var item = new NpgsqlCommand("""
                    INSERT INTO fiscal.documento_item (documento_id, n_item, cod_produto, descricao, ncm, cest, cfop, unidade, quantidade, valor_unitario,
                        valor_produto, valor_desconto, valor_frete, valor_seguro, valor_outras, valor_contabil, cst_icms, origem_mercadoria,
                        vl_bc_icms, aliq_icms, vl_icms, vl_bc_icms_st, vl_icms_st, cst_ipi, vl_bc_ipi, aliq_ipi, vl_ipi,
                        cst_pis, vl_bc_pis, aliq_pis, vl_pis, cst_cofins, vl_bc_cofins, aliq_cofins, vl_cofins, vl_bc_iss, aliq_iss, vl_iss,
                        produto_id, gtin, centro_custo_id)
                    VALUES (@d, @n, @cod, @desc, @ncm, @cest, @cfop, @un, @qtd, @vu, @vp, @vd, @vf, @vs, @vo, @vc, @cst, @orig,
                        @bc, @aliq, @icms, @bcst, @st, @cipi, @bcipi, @aipi, @ipi, @cpis, @bcpis, @apis, @pis, @ccof, @bccof, @acof, @cof,
                        @bciss, @aiss, @iss, @prod, @gtin, @cc)
                    """, con, tx)
                    .Com("d", id).Com("n", i.Numero).ComTexto("cod", i.CodProduto).ComTexto("desc", i.Descricao).ComTexto("ncm", i.Ncm).ComTexto("cest", i.Cest)
                    .Com("cfop", i.Cfop).ComTexto("un", i.Unidade).Com("qtd", i.Quantidade).Com("vu", i.ValorUnitario).Com("vp", i.ValorProduto).Com("vd", i.Desconto)
                    .Com("vf", i.Frete).Com("vs", i.Seguro).Com("vo", i.Outras).Com("vc", i.ValorContabil).ComTexto("cst", i.CstIcms).ComTexto("orig", i.Origem)
                    .Com("bc", i.BcIcms).Com("aliq", i.AliqIcms).Com("icms", i.Icms).Com("bcst", i.BcIcmsSt).Com("st", i.IcmsSt)
                    .ComTexto("cipi", i.CstIpi).Com("bcipi", i.BcIpi).Com("aipi", i.AliqIpi).Com("ipi", i.Ipi)
                    .ComTexto("cpis", i.CstPis).Com("bcpis", i.BcPis).Com("apis", i.AliqPis).Com("pis", i.Pis)
                    .ComTexto("ccof", i.CstCofins).Com("bccof", i.BcCofins).Com("acof", i.AliqCofins).Com("cof", i.Cofins)
                    .Com("bciss", i.BcIss).Com("aiss", i.AliqIss).Com("iss", i.Iss)
                    .Com("prod", NpgsqlDbType.Bigint, i.ProdutoId).ComTexto("gtin", i.Gtin).Com("cc", NpgsqlDbType.Bigint, i.CentroCustoId);
                await item.ExecuteNonQueryAsync(ct);
            }

            await SincronizarDuplicatasAsync(con, tx, empresaId, id, d, part, entrada, usuarioId, ct);

            // O participante passa a constar como fornecedor (entrada) ou cliente (saída).
            await using (var flag = new NpgsqlCommand(entrada
                       ? "UPDATE core.participante SET fornecedor = TRUE WHERE id = @p AND NOT fornecedor"
                       : "UPDATE core.participante SET cliente = TRUE WHERE id = @p AND NOT cliente", con, tx).Com("p", part.Id))
                await flag.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return id;
        });

    /// <summary>
    /// Parcelas da nota: lista informada substitui as duplicatas (recusa se já houver baixa e a lista mudou); nota nova sem parcelas gera a duplicata à vista da
    /// configuração; sem lista (null) numa nota existente, nada muda. Participante e número da nota são sempre atualizados nas duplicatas abertas.
    /// </summary>
    private static async Task SincronizarDuplicatasAsync(NpgsqlConnection con, NpgsqlTransaction tx, long empresaId, long id, DocumentoDigitado d, ParticipanteNota part,
                                                         bool entrada, long usuarioId, CancellationToken ct)
    {
        var existentes = new List<(long Id, string Parcela, DateOnly Venc, decimal Valor, bool Baixada, string Situacao)>();
        if (d.Id != 0)
            await using (var q = new NpgsqlCommand("""
                SELECT u.id, u.parcela, u.vencimento, u.valor, EXISTS (SELECT 1 FROM fiscal.duplicata_baixa b WHERE b.duplicata_id = u.id), u.situacao
                  FROM fiscal.duplicata u WHERE u.documento_id = @d AND u.situacao <> 'CANCELADA' ORDER BY u.parcela
                """, con, tx).Com("d", id))
            await using (var r = await q.ExecuteReaderAsync(ct))
                while (await r.ReadAsync(ct)) existentes.Add((r.GetInt64(0), r.GetString(1), r.GetFieldValue<DateOnly>(2), r.GetDecimal(3), r.GetBoolean(4), r.GetString(5)));

        var informadas = d.Parcelas;
        if (d.Id != 0 && informadas is null) return;
        var config = await RepositorioDuplicatas.ConfiguracaoAsync(con, tx, empresaId, ct);
        var novas = d.Id == 0 ? RepositorioDuplicatas.ParcelasOuAVista(informadas, config, d.Totais.Total, d.Emissao) : informadas!;

        if (existentes.Any(e => e.Baixada))
        {
            var iguais = existentes.Count == novas.Count && existentes.Zip(novas).All(x => x.First.Parcela == x.Second.Numero.Trim() && x.First.Venc == x.Second.Vencimento && x.First.Valor == x.Second.Valor);
            if (!iguais) throw new ViolacaoRegraException("A nota tem duplicata baixada: estorne as baixas (Lançamentos › Duplicatas) antes de alterar as parcelas.");
            return;
        }
        if (existentes.Count > 0)
            await using (var del = new NpgsqlCommand("DELETE FROM fiscal.duplicata WHERE documento_id = @d AND situacao <> 'CANCELADA'", con, tx).Com("d", id))
                await del.ExecuteNonQueryAsync(ct);
        if (novas.Count > 0)
            await RepositorioDuplicatas.InserirDoDocumentoAsync(con, tx, empresaId, id, part.Id, entrada, d.Numero, d.Emissao, novas, usuarioId, ct);
    }

    public async Task<DocumentoDigitado?> ObterAsync(long empresaId, long documentoId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT d.id, d.tipo_operacao, d.modelo, d.serie, d.numero, d.chave_acesso, d.data_emissao, d.data_entrada_saida, d.natureza_operacao, d.participante_id,
                   d.centro_custo_id, d.valor_produtos, d.valor_desconto, d.valor_frete, d.valor_seguro, d.valor_outras, d.valor_total, d.vl_bc_icms, d.vl_icms,
                   d.vl_bc_icms_st, d.vl_icms_st, d.vl_ipi, d.vl_pis, d.vl_cofins, d.vl_iss, d.iss_retido, d.vl_ret_pis, d.vl_ret_cofins, d.vl_ret_csll, d.vl_irrf,
                   d.vl_ret_prev, d.origem, COALESCE(l.status = 'EFETIVADO', FALSE), d.observacoes, d.conta_debito, d.conta_credito, d.forma_pagamento_id
              FROM fiscal.documento d LEFT JOIN contabil.lancamento l ON l.id = d.lancamento_id
             WHERE d.id = @d AND d.empresa_id = @e
            """).Com("d", documentoId).Com("e", empresaId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var totais = new TotaisDocumento(r.GetDecimal(11), r.GetDecimal(12), r.GetDecimal(13), r.GetDecimal(14), r.GetDecimal(15), r.GetDecimal(16), r.GetDecimal(17),
            r.GetDecimal(18), r.GetDecimal(19), r.GetDecimal(20), r.GetDecimal(21), r.GetDecimal(22), r.GetDecimal(23), r.GetDecimal(24), r.GetBoolean(25),
            r.GetDecimal(26), r.GetDecimal(27), r.GetDecimal(28), r.GetDecimal(29), r.GetDecimal(30));
        var doc = new DocumentoDigitado(r.GetInt64(0), ModelosExtensions.OperacaoDe(r.GetString(1)), ModelosExtensions.ModeloDe(r.GetString(2)), r.GetString(3), r.GetString(4),
            r.Texto(5), r.GetFieldValue<DateOnly>(6), r.GetFieldValue<DateOnly>(7), r.Texto(8), r.LongOuNulo(9) ?? 0, r.LongOuNulo(10), totais, [],
            r.GetBoolean(32), r.GetString(31), r.Texto(33), r.Texto(34), r.Texto(35), FormaPagamentoId: r.LongOuNulo(36));
        await r.CloseAsync();
        await using var cmdItens = ds.CreateCommand($"""
            SELECT {RepositorioDocumentos.ColunasItemSql} FROM fiscal.documento_item i LEFT JOIN contabil.centro_custo cci ON cci.id = i.centro_custo_id
             WHERE i.documento_id = @d ORDER BY i.n_item
            """).Com("d", documentoId);
        var itens = new List<ItemFiscal>();
        await using var ri = await cmdItens.ExecuteReaderAsync(ct);
        while (await ri.ReadAsync(ct)) itens.Add(RepositorioDocumentos.LerItemSql(ri, 0));
        return doc with { Itens = itens };
    }

    public async Task<bool> ContaAnaliticaExisteAsync(long empresaId, string codigo, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT 1 FROM contabil.plano_contas WHERE empresa_id = @e AND codigo = @c AND tipo = 'A' AND ativa").Com("e", empresaId).Com("c", codigo);
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }

    public Task GravarEscrituracaoAsync(long empresaId, EscrituracaoDocumento esc, long usuarioId, CancellationToken ct)
        => Sql.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await Sql.DefinirUsuarioAsync(con, tx, usuarioId, ct);
            string? emitente;
            await using (var q = new NpgsqlCommand("""
                SELECT d.emitente_documento, COALESCE(l.status = 'EFETIVADO', FALSE)
                  FROM fiscal.documento d LEFT JOIN contabil.lancamento l ON l.id = d.lancamento_id
                 WHERE d.id = @d AND d.empresa_id = @e FOR UPDATE OF d
                """, con, tx).Com("d", esc.DocumentoId).Com("e", empresaId))
            await using (var r = await q.ExecuteReaderAsync(ct))
            {
                if (!await r.ReadAsync(ct)) throw new ViolacaoRegraException("Documento não encontrado.");
                emitente = r.Texto(0);
                if (r.GetBoolean(1)) throw new ViolacaoRegraException("A nota está contabilizada: desfaça a contabilização para alterar a escrituração.");
            }
            await using (var up = new NpgsqlCommand("""
                UPDATE fiscal.documento SET observacoes = @obs, centro_custo_id = @cc, conta_debito = @cd, conta_credito = @cr, atualizado_em = now()
                 WHERE id = @d AND empresa_id = @e
                """, con, tx).Com("d", esc.DocumentoId).Com("e", empresaId).ComTexto("obs", esc.Observacoes).Com("cc", NpgsqlDbType.Bigint, esc.CentroCustoId)
                             .ComTexto("cd", esc.ContaDebito).ComTexto("cr", esc.ContaCredito))
                await up.ExecuteNonQueryAsync(ct);
            var emit = new string((emitente ?? "").Where(char.IsAsciiDigit).ToArray());
            foreach (var i in esc.Itens)
            {
                string? codEmitente;
                await using (var it = new NpgsqlCommand("""
                    UPDATE fiscal.documento_item SET cfop_escrituracao = CASE WHEN @cfop = cfop THEN NULL ELSE @cfop END, produto_id = @p, centro_custo_id = @cc
                     WHERE documento_id = @d AND n_item = @n RETURNING cod_produto
                    """, con, tx).Com("d", esc.DocumentoId).Com("n", i.Numero).ComTexto("cfop", i.Cfop).Com("p", NpgsqlDbType.Bigint, i.ProdutoId)
                                 .Com("cc", NpgsqlDbType.Bigint, i.CentroCustoId))
                {
                    var r = await it.ExecuteScalarAsync(ct);
                    if (r is null) throw new ViolacaoRegraException($"Item {i.Numero} não encontrado na nota.");
                    codEmitente = r as string;
                }
                // O produto escolhido passa a valer para as próximas notas do mesmo fornecedor com este código.
                if (i.ProdutoId is { } pid && !string.IsNullOrWhiteSpace(codEmitente) && emit.Length > 0)
                    await using (var v = new NpgsqlCommand("""
                        INSERT INTO fiscal.produto_vinculo (empresa_id, produto_id, emitente_documento, cod_produto_emitente) VALUES (@e, @p, @em, @c)
                        ON CONFLICT (empresa_id, emitente_documento, cod_produto_emitente) DO UPDATE SET produto_id = EXCLUDED.produto_id
                        """, con, tx).Com("e", empresaId).Com("p", pid).Com("em", emit.Length > 14 ? emit[..14] : emit).Com("c", codEmitente))
                        await v.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            return 0;
        });
}
