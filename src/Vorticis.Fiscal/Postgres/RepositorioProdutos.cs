using Npgsql;
using NpgsqlTypes;
using Vorticis.Domain.Comum;
using Vorticis.Fiscal.Documentos;
using Vorticis.Fiscal.Produtos;

namespace Vorticis.Fiscal.Postgres;

public sealed class RepositorioProdutos(NpgsqlDataSource ds) : IRepositorioProdutos
{
    private const string Colunas = """
        p.id, p.codigo, p.descricao, p.unidade, p.ncm, p.cest, p.gtin, p.origem, p.tipo_item, p.cfop_entrada, p.cfop_saida,
        p.cst_icms_entrada, p.cst_icms_saida, p.aliq_icms, p.aliq_ipi, p.aliq_pis, p.aliq_cofins, p.ativo
        """;

    public async Task<IReadOnlyList<Produto>> ListarAsync(long empresaId, bool apenasAtivos, string? texto, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand($"""
            SELECT {Colunas} FROM fiscal.produto p
             WHERE p.empresa_id = @e AND (NOT @ativos OR p.ativo)
               AND (@t::text IS NULL OR p.descricao ILIKE '%' || @t || '%' OR p.codigo::text = @t OR p.gtin = @t OR p.ncm LIKE @t || '%')
             ORDER BY p.codigo
            """).Com("e", empresaId).Com("ativos", apenasAtivos).Com("t", NpgsqlDbType.Text, string.IsNullOrWhiteSpace(texto) ? null : texto.Trim());
        var res = new List<Produto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new Produto(r.GetInt64(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.Texto(4), r.Texto(5), r.Texto(6), r.GetString(7).Trim(), r.GetString(8).Trim(),
                r.Texto(9), r.Texto(10), r.Texto(11), r.Texto(12), r.GetDecimal(13), r.GetDecimal(14), r.GetDecimal(15), r.GetDecimal(16), r.GetBoolean(17)));
        return res;
    }

    public Task<long> GravarAsync(long empresaId, Produto p, long usuarioId, CancellationToken ct)
        => Sql.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await Sql.DefinirUsuarioAsync(con, tx, usuarioId, ct);
            await using (var trava = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext('fiscal_produto'), @e::int)", con, tx).Com("e", empresaId))
                await trava.ExecuteNonQueryAsync(ct);

            if (p.Gtin is not null)
                await using (var dup = new NpgsqlCommand("SELECT codigo || ' - ' || descricao FROM fiscal.produto WHERE empresa_id = @e AND gtin = @g AND id <> @i", con, tx)
                                 .Com("e", empresaId).Com("g", p.Gtin).Com("i", p.Id))
                    if (await dup.ExecuteScalarAsync(ct) is string outro)
                        throw new ViolacaoRegraException($"O código de barras {p.Gtin} já está cadastrado no produto {outro}.");

