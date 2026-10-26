using Npgsql;
using Vorticis.Application.Relatorios;
using Vorticis.Domain.Comum;
using Vorticis.Infra.Postgres;
using Vorticis.Infra.Relatorios;
using P = Vorticis.Domain.Seguranca.Permissao;

namespace Vorticis.Frota;

/// <summary>
/// TMS (gestão do transporte): tabelas de frete, cotações, conhecimentos (CT-e), manifestos (MDF-e), ocorrências de entrega e faturas de frete.
/// Alterações exigem FROTA_TMS; a consulta vale para quem tem qualquer função da Frota. A transmissão à SEFAZ fica para depois: o CT-e e o MDF-e
/// são registrados com a chave de acesso informada pelo usuário.
/// </summary>
public sealed partial class ServicoFrota
{
    private static async Task<int> Exec(NpgsqlConnection con, NpgsqlTransaction tx, string sql, CancellationToken ct, params (string, object?)[] p)
    {
        await using var cmd = new NpgsqlCommand(sql, con, tx);
        foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<T?> Escalar<T>(NpgsqlConnection con, NpgsqlTransaction tx, string sql, CancellationToken ct, params (string, object?)[] p)
    {
        await using var cmd = new NpgsqlCommand(sql, con, tx);
        foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        var v2 = await cmd.ExecuteScalarAsync(ct);
        return v2 is null or DBNull ? default : (T)v2;
    }

    // ------------------------------------------------------------------ tabelas de frete

    public async Task<List<TabelaFrete>> TabelasFreteAsync(long empresa, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await ListarTabelasFreteAsync(empresa, ct) : [];

    private Task<List<TabelaFrete>> ListarTabelasFreteAsync(long empresa, CancellationToken ct) => _db.ListarAsync("""
        SELECT t.id, t.codigo, t.descricao, t.rota_id, t.uf_origem, t.uf_destino, t.valor_kg, t.frete_minimo, t.fator_cubagem, t.ad_valorem_pct, t.gris_pct,
               t.pedagio_fracao, t.taxa_coleta, t.taxa_entrega, t.outras_taxas, t.aliquota_icms, t.prazo_dias, t.ativa, r.descricao
          FROM tms.tabela_frete t LEFT JOIN frota.rota r ON r.id = t.rota_id WHERE t.empresa_id = @e ORDER BY t.codigo
        """, r => new TabelaFrete(r.GetInt64(0), r.GetInt32(1), r.GetString(2), r.Id(3), r.Texto(4), r.Texto(5), r.GetDecimal(6), r.GetDecimal(7), r.GetDecimal(8),
                                  r.GetDecimal(9), r.GetDecimal(10), r.GetDecimal(11), r.GetDecimal(12), r.GetDecimal(13), r.GetDecimal(14), r.GetDecimal(15), r.GetInt32(16),
                                  r.GetBoolean(17), r.Texto(18)), ct, ("e", empresa));

    public async Task<Resultado<long>> GravarTabelaFreteAsync(long empresa, TabelaFrete t, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaTms, ct) is { } n) return Resultado<long>.Falha(n);
        t = t with { Descricao = t.Descricao?.Trim() ?? "", UfOrigem = N(t.UfOrigem)?.ToUpperInvariant(), UfDestino = N(t.UfDestino)?.ToUpperInvariant() };
        if (RegrasTms.Validar(t) is { Count: > 0 } erros) return Resultado<long>.Falha(erros);
        (string, object?)[] p =
        [
            ("e", empresa), ("id", t.Id), ("c", t.Codigo), ("d", t.Descricao), ("r", t.RotaId), ("uo", t.UfOrigem), ("ud", t.UfDestino), ("kg", t.ValorKg), ("min", t.FreteMinimo),
            ("cub", t.FatorCubagem), ("adv", t.AdValoremPct), ("gris", t.GrisPct), ("ped", t.PedagioFracao), ("tc", t.TaxaColeta), ("te", t.TaxaEntrega), ("to", t.OutrasTaxas),
            ("icms", t.AliquotaIcms), ("pz", t.PrazoDias), ("a", t.Ativa),
        ];
        return await Tentar(async () => t.Id == 0
            ? await _db.EscalarAsync<long>("""
                INSERT INTO tms.tabela_frete (empresa_id, codigo, descricao, rota_id, uf_origem, uf_destino, valor_kg, frete_minimo, fator_cubagem, ad_valorem_pct, gris_pct,
                                              pedagio_fracao, taxa_coleta, taxa_entrega, outras_taxas, aliquota_icms, prazo_dias, ativa)
                VALUES (@e, @c, @d, @r, @uo, @ud, @kg, @min, @cub, @adv, @gris, @ped, @tc, @te, @to, @icms, @pz, @a) RETURNING id
                """, ct, p)
            : await _db.EscalarAsync<long?>("""
                UPDATE tms.tabela_frete SET codigo = @c, descricao = @d, rota_id = @r, uf_origem = @uo, uf_destino = @ud, valor_kg = @kg, frete_minimo = @min, fator_cubagem = @cub,
                       ad_valorem_pct = @adv, gris_pct = @gris, pedagio_fracao = @ped, taxa_coleta = @tc, taxa_entrega = @te, outras_taxas = @to, aliquota_icms = @icms,
                       prazo_dias = @pz, ativa = @a WHERE id = @id AND empresa_id = @e RETURNING id
                """, ct, p) ?? throw new ViolacaoRegraException("Tabela de frete não encontrada."));
    }

