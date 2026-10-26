using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using Vorticis.Fiscal.Documentos;

namespace Vorticis.Fiscal.Postgres;

public sealed class RepositorioDocumentos(NpgsqlDataSource ds) : IRepositorioDocumentos
{
    public async Task<EmpresaFiscal?> ObterEmpresaAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT cnpj, razao_social, data_inicio_ativ FROM core.empresa WHERE id = @e").Com("e", empresaId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var (cnpj, razao, inicio) = (r.GetString(0).Trim(), r.GetString(1), r.GetFieldValue<DateOnly>(2));
        await r.CloseAsync();
        var estabelecimentos = new List<string>();
        await using var cmd2 = ds.CreateCommand("SELECT cnpj FROM core.estabelecimento WHERE empresa_id = @e").Com("e", empresaId);
        await using var r2 = await cmd2.ExecuteReaderAsync(ct);
        while (await r2.ReadAsync(ct)) estabelecimentos.Add(r2.GetString(0).Trim());
        return new EmpresaFiscal(empresaId, cnpj, estabelecimentos, razao, inicio);
    }

    public Task<GravacaoDocumento> GravarAsync(long empresaId, DocumentoLido d, Identificacao ident, long usuarioId, CancellationToken ct)
        => Sql.TraduzirErrosAsync(async () =>
        {
            await using var lote = await IniciarLoteAsync(empresaId, usuarioId, ct);
            var g = await lote.GravarAsync(d, ident, ct);
            await lote.ConfirmarAsync(ct);
            return g;
        });

    public async Task<ILoteGravacao> IniciarLoteAsync(long empresaId, long usuarioId, CancellationToken ct)
    {
        var con = await ds.OpenConnectionAsync(ct);
        try
        {
            var tx = await con.BeginTransactionAsync(ct);
            await Sql.DefinirUsuarioAsync(con, tx, usuarioId, ct);
            await using (var cmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext('fiscal_importacao'), @e::int)", con, tx).Com("e", empresaId))
                await cmd.ExecuteNonQueryAsync(ct);
            return new LoteGravacao(con, tx, empresaId, usuarioId);
        }
        catch
        {
            await con.DisposeAsync();
            throw;
        }
    }

    /// <summary>Várias notas na mesma transação; cada uma num savepoint, de modo que uma falha desfaz só aquela nota.</summary>
    private sealed class LoteGravacao(NpgsqlConnection con, NpgsqlTransaction tx, long empresaId, long usuarioId) : ILoteGravacao
    {
        private Duplicatas.ConfiguracaoDuplicatas? _configuracao;

        public async Task<GravacaoDocumento> GravarAsync(DocumentoLido d, Identificacao ident, CancellationToken ct)
        {
            _configuracao ??= await RepositorioDuplicatas.ConfiguracaoAsync(con, tx, empresaId, ct);
            await using (var sp = new NpgsqlCommand("SAVEPOINT nota", con, tx)) await sp.ExecuteNonQueryAsync(ct);
            try
            {
                var g = await Sql.TraduzirErrosAsync(() => GravarNaTransacaoAsync(con, tx, empresaId, d, ident, usuarioId, _configuracao, ct));
                await using var rel = new NpgsqlCommand("RELEASE SAVEPOINT nota", con, tx);
                await rel.ExecuteNonQueryAsync(CancellationToken.None);
                return g;
            }
            catch
            {
                try { await using var rb = new NpgsqlCommand("ROLLBACK TO SAVEPOINT nota", con, tx); await rb.ExecuteNonQueryAsync(CancellationToken.None); }
                catch (Exception) { /* conexão perdida: a transação inteira será descartada */ }
                throw;
            }
        }

        public Task ConfirmarAsync(CancellationToken ct) => tx.CommitAsync(ct);

        public async ValueTask DisposeAsync()
        {
            await tx.DisposeAsync();
            await con.DisposeAsync();
        }
    }

    private static async Task<GravacaoDocumento> GravarNaTransacaoAsync(NpgsqlConnection con, NpgsqlTransaction tx, long empresaId, DocumentoLido d,
                                                                       Identificacao ident, long usuarioId, Duplicatas.ConfiguracaoDuplicatas configuracao, CancellationToken ct)
    {
            await using (var existe = new NpgsqlCommand("SELECT id FROM fiscal.documento WHERE empresa_id = @e AND chave_acesso = @c", con, tx)
                             .Com("e", empresaId).Com("c", d.Chave))
                if (await existe.ExecuteScalarAsync(ct) is long id)
                    return new GravacaoDocumento(true, id, false, null);

            var (participanteId, criado, nomePart) = await VincularParticipanteAsync(con, tx, empresaId, ident, ct);

            var situacao = d.Situacao;
            await using (var pend = new NpgsqlCommand("DELETE FROM fiscal.cancelamento_pendente WHERE empresa_id = @e AND chave_acesso = @c", con, tx)
                             .Com("e", empresaId).Com("c", d.Chave))
                if (await pend.ExecuteNonQueryAsync(ct) > 0) situacao = SituacaoDocumento.Cancelada;

            long documentoId;
            var t = d.Totais;
            await using (var ins = new NpgsqlCommand("""
                INSERT INTO fiscal.documento (empresa_id, modelo, chave_acesso, tipo_operacao, numero, serie, data_emissao, data_entrada_saida,
                    natureza_operacao, situacao, emitente_documento, emitente_nome, emitente_uf, destinatario_documento, destinatario_nome,
                    destinatario_uf, participante_id, valor_produtos, valor_desconto, valor_frete, valor_seguro, valor_outras, valor_total,
                    vl_bc_icms, vl_icms, vl_bc_icms_st, vl_icms_st, vl_ipi, vl_pis, vl_cofins, vl_iss, iss_retido,
                    vl_ret_pis, vl_ret_cofins, vl_ret_csll, vl_irrf, vl_ret_prev, xml_original, sha256, importado_por)
                VALUES (@e, @mod, @chave, @op, @num, @serie, @emi, @es, @nat, @sit, @ed, @en, @eu, @dd, @dn, @du, @part, @vprod, @vdesc, @vfrete,
                    @vseg, @voutras, @vtotal, @bcicms, @icms, @bcst, @st, @ipi, @pis, @cofins, @iss, @issret,
                    @rpis, @rcofins, @rcsll, @irrf, @rprev, @xml, @sha, @usu)
                RETURNING id
                """, con, tx)
                .Com("e", empresaId).Com("mod", d.Modelo.Codigo()).Com("chave", d.Chave).Com("op", ident.Operacao.Codigo())
                .Com("num", d.Numero).Com("serie", d.Serie).Com("emi", d.Emissao).Com("es", d.DataLivro).ComTexto("nat", Cortar(d.NaturezaOperacao, 200))
                .Com("sit", situacao.Codigo())
                .ComTexto("ed", d.Emitente.Documento).ComTexto("en", Cortar(d.Emitente.Nome, 150)).ComTexto("eu", d.Emitente.Uf)
                .ComTexto("dd", d.Destinatario?.Documento).ComTexto("dn", Cortar(d.Destinatario?.Nome, 150)).ComTexto("du", d.Destinatario?.Uf)
                .Com("part", NpgsqlDbType.Bigint, participanteId)
                .Com("vprod", t.Produtos).Com("vdesc", t.Desconto).Com("vfrete", t.Frete).Com("vseg", t.Seguro).Com("voutras", t.Outras).Com("vtotal", t.Total)
                .Com("bcicms", t.BcIcms).Com("icms", t.Icms).Com("bcst", t.BcIcmsSt).Com("st", t.IcmsSt).Com("ipi", t.Ipi).Com("pis", t.Pis)
                .Com("cofins", t.Cofins).Com("iss", t.Iss).Com("issret", t.IssRetido)
                .Com("rpis", t.RetPis).Com("rcofins", t.RetCofins).Com("rcsll", t.RetCsll).Com("irrf", t.Irrf).Com("rprev", t.RetPrev)
                .Com("xml", NpgsqlDbType.Bytea, Compactar(d.Xml)).Com("sha", Convert.ToHexString(SHA256.HashData(d.Xml)).ToLowerInvariant())
                .Com("usu", NpgsqlDbType.Bigint, usuarioId))
                documentoId = (long)(await ins.ExecuteScalarAsync(ct))!;

            foreach (var i in d.Itens)
            {
                var produtoId = i.Cfop == Cfop.Servico ? null : await VincularProdutoAsync(con, tx, empresaId, d.Emitente.Documento, ident.Operacao, i, ct);
                await using var item = new NpgsqlCommand("""
                    INSERT INTO fiscal.documento_item (documento_id, n_item, cod_produto, descricao, ncm, cest, cfop, unidade, quantidade, valor_unitario,
                        valor_produto, valor_desconto, valor_frete, valor_seguro, valor_outras, valor_contabil, cst_icms, origem_mercadoria,
                        vl_bc_icms, aliq_icms, vl_icms, vl_bc_icms_st, vl_icms_st, cst_ipi, vl_bc_ipi, aliq_ipi, vl_ipi,
                        cst_pis, vl_bc_pis, aliq_pis, vl_pis, cst_cofins, vl_bc_cofins, aliq_cofins, vl_cofins, vl_bc_iss, aliq_iss, vl_iss, produto_id, gtin)
                    VALUES (@d, @n, @cod, @desc, @ncm, @cest, @cfop, @un, @qtd, @vu, @vp, @vd, @vf, @vs, @vo, @vc, @cst, @orig,
                        @bc, @aliq, @icms, @bcst, @st, @cipi, @bcipi, @aipi, @ipi, @cpis, @bcpis, @apis, @pis, @ccof, @bccof, @acof, @cof,
                        @bciss, @aiss, @iss, @prod, @gtin)
                    """, con, tx)
                    .Com("d", documentoId).Com("n", i.Numero).ComTexto("cod", Cortar(i.CodProduto, 60)).ComTexto("desc", Cortar(i.Descricao, 500))
                    .ComTexto("ncm", Cortar(i.Ncm, 8)).ComTexto("cest", Cortar(i.Cest, 7)).Com("cfop", Cortar(i.Cfop, 10) ?? "").ComTexto("un", Cortar(i.Unidade, 10))
                    .Com("qtd", i.Quantidade).Com("vu", i.ValorUnitario).Com("vp", i.ValorProduto).Com("vd", i.Desconto).Com("vf", i.Frete)
                    .Com("vs", i.Seguro).Com("vo", i.Outras).Com("vc", i.ValorContabil)
                    .ComTexto("cst", Cortar(i.CstIcms, 4)).ComTexto("orig", Cortar(i.Origem, 1))
                    .Com("bc", i.BcIcms).Com("aliq", i.AliqIcms).Com("icms", i.Icms).Com("bcst", i.BcIcmsSt).Com("st", i.IcmsSt)
                    .ComTexto("cipi", Cortar(i.CstIpi, 2)).Com("bcipi", i.BcIpi).Com("aipi", i.AliqIpi).Com("ipi", i.Ipi)
                    .ComTexto("cpis", Cortar(i.CstPis, 2)).Com("bcpis", i.BcPis).Com("apis", i.AliqPis).Com("pis", i.Pis)
                    .ComTexto("ccof", Cortar(i.CstCofins, 2)).Com("bccof", i.BcCofins).Com("acof", i.AliqCofins).Com("cof", i.Cofins)
                    .Com("bciss", i.BcIss).Com("aiss", i.AliqIss).Com("iss", i.Iss)
                    .Com("prod", NpgsqlDbType.Bigint, produtoId).ComTexto("gtin", i.Gtin);
                await item.ExecuteNonQueryAsync(ct);
            }

            // Duplicatas: as parcelas da cobrança do XML ou, sem cobrança, uma parcela à vista (conforme a configuração). NFC-e só tem duplicata se trouxer cobrança.
            if (situacao == SituacaoDocumento.Autorizada)
            {
                var informadas = d.Duplicatas;
                var parcelas = d.Modelo == ModeloDocumento.NFCe65 && informadas is not { Count: > 0 }
                    ? []
                    : RepositorioDuplicatas.ParcelasOuAVista(informadas, configuracao, d.Totais.Total, d.Emissao);
                if (parcelas.Count > 0)
                    await RepositorioDuplicatas.InserirDoDocumentoAsync(con, tx, empresaId, documentoId, participanteId, ident.Operacao == TipoOperacao.Entrada, d.Numero, d.Emissao,
                                                                        parcelas, usuarioId, ct);
            }
            return new GravacaoDocumento(false, documentoId, criado, nomePart);
    }

    /// <summary>Procura o participante pelo CPF/CNPJ na empresa (cria se não existir) e marca cliente (saída) ou fornecedor (entrada).</summary>
    private static async Task<(long? Id, bool Criado, string? Nome)> VincularParticipanteAsync(NpgsqlConnection con, NpgsqlTransaction tx, long empresaId,
                                                                                               Identificacao ident, CancellationToken ct)
    {
        var parte = ident.Contraparte;
        if (parte?.Documento is not { Length: 11 or 14 } doc) return (null, false, null);
        var cliente = ident.Operacao == TipoOperacao.Saida;

        await using (var busca = new NpgsqlCommand("SELECT id, nome, cliente, fornecedor FROM core.participante WHERE empresa_id = @e AND cpf_cnpj = @d", con, tx)
                         .Com("e", empresaId).Com("d", doc))
        await using (var r = await busca.ExecuteReaderAsync(ct))
        {
            if (await r.ReadAsync(ct))
            {
                var (id, nome, ehCliente, ehFornecedor) = (r.GetInt64(0), r.GetString(1), r.GetBoolean(2), r.GetBoolean(3));
                await r.CloseAsync();
                if ((cliente && !ehCliente) || (!cliente && !ehFornecedor))
                    await using (var up = new NpgsqlCommand(cliente ? "UPDATE core.participante SET cliente = TRUE WHERE id = @i" : "UPDATE core.participante SET fornecedor = TRUE WHERE id = @i", con, tx).Com("i", id))
                        await up.ExecuteNonQueryAsync(ct);
                return (id, false, nome);
            }
        }

        string codigo;
        await using (var prox = new NpgsqlCommand("SELECT core.fn_proximo_participante(@e)", con, tx).Com("e", empresaId))
            codigo = ((long)(await prox.ExecuteScalarAsync(ct))!).ToString();
        var nomeNovo = Cortar(string.IsNullOrWhiteSpace(parte.Nome) ? doc : parte.Nome, 150)!;
        await using var ins = new NpgsqlCommand("""
            INSERT INTO core.participante (empresa_id, codigo, nome, cpf_cnpj, uf, cod_municipio_ibge, municipio, inscricao_estadual, cliente, fornecedor)
            VALUES (@e, @c, @n, @d, @uf, @ibge, @mun, @ie, @cli, @for) RETURNING id
            """, con, tx)
            .Com("e", empresaId).Com("c", codigo).Com("n", nomeNovo).Com("d", doc)
            .ComTexto("uf", parte.Uf is { Length: 2 } ? parte.Uf : null)
            .ComTexto("ibge", parte.CodMunicipioIbge is { Length: 7 } ? parte.CodMunicipioIbge : null).ComTexto("mun", Cortar(parte.Municipio, 100))
            .ComTexto("ie", Cortar(parte.InscricaoEstadual, 20)).Com("cli", cliente).Com("for", !cliente);
        return ((long)(await ins.ExecuteScalarAsync(ct))!, true, nomeNovo);
    }


    /// <summary>
    /// Vincula o item ao cadastro de produtos: pelo vínculo (emitente + código do produto no emitente); na falta, pelo GTIN válido e depois
    /// pela descrição idêntica; se nada casar, cadastra o produto. O vínculo (emitente + código) é gravado para as próximas notas.
    /// </summary>
    private static async Task<long?> VincularProdutoAsync(NpgsqlConnection con, NpgsqlTransaction tx, long empresaId, string? emitente, TipoOperacao operacao,
                                                          ItemFiscal i, CancellationToken ct)
    {
        var cod = Cortar(i.CodProduto?.Trim(), 60) is { Length: > 0 } c ? c : null;
        var descricao = Cortar(i.Descricao?.Trim(), 200) is { Length: > 0 } ds ? ds : null;
        if (cod is null && descricao is null) return null;
        var emit = new string((emitente ?? "").Where(char.IsAsciiDigit).ToArray());
        if (emit.Length > 14) emit = emit[..14];

        long? id = null;
        if (cod is not null)
            await using (var q = new NpgsqlCommand("SELECT produto_id FROM fiscal.produto_vinculo WHERE empresa_id = @e AND emitente_documento = @em AND cod_produto_emitente = @c", con, tx)
                             .Com("e", empresaId).Com("em", emit).Com("c", cod))
                id = await q.ExecuteScalarAsync(ct) as long?;
        if (id is null && i.Gtin is not null)
            await using (var q = new NpgsqlCommand("SELECT id FROM fiscal.produto WHERE empresa_id = @e AND gtin = @g", con, tx).Com("e", empresaId).Com("g", i.Gtin))
                id = await q.ExecuteScalarAsync(ct) as long?;
        if (id is null && descricao is not null)
            await using (var q = new NpgsqlCommand("SELECT id FROM fiscal.produto WHERE empresa_id = @e AND lower(descricao) = lower(@d) ORDER BY ativo DESC, codigo LIMIT 1", con, tx)
                             .Com("e", empresaId).Com("d", descricao))
                id = await q.ExecuteScalarAsync(ct) as long?;
        if (id is null)
        {
            await using (var trava = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext('fiscal_produto'), @e::int)", con, tx).Com("e", empresaId))
                await trava.ExecuteNonQueryAsync(ct);
            static string? Digitos(string? t, int n) => t is null ? null : new string(t.Where(char.IsAsciiDigit).ToArray()) is { Length: > 0 } d && d.Length == n ? d : null;
            var entrada = operacao == TipoOperacao.Entrada;
            var cfop = Digitos(i.Cfop, 4);
            await using var ins = new NpgsqlCommand("""
                INSERT INTO fiscal.produto (empresa_id, codigo, descricao, unidade, ncm, cest, gtin, origem, tipo_item, cfop_entrada, cfop_saida,
                                            cst_icms_entrada, cst_icms_saida, aliq_icms, aliq_ipi, aliq_pis, aliq_cofins)
                VALUES (@e, fiscal.fn_proximo_produto(@e), @d, @un, @ncm, @cest, @g, @orig, '00', @ce, @cs,
                        @cste, @csts, @aicms, @aipi, @apis, @acof)
                RETURNING id
                """, con, tx)
                .Com("e", empresaId).Com("d", descricao ?? cod!).Com("un", Cortar(string.IsNullOrWhiteSpace(i.Unidade) ? "UN" : i.Unidade.Trim(), 10)!)
                .ComTexto("ncm", Digitos(i.Ncm, 8)).ComTexto("cest", Digitos(i.Cest, 7)).ComTexto("g", i.Gtin)
                .Com("orig", i.Origem is { Length: 1 } o && o[0] is >= '0' and <= '8' ? o : "0")
                .ComTexto("ce", entrada ? cfop : null).ComTexto("cs", entrada ? null : cfop)
                .ComTexto("cste", entrada ? Cortar(i.CstIcms, 3) : null).ComTexto("csts", entrada ? null : Cortar(i.CstIcms, 3))
                .Com("aicms", Math.Min(i.AliqIcms, 100m)).Com("aipi", Math.Min(i.AliqIpi, 100m)).Com("apis", Math.Min(i.AliqPis, 100m)).Com("acof", Math.Min(i.AliqCofins, 100m));
            id = (long)(await ins.ExecuteScalarAsync(ct))!;
        }
        if (cod is not null)
            await using (var v = new NpgsqlCommand("""
                INSERT INTO fiscal.produto_vinculo (empresa_id, produto_id, emitente_documento, cod_produto_emitente) VALUES (@e, @p, @em, @c)
                ON CONFLICT (empresa_id, emitente_documento, cod_produto_emitente) DO NOTHING
                """, con, tx).Com("e", empresaId).Com("p", id!.Value).Com("em", emit).Com("c", cod))
                await v.ExecuteNonQueryAsync(ct);
        return id;
    }

    public async Task<CancelamentoRegistrado> CancelarPorChaveAsync(long empresaId, string chave, CancellationToken ct)
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        long id;
        string situacao, numero;
        bool contabilizado;
        await using (var busca = new NpgsqlCommand("""
            SELECT d.id, d.situacao, d.numero, COALESCE(l.status = 'EFETIVADO', FALSE)
              FROM fiscal.documento d LEFT JOIN contabil.lancamento l ON l.id = d.lancamento_id
             WHERE d.empresa_id = @e AND d.chave_acesso = @c
            """, con, tx).Com("e", empresaId).Com("c", chave))
        await using (var r = await busca.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct))
            {
                await r.CloseAsync();
                await using var pend = new NpgsqlCommand("INSERT INTO fiscal.cancelamento_pendente (empresa_id, chave_acesso) VALUES (@e, @c) ON CONFLICT DO NOTHING", con, tx)
                    .Com("e", empresaId).Com("c", chave);
                await pend.ExecuteNonQueryAsync(ct);
                await tx.CommitAsync(ct);
                return new CancelamentoRegistrado(false, false, false, 0, null);
            }
            (id, situacao, numero, contabilizado) = (r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetBoolean(3));
        }
        if (situacao == "CANCELADA") return new CancelamentoRegistrado(true, true, contabilizado, id, numero);
        await using (var up = new NpgsqlCommand("UPDATE fiscal.documento SET situacao = 'CANCELADA' WHERE id = @i", con, tx).Com("i", id))
            await up.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return new CancelamentoRegistrado(true, false, contabilizado, id, numero);
    }

    private const string ColunasDocumento = """
        d.id, d.modelo, COALESCE(d.chave_acesso, ''), d.tipo_operacao, d.numero, d.serie, d.data_emissao, d.data_entrada_saida, d.natureza_operacao, d.situacao,
        d.emitente_documento, d.emitente_nome, d.emitente_uf, d.destinatario_documento, d.destinatario_nome, d.destinatario_uf,
        d.participante_id, p.codigo,
        d.valor_produtos, d.valor_desconto, d.valor_frete, d.valor_seguro, d.valor_outras, d.valor_total,
        d.vl_bc_icms, d.vl_icms, d.vl_bc_icms_st, d.vl_icms_st, d.vl_ipi, d.vl_pis, d.vl_cofins, d.vl_iss, d.iss_retido,
        d.vl_ret_pis, d.vl_ret_cofins, d.vl_ret_csll, d.vl_irrf, d.vl_ret_prev,
        d.lancamento_id, COALESCE(l.status = 'EFETIVADO', FALSE), d.origem, d.centro_custo_id, ccd.codigo, d.observacoes, d.conta_debito, d.conta_credito
        """;

    public async Task<IReadOnlyList<DocumentoFiscal>> ListarAsync(long empresaId, FiltroDocumentos f, CancellationToken ct)
    {
        var onde = new StringBuilder("d.empresa_id = @e");
        await using var cmd = ds.CreateCommand("").Com("e", empresaId);
        if (f.Inicio is { } ini) { onde.Append(" AND d.data_entrada_saida >= @ini"); cmd.Com("ini", ini); }
        if (f.Fim is { } fim) { onde.Append(" AND d.data_entrada_saida <= @fim"); cmd.Com("fim", fim); }
        if (f.Operacao is { } op) { onde.Append(" AND d.tipo_operacao = @op"); cmd.Com("op", op.Codigo()); }
        if (f.Modelo is { } mod) { onde.Append(" AND d.modelo = @mod"); cmd.Com("mod", mod.Codigo()); }
        if (f.Situacao is { } sit) { onde.Append(" AND d.situacao = @sit"); cmd.Com("sit", sit.Codigo()); }
        if (!string.IsNullOrWhiteSpace(f.Cfop))
        {
            onde.Append(" AND EXISTS (SELECT 1 FROM fiscal.documento_item i WHERE i.documento_id = d.id AND i.cfop LIKE @cfop || '%')");
            cmd.Com("cfop", f.Cfop.Trim().ToUpperInvariant());
        }
        if (f.Contabilizado is true) onde.Append(" AND COALESCE(l.status = 'EFETIVADO', FALSE)");
        if (f.Contabilizado is false) onde.Append(" AND NOT COALESCE(l.status = 'EFETIVADO', FALSE)");
        if (!string.IsNullOrWhiteSpace(f.Texto))
        {
            var texto = f.Texto.Trim();
            onde.Append("""
                 AND (d.numero = @txt OR d.chave_acesso LIKE '%' || @txt || '%' OR d.emitente_nome ILIKE '%' || @txt || '%'
                      OR d.destinatario_nome ILIKE '%' || @txt || '%' OR d.emitente_documento LIKE '%' || @dig || '%'
                      OR d.destinatario_documento LIKE '%' || @dig || '%')
                """);
            cmd.Com("txt", texto).Com("dig", new string(texto.Where(char.IsAsciiDigit).ToArray()) is { Length: > 0 } dig ? dig : "\u0001");
        }
        cmd.CommandText = $"""
            SELECT {ColunasDocumento}
              FROM fiscal.documento d
              LEFT JOIN core.participante p ON p.id = d.participante_id
              LEFT JOIN contabil.lancamento l ON l.id = d.lancamento_id
              LEFT JOIN contabil.centro_custo ccd ON ccd.id = d.centro_custo_id
             WHERE {onde}
             ORDER BY d.data_entrada_saida, length(d.numero), d.numero, d.id
             LIMIT @lim
            """;
        cmd.Com("lim", f.Limite);
        return await LerDocumentosAsync(cmd, ct);
    }

    private static async Task<IReadOnlyList<DocumentoFiscal>> LerDocumentosAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var res = new List<DocumentoFiscal>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var totais = new TotaisDocumento(r.GetDecimal(18), r.GetDecimal(19), r.GetDecimal(20), r.GetDecimal(21), r.GetDecimal(22), r.GetDecimal(23),
                r.GetDecimal(24), r.GetDecimal(25), r.GetDecimal(26), r.GetDecimal(27), r.GetDecimal(28), r.GetDecimal(29), r.GetDecimal(30),
                r.GetDecimal(31), r.GetBoolean(32), r.GetDecimal(33), r.GetDecimal(34), r.GetDecimal(35), r.GetDecimal(36), r.GetDecimal(37));
            res.Add(new DocumentoFiscal(r.GetInt64(0), ModelosExtensions.ModeloDe(r.GetString(1)), r.GetString(2), ModelosExtensions.OperacaoDe(r.GetString(3)),
                r.GetString(4), r.GetString(5), r.GetFieldValue<DateOnly>(6), r.GetFieldValue<DateOnly>(7), r.Texto(8), ModelosExtensions.SituacaoDe(r.GetString(9)),
                new ParteDocumento(r.Texto(10), r.Texto(11), r.Texto(12)),
                r.IsDBNull(13) && r.IsDBNull(14) ? null : new ParteDocumento(r.Texto(13), r.Texto(14), r.Texto(15)),
                r.LongOuNulo(16), r.Texto(17), totais, r.LongOuNulo(38), r.GetBoolean(39), r.GetString(40), r.LongOuNulo(41), r.Texto(42), r.Texto(43), r.Texto(44), r.Texto(45)));
        }
        return res;
    }

    internal const string ColunasItemSql = """
        i.n_item, i.cod_produto, i.descricao, i.ncm, i.cest, COALESCE(i.cfop_escrituracao, i.cfop), i.unidade, i.quantidade, i.valor_unitario, i.valor_produto, i.valor_desconto,
        i.valor_frete, i.valor_seguro, i.valor_outras, i.valor_contabil, i.cst_icms, i.origem_mercadoria, i.vl_bc_icms, i.aliq_icms, i.vl_icms,
        i.vl_bc_icms_st, i.vl_icms_st, i.cst_ipi, i.vl_bc_ipi, i.aliq_ipi, i.vl_ipi, i.cst_pis, i.vl_bc_pis, i.aliq_pis, i.vl_pis,
        i.cst_cofins, i.vl_bc_cofins, i.aliq_cofins, i.vl_cofins, i.vl_bc_iss, i.aliq_iss, i.vl_iss,
        i.gtin, i.produto_id, i.centro_custo_id, cci.codigo, i.cfop
        """;

    internal static ItemFiscal LerItemSql(NpgsqlDataReader r, int o) => new(
        r.GetInt32(o), r.Texto(o + 1), r.Texto(o + 2), r.Texto(o + 3), r.Texto(o + 4), r.GetString(o + 5), r.Texto(o + 6), r.GetDecimal(o + 7),
        r.GetDecimal(o + 8), r.GetDecimal(o + 9), r.GetDecimal(o + 10), r.GetDecimal(o + 11), r.GetDecimal(o + 12), r.GetDecimal(o + 13),
        r.GetDecimal(o + 14), r.Texto(o + 15), r.Texto(o + 16), r.GetDecimal(o + 17), r.GetDecimal(o + 18), r.GetDecimal(o + 19), r.GetDecimal(o + 20),
        r.GetDecimal(o + 21), r.Texto(o + 22), r.GetDecimal(o + 23), r.GetDecimal(o + 24), r.GetDecimal(o + 25), r.Texto(o + 26),
        r.GetDecimal(o + 27), r.GetDecimal(o + 28), r.GetDecimal(o + 29), r.Texto(o + 30), r.GetDecimal(o + 31), r.GetDecimal(o + 32),
        r.GetDecimal(o + 33), r.GetDecimal(o + 34), r.GetDecimal(o + 35), r.GetDecimal(o + 36),
        r.Texto(o + 37), r.LongOuNulo(o + 38), r.LongOuNulo(o + 39), r.Texto(o + 40), r.Texto(o + 41));

    public async Task<IReadOnlyList<ItemFiscal>> ListarItensDoDocumentoAsync(long documentoId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand($"SELECT {ColunasItemSql} FROM fiscal.documento_item i LEFT JOIN contabil.centro_custo cci ON cci.id = i.centro_custo_id WHERE i.documento_id = @d ORDER BY i.n_item").Com("d", documentoId);
        var res = new List<ItemFiscal>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(LerItemSql(r, 0));
        return res;
    }

    public async Task<IReadOnlyList<LinhaItemFiscal>> ListarItensAsync(long empresaId, DateOnly inicio, DateOnly fim, TipoOperacao? operacao, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand($"""
            SELECT d.id, d.modelo, d.tipo_operacao, d.numero, d.serie, d.data_emissao, d.data_entrada_saida, d.situacao,
                   CASE WHEN d.tipo_operacao = 'E' THEN d.emitente_nome ELSE d.destinatario_nome END,
                   CASE WHEN d.tipo_operacao = 'E' THEN d.emitente_documento ELSE d.destinatario_documento END,
                   CASE WHEN d.tipo_operacao = 'E' THEN d.emitente_uf ELSE d.destinatario_uf END, COALESCE(d.chave_acesso, ''), d.iss_retido,
                   {ColunasItemSql}
              FROM fiscal.documento d JOIN fiscal.documento_item i ON i.documento_id = d.id
              LEFT JOIN contabil.centro_custo cci ON cci.id = i.centro_custo_id
             WHERE d.empresa_id = @e AND d.data_entrada_saida BETWEEN @ini AND @fim AND (@op::text IS NULL OR d.tipo_operacao = @op)
             ORDER BY d.data_entrada_saida, d.id, i.n_item
            """).Com("e", empresaId).Com("ini", inicio).Com("fim", fim).Com("op", NpgsqlDbType.Text, operacao?.Codigo());
        var res = new List<LinhaItemFiscal>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new LinhaItemFiscal(r.GetInt64(0), ModelosExtensions.ModeloDe(r.GetString(1)), ModelosExtensions.OperacaoDe(r.GetString(2)), r.GetString(3),
                r.GetString(4), r.GetFieldValue<DateOnly>(5), r.GetFieldValue<DateOnly>(6), ModelosExtensions.SituacaoDe(r.GetString(7)), r.Texto(8),
                r.Texto(9), r.Texto(10), r.GetString(11), r.GetBoolean(12), LerItemSql(r, 13)));
        return res;
    }

    public Task<int> ExcluirAsync(long empresaId, IReadOnlyCollection<long> ids, CancellationToken ct)
        => Sql.TraduzirErrosAsync(async () =>
        {
            await using var cmd = ds.CreateCommand("""
                DELETE FROM fiscal.documento d
                 WHERE d.empresa_id = @e AND d.id = ANY(@ids)
                   AND NOT EXISTS (SELECT 1 FROM contabil.lancamento l WHERE l.id = d.lancamento_id AND l.status = 'EFETIVADO')
                """).Com("e", empresaId).Com("ids", ids.ToArray());
            return await cmd.ExecuteNonQueryAsync(ct);   // nota com duplicata baixada: o banco recusa (mensagem em português)
        });

    public async Task<byte[]?> ObterXmlAsync(long empresaId, long documentoId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT xml_original FROM fiscal.documento WHERE id = @d AND empresa_id = @e").Com("d", documentoId).Com("e", empresaId);
        if (await cmd.ExecuteScalarAsync(ct) is not byte[] gz) return null;
        using var saida = new MemoryStream();
        await using (var gzip = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress)) await gzip.CopyToAsync(saida, ct);
        return saida.ToArray();
    }

    private static byte[] Compactar(byte[] xml)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, true)) gz.Write(xml);
        return ms.ToArray();
    }

    private static string? Cortar(string? texto, int max) => texto is null ? null : texto.Length <= max ? texto : texto[..max];
}