            await using var cmd = p.Id == 0
                ? new NpgsqlCommand("""
                    INSERT INTO fiscal.produto (empresa_id, codigo, descricao, unidade, ncm, cest, gtin, origem, tipo_item, cfop_entrada, cfop_saida,
                                                cst_icms_entrada, cst_icms_saida, aliq_icms, aliq_ipi, aliq_pis, aliq_cofins, ativo)
                    VALUES (@e, fiscal.fn_proximo_produto(@e), @d, @un, @ncm, @cest, @g, @orig, @tipo,
                            @ce, @cs, @cste, @csts, @aicms, @aipi, @apis, @acof, @ativo)
                    RETURNING id
                    """, con, tx)
                : new NpgsqlCommand("""
                    UPDATE fiscal.produto SET descricao = @d, unidade = @un, ncm = @ncm, cest = @cest, gtin = @g, origem = @orig, tipo_item = @tipo,
                           cfop_entrada = @ce, cfop_saida = @cs, cst_icms_entrada = @cste, cst_icms_saida = @csts,
                           aliq_icms = @aicms, aliq_ipi = @aipi, aliq_pis = @apis, aliq_cofins = @acof, ativo = @ativo
                     WHERE id = @id AND empresa_id = @e RETURNING id
                    """, con, tx).Com("id", p.Id);
            cmd.Com("e", empresaId).Com("d", p.Descricao).Com("un", p.Unidade).ComTexto("ncm", p.Ncm).ComTexto("cest", p.Cest).ComTexto("g", p.Gtin)
               .Com("orig", p.Origem).Com("tipo", p.TipoItem).ComTexto("ce", p.CfopEntrada).ComTexto("cs", p.CfopSaida)
               .ComTexto("cste", p.CstIcmsEntrada).ComTexto("csts", p.CstIcmsSaida)
               .Com("aicms", p.AliqIcms).Com("aipi", p.AliqIpi).Com("apis", p.AliqPis).Com("acof", p.AliqCofins).Com("ativo", p.Ativo);
            var id = await cmd.ExecuteScalarAsync(ct) as long? ?? throw new ViolacaoRegraException("Produto não encontrado.");
            await tx.CommitAsync(ct);
            return id;
        });

    public async Task<int> ContarUsoAsync(long empresaId, long produtoId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT count(*) FROM fiscal.documento_item i JOIN fiscal.documento d ON d.id = i.documento_id
             WHERE d.empresa_id = @e AND i.produto_id = @p
            """).Com("e", empresaId).Com("p", produtoId);
        return (int)(long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<bool> ExcluirAsync(long empresaId, long produtoId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("DELETE FROM fiscal.produto WHERE id = @p AND empresa_id = @e").Com("p", produtoId).Com("e", empresaId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<IReadOnlyList<LinhaItemNota>> ListarItensNotasAsync(long empresaId, FiltroItensNotas f, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT d.data_entrada_saida, d.tipo_operacao, d.modelo, d.serie, d.numero,
                   CASE WHEN d.tipo_operacao = 'E' THEN d.emitente_nome ELSE d.destinatario_nome END,
                   CASE WHEN d.tipo_operacao = 'E' THEN d.emitente_documento ELSE d.destinatario_documento END,
                   i.n_item, p.codigo, i.descricao, i.ncm, i.cfop, i.unidade, i.quantidade, i.valor_unitario, i.valor_produto, i.valor_contabil,
                   i.vl_icms, i.vl_icms_st, i.vl_ipi, i.vl_pis, i.vl_cofins, i.vl_iss
              FROM fiscal.documento d
              JOIN fiscal.documento_item i ON i.documento_id = d.id
              LEFT JOIN fiscal.produto p ON p.id = i.produto_id
             WHERE d.empresa_id = @e AND d.situacao = 'AUTORIZADA' AND d.data_entrada_saida BETWEEN @ini AND @fim
               AND (@op::text IS NULL OR d.tipo_operacao = @op)
               AND (@part::bigint IS NULL OR d.participante_id = @part)
               AND (@prod::bigint IS NULL OR i.produto_id = @prod)
               AND (@cfop::text IS NULL OR i.cfop LIKE @cfop || '%')
               AND (@ncm::text IS NULL OR i.ncm LIKE @ncm || '%')
             ORDER BY d.data_entrada_saida, d.id, i.n_item
             LIMIT @lim
            """)
            .Com("e", empresaId).Com("ini", f.Inicio).Com("fim", f.Fim).Com("op", NpgsqlDbType.Text, f.Operacao?.Codigo())
            .Com("part", NpgsqlDbType.Bigint, f.ParticipanteId).Com("prod", NpgsqlDbType.Bigint, f.ProdutoId)
            .Com("cfop", NpgsqlDbType.Text, string.IsNullOrWhiteSpace(f.Cfop) ? null : f.Cfop.Trim().ToUpperInvariant())
            .Com("ncm", NpgsqlDbType.Text, string.IsNullOrWhiteSpace(f.Ncm) ? null : new string(f.Ncm.Where(char.IsAsciiDigit).ToArray()) is { Length: > 0 } n ? n : null)
            .Com("lim", f.Limite);
        var res = new List<LinhaItemNota>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new LinhaItemNota(r.GetFieldValue<DateOnly>(0), ModelosExtensions.OperacaoDe(r.GetString(1)), ModelosExtensions.ModeloDe(r.GetString(2)), r.GetString(3), r.GetString(4),
                r.Texto(5), r.Texto(6), r.GetInt32(7), r.IsDBNull(8) ? null : r.GetInt32(8), r.Texto(9), r.Texto(10), r.GetString(11), r.Texto(12),
                r.GetDecimal(13), r.GetDecimal(14), r.GetDecimal(15), r.GetDecimal(16), r.GetDecimal(17), r.GetDecimal(18), r.GetDecimal(19),
                r.GetDecimal(20), r.GetDecimal(21), r.GetDecimal(22)));
        return res;
    }
}