    public async Task<Resultado> ExcluirTabelaFreteAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaTms, ct) is { } n) return Resultado.Falha(n);
        var r = await Tentar(() => _db.ExecutarAsync("DELETE FROM tms.tabela_frete WHERE id = @id AND empresa_id = @e", ct, ("id", id), ("e", empresa)));
        return r.Sucesso ? r : Resultado.Falha("A tabela já foi usada em cotações: em vez de excluir, desmarque Ativa.");
    }

    // ------------------------------------------------------------------ cotações

    public async Task<List<Cotacao>> CotacoesAsync(long empresa, DateOnly inicio, DateOnly fim, string? situacao, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await ListarCotacoesAsync(empresa, inicio, fim, situacao, ct) : [];

    private Task<List<Cotacao>> ListarCotacoesAsync(long empresa, DateOnly inicio, DateOnly fim, string? situacao, CancellationToken ct) => _db.ListarAsync("""
        SELECT c.id, c.numero, c.data, c.validade, c.cliente_id, c.tabela_id, c.origem, c.destino, c.peso_kg, c.volume_m3, c.valor_mercadoria, c.coleta, c.entrega,
               c.valor_total, c.situacao, c.observacao, p.nome, t.codigo || ' – ' || t.descricao
          FROM tms.cotacao c JOIN core.participante p ON p.id = c.cliente_id JOIN tms.tabela_frete t ON t.id = c.tabela_id
         WHERE c.empresa_id = @e AND c.data BETWEEN @i AND @f AND (@s::text IS NULL OR c.situacao = @s) ORDER BY c.data DESC, c.numero DESC
        """, r => new Cotacao(r.GetInt64(0), r.GetInt32(1), r.GetFieldValue<DateOnly>(2), r.GetFieldValue<DateOnly>(3), r.GetInt64(4), r.GetInt64(5), r.GetString(6),
                              r.GetString(7), r.GetDecimal(8), r.GetDecimal(9), r.GetDecimal(10), r.GetBoolean(11), r.GetBoolean(12), r.GetDecimal(13), r.GetString(14),
                              r.Texto(15), r.GetString(16), r.GetString(17)), ct, ("e", empresa), ("i", inicio), ("f", fim), ("s", situacao));

    /// <summary>Grava a cotação (só aberta); o valor é sempre recalculado pela tabela escolhida.</summary>
    public async Task<Resultado<Cotacao>> GravarCotacaoAsync(long empresa, Cotacao c, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaTms, ct) is { } n) return Resultado<Cotacao>.Falha(n);
        var erros = new List<string>();
        if (c.ClienteId <= 0) erros.Add("Escolha o cliente.");
        if (string.IsNullOrWhiteSpace(c.Origem) || string.IsNullOrWhiteSpace(c.Destino)) erros.Add("Informe a origem e o destino.");
        if (c.Validade < c.Data) erros.Add("A validade não pode ser antes da data da cotação.");
        if (c.PesoKg <= 0 && c.VolumeM3 <= 0) erros.Add("Informe o peso ou a cubagem da carga.");
        var tabela = (await ListarTabelasFreteAsync(empresa, ct)).FirstOrDefault(t => t.Id == c.TabelaId);
        if (tabela is null) erros.Add("Escolha a tabela de frete.");
        if (erros.Count > 0) return Resultado<Cotacao>.Falha(erros);
        var total = RegrasTms.Calcular(tabela!, c.PesoKg, c.VolumeM3, c.ValorMercadoria, c.Coleta, c.Entrega).Total;
        return await Tentar(async () =>
        {
            if (c.Id != 0 && await _db.EscalarAsync<string>("SELECT situacao FROM tms.cotacao WHERE id = @id AND empresa_id = @e", ct, ("id", c.Id), ("e", empresa)) is { } s && s != "ABERTA")
                throw new ViolacaoRegraException($"A cotação já está {RegrasTms.Rotulo(s).ToLowerInvariant()} e não pode mais ser alterada.");
            var numero = c.Id == 0 ? await _db.EscalarAsync<int>("SELECT COALESCE(max(numero), 0) + 1 FROM tms.cotacao WHERE empresa_id = @e", ct, ("e", empresa)) : c.Numero;
            (string, object?)[] p =
            [
                ("e", empresa), ("id", c.Id), ("n", numero), ("d", c.Data), ("v", c.Validade), ("cl", c.ClienteId), ("t", c.TabelaId), ("o", c.Origem.Trim()), ("ds", c.Destino.Trim()),
                ("peso", c.PesoKg), ("m3", c.VolumeM3), ("val", c.ValorMercadoria), ("col", c.Coleta), ("ent", c.Entrega), ("tot", total), ("obs", N(c.Observacao)),
            ];
            var id = c.Id == 0
                ? await _db.EscalarAsync<long>("""
                    INSERT INTO tms.cotacao (empresa_id, numero, data, validade, cliente_id, tabela_id, origem, destino, peso_kg, volume_m3, valor_mercadoria, coleta, entrega,
                                             valor_total, observacao)
                    VALUES (@e, @n, @d, @v, @cl, @t, @o, @ds, @peso, @m3, @val, @col, @ent, @tot, @obs) RETURNING id
                    """, ct, p)
                : await _db.EscalarAsync<long>("""
                    UPDATE tms.cotacao SET data = @d, validade = @v, cliente_id = @cl, tabela_id = @t, origem = @o, destino = @ds, peso_kg = @peso, volume_m3 = @m3,
                           valor_mercadoria = @val, coleta = @col, entrega = @ent, valor_total = @tot, observacao = @obs WHERE id = @id AND empresa_id = @e RETURNING id
                    """, ct, p);
            return c with { Id = id, Numero = numero, ValorTotal = total, Situacao = "ABERTA" };
        });
    }

    /// <summary>Aprova ou recusa a cotação aberta (ou reabre uma recusada).</summary>
    public async Task<Resultado> MudarSituacaoCotacaoAsync(long empresa, long id, string situacao, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaTms, ct) is { } n) return Resultado.Falha(n);
        var de = situacao switch { "APROVADA" or "RECUSADA" => "ABERTA", "ABERTA" => "RECUSADA", _ => null };
        if (de is null) return Resultado.Falha("Situação inválida.");
        return await Tentar(async () =>
        {
            if (await _db.ExecutarAsync("UPDATE tms.cotacao SET situacao = @s WHERE id = @id AND empresa_id = @e AND situacao = @de", ct,
                                        ("s", situacao), ("id", id), ("e", empresa), ("de", de)) == 0)
                throw new ViolacaoRegraException(situacao == "ABERTA" ? "Só cotações recusadas podem ser reabertas." : "Só cotações abertas podem ser aprovadas ou recusadas.");
        });
    }

    public async Task<Resultado> ExcluirCotacaoAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaTms, ct) is { } n) return Resultado.Falha(n);
        return await Tentar(async () =>
        {
            if (await _db.ExecutarAsync("DELETE FROM tms.cotacao WHERE id = @id AND empresa_id = @e AND situacao IN ('ABERTA', 'RECUSADA')", ct, ("id", id), ("e", empresa)) == 0)
                throw new ViolacaoRegraException("Só cotações abertas ou recusadas podem ser excluídas.");
        });
    }

    // ------------------------------------------------------------------ conhecimentos (CT-e)

    private const string SelectCte = """
        SELECT c.id, c.serie, c.numero, c.emissao, c.cfop, c.remetente_id, c.destinatario_id, c.tomador_id, c.origem_uf, c.origem_ibge, c.origem_municipio,
               c.destino_uf, c.destino_ibge, c.destino_municipio, c.tabela_id, c.cotacao_id, c.coleta_id, c.viagem_id, c.veiculo_id, c.motorista_id, c.produto,
               c.volumes, c.peso_kg, c.volume_m3, c.valor_mercadoria, c.chaves_nfe, c.frete_peso, c.ad_valorem, c.gris, c.pedagio, c.taxas, c.aliquota_icms, c.icms,
               c.valor_total, c.previsao_entrega, c.chave, c.protocolo, c.situacao, c.entregue_em, c.recebedor, c.fatura_id, c.observacao,
               rm.nome, ds.nome, tm.nome, v.placa, m.nome, f.numero
          FROM tms.cte c JOIN core.participante rm ON rm.id = c.remetente_id JOIN core.participante ds ON ds.id = c.destinatario_id
               JOIN core.participante tm ON tm.id = c.tomador_id LEFT JOIN frota.veiculo v ON v.id = c.veiculo_id LEFT JOIN frota.motorista m ON m.id = c.motorista_id
               LEFT JOIN tms.fatura f ON f.id = c.fatura_id
        """;

    private static Cte LerCte(NpgsqlDataReader r) => new(
        r.GetInt64(0), r.GetInt16(1), r.GetInt32(2), r.GetFieldValue<DateOnly>(3), r.Texto(4), r.GetInt64(5), r.GetInt64(6), r.GetInt64(7),
        new ParadaRota(r.GetString(8), r.GetString(9), r.GetString(10)), new ParadaRota(r.GetString(11), r.GetString(12), r.GetString(13)),
        r.Id(14), r.Id(15), r.Id(16), r.Id(17), r.Id(18), r.Id(19), r.Texto(20), r.GetInt32(21), r.GetDecimal(22), r.GetDecimal(23), r.GetDecimal(24), r.Texto(25),
        new ComponentesFrete(0, r.GetDecimal(26), r.GetDecimal(27), r.GetDecimal(28), r.GetDecimal(29), r.GetDecimal(30), r.GetDecimal(31), r.GetDecimal(32), r.GetDecimal(33)),
        r.Data(34), r.Texto(35), r.Texto(36), r.GetString(37), r.IsDBNull(38) ? null : r.GetDateTime(38), r.Texto(39), r.Id(40), r.Texto(41),
        r.GetString(42), r.GetString(43), r.GetString(44), r.Texto(45), r.Texto(46), r.IsDBNull(47) ? null : r.GetInt32(47));

    public async Task<List<Cte>> CtesAsync(long empresa, DateOnly inicio, DateOnly fim, string? situacao, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await ListarCtesAsync(empresa, inicio, fim, situacao, ct) : [];

    private Task<List<Cte>> ListarCtesAsync(long empresa, DateOnly inicio, DateOnly fim, string? situacao, CancellationToken ct) => _db.ListarAsync(SelectCte + """

         WHERE c.empresa_id = @e AND c.emissao BETWEEN @i AND @f AND (@s::text IS NULL OR c.situacao = @s) ORDER BY c.emissao DESC, c.serie, c.numero DESC
        """, LerCte, ct, ("e", empresa), ("i", inicio), ("f", fim), ("s", situacao));

    private async Task<Cte?> CteAsync(long empresa, long id, CancellationToken ct)
        => (await _db.ListarAsync(SelectCte + "\n WHERE c.empresa_id = @e AND c.id = @id", LerCte, ct, ("e", empresa), ("id", id))).FirstOrDefault();

    /// <summary>CT-e em trânsito ou à espera (emitidos, não entregues nem cancelados), de qualquer data, para o acompanhamento das entregas.</summary>
    public async Task<List<Cte>> CtesEmAbertoAsync(long empresa, long usuario, CancellationToken ct = default)
        => !await PodeVerAsync(usuario, empresa, ct) ? [] : await _db.ListarAsync(SelectCte + """

         WHERE c.empresa_id = @e AND c.situacao IN ('EMITIDO', 'EM_TRANSITO') ORDER BY c.previsao_entrega NULLS LAST, c.emissao, c.numero
        """, LerCte, ct, ("e", empresa));

    /// <summary>Grava o CT-e digitado. Novo: número seguinte da série. Com cotação aprovada, a cotação passa a "convertida em CT-e".</summary>
    public async Task<Resultado<Cte>> GravarCteAsync(long empresa, Cte c, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaTms, ct) is { } n) return Resultado<Cte>.Falha(n);
        var f = c.Frete;
        c = c with
        {
            Cfop = N(c.Cfop), Produto = N(c.Produto), Observacao = N(c.Observacao), ChavesNfe = RegrasTms.ChavesNfe(c.ChavesNfe) is { Count: > 0 } ch ? string.Join('\n', ch) : null,
            Frete = RegrasTms.ComIcms(f.FretePeso, f.AdValorem, f.Gris, f.Pedagio, f.Taxas, f.AliquotaIcms, f.PesoTaxado),
        };
        if (RegrasTms.Validar(c) is { Count: > 0 } erros) return Resultado<Cte>.Falha(erros);
        if (c.PrevisaoEntrega is null && c.TabelaId is { } tid && (await ListarTabelasFreteAsync(empresa, ct)).FirstOrDefault(t => t.Id == tid) is { } tab)
            c = c with { PrevisaoEntrega = RegrasTms.Previsao(c.Emissao, tab.PrazoDias) };
        return await Tentar(() => EmTransacaoAsync(async (con, tx) =>
        {
            if (c.Id != 0)
            {
                var atual = await Escalar<string>(con, tx, "SELECT situacao FROM tms.cte WHERE id = @id AND empresa_id = @e FOR UPDATE", ct, ("id", c.Id), ("e", empresa))
                            ?? throw new ViolacaoRegraException("CT-e não encontrado.");
                if (!RegrasTms.CteEditavel(atual)) throw new ViolacaoRegraException($"O CT-e já está {RegrasTms.Rotulo(atual).ToLowerInvariant()} e não pode mais ser alterado.");
            }
            if (c.CotacaoId is { } cot)
            {
                var s = await Escalar<string>(con, tx, "SELECT situacao FROM tms.cotacao WHERE id = @id AND empresa_id = @e FOR UPDATE", ct, ("id", cot), ("e", empresa));
                var deste = c.Id != 0 && await Escalar<long?>(con, tx, "SELECT cotacao_id FROM tms.cte WHERE id = @id", ct, ("id", c.Id)) == cot;
                if (s != "APROVADA" && !(s == "CONVERTIDA" && deste)) throw new ViolacaoRegraException("Só cotações aprovadas podem virar CT-e.");
            }
            var numero = c.Id == 0
                ? await Escalar<int>(con, tx, "SELECT COALESCE(max(numero), 0) + 1 FROM tms.cte WHERE empresa_id = @e AND serie = @s", ct, ("e", empresa), ("s", (short)c.Serie))
                : c.Numero;
            (string, object?)[] p =
            [
                ("e", empresa), ("id", c.Id), ("s", (short)c.Serie), ("n", numero), ("em", c.Emissao), ("cfop", c.Cfop), ("rm", c.RemetenteId), ("ds", c.DestinatarioId),
                ("tm", c.TomadorId), ("ouf", c.Origem.Uf.ToUpperInvariant()), ("oib", c.Origem.MunicipioIbge), ("omu", c.Origem.Municipio), ("duf", c.Destino.Uf.ToUpperInvariant()),
                ("dib", c.Destino.MunicipioIbge), ("dmu", c.Destino.Municipio), ("tab", c.TabelaId), ("cot", c.CotacaoId), ("col", c.ColetaId), ("via", c.ViagemId),
                ("vei", c.VeiculoId), ("mot", c.MotoristaId), ("prod", c.Produto), ("vol", c.Volumes), ("peso", c.PesoKg), ("m3", c.VolumeM3), ("val", c.ValorMercadoria),
                ("nfe", c.ChavesNfe), ("fp", c.Frete.FretePeso), ("adv", c.Frete.AdValorem), ("gris", c.Frete.Gris), ("ped", c.Frete.Pedagio), ("tx", c.Frete.Taxas),
                ("aliq", c.Frete.AliquotaIcms), ("icms", c.Frete.Icms), ("tot", c.Frete.Total), ("prev", c.PrevisaoEntrega), ("obs", c.Observacao),
            ];
            long id;
            if (c.Id == 0)
                id = await Escalar<long>(con, tx, """
                    INSERT INTO tms.cte (empresa_id, serie, numero, emissao, cfop, remetente_id, destinatario_id, tomador_id, origem_uf, origem_ibge, origem_municipio,
                                         destino_uf, destino_ibge, destino_municipio, tabela_id, cotacao_id, coleta_id, viagem_id, veiculo_id, motorista_id, produto,
                                         volumes, peso_kg, volume_m3, valor_mercadoria, chaves_nfe, frete_peso, ad_valorem, gris, pedagio, taxas, aliquota_icms, icms,
                                         valor_total, previsao_entrega, observacao)
                    VALUES (@e, @s, @n, @em, @cfop, @rm, @ds, @tm, @ouf, @oib, @omu, @duf, @dib, @dmu, @tab, @cot, @col, @via, @vei, @mot, @prod, @vol, @peso, @m3, @val,
                            @nfe, @fp, @adv, @gris, @ped, @tx, @aliq, @icms, @tot, @prev, @obs) RETURNING id
                    """, ct, p);
            else
            {
                // cotação trocada ou retirada: a anterior volta a aprovada
                await Exec(con, tx, """
                    UPDATE tms.cotacao SET situacao = 'APROVADA'
                     WHERE situacao = 'CONVERTIDA' AND id = (SELECT cotacao_id FROM tms.cte WHERE id = @id) AND id IS DISTINCT FROM @cot
                    """, ct, ("id", c.Id), ("cot", c.CotacaoId));
                await Exec(con, tx, """
                    UPDATE tms.cte SET emissao = @em, cfop = @cfop, remetente_id = @rm, destinatario_id = @ds, tomador_id = @tm, origem_uf = @ouf, origem_ibge = @oib,
                           origem_municipio = @omu, destino_uf = @duf, destino_ibge = @dib, destino_municipio = @dmu, tabela_id = @tab, cotacao_id = @cot, coleta_id = @col,
                           viagem_id = @via, veiculo_id = @vei, motorista_id = @mot, produto = @prod, volumes = @vol, peso_kg = @peso, volume_m3 = @m3, valor_mercadoria = @val,
                           chaves_nfe = @nfe, frete_peso = @fp, ad_valorem = @adv, gris = @gris, pedagio = @ped, taxas = @tx, aliquota_icms = @aliq, icms = @icms,
                           valor_total = @tot, previsao_entrega = @prev, observacao = @obs
                     WHERE id = @id AND empresa_id = @e
                    """, ct, p);
                id = c.Id;
            }
            if (c.CotacaoId is { } cotacao)
                await Exec(con, tx, "UPDATE tms.cotacao SET situacao = 'CONVERTIDA' WHERE id = @id", ct, ("id", cotacao));
            return c with { Id = id, Numero = numero, Situacao = "DIGITADO" };
        }, ct));
    }

    /// <summary>
    /// Registra a emissão do CT-e com a chave de acesso (44 dígitos, modelo 57) e o protocolo de autorização. A chave tem de ser deste CT-e:
    /// o CNPJ da transportadora, a série e o número que estão dentro dela conferem com o documento.
    /// </summary>
    public async Task<Resultado> EmitirCteAsync(long empresa, long id, string chave, string? protocolo, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaTms, ct) is { } n) return Resultado.Falha(n);
        var c = await CteAsync(empresa, id, ct);
        if (c is null) return Resultado.Falha("CT-e não encontrado.");
        if (c.Situacao != "DIGITADO") return Resultado.Falha("Só o CT-e digitado pode ser emitido.");
        var d = RegrasTms.Digitos(chave);
        if (!RegrasTms.ChaveValida(d, RegrasTms.ModeloCte)) return Resultado.Falha("Chave de acesso inválida: confira os 44 dígitos (modelo 57, com o dígito verificador).");
        var cnpj = await _db.EscalarAsync<string>("SELECT cnpj FROM core.empresa WHERE id = @e", ct, ("e", empresa));
        var erros = new List<string>();
        if (cnpj is not null && d.Substring(6, 14) != RegrasTms.Digitos(cnpj)) erros.Add("A chave de acesso não é do CNPJ desta transportadora.");
        if (int.Parse(d.Substring(22, 3)) != c.Serie || int.Parse(d.Substring(25, 9)) != c.Numero)
            erros.Add($"A série e o número da chave ({int.Parse(d.Substring(22, 3))}-{int.Parse(d.Substring(25, 9))}) não são os deste CT-e ({c.Serie}-{c.Numero}).");
        if (erros.Count > 0) return Resultado.Falha(erros);
        return await Tentar(async () =>
        {
            await _db.ExecutarAsync("UPDATE tms.cte SET chave = @ch, protocolo = @pr, situacao = 'EMITIDO' WHERE id = @id AND situacao = 'DIGITADO'", ct,
                                    ("ch", d), ("pr", N(protocolo)), ("id", id));
        });
    }

    /// <summary>Cancela o CT-e digitado ou emitido que ainda não está numa fatura nem num manifesto aberto.</summary>
    public async Task<Resultado> CancelarCteAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaTms, ct) is { } n) return Resultado.Falha(n);
        var c = await CteAsync(empresa, id, ct);
        if (c is null) return Resultado.Falha("CT-e não encontrado.");
        if (c.Situacao is not ("DIGITADO" or "EMITIDO")) return Resultado.Falha($"O CT-e está {RegrasTms.Rotulo(c.Situacao).ToLowerInvariant()} e não pode ser cancelado.");
        if (c.FaturaId is not null) return Resultado.Falha($"O CT-e está na fatura nº {c.FaturaNumero}: cancele a fatura antes.");
        if (await _db.EscalarAsync<int>("""
                SELECT count(*)::int FROM tms.manifesto_cte mc JOIN tms.manifesto m ON m.id = mc.manifesto_id WHERE mc.cte_id = @id AND m.situacao = 'ABERTO'
                """, ct, ("id", id)) > 0)
            return Resultado.Falha("O CT-e está num manifesto aberto: retire-o do manifesto antes.");
        return await Tentar(async () =>
        {
            await _db.ExecutarAsync("UPDATE tms.cte SET situacao = 'CANCELADO' WHERE id = @id", ct, ("id", id));
            await _db.ExecutarAsync("UPDATE tms.cotacao SET situacao = 'APROVADA' WHERE id = @c AND situacao = 'CONVERTIDA'", ct, ("c", c.CotacaoId));
        });
    }

    public async Task<Resultado> ExcluirCteAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaTms, ct) is { } n) return Resultado.Falha(n);
        return await Tentar(() => EmTransacaoAsync(async (con, tx) =>
        {
            var cot = await Escalar<long?>(con, tx, "SELECT cotacao_id FROM tms.cte WHERE id = @id AND empresa_id = @e AND situacao = 'DIGITADO'", ct, ("id", id), ("e", empresa));
            if (await Exec(con, tx, "DELETE FROM tms.cte WHERE id = @id AND empresa_id = @e AND situacao = 'DIGITADO'", ct, ("id", id), ("e", empresa)) == 0)
                throw new ViolacaoRegraException("Só o CT-e digitado (ainda não emitido) pode ser excluído; o emitido é cancelado.");
            await Exec(con, tx, "UPDATE tms.cotacao SET situacao = 'APROVADA' WHERE id = @c AND situacao = 'CONVERTIDA'", ct, ("c", cot));
            return true;
        }, ct));
    }

    /// <summary>Minuta do CT-e (PDF, retrato) com as partes, a carga, os componentes do frete e o campo de recebimento.</summary>
    public static byte[] MinutaCtePdf(Cte c, string empresaDescricao, string emitidoPor)
    {
        var linhas = new List<LinhaRelatorio>();
        void L(string campo, string? valor) => linhas.Add(new LinhaRelatorio([campo, string.IsNullOrWhiteSpace(valor) ? "-" : valor]));
        L("Série / número", $"{c.Serie} / {c.Numero}");
        L("Emissão", c.Emissao.ToString("dd/MM/yyyy"));
        L("Situação", RegrasTms.Rotulo(c.Situacao));
        L("Chave de acesso", c.Chave);
        L("CFOP", c.Cfop);
        L("Remetente", c.Remetente);
        L("Destinatário", c.Destinatario);
        L("Tomador (paga o frete)", c.Tomador);
        L("Trecho", c.Trecho);
        L("Produto predominante", c.Produto);
        L("Volumes / peso (kg) / m³", $"{c.Volumes} / {c.PesoKg.ToString("N3", Formato.PtBr)} / {c.VolumeM3.ToString("N3", Formato.PtBr)}");
        L("Valor da mercadoria", Formato.Moeda(c.ValorMercadoria));
        L("NF-e transportadas", c.ChavesNfe?.Replace('\n', ' '));
        L("Frete-peso", Formato.Moeda(c.Frete.FretePeso));
        L("Ad valorem / GRIS", $"{Formato.Moeda(c.Frete.AdValorem)} / {Formato.Moeda(c.Frete.Gris)}");
        L("Pedágio / taxas", $"{Formato.Moeda(c.Frete.Pedagio)} / {Formato.Moeda(c.Frete.Taxas)}");
        L("ICMS", $"{c.Frete.AliquotaIcms.ToString("N2", Formato.PtBr)}% = {Formato.Moeda(c.Frete.Icms)}");
        L("Valor total da prestação", Formato.Moeda(c.Frete.Total));
        L("Previsão de entrega", c.PrevisaoEntrega?.ToString("dd/MM/yyyy"));
        L("Veículo / motorista", $"{(c.Placa is null ? "-" : RegrasFrota.FormatarPlaca(c.Placa))} / {c.MotoristaNome ?? "-"}");
        L("Observação", c.Observacao);
        var tabela = new TabelaRelatorio($"Minuta do CT-e {c.Serie}-{c.Numero}", [new("Campo", LarguraCm: 5), new("Informação")], linhas,
                                         "Recebi a mercadoria em ____/____/______ às ____:____   Nome e documento: ______________________________   Assinatura: ____________________");
        var doc = ExportacaoRelatorio.ParaPdf($"Minuta do CT-e {c.Serie}-{c.Numero}", [empresaDescricao, $"Emitida em {DateTime.Now:dd/MM/yyyy HH:mm} por {emitidoPor}",
                                              "Documento auxiliar interno: não substitui o DACTE autorizado pela SEFAZ."], [tabela], false);
        return new GeradorPdf().Gerar(doc).Conteudo;
    }

    // ------------------------------------------------------------------ manifestos (MDF-e)

    public async Task<List<Manifesto>> ManifestosAsync(long empresa, DateOnly inicio, DateOnly fim, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await ListarManifestosAsync(empresa, inicio, fim, null, ct) : [];

    private Task<List<Manifesto>> ListarManifestosAsync(long empresa, DateOnly inicio, DateOnly fim, long? id, CancellationToken ct) => _db.ListarAsync("""
        SELECT m.id, m.serie, m.numero, m.emissao, m.uf_inicio, m.uf_fim, m.ufs_percurso, m.veiculo_id, m.motorista_id, m.viagem_id, m.chave, m.situacao, m.encerrado_em,
               m.observacao, COALESCE(array_agg(c.id ORDER BY c.numero) FILTER (WHERE c.id IS NOT NULL), '{}'), v.placa, mo.nome,
               COALESCE(sum(c.peso_kg), 0), COALESCE(sum(c.valor_mercadoria), 0), COALESCE(sum(c.valor_total), 0)
          FROM tms.manifesto m JOIN frota.veiculo v ON v.id = m.veiculo_id JOIN frota.motorista mo ON mo.id = m.motorista_id
               LEFT JOIN tms.manifesto_cte mc ON mc.manifesto_id = m.id LEFT JOIN tms.cte c ON c.id = mc.cte_id
         WHERE m.empresa_id = @e AND (@id::bigint IS NOT NULL AND m.id = @id OR @id::bigint IS NULL AND m.emissao BETWEEN @i AND @f)
         GROUP BY m.id, v.placa, mo.nome ORDER BY m.emissao DESC, m.numero DESC
        """, r => new Manifesto(r.GetInt64(0), r.GetInt16(1), r.GetInt32(2), r.GetFieldValue<DateOnly>(3), r.GetString(4), r.GetString(5), r.Texto(6), r.GetInt64(7),
                                r.GetInt64(8), r.Id(9), r.Texto(10), r.GetString(11), r.Data(12), r.Texto(13), r.GetFieldValue<long[]>(14), r.GetString(15), r.GetString(16),
                                r.GetDecimal(17), r.GetDecimal(18), r.GetDecimal(19)),
        ct, ("e", empresa), ("i", inicio), ("f", fim), ("id", id));

    /// <summary>CT-e emitidos que podem entrar num manifesto: emitidos ou em trânsito e fora de outro manifesto aberto (os do próprio manifesto também).</summary>
    public async Task<List<Cte>> CtesParaManifestoAsync(long empresa, long manifestoId, long usuario, CancellationToken ct = default)
        => !await PodeVerAsync(usuario, empresa, ct) ? [] : await _db.ListarAsync(SelectCte + """

         WHERE c.empresa_id = @e AND c.situacao IN ('EMITIDO', 'EM_TRANSITO')
           AND NOT EXISTS (SELECT 1 FROM tms.manifesto_cte mc JOIN tms.manifesto m ON m.id = mc.manifesto_id
                            WHERE mc.cte_id = c.id AND m.situacao = 'ABERTO' AND m.id <> @m)
         ORDER BY c.emissao, c.numero
        """, LerCte, ct, ("e", empresa), ("m", manifestoId));

    /// <summary>Grava o manifesto aberto com os CT-e escolhidos; os CT-e do manifesto passam a "em trânsito" e os retirados voltam a "emitido".</summary>
    public async Task<Resultado<Manifesto>> GravarManifestoAsync(long empresa, Manifesto m, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaTms, ct) is { } n) return Resultado<Manifesto>.Falha(n);
        m = m with { UfInicio = m.UfInicio.Trim().ToUpperInvariant(), UfFim = m.UfFim.Trim().ToUpperInvariant(), Chave = N(RegrasTms.Digitos(m.Chave)),
                     UfsPercurso = N(string.Join(",", (m.UfsPercurso ?? "").ToUpperInvariant().Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries))),
                     Observacao = N(m.Observacao), Ctes = [.. m.Ctes.Distinct()] };
        if (RegrasTms.Validar(m) is { Count: > 0 } erros) return Resultado<Manifesto>.Falha(erros);
        var disponiveis = (await CtesParaManifestoAsync(empresa, m.Id, usuario, ct)).Select(c => c.Id).ToHashSet();
        if (m.Ctes.FirstOrDefault(c => !disponiveis.Contains(c)) is var fora && fora != 0)
            return Resultado<Manifesto>.Falha("Há CT-e que não pode entrar no manifesto (não emitido, cancelado, entregue ou já em outro manifesto aberto).");
        return await Tentar(() => EmTransacaoAsync(async (con, tx) =>
        {
            int numero = m.Numero;
            long id = m.Id;
            (string, object?)[] p =
            [
                ("e", empresa), ("id", m.Id), ("s", (short)m.Serie), ("em", m.Emissao), ("ui", m.UfInicio), ("uf", m.UfFim), ("up", m.UfsPercurso), ("v", m.VeiculoId),
                ("mo", m.MotoristaId), ("via", m.ViagemId), ("ch", m.Chave), ("obs", m.Observacao),
            ];
            if (m.Id == 0)
            {
                numero = await Escalar<int>(con, tx, "SELECT COALESCE(max(numero), 0) + 1 FROM tms.manifesto WHERE empresa_id = @e AND serie = @s", ct, ("e", empresa), ("s", (short)m.Serie));
                id = await Escalar<long>(con, tx, """
                    INSERT INTO tms.manifesto (empresa_id, serie, numero, emissao, uf_inicio, uf_fim, ufs_percurso, veiculo_id, motorista_id, viagem_id, chave, observacao)
                    VALUES (@e, @s, @n, @em, @ui, @uf, @up, @v, @mo, @via, @ch, @obs) RETURNING id
                    """, ct, [.. p, ("n", numero)]);
            }
            else
            {
                var atual = await Escalar<string>(con, tx, "SELECT situacao FROM tms.manifesto WHERE id = @id AND empresa_id = @e FOR UPDATE", ct, ("id", m.Id), ("e", empresa))
                            ?? throw new ViolacaoRegraException("Manifesto não encontrado.");
                if (atual != "ABERTO") throw new ViolacaoRegraException($"O manifesto já está {RegrasTms.Rotulo(atual).ToLowerInvariant()} e não pode mais ser alterado.");
                await Exec(con, tx, """
                    UPDATE tms.manifesto SET emissao = @em, uf_inicio = @ui, uf_fim = @uf, ufs_percurso = @up, veiculo_id = @v, motorista_id = @mo, viagem_id = @via,
                           chave = @ch, observacao = @obs WHERE id = @id
                    """, ct, p);
                await LiberarCtesDoManifestoAsync(con, tx, id, m.Ctes.ToArray(), ct);
                await Exec(con, tx, "DELETE FROM tms.manifesto_cte WHERE manifesto_id = @id AND NOT (cte_id = ANY(@c))", ct, ("id", id), ("c", m.Ctes.ToArray()));
            }
            await Exec(con, tx, "INSERT INTO tms.manifesto_cte (manifesto_id, cte_id) SELECT @id, unnest(@c) ON CONFLICT DO NOTHING", ct, ("id", id), ("c", m.Ctes.ToArray()));
            await Exec(con, tx, "UPDATE tms.cte SET situacao = 'EM_TRANSITO' WHERE id = ANY(@c) AND situacao = 'EMITIDO'", ct, ("c", m.Ctes.ToArray()));
            return m with { Id = id, Numero = numero, Situacao = "ABERTO" };
        }, ct));
    }

    /// <summary>CT-e que saem do manifesto e ainda não tiveram ocorrência voltam a "emitido".</summary>
    private static Task<int> LiberarCtesDoManifestoAsync(NpgsqlConnection con, NpgsqlTransaction tx, long manifesto, long[] ficam, CancellationToken ct) => Exec(con, tx, """
        UPDATE tms.cte c SET situacao = 'EMITIDO'
          FROM tms.manifesto_cte mc
         WHERE mc.manifesto_id = @m AND mc.cte_id = c.id AND NOT (c.id = ANY(@f)) AND c.situacao = 'EM_TRANSITO'
           AND NOT EXISTS (SELECT 1 FROM tms.ocorrencia o WHERE o.cte_id = c.id)
        """, ct, ("m", manifesto), ("f", ficam));

    public async Task<Resultado> EncerrarManifestoAsync(long empresa, long id, DateOnly data, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaTms, ct) is { } n) return Resultado.Falha(n);
        return await Tentar(async () =>
        {
            if (await _db.ExecutarAsync("""
                    UPDATE tms.manifesto SET situacao = 'ENCERRADO', encerrado_em = @d WHERE id = @id AND empresa_id = @e AND situacao = 'ABERTO' AND emissao <= @d
                    """, ct, ("d", data), ("id", id), ("e", empresa)) == 0)
                throw new ViolacaoRegraException("Só o manifesto aberto pode ser encerrado, numa data igual ou depois da emissão.");
        });
    }

    public async Task<Resultado> CancelarManifestoAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaTms, ct) is { } n) return Resultado.Falha(n);
        return await Tentar(() => EmTransacaoAsync(async (con, tx) =>
        {
            if (await Exec(con, tx, "UPDATE tms.manifesto SET situacao = 'CANCELADO' WHERE id = @id AND empresa_id = @e AND situacao = 'ABERTO'", ct, ("id", id), ("e", empresa)) == 0)
                throw new ViolacaoRegraException("Só o manifesto aberto pode ser cancelado.");
            await LiberarCtesDoManifestoAsync(con, tx, id, [], ct);
            return true;
        }, ct));
    }

    /// <summary>Manifesto de carga (PDF) com os CT-e transportados e os totais.</summary>
    public async Task<Resultado<byte[]>> ManifestoPdfAsync(long empresa, long id, string empresaDescricao, string emitidoPor, long usuario, CancellationToken ct = default)
    {
        if (!await PodeVerAsync(usuario, empresa, ct)) return Resultado<byte[]>.Falha("Sem permissão para consultar a Frota nesta empresa.");
        if ((await ListarManifestosAsync(empresa, DateOnly.MinValue, DateOnly.MaxValue, id, ct)).FirstOrDefault() is not { } m) return Resultado<byte[]>.Falha("Manifesto não encontrado.");
        var ctes = (await _db.ListarAsync(SelectCte + "\n WHERE c.empresa_id = @e AND c.id = ANY(@c) ORDER BY c.serie, c.numero", LerCte, ct, ("e", empresa), ("c", m.Ctes.ToArray())));
        var linhas = ctes.Select(c => new LinhaRelatorio([$"{c.Serie}-{c.Numero}", c.Destinatario, c.Trecho, (long)c.Volumes, c.PesoKg, c.ValorMercadoria, c.ValorTotal])).ToList();
        linhas.Add(new LinhaRelatorio(["TOTAL", $"{ctes.Count} CT-e", "", (long)ctes.Sum(c => c.Volumes), m.PesoKg, m.ValorMercadoria, m.ValorFrete], true));
        var tabela = new TabelaRelatorio($"Manifesto {m.Serie}-{m.Numero}",
            [new("CT-e", LarguraCm: 2), new("Destinatário"), new("Trecho"), new("Volumes", TipoColuna.Inteiro, 1.8), new("Peso (kg)", TipoColuna.Valor),
             new("Valor da mercadoria", TipoColuna.Valor), new("Frete", TipoColuna.Valor)], linhas,
            $"Veículo {RegrasFrota.FormatarPlaca(m.Placa ?? "")} · motorista {m.MotoristaNome} · percurso {m.UfInicio}{(m.UfsPercurso is null ? "" : " › " + m.UfsPercurso.Replace(",", " › "))} › {m.UfFim}"
            + (m.Chave is null ? "" : $" · chave {m.Chave}"));
        var doc = ExportacaoRelatorio.ParaPdf($"Manifesto de carga {m.Serie}-{m.Numero}", [empresaDescricao, $"Emissão {m.Emissao:dd/MM/yyyy} · {RegrasTms.Rotulo(m.Situacao)}",
                                              $"Impresso em {DateTime.Now:dd/MM/yyyy HH:mm} por {emitidoPor}"], [tabela], true);
        return Resultado<byte[]>.Ok(new GeradorPdf().Gerar(doc).Conteudo);
    }

    // ------------------------------------------------------------------ entregas e ocorrências

    public async Task<List<Ocorrencia>> OcorrenciasAsync(long empresa, long cteId, long usuario, CancellationToken ct = default)
        => !await PodeVerAsync(usuario, empresa, ct) ? [] : await _db.ListarAsync("""
            SELECT o.id, o.cte_id, o.data_hora, o.tipo, o.descricao, o.recebedor, u.nome
              FROM tms.ocorrencia o JOIN tms.cte c ON c.id = o.cte_id LEFT JOIN core.usuario u ON u.id = o.registrado_por
             WHERE c.empresa_id = @e AND o.cte_id = @c ORDER BY o.data_hora, o.id
            """, r => new Ocorrencia(r.GetInt64(0), r.GetInt64(1), r.GetDateTime(2), r.GetString(3), r.Texto(4), r.Texto(5), r.Texto(6)), ct, ("e", empresa), ("c", cteId));

    /// <summary>Registra a ocorrência; saída, chegada e saiu para entrega põem o CT-e em trânsito; "entregue" exige o recebedor e fecha o CT-e.</summary>
    public async Task<Resultado<string>> RegistrarOcorrenciaAsync(long empresa, long cteId, string tipo, DateTime dataHora, string? descricao, string? recebedor, long usuario,
                                                                  CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaTms, ct) is { } n) return Resultado<string>.Falha(n);
        if (!RegrasTms.TiposOcorrencia.Contains(tipo)) return Resultado<string>.Falha("Escolha o tipo da ocorrência.");
        if (tipo == "ENTREGUE" && string.IsNullOrWhiteSpace(recebedor)) return Resultado<string>.Falha("Informe quem recebeu a mercadoria (nome e documento).");
        if (dataHora > DateTime.Now.AddMinutes(10)) return Resultado<string>.Falha("A data e hora da ocorrência não pode ser no futuro.");
        return await Tentar(() => EmTransacaoAsync(async (con, tx) =>
        {
            var (atual, emissao) = (await Escalar<string>(con, tx, "SELECT situacao FROM tms.cte WHERE id = @id AND empresa_id = @e FOR UPDATE", ct, ("id", cteId), ("e", empresa))
                                    ?? throw new ViolacaoRegraException("CT-e não encontrado."),
                                    DateOnly.FromDateTime(await Escalar<DateTime>(con, tx, "SELECT emissao::timestamp FROM tms.cte WHERE id = @id", ct, ("id", cteId))));
            var nova = RegrasTms.SituacaoAposOcorrencia(atual, tipo)
                       ?? throw new ViolacaoRegraException($"O CT-e está {RegrasTms.Rotulo(atual).ToLowerInvariant()}: só CT-e emitido ou em trânsito recebe ocorrências.");
            if (DateOnly.FromDateTime(dataHora) < emissao) throw new ViolacaoRegraException("A ocorrência não pode ser antes da emissão do CT-e.");
            await Exec(con, tx, "INSERT INTO tms.ocorrencia (cte_id, data_hora, tipo, descricao, recebedor, registrado_por) VALUES (@c, @d, @t, @ds, @r, @u)", ct,
                       ("c", cteId), ("d", dataHora), ("t", tipo), ("ds", N(descricao)), ("r", N(recebedor)), ("u", usuario));
            await Exec(con, tx, """
                UPDATE tms.cte SET situacao = @s, entregue_em = CASE WHEN @s = 'ENTREGUE' THEN @d ELSE entregue_em END,
                       recebedor = CASE WHEN @s = 'ENTREGUE' THEN @r ELSE recebedor END WHERE id = @c
                """, ct, ("s", nova), ("d", dataHora), ("r", N(recebedor)), ("c", cteId));
            return nova;
        }, ct));
    }

    // ------------------------------------------------------------------ faturas de frete

    public async Task<List<FaturaFrete>> FaturasAsync(long empresa, DateOnly inicio, DateOnly fim, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await ListarFaturasAsync(empresa, inicio, fim, ct) : [];

    private Task<List<FaturaFrete>> ListarFaturasAsync(long empresa, DateOnly inicio, DateOnly fim, CancellationToken ct) => _db.ListarAsync("""
        SELECT f.id, f.numero, f.emissao, f.vencimento, f.cliente_id, f.valor_total, f.duplicata_id, f.situacao, f.observacao, p.nome, d.saldo,
               (SELECT count(*)::int FROM tms.cte c WHERE c.fatura_id = f.id)
          FROM tms.fatura f JOIN core.participante p ON p.id = f.cliente_id LEFT JOIN fiscal.duplicata d ON d.id = f.duplicata_id
         WHERE f.empresa_id = @e AND f.emissao BETWEEN @i AND @f ORDER BY f.emissao DESC, f.numero DESC
        """, r => new FaturaFrete(r.GetInt64(0), r.GetInt32(1), r.GetFieldValue<DateOnly>(2), r.GetFieldValue<DateOnly>(3), r.GetInt64(4), r.GetDecimal(5), r.Id(6),
                                  r.GetString(7), r.Texto(8), r.GetString(9), r.Valor(10), r.GetInt32(11)), ct, ("e", empresa), ("i", inicio), ("f", fim));

    /// <summary>CT-e emitidos do tomador ainda sem fatura (os cancelados e os digitados não entram).</summary>
    public async Task<List<Cte>> CtesAFaturarAsync(long empresa, long clienteId, long usuario, CancellationToken ct = default)
        => !await PodeVerAsync(usuario, empresa, ct) ? [] : await _db.ListarAsync(SelectCte + """

         WHERE c.empresa_id = @e AND c.tomador_id = @t AND c.fatura_id IS NULL AND c.situacao IN ('EMITIDO', 'EM_TRANSITO', 'ENTREGUE') ORDER BY c.emissao, c.numero
        """, LerCte, ct, ("e", empresa), ("t", clienteId));

    /// <summary>Gera a fatura com os CT-e escolhidos do mesmo tomador e a conta a receber (Financeiro › Contas a receber) no vencimento informado.</summary>
    public async Task<Resultado<FaturaFrete>> GerarFaturaAsync(long empresa, long clienteId, IReadOnlyList<long> ctes, DateOnly emissao, DateOnly vencimento, string? observacao,
                                                               long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaTms, ct) is { } n) return Resultado<FaturaFrete>.Falha(n);
        if (ctes.Count == 0) return Resultado<FaturaFrete>.Falha("Marque ao menos um CT-e para faturar.");
        if (vencimento < emissao) return Resultado<FaturaFrete>.Falha("O vencimento não pode ser antes da emissão.");
        var aFaturar = (await CtesAFaturarAsync(empresa, clienteId, usuario, ct)).Where(c => ctes.Contains(c.Id)).ToList();
        if (aFaturar.Count != ctes.Distinct().Count()) return Resultado<FaturaFrete>.Falha("Há CT-e que não pode ser faturado (de outro tomador, não emitido, cancelado ou já faturado).");
        var total = aFaturar.Sum(c => c.ValorTotal);
        var cliente = aFaturar[0].Tomador;
        return await Tentar(() => EmTransacaoAsync(async (con, tx) =>
        {
            var numero = await Escalar<int>(con, tx, "SELECT COALESCE(max(numero), 0) + 1 FROM tms.fatura WHERE empresa_id = @e", ct, ("e", empresa));
            var duplicata = await Escalar<long>(con, tx, """
                INSERT INTO fiscal.duplicata (empresa_id, participante_id, tipo, parcela, numero_documento, emissao, vencimento, valor, saldo, observacao, origem, criado_por)
                VALUES (@e, @p, 'RECEBER', '1/1', @doc, @em, @v, @val, @val, @obs, 'MANUAL', @u) RETURNING id
                """, ct, ("e", empresa), ("p", clienteId), ("doc", $"FAT-{numero}"), ("em", emissao), ("v", vencimento), ("val", total),
                ("obs", $"Fatura de frete nº {numero} – {aFaturar.Count} CT-e"), ("u", usuario));
            var id = await Escalar<long>(con, tx, """
                INSERT INTO tms.fatura (empresa_id, numero, emissao, vencimento, cliente_id, valor_total, duplicata_id, observacao)
                VALUES (@e, @n, @em, @v, @c, @val, @d, @obs) RETURNING id
                """, ct, ("e", empresa), ("n", numero), ("em", emissao), ("v", vencimento), ("c", clienteId), ("val", total), ("d", duplicata), ("obs", N(observacao)));
            if (await Exec(con, tx, "UPDATE tms.cte SET fatura_id = @f WHERE id = ANY(@c) AND fatura_id IS NULL", ct, ("f", id), ("c", ctes.ToArray())) != aFaturar.Count)
                throw new ViolacaoRegraException("Algum CT-e foi faturado por outro usuário ao mesmo tempo. Atualize a lista e tente de novo.");
            return new FaturaFrete(id, numero, emissao, vencimento, clienteId, total, duplicata, "ABERTA", N(observacao), cliente, total, aFaturar.Count);
        }, ct));
    }

    /// <summary>Cancela a fatura sem recebimento: apaga a conta a receber e libera os CT-e para outra fatura.</summary>
    public async Task<Resultado> CancelarFaturaAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaTms, ct) is { } n) return Resultado.Falha(n);
        return await Tentar(() => EmTransacaoAsync(async (con, tx) =>
        {
            var dup = await Escalar<long?>(con, tx, "SELECT duplicata_id FROM tms.fatura WHERE id = @id AND empresa_id = @e AND situacao = 'ABERTA' FOR UPDATE", ct, ("id", id), ("e", empresa));
            if (await Escalar<string>(con, tx, "SELECT situacao FROM tms.fatura WHERE id = @id AND empresa_id = @e", ct, ("id", id), ("e", empresa)) != "ABERTA")
                throw new ViolacaoRegraException("Só a fatura aberta pode ser cancelada.");
            if (dup is { } d && await Escalar<int>(con, tx, "SELECT count(*)::int FROM fiscal.duplicata_baixa WHERE duplicata_id = @d", ct, ("d", d)) > 0)
                throw new ViolacaoRegraException("A fatura já tem recebimento: estorne a baixa em Financeiro › Contas a receber antes de cancelar.");
            await Exec(con, tx, "UPDATE tms.fatura SET situacao = 'CANCELADA', duplicata_id = NULL WHERE id = @id", ct, ("id", id));
            await Exec(con, tx, "UPDATE tms.cte SET fatura_id = NULL WHERE fatura_id = @id", ct, ("id", id));
            if (dup is { } dd) await Exec(con, tx, "DELETE FROM fiscal.duplicata WHERE id = @d", ct, ("d", dd));
            return true;
        }, ct));
    }

    /// <summary>Fatura de frete (PDF) com os CT-e e o vencimento.</summary>
    public async Task<Resultado<byte[]>> FaturaPdfAsync(long empresa, FaturaFrete f, string empresaDescricao, string emitidoPor, long usuario, CancellationToken ct = default)
    {
        if (!await PodeVerAsync(usuario, empresa, ct)) return Resultado<byte[]>.Falha("Sem permissão para consultar a Frota nesta empresa.");
        var ctes = await _db.ListarAsync(SelectCte + "\n WHERE c.empresa_id = @e AND c.fatura_id = @f ORDER BY c.emissao, c.numero", LerCte, ct, ("e", empresa), ("f", f.Id));
        var linhas = ctes.Select(c => new LinhaRelatorio([$"{c.Serie}-{c.Numero}", c.Emissao, c.Trecho, c.Destinatario, c.PesoKg, c.ValorTotal])).ToList();
        linhas.Add(new LinhaRelatorio(["TOTAL", null, $"{ctes.Count} CT-e", "", ctes.Sum(c => c.PesoKg), f.ValorTotal], true));
        var tabela = new TabelaRelatorio($"Fatura de frete nº {f.Numero}",
            [new("CT-e", LarguraCm: 2), new("Emissão", TipoColuna.Data), new("Trecho"), new("Destinatário"), new("Peso (kg)", TipoColuna.Valor), new("Frete", TipoColuna.Valor)],
            linhas, $"Cliente: {f.Cliente} · emissão {f.Emissao:dd/MM/yyyy} · vencimento {f.Vencimento:dd/MM/yyyy} · valor {Formato.Moeda(f.ValorTotal)}");
        var doc = ExportacaoRelatorio.ParaPdf($"Fatura de frete nº {f.Numero}", [empresaDescricao, $"Impressa em {DateTime.Now:dd/MM/yyyy HH:mm} por {emitidoPor}"], [tabela], false);
        return Resultado<byte[]>.Ok(new GeradorPdf().Gerar(doc).Conteudo);
    }

    // ------------------------------------------------------------------ relatórios do TMS (a permissão já foi conferida por RelatorioAsync)

    private async Task<TabelaRelatorio> RelatorioTmsAsync(long empresa, string titulo, DateOnly inicio, DateOnly fim, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        return titulo switch
        {
            TabelasTms.TituloFaturas => TabelasTms.Faturas(await ListarFaturasAsync(empresa, inicio, fim, ct), hoje),
            _ => TabelasTms.Gerar(titulo, await ListarCtesAsync(empresa, inicio, fim, null, ct), hoje),
        };
    }
}
