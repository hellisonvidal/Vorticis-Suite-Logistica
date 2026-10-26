using Npgsql;
using Vorticis.Fiscal.Postgres;
using NpgsqlTypes;
using Vorticis.Fiscal.Lucro;
using Vorticis.Fiscal.Simples;

namespace Vorticis.Fiscal.Sped;

/// <summary>EFD gerada, para o histórico (fiscal.efd_gerada).</summary>
public sealed record EfdRegistrada(long Id, TipoEfd Tipo, DateOnly Inicio, DateOnly Fim, string? Perfil, string Finalidade, string HashSha1, int Linhas, int Assinaturas,
                                   DateTimeOffset GeradoEm);

public enum TipoEfd { IcmsIpi, Contribuicoes }

/// <summary>Leitura dos dados da EFD e histórico dos arquivos gerados.</summary>
public interface IRepositorioEfd
{
    Task<EmpresaEfd?> EmpresaAsync(long empresaId, DateOnly inicio, DateOnly fim, CancellationToken ct);
    Task<ContadorEfd?> ContadorAsync(long empresaId, DateOnly inicio, DateOnly fim, CancellationToken ct);
    Task<IReadOnlyList<DocumentoEfd>> DocumentosAsync(long empresaId, DateOnly inicio, DateOnly fim, CancellationToken ct);
    Task<ApuracaoPisCofinsEfd?> ApuracaoPisCofinsAsync(long empresaId, DateOnly competencia, CancellationToken ct);
    /// <summary>Ajustes da apuração do ICMS (E111) da competência.</summary>
    Task<IReadOnlyList<AjusteIcmsEfd>> AjustesIcmsAsync(long empresaId, DateOnly competencia, CancellationToken ct);
    /// <summary>Contas de energia, gás, água e comunicação com entrada no período (C500/D500).</summary>
    Task<IReadOnlyList<ContaConsumoEfd>> ContasConsumoAsync(long empresaId, DateOnly inicio, DateOnly fim, CancellationToken ct);
    /// <summary>Inventário marcado para ser informado na EFD da competência (bloco H), se houver.</summary>
    Task<InventarioEfd?> InventarioAsync(long empresaId, DateOnly competencia, CancellationToken ct);
    Task<ConfiguracaoEfd> ConfiguracaoAsync(long empresaId, CancellationToken ct);
    Task SalvarConfiguracaoAsync(long empresaId, ConfiguracaoEfd c, CancellationToken ct);
    Task<long> RegistrarAsync(long empresaId, TipoEfd tipo, DateOnly inicio, DateOnly fim, string? perfil, string finalidade, string hashSha1, int linhas, int assinaturas,
                              long? usuarioId, CancellationToken ct);
    Task<IReadOnlyList<EfdRegistrada>> ListarAsync(long empresaId, TipoEfd? tipo, CancellationToken ct);
}

public sealed class RepositorioEfd(NpgsqlDataSource ds) : IRepositorioEfd
{
    public async Task<EmpresaEfd?> EmpresaAsync(long empresaId, DateOnly inicio, DateOnly fim, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT e.cnpj, e.razao_social, e.nome_fantasia, e.uf, e.cod_municipio_ibge, e.inscricao_estadual, e.inscricao_municipal, e.cep, e.logradouro, e.numero,
                   e.complemento, e.bairro, e.telefone, e.email,
                   (SELECT r.regime::text FROM core.empresa_regime r WHERE r.empresa_id = e.id AND r.vigencia_inicio <= @fim AND (r.vigencia_fim IS NULL OR r.vigencia_fim >= @ini)
                     ORDER BY r.vigencia_inicio DESC LIMIT 1),
                   COALESCE((SELECT r.regime_caixa FROM core.empresa_regime r WHERE r.empresa_id = e.id AND r.vigencia_inicio <= @fim AND (r.vigencia_fim IS NULL OR r.vigencia_fim >= @ini)
                     ORDER BY r.vigencia_inicio DESC LIMIT 1), FALSE)
              FROM core.empresa e WHERE e.id = @e
            """).Com("e", empresaId).Com("ini", inicio).Com("fim", fim);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new EmpresaEfd(r.GetString(0).Trim(), r.GetString(1), r.Texto(2), r.GetString(3).Trim(), r.GetString(4).Trim(), r.Texto(5), r.Texto(6), r.Texto(7), r.Texto(8),
            r.Texto(9), r.Texto(10), r.Texto(11), r.Texto(12), r.Texto(13), r.Texto(14), r.GetBoolean(15));
    }

    public async Task<ContadorEfd?> ContadorAsync(long empresaId, DateOnly inicio, DateOnly fim, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT c.nome, c.cpf, c.crc, es.cnpj, COALESCE(c.email, es.email), COALESCE(c.telefone, es.telefone)
              FROM core.empresa_contabilidade ec
              JOIN core.contador c ON c.id = ec.contador_id
              LEFT JOIN core.escritorio_contabil es ON es.id = c.escritorio_id
             WHERE ec.empresa_id = @e AND ec.inicio <= @fim AND (ec.fim IS NULL OR ec.fim >= @ini)
             ORDER BY ec.responsavel_tecnico DESC, ec.inicio DESC LIMIT 1
            """).Com("e", empresaId).Com("ini", inicio).Com("fim", fim);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new ContadorEfd(r.GetString(0), r.GetString(1).Trim(), r.GetString(2), r.Texto(3), r.Texto(4), r.Texto(5)) : null;
    }

    public async Task<IReadOnlyList<DocumentoEfd>> DocumentosAsync(long empresaId, DateOnly inicio, DateOnly fim, CancellationToken ct)
    {
        var cabecalhos = new List<(DocumentoEfd Doc, long Id)>();
        await using (var cmd = ds.CreateCommand("""
            SELECT d.id, d.modelo, d.chave_acesso, d.tipo_operacao, d.numero, d.serie, d.data_emissao, d.data_entrada_saida, d.situacao,
                   d.emitente_documento, d.emitente_nome, d.emitente_uf, d.destinatario_documento, d.destinatario_nome, d.destinatario_uf,
                   d.valor_produtos, d.valor_desconto, d.valor_frete, d.valor_seguro, d.valor_outras, d.valor_total, d.vl_bc_icms, d.vl_icms, d.vl_bc_icms_st, d.vl_icms_st,
                   d.vl_ipi, d.vl_pis, d.vl_cofins, d.vl_iss, d.vl_ret_pis, d.vl_ret_cofins,
                   p.codigo, p.nome, p.cpf_cnpj, p.inscricao_estadual, p.cod_municipio_ibge, p.uf, p.logradouro, p.numero, p.complemento, p.bairro, p.cod_pais
              FROM fiscal.documento d
              LEFT JOIN core.participante p ON p.id = d.participante_id
             WHERE d.empresa_id = @e AND d.data_entrada_saida BETWEEN @ini AND @fim
             ORDER BY d.data_entrada_saida, d.id
            """).Com("e", empresaId).Com("ini", inicio).Com("fim", fim))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                var entrada = r.GetString(3) == "E";
                ParticipanteEfd? part = r.IsDBNull(31)
                    ? Sintetico(entrada, r.Texto(9), r.Texto(10), r.Texto(11), r.Texto(12), r.Texto(13), r.Texto(14))
                    : new ParticipanteEfd(r.GetString(31), r.GetString(32), r.Texto(33), r.Texto(34), r.Texto(35), r.Texto(36), r.Texto(37), r.Texto(38), r.Texto(39), r.Texto(40),
                                          r.GetString(41).Trim(), true);
                var doc = new DocumentoEfd(r.GetInt64(0), r.GetString(1), r.Texto(2), entrada, r.GetString(4).Trim(), r.GetString(5).Trim(), r.GetFieldValue<DateOnly>(6),
                    r.GetFieldValue<DateOnly>(7), r.GetString(8).Trim(), r.Texto(9), r.Texto(10), r.Texto(11), r.Texto(12), r.Texto(13), r.Texto(14), part,
                    r.GetDecimal(15), r.GetDecimal(16), r.GetDecimal(17), r.GetDecimal(18), r.GetDecimal(19), r.GetDecimal(20), r.GetDecimal(21), r.GetDecimal(22),
                    r.GetDecimal(23), r.GetDecimal(24), r.GetDecimal(25), r.GetDecimal(26), r.GetDecimal(27), r.GetDecimal(28), r.GetDecimal(29), r.GetDecimal(30), []);
                cabecalhos.Add((doc, doc.Id));
            }
        if (cabecalhos.Count == 0) return [];

        var itens = new Dictionary<long, List<ItemEfd>>();
        await using (var cmd = ds.CreateCommand("""
            SELECT i.documento_id, i.n_item, i.produto_id, pr.codigo, i.cod_produto, COALESCE(pr.descricao, i.descricao), COALESCE(NULLIF(i.ncm, ''), pr.ncm),
                   COALESCE(NULLIF(i.cest, ''), pr.cest), COALESCE(i.gtin, pr.gtin), pr.tipo_item, pr.aliq_icms, COALESCE(i.cfop_escrituracao, i.cfop), i.unidade,
                   i.quantidade, i.valor_produto, i.valor_desconto, i.valor_frete, i.valor_seguro, i.valor_outras, i.valor_contabil, i.cst_icms, i.origem_mercadoria,
                   i.vl_bc_icms, i.aliq_icms, i.vl_icms, i.vl_bc_icms_st, i.vl_icms_st, i.cst_ipi, i.vl_bc_ipi, i.aliq_ipi, i.vl_ipi,
                   i.cst_pis, i.vl_bc_pis, i.aliq_pis, i.vl_pis, i.cst_cofins, i.vl_bc_cofins, i.aliq_cofins, i.vl_cofins, i.vl_bc_iss, i.aliq_iss, i.vl_iss, d.modelo
              FROM fiscal.documento_item i
              JOIN fiscal.documento d ON d.id = i.documento_id
              LEFT JOIN fiscal.produto pr ON pr.id = i.produto_id
             WHERE d.empresa_id = @e AND d.data_entrada_saida BETWEEN @ini AND @fim
             ORDER BY i.documento_id, i.n_item
            """).Com("e", empresaId).Com("ini", inicio).Com("fim", fim))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                var servico = r.GetString(42) == "NFSE";
                var item = new ItemEfd(r.GetInt32(1), r.LongOuNulo(2), r.IsDBNull(3) ? null : r.GetInt32(3), r.Texto(4) ?? (servico ? "SERVICO" : null),
                    r.Texto(5) ?? "Item sem descrição", r.Texto(6), r.Texto(7), r.Texto(8), servico ? "09" : r.IsDBNull(9) ? "00" : r.GetString(9).Trim(),
                    r.IsDBNull(10) ? null : r.GetDecimal(10), r.GetString(11).Trim(), r.Texto(12) ?? "UN", r.GetDecimal(13), r.GetDecimal(14), r.GetDecimal(15), r.GetDecimal(16),
                    r.GetDecimal(17), r.GetDecimal(18), r.GetDecimal(19), r.Texto(20), r.Texto(21), r.GetDecimal(22), r.GetDecimal(23), r.GetDecimal(24), r.GetDecimal(25),
                    r.GetDecimal(26), r.Texto(27), r.GetDecimal(28), r.GetDecimal(29), r.GetDecimal(30), r.Texto(31), r.GetDecimal(32), r.GetDecimal(33), r.GetDecimal(34),
                    r.Texto(35), r.GetDecimal(36), r.GetDecimal(37), r.GetDecimal(38), r.GetDecimal(39), r.GetDecimal(40), r.GetDecimal(41));
                var id = r.GetInt64(0);
                if (!itens.TryGetValue(id, out var lista)) itens[id] = lista = [];
                lista.Add(item);
            }
        return cabecalhos.Select(c => c.Doc with { Itens = itens.GetValueOrDefault(c.Id) ?? [] }).ToList();
    }

    public async Task<IReadOnlyList<AjusteIcmsEfd>> AjustesIcmsAsync(long empresaId, DateOnly competencia, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT id, competencia, codigo, descricao, valor FROM fiscal.efd_ajuste_icms WHERE empresa_id = @e AND competencia = @c ORDER BY codigo, id
            """).Com("e", empresaId).Com("c", new DateOnly(competencia.Year, competencia.Month, 1));
        var res = new List<AjusteIcmsEfd>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(new AjusteIcmsEfd(r.GetInt64(0), r.GetFieldValue<DateOnly>(1), r.GetString(2), r.GetString(3), r.GetDecimal(4)));
        return res;
    }

    public async Task<IReadOnlyList<ContaConsumoEfd>> ContasConsumoAsync(long empresaId, DateOnly inicio, DateOnly fim, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT c.id, c.modelo, c.numero, c.serie, c.data_emissao, c.data_entrada, c.classe_consumo, c.valor, c.cfop, c.cst_icms, c.base_icms, c.aliq_icms,
                   c.valor_icms, c.valor_pis, c.valor_cofins,
                   p.codigo, p.nome, p.cpf_cnpj, p.inscricao_estadual, p.cod_municipio_ibge, p.uf, p.logradouro, p.numero, p.complemento, p.bairro, p.cod_pais
              FROM fiscal.conta_consumo c JOIN core.participante p ON p.id = c.participante_id
             WHERE c.empresa_id = @e AND c.data_entrada BETWEEN @ini AND @fim
             ORDER BY c.data_entrada, c.id
            """).Com("e", empresaId).Com("ini", inicio).Com("fim", fim);
        var res = new List<ContaConsumoEfd>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var part = new ParticipanteEfd(r.GetString(15), r.GetString(16), r.Texto(17), r.Texto(18), r.Texto(19), r.Texto(20), r.Texto(21), r.Texto(22), r.Texto(23),
                                           r.Texto(24), r.GetString(25).Trim(), true);
            res.Add(new ContaConsumoEfd(r.GetInt64(0), r.GetString(1), part, r.GetString(2), r.GetString(3), r.GetFieldValue<DateOnly>(4), r.GetFieldValue<DateOnly>(5),
                r.GetString(6), r.GetDecimal(7), r.GetString(8), r.GetString(9), r.GetDecimal(10), r.GetDecimal(11), r.GetDecimal(12), r.GetDecimal(13), r.GetDecimal(14)));
        }
        return res;
    }

    public async Task<InventarioEfd?> InventarioAsync(long empresaId, DateOnly competencia, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT i.id, i.data, i.motivo, i.conta_contabil, p.codigo, p.descricao, it.unidade, p.ncm, p.tipo_item, it.quantidade, it.valor_unitario, it.valor
              FROM fiscal.inventario i
              LEFT JOIN fiscal.inventario_item it ON it.inventario_id = i.id
              LEFT JOIN fiscal.produto p ON p.id = it.produto_id
             WHERE i.empresa_id = @e AND i.competencia_efd = @c
             ORDER BY i.data DESC, p.codigo
            """).Com("e", empresaId).Com("c", new DateOnly(competencia.Year, competencia.Month, 1));
        InventarioEfd? inv = null;
        long? primeiro = null;
        var itens = new List<ItemInventarioEfd>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            primeiro ??= r.GetInt64(0);
            if (r.GetInt64(0) != primeiro) break;   // um inventário por EFD: o de data mais recente
            inv ??= new InventarioEfd(r.GetFieldValue<DateOnly>(1), r.GetString(2), r.GetString(3), itens);
            if (!r.IsDBNull(4))
                itens.Add(new ItemInventarioEfd(r.GetInt32(4), r.GetString(5), r.GetString(6), r.Texto(7), r.GetString(8).Trim(), r.GetDecimal(9), r.GetDecimal(10), r.GetDecimal(11)));
        }
        return inv;
    }

    /// <summary>Participante montado com os dados da própria nota (a outra parte da operação), quando a nota não tem cadastro vinculado.</summary>
    private static ParticipanteEfd? Sintetico(bool entrada, string? emitDoc, string? emitNome, string? emitUf, string? destDoc, string? destNome, string? destUf)
    {
        var (doc, nome, uf) = entrada ? (emitDoc, emitNome, emitUf) : (destDoc, destNome, destUf);
        var digitos = CamposEfd.Digitos(doc);
        return digitos.Length == 0 && string.IsNullOrWhiteSpace(nome) ? null
            : new ParticipanteEfd(digitos.Length > 0 ? "DOC" + digitos : "NOME" + Math.Abs((nome ?? "").GetHashCode()), nome ?? "Sem nome", digitos.Length > 0 ? digitos : null,
                                  null, null, uf, null, null, null, null, "01058", false);
    }

    public async Task<ApuracaoPisCofinsEfd?> ApuracaoPisCofinsAsync(long empresaId, DateOnly competencia, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT regime, receita_base, base_credito, pis_debito, cofins_debito, pis_credito, cofins_credito, pis_retido, cofins_retido, pis_a_pagar, cofins_a_pagar, resultado::text
              FROM fiscal.apuracao_pis_cofins WHERE empresa_id = @e AND competencia = @c
            """).Com("e", empresaId).Com("c", new DateOnly(competencia.Year, competencia.Month, 1));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var resultado = JsonFiscal.Ler<ResultadoPisCofins>(r.GetString(11));
        return new ApuracaoPisCofinsEfd(new DateOnly(competencia.Year, competencia.Month, 1), r.GetString(0), r.GetDecimal(1), resultado.AliquotaPis, resultado.AliquotaCofins,
            r.GetDecimal(3), r.GetDecimal(4), r.GetDecimal(2), r.GetDecimal(5), r.GetDecimal(6), r.GetDecimal(7), r.GetDecimal(8), r.GetDecimal(9), r.GetDecimal(10));
    }

    public async Task<ConfiguracaoEfd> ConfiguracaoAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT icms_perfil, icms_ind_ativ, icms_cod_receita, icms_cod_receita_st, icms_dia_vencimento, contrib_ind_nat_pj, contrib_ind_ativ
              FROM fiscal.efd_configuracao WHERE empresa_id = @e
            """).Com("e", empresaId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)
            ? new ConfiguracaoEfd(r.GetString(0)[0], r.GetString(1)[0], r.Texto(2), r.Texto(3), r.GetInt16(4), r.GetString(5).Trim(), r.GetString(6)[0])
            : new ConfiguracaoEfd();
    }

    public async Task SalvarConfiguracaoAsync(long empresaId, ConfiguracaoEfd c, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            INSERT INTO fiscal.efd_configuracao (empresa_id, icms_perfil, icms_ind_ativ, icms_cod_receita, icms_cod_receita_st, icms_dia_vencimento, contrib_ind_nat_pj, contrib_ind_ativ)
            VALUES (@e, @p, @a, @r, @rs, @d, @n, @ca)
            ON CONFLICT (empresa_id) DO UPDATE SET icms_perfil = @p, icms_ind_ativ = @a, icms_cod_receita = @r, icms_cod_receita_st = @rs, icms_dia_vencimento = @d,
                   contrib_ind_nat_pj = @n, contrib_ind_ativ = @ca, atualizado_em = now()
            """).Com("e", empresaId).Com("p", c.IcmsPerfil.ToString()).Com("a", c.IcmsIndicadorAtividade.ToString()).ComTexto("r", c.IcmsCodigoReceita)
                .ComTexto("rs", c.IcmsCodigoReceitaSt).Com("d", (short)c.IcmsDiaVencimento).Com("n", c.ContribNaturezaPj).Com("ca", c.ContribIndicadorAtividade.ToString());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<long> RegistrarAsync(long empresaId, TipoEfd tipo, DateOnly inicio, DateOnly fim, string? perfil, string finalidade, string hashSha1, int linhas, int assinaturas,
                                           long? usuarioId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            INSERT INTO fiscal.efd_gerada (empresa_id, tipo, dt_ini, dt_fin, perfil, finalidade, hash_sha1, linhas, assinaturas, gerado_por)
            VALUES (@e, @t, @ini, @fim, @p, @f, @h, @l, @s, (SELECT id FROM core.usuario WHERE id = @u))
            RETURNING id
            """).Com("e", empresaId).Com("t", tipo == TipoEfd.IcmsIpi ? "ICMS_IPI" : "CONTRIBUICOES").Com("ini", inicio).Com("fim", fim).Com("p", NpgsqlDbType.Char, perfil)
                .Com("f", finalidade).Com("h", hashSha1).Com("l", linhas).Com("s", (short)assinaturas).Com("u", NpgsqlDbType.Bigint, usuarioId);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<IReadOnlyList<EfdRegistrada>> ListarAsync(long empresaId, TipoEfd? tipo, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT id, tipo, dt_ini, dt_fin, perfil, finalidade, hash_sha1, linhas, assinaturas, gerado_em
              FROM fiscal.efd_gerada WHERE empresa_id = @e AND (@t::text IS NULL OR tipo = @t) ORDER BY gerado_em DESC, id DESC
            """).Com("e", empresaId).Com("t", NpgsqlDbType.Text, tipo is null ? null : tipo == TipoEfd.IcmsIpi ? "ICMS_IPI" : "CONTRIBUICOES");
        var res = new List<EfdRegistrada>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new EfdRegistrada(r.GetInt64(0), r.GetString(1) == "ICMS_IPI" ? TipoEfd.IcmsIpi : TipoEfd.Contribuicoes, r.GetFieldValue<DateOnly>(2),
                r.GetFieldValue<DateOnly>(3), r.Texto(4), r.GetString(5).Trim(), r.GetString(6), r.GetInt32(7), r.GetInt16(8), r.GetFieldValue<DateTimeOffset>(9)));
        return res;
    }
}
