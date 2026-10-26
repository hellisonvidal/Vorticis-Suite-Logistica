using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal;
using Vorticis.Fiscal.Documentos;
using Vorticis.Fiscal.Duplicatas;
using Vorticis.Fiscal.Produtos;
using Vorticis.Infra;
using Vorticis.Infra.Postgres;

namespace Vorticis.Erp;

/// <summary>
/// Módulo ERP. Pedido de venda faturado vira nota fiscal de saída (com as duplicatas a receber) e baixa o estoque; pedido de compra recebido
/// vira nota de entrada (duplicatas a pagar) e dá entrada no estoque — tudo pelos serviços do Fiscal, nas mesmas tabelas.
/// Permissões ERP_* conferidas aqui; a gravação da nota confere também as do Fiscal.
/// </summary>
public sealed class ServicoErp(VorticisApp app)
{
    private readonly BancoModulo _db = new(app.DataSource);
    private readonly Autorizador _aut = app.Autorizador;
    private ServicosFiscais Fiscal => ServicosFiscais.De(app);

    private Task<string?> Negar(long u, long e, Permissao p, CancellationToken ct) => _aut.VerificarAsync(u, e, p, ct);

    private async Task<bool> PodeVerAsync(long u, long e, CancellationToken ct)
    {
        var p = await _aut.ObterAsync(u, e, ct);
        return p.Administrador || new[] { Permissao.ErpVendas, Permissao.ErpCompras, Permissao.ErpEstoque, Permissao.ErpTransporte, Permissao.ErpRelatorios }.Any(p.Pode);
    }

    private static Permissao PermissaoDo(TipoPedido t) => t == TipoPedido.Venda ? Domain.Seguranca.Permissao.ErpVendas : Domain.Seguranca.Permissao.ErpCompras;

    // ------------------------------------------------------------------ cadastros auxiliares

    public async Task<List<(long Id, string Nome)>> ParticipantesAsync(long empresa, TipoPedido tipo, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct)
            ? await _db.ListarAsync($"SELECT id, codigo || ' - ' || nome FROM core.participante WHERE empresa_id = @e AND ativo AND {(tipo == TipoPedido.Venda ? "cliente" : "fornecedor")} ORDER BY nome",
                                    r => (r.GetInt64(0), r.GetString(1)), ct, ("e", empresa))
            : [];

    public async Task<List<PosicaoEstoque>> ProdutosAsync(long empresa, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await _db.ListarAsync("""
            SELECT p.id, p.codigo, p.descricao, p.unidade,
                   COALESCE((SELECT SUM(m.quantidade) FROM erp.estoque_movimento m WHERE m.produto_id = p.id), 0),
                   COALESCE((SELECT SUM(m.quantidade * m.custo_unitario) / NULLIF(SUM(m.quantidade), 0) FROM erp.estoque_movimento m
                              WHERE m.produto_id = p.id AND m.quantidade > 0), 0),
                   COALESCE(pp.estoque_minimo, 0), COALESCE(pp.preco_venda, 0)
              FROM fiscal.produto p LEFT JOIN erp.produto_parametro pp ON pp.produto_id = p.id
             WHERE p.empresa_id = @e AND p.ativo ORDER BY p.descricao
            """, r => new PosicaoEstoque(r.GetInt64(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetDecimal(4), Math.Round(r.GetDecimal(5), 4),
                                         r.GetDecimal(6), r.GetDecimal(7)), ct, ("e", empresa)) : [];

    public async Task<Resultado> GravarParametroProdutoAsync(long empresa, long produtoId, decimal preco, decimal minimo, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, Domain.Seguranca.Permissao.ErpEstoque, ct) is { } n) return Resultado.Falha(n);
        if (preco < 0 || minimo < 0) return Resultado.Falha("Preço e estoque mínimo não podem ser negativos.");
        await _db.ExecutarAsync("""
            INSERT INTO erp.produto_parametro (produto_id, preco_venda, estoque_minimo)
            SELECT id, @p, @m FROM fiscal.produto WHERE id = @id AND empresa_id = @e
            ON CONFLICT (produto_id) DO UPDATE SET preco_venda = EXCLUDED.preco_venda, estoque_minimo = EXCLUDED.estoque_minimo
            """, ct, ("p", preco), ("m", minimo), ("id", produtoId), ("e", empresa));
        return Resultado.Ok();
    }

    // ------------------------------------------------------------------ pedidos

    public async Task<List<Pedido>> ListarPedidosAsync(long empresa, TipoPedido tipo, DateOnly inicio, DateOnly fim, long usuario, CancellationToken ct = default)
    {
        if (!await PodeVerAsync(usuario, empresa, ct)) return [];
        var itens = (await _db.ListarAsync("""
            SELECT i.pedido_id, i.produto_id, i.quantidade, i.valor_unitario, i.desconto, p.descricao, p.codigo
              FROM erp.pedido_item i JOIN erp.pedido x ON x.id = i.pedido_id JOIN fiscal.produto p ON p.id = i.produto_id
             WHERE x.empresa_id = @e AND x.tipo = @t AND x.data BETWEEN @i AND @f ORDER BY i.id
            """, r => (Pedido: r.GetInt64(0), Item: new ItemPedido(r.GetInt64(1), r.GetDecimal(2), r.GetDecimal(3), r.GetDecimal(4), r.GetString(5), r.GetInt32(6))),
            ct, ("e", empresa), ("t", tipo.Sql()), ("i", inicio), ("f", fim))).ToLookup(x => x.Pedido, x => x.Item);
        return await _db.ListarAsync("""
            SELECT x.id, x.numero, x.data, x.participante_id, x.situacao, x.parcelas, x.intervalo_dias, x.primeiro_vencimento, x.observacao, p.nome, x.documento_id, x.concluido_em
              FROM erp.pedido x JOIN core.participante p ON p.id = x.participante_id
             WHERE x.empresa_id = @e AND x.tipo = @t AND x.data BETWEEN @i AND @f ORDER BY x.numero DESC
            """, r => new Pedido(r.GetInt64(0), tipo, r.GetInt32(1), r.GetFieldValue<DateOnly>(2), r.GetInt64(3), RegrasErp.SituacaoDe(r.GetString(4)),
                                 r.GetInt16(5), r.GetInt16(6), r.Data(7), r.Texto(8), [.. itens[r.GetInt64(0)]], r.GetString(9), r.Id(10), r.Data(11)),
            ct, ("e", empresa), ("t", tipo.Sql()), ("i", inicio), ("f", fim));
    }

    /// <summary>Inclui (Id = 0, número automático) ou altera um pedido em aberto, com os itens.</summary>
    public async Task<Resultado<long>> GravarPedidoAsync(long empresa, Pedido p, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, PermissaoDo(p.Tipo), ct) is { } n) return Resultado<long>.Falha(n);
        var erros = RegrasErp.Validar(p);
        if (erros.Count > 0) return Resultado<long>.Falha(erros);
        try
        {
            return Resultado<long>.Ok(await BancoModulo.Traduzir(async () =>
            {
                await using var con = await app.DataSource.OpenConnectionAsync(ct);
                await using var tx = await con.BeginTransactionAsync(ct);
                long id;
                await using (var cmd = new Npgsql.NpgsqlCommand(p.Id == 0
                    ? """
                      INSERT INTO erp.pedido (empresa_id, tipo, numero, data, participante_id, parcelas, intervalo_dias, primeiro_vencimento, observacao)
                      VALUES (@e, @t, (SELECT COALESCE(MAX(numero), 0) + 1 FROM erp.pedido WHERE empresa_id = @e AND tipo = @t), @d, @p, @n, @iv, @pv, @o) RETURNING id
                      """
                    : """
                      UPDATE erp.pedido SET data = @d, participante_id = @p, parcelas = @n, intervalo_dias = @iv, primeiro_vencimento = @pv, observacao = @o
                       WHERE id = @id AND empresa_id = @e RETURNING id
                      """, con, tx))
                {
                    cmd.Parameters.AddWithValue("e", empresa); cmd.Parameters.AddWithValue("t", p.Tipo.Sql()); cmd.Parameters.AddWithValue("d", p.Data);
                    cmd.Parameters.AddWithValue("p", p.ParticipanteId); cmd.Parameters.AddWithValue("n", (short)p.Parcelas); cmd.Parameters.AddWithValue("iv", (short)p.IntervaloDias);
                    cmd.Parameters.AddWithValue("pv", NpgsqlTypes.NpgsqlDbType.Date, (object?)p.PrimeiroVencimento ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("o", (object?)p.Observacao ?? DBNull.Value); cmd.Parameters.AddWithValue("id", p.Id);
                    id = (long)(await cmd.ExecuteScalarAsync(ct) ?? throw new Domain.Comum.ViolacaoRegraException("Pedido não encontrado."));
                }
                await using (var del = new Npgsql.NpgsqlCommand("DELETE FROM erp.pedido_item WHERE pedido_id = @id", con, tx))
                {
                    del.Parameters.AddWithValue("id", id);
                    await del.ExecuteNonQueryAsync(ct);
                }
                foreach (var i in p.Itens)
                {
                    await using var ins = new Npgsql.NpgsqlCommand("""
                        INSERT INTO erp.pedido_item (pedido_id, produto_id, quantidade, valor_unitario, desconto)
                        SELECT @id, pr.id, @q, @v, @d FROM fiscal.produto pr WHERE pr.id = @pr AND pr.empresa_id = @e
                        """, con, tx);
                    ins.Parameters.AddWithValue("id", id); ins.Parameters.AddWithValue("pr", i.ProdutoId); ins.Parameters.AddWithValue("e", empresa);
                    ins.Parameters.AddWithValue("q", i.Quantidade); ins.Parameters.AddWithValue("v", i.ValorUnitario); ins.Parameters.AddWithValue("d", i.Desconto);
                    if (await ins.ExecuteNonQueryAsync(ct) == 0) throw new Domain.Comum.ViolacaoRegraException("Produto não encontrado nesta empresa.");
                }
                await tx.CommitAsync(ct);
                return id;
            }));
        }
        catch (Domain.Comum.ViolacaoRegraException ex) { return Resultado<long>.Falha(ex.Message); }
    }

    public async Task<Resultado> CancelarPedidoAsync(long empresa, Pedido p, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, PermissaoDo(p.Tipo), ct) is { } n) return Resultado.Falha(n);
        if (p.Situacao != SituacaoPedido.Aberto) return Resultado.Falha("Só pedidos em aberto podem ser cancelados.");
        await _db.ExecutarAsync("UPDATE erp.pedido SET situacao = 'CANCELADO' WHERE id = @id AND empresa_id = @e", ct, ("id", p.Id), ("e", empresa));
        return Resultado.Ok();
    }

    /// <summary>Número sugerido para a próxima nota de saída digitada (série 1).</summary>
    public Task<int> ProximoNumeroNotaAsync(long empresa, CancellationToken ct = default)
        => _db.EscalarAsync<int>("""
            SELECT COALESCE(MAX(NULLIF(regexp_replace(numero, '\D', '', 'g'), '')::bigint), 0)::int + 1
              FROM fiscal.documento WHERE empresa_id = @e AND tipo_operacao = 'S' AND modelo = '55'
            """, ct, ("e", empresa));

    /// <summary>
    /// Conclui o pedido: venda = fatura (nota de saída); compra = recebe (nota de entrada com o número do fornecedor). Gera a nota no Fiscal com as
    /// parcelas do pedido (duplicatas), movimenta o estoque e marca o pedido como concluído.
    /// </summary>
    public async Task<Resultado<long>> ConcluirPedidoAsync(long empresa, Pedido p, string numeroNota, string serie, string? chave, DateOnly data, long usuario,
                                                           CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, PermissaoDo(p.Tipo), ct) is { } n) return Resultado<long>.Falha(n);
        if (p.Situacao != SituacaoPedido.Aberto) return Resultado<long>.Falha("O pedido não está em aberto.");
        if (string.IsNullOrWhiteSpace(numeroNota)) return Resultado<long>.Falha("Informe o número da nota fiscal.");
        var venda = p.Tipo == TipoPedido.Venda;
        var produtos = await Fiscal.Produtos.ListarAsync(empresa, false, null, usuario, ct);
        if (!produtos.Sucesso) return Resultado<long>.Falha(produtos.Erros);
        var porId = produtos.Valor.ToDictionary(x => x.Id);
        var itens = new List<ItemFiscal>();
        var nItem = 0;
        foreach (var i in p.Itens)
        {
            if (!porId.TryGetValue(i.ProdutoId, out var pr)) return Resultado<long>.Falha("Produto do pedido não encontrado.");
            itens.Add(ItemDaNota(++nItem, pr, i, venda));
        }
        var totais = CalculadoraNota.Totalizar(itens, new TotaisDocumento(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0, 0, 0, 0, 0));
        var parcelas = GeradorParcelas.Gerar(totais.Total, p.Parcelas, p.PrimeiroVencimento ?? data.AddDays(p.IntervaloDias), p.IntervaloDias);
        var doc = new DocumentoDigitado(0, venda ? TipoOperacao.Saida : TipoOperacao.Entrada, ModeloDocumento.NFe55, serie.Trim(), numeroNota.Trim(),
                                        string.IsNullOrWhiteSpace(chave) ? null : chave.Trim(), data, data, venda ? "Venda de mercadoria" : "Compra de mercadoria",
                                        p.ParticipanteId, null, totais, itens.Select(CalculadoraNota.Completar).ToList(),
                                        Observacoes: $"Pedido de {(venda ? "venda" : "compra")} nº {p.Numero} (Gerencial)", Parcelas: parcelas);
        var gravada = await Fiscal.Digitacao.GravarAsync(empresa, doc, usuario, ct);
        if (!gravada.Sucesso) return gravada;
        foreach (var i in p.Itens)
            await _db.ExecutarAsync("""
                INSERT INTO erp.estoque_movimento (empresa_id, produto_id, data, tipo, quantidade, custo_unitario, pedido_id, observacao, usuario_id)
                VALUES (@e, @p, @d, @t, @q, @c, @ped, @o, @u)
                """, ct, ("e", empresa), ("p", i.ProdutoId), ("d", data), ("t", venda ? "SAIDA" : "ENTRADA"), ("q", venda ? -i.Quantidade : i.Quantidade),
                ("c", venda ? 0m : Math.Round(i.Total / i.Quantidade, 4)), ("ped", p.Id), ("o", $"Nota {numeroNota.Trim()}"), ("u", usuario));
        await _db.ExecutarAsync("UPDATE erp.pedido SET situacao = 'CONCLUIDO', documento_id = @d, concluido_em = @dt WHERE id = @id",
                                ct, ("d", gravada.Valor), ("dt", data), ("id", p.Id));
        return gravada;
    }

    /// <summary>Item da nota a partir do produto: CFOP, CST e alíquotas do cadastro do produto; impostos sobre (valor − desconto).</summary>
    public static ItemFiscal ItemDaNota(int numero, Produto pr, ItemPedido i, bool venda)
    {
        var valor = Dinheiro.Arredondar(i.Quantidade * i.ValorUnitario);
        var bc = valor - i.Desconto;
        decimal Imp(decimal aliq) => Dinheiro.Arredondar(bc * aliq / 100m);
        var cfop = (venda ? pr.CfopSaida : pr.CfopEntrada) is { Length: 4 } c ? c : venda ? "5102" : "1102";
        return new ItemFiscal(numero, pr.Codigo.ToString(), pr.Descricao, pr.Ncm, pr.Cest, cfop, pr.Unidade, i.Quantidade, i.ValorUnitario, valor, i.Desconto, 0, 0, 0, 0,
                              venda ? pr.CstIcmsSaida : pr.CstIcmsEntrada, pr.Origem, pr.AliqIcms > 0 ? bc : 0, pr.AliqIcms, Imp(pr.AliqIcms), 0, 0,
                              null, pr.AliqIpi > 0 ? bc : 0, pr.AliqIpi, Imp(pr.AliqIpi), null, pr.AliqPis > 0 ? bc : 0, pr.AliqPis, Imp(pr.AliqPis),
                              null, pr.AliqCofins > 0 ? bc : 0, pr.AliqCofins, Imp(pr.AliqCofins), 0, 0, 0, pr.Gtin, pr.Id);
    }

    // ------------------------------------------------------------------ estoque

    public async Task<List<MovimentoEstoque>> MovimentosAsync(long empresa, long? produtoId, DateOnly inicio, DateOnly fim, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await _db.ListarAsync("""
            SELECT m.id, m.produto_id, p.descricao, m.data, m.tipo, m.quantidade, m.custo_unitario, x.numero, m.observacao
              FROM erp.estoque_movimento m JOIN fiscal.produto p ON p.id = m.produto_id LEFT JOIN erp.pedido x ON x.id = m.pedido_id
             WHERE m.empresa_id = @e AND (@p::bigint IS NULL OR m.produto_id = @p) AND m.data BETWEEN @i AND @f ORDER BY m.data, m.id
            """, r => new MovimentoEstoque(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetFieldValue<DateOnly>(3), r.GetString(4), r.GetDecimal(5),
                                           r.GetDecimal(6), r.IsDBNull(7) ? null : r.GetInt32(7), r.Texto(8)),
            ct, ("e", empresa), ("p", produtoId), ("i", inicio), ("f", fim)) : [];

    /// <summary>Ajuste manual (inventário): quantidade com sinal; entradas com custo unitário.</summary>
    public async Task<Resultado> AjustarEstoqueAsync(long empresa, long produtoId, DateOnly data, decimal quantidade, decimal custo, string? observacao, long usuario,
                                                     CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, Domain.Seguranca.Permissao.ErpEstoque, ct) is { } n) return Resultado.Falha(n);
        if (quantidade == 0) return Resultado.Falha("Informe a quantidade do ajuste (positiva para entrada, negativa para saída).");
        if (custo < 0) return Resultado.Falha("O custo não pode ser negativo.");
        try
        {
            var r = await _db.ExecutarAsync("""
                INSERT INTO erp.estoque_movimento (empresa_id, produto_id, data, tipo, quantidade, custo_unitario, observacao, usuario_id)
                SELECT @e, id, @d, 'AJUSTE', @q, @c, @o, @u FROM fiscal.produto WHERE id = @p AND empresa_id = @e
                """, ct, ("e", empresa), ("p", produtoId), ("d", data), ("q", quantidade), ("c", custo), ("o", observacao), ("u", usuario));
            return r == 0 ? Resultado.Falha("Produto não encontrado.") : Resultado.Ok();
        }
        catch (ViolacaoRegraException ex) { return Resultado.Falha(ex.Message); }
    }

    // ------------------------------------------------------------------ transporte

    public async Task<List<Veiculo>> VeiculosAsync(long empresa, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await _db.ListarAsync("SELECT id, placa, renavam, tipo, uf, tara_kg, capacidade_kg, ativo FROM erp.veiculo WHERE empresa_id = @e ORDER BY placa",
            r => new Veiculo(r.GetInt64(0), r.GetString(1), r.Texto(2), r.GetString(3), r.GetString(4), r.GetInt32(5), r.GetInt32(6), r.GetBoolean(7)), ct, ("e", empresa)) : [];

    public async Task<Resultado> GravarVeiculoAsync(long empresa, Veiculo v, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, Domain.Seguranca.Permissao.ErpTransporte, ct) is { } n) return Resultado.Falha(n);
        if (RegrasErp.PlacaValida(v.Placa) is not { } placa) return Resultado.Falha("Placa inválida: use ABC1234 ou ABC1D23.");
        if (string.IsNullOrWhiteSpace(v.Uf) || v.Uf.Trim().Length != 2) return Resultado.Falha("Informe a UF do veículo.");
        try
        {
            await _db.ExecutarAsync(v.Id == 0
                ? "INSERT INTO erp.veiculo (empresa_id, placa, renavam, tipo, uf, tara_kg, capacidade_kg, ativo) VALUES (@e, @p, @r, @t, @uf, @ta, @ca, @a)"
                : "UPDATE erp.veiculo SET placa = @p, renavam = @r, tipo = @t, uf = @uf, tara_kg = @ta, capacidade_kg = @ca, ativo = @a WHERE id = @id AND empresa_id = @e",
                ct, ("e", empresa), ("p", placa), ("r", v.Renavam), ("t", v.Tipo), ("uf", v.Uf.Trim().ToUpperInvariant()), ("ta", v.TaraKg), ("ca", v.CapacidadeKg), ("a", v.Ativo), ("id", v.Id));
            return Resultado.Ok();
        }
        catch (ViolacaoRegraException ex) { return Resultado.Falha(ex.Message); }
    }

    public async Task<List<Motorista>> MotoristasAsync(long empresa, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await _db.ListarAsync("SELECT id, nome, cpf, cnh, cnh_validade, ativo FROM erp.motorista WHERE empresa_id = @e ORDER BY nome",
            r => new Motorista(r.GetInt64(0), r.GetString(1), r.GetString(2), r.Texto(3), r.Data(4), r.GetBoolean(5)), ct, ("e", empresa)) : [];

    public async Task<Resultado> GravarMotoristaAsync(long empresa, Motorista m, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, Domain.Seguranca.Permissao.ErpTransporte, ct) is { } n) return Resultado.Falha(n);
        var cpf = new string((m.Cpf ?? "").Where(char.IsAsciiDigit).ToArray());
        if (string.IsNullOrWhiteSpace(m.Nome)) return Resultado.Falha("Informe o nome do motorista.");
        if (!Domain.Cadastros.Cpf.Valido(cpf)) return Resultado.Falha("CPF inválido.");
        try
        {
            await _db.ExecutarAsync(m.Id == 0
                ? "INSERT INTO erp.motorista (empresa_id, nome, cpf, cnh, cnh_validade, ativo) VALUES (@e, @n, @c, @h, @v, @a)"
                : "UPDATE erp.motorista SET nome = @n, cpf = @c, cnh = @h, cnh_validade = @v, ativo = @a WHERE id = @id AND empresa_id = @e",
                ct, ("e", empresa), ("n", m.Nome.Trim()), ("c", cpf), ("h", m.Cnh), ("v", m.CnhValidade), ("a", m.Ativo), ("id", m.Id));
            return Resultado.Ok();
        }
        catch (ViolacaoRegraException ex) { return Resultado.Falha(ex.Message); }
    }

    public async Task<List<DocumentoTransporte>> DocumentosTransporteAsync(long empresa, DateOnly inicio, DateOnly fim, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await _db.ListarAsync("""
            SELECT d.id, d.tipo, d.numero, d.serie, d.emissao, d.chave, d.remetente_id, d.destinatario_id, d.uf_inicio, d.uf_fim, d.veiculo_id, d.motorista_id,
                   d.valor_frete, d.valor_carga, d.peso_kg, d.chaves_nfe, d.situacao, d.observacao, r.nome, t.nome, v.placa, m.nome
              FROM erp.documento_transporte d
              LEFT JOIN core.participante r ON r.id = d.remetente_id LEFT JOIN core.participante t ON t.id = d.destinatario_id
              LEFT JOIN erp.veiculo v ON v.id = d.veiculo_id LEFT JOIN erp.motorista m ON m.id = d.motorista_id
             WHERE d.empresa_id = @e AND d.emissao BETWEEN @i AND @f ORDER BY d.emissao DESC, d.numero DESC
            """, r => new DocumentoTransporte(r.GetInt64(0), r.GetString(1), r.GetInt32(2), r.GetInt16(3), r.GetFieldValue<DateOnly>(4), r.Texto(5), r.Id(6), r.Id(7),
                                              r.GetString(8), r.GetString(9), r.Id(10), r.Id(11), r.GetDecimal(12), r.GetDecimal(13), r.GetDecimal(14), r.Texto(15),
                                              r.GetString(16), r.Texto(17), r.Texto(18), r.Texto(19), r.Texto(20), r.Texto(21)),
            ct, ("e", empresa), ("i", inicio), ("f", fim)) : [];

    public async Task<Resultado> GravarDocumentoTransporteAsync(long empresa, DocumentoTransporte d, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, Domain.Seguranca.Permissao.ErpTransporte, ct) is { } n) return Resultado.Falha(n);
        var erros = new List<string>();
        if (d.Tipo is not ("CTE" or "MDFE")) erros.Add("Tipo: CT-e ou MDF-e.");
        if (d.Numero <= 0) erros.Add("Informe o número.");
        if (d.UfInicio.Trim().Length != 2 || d.UfFim.Trim().Length != 2) erros.Add("Informe as UFs de início e fim.");
        if (d.Chave is { Length: > 0 } c && (c.Length != 44 || !c.All(char.IsAsciiDigit))) erros.Add("A chave de acesso tem 44 dígitos.");
        if (d.Tipo == "MDFE" && d.VeiculoId is null) erros.Add("O MDF-e precisa do veículo.");
        if (erros.Count > 0) return Resultado.Falha(erros);
        try
        {
            await _db.ExecutarAsync(d.Id == 0
                ? """
                  INSERT INTO erp.documento_transporte (empresa_id, tipo, numero, serie, emissao, chave, remetente_id, destinatario_id, uf_inicio, uf_fim, veiculo_id,
                                                        motorista_id, valor_frete, valor_carga, peso_kg, chaves_nfe, situacao, observacao)
                  VALUES (@e, @t, @n, @s, @em, @ch, @r, @de, @ui, @uf, @v, @m, @vf, @vc, @p, @cn, @si, @o)
                  """
                : """
                  UPDATE erp.documento_transporte SET tipo = @t, numero = @n, serie = @s, emissao = @em, chave = @ch, remetente_id = @r, destinatario_id = @de,
                         uf_inicio = @ui, uf_fim = @uf, veiculo_id = @v, motorista_id = @m, valor_frete = @vf, valor_carga = @vc, peso_kg = @p, chaves_nfe = @cn,
                         situacao = @si, observacao = @o
                   WHERE id = @id AND empresa_id = @e
                  """, ct, ("e", empresa), ("t", d.Tipo), ("n", d.Numero), ("s", (short)d.Serie), ("em", d.Emissao), ("ch", string.IsNullOrWhiteSpace(d.Chave) ? null : d.Chave),
                ("r", d.RemetenteId), ("de", d.DestinatarioId), ("ui", d.UfInicio.Trim().ToUpperInvariant()), ("uf", d.UfFim.Trim().ToUpperInvariant()),
                ("v", d.VeiculoId), ("m", d.MotoristaId), ("vf", d.ValorFrete), ("vc", d.ValorCarga), ("p", d.PesoKg), ("cn", d.ChavesNfe), ("si", d.Situacao),
                ("o", d.Observacao), ("id", d.Id));
            return Resultado.Ok();
        }
        catch (ViolacaoRegraException ex) { return Resultado.Falha(ex.Message); }
    }

    // ------------------------------------------------------------------ painel do empresário

    public async Task<Resultado<PainelEmpresario>> PainelAsync(long empresa, DateOnly referencia, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, Domain.Seguranca.Permissao.ErpRelatorios, ct) is { } n) return Resultado<PainelEmpresario>.Falha(n);
        var ini = new DateOnly(referencia.Year, referencia.Month, 1);
        var fim = ini.AddMonths(1).AddDays(-1);
        (string, object?)[] p = [("e", empresa), ("i", ini), ("f", fim), ("h", referencia), ("h7", referencia.AddDays(7))];
        Task<decimal> Soma(string sql) => _db.EscalarAsync<decimal>($"SELECT COALESCE(({sql}), 0)::numeric", ct, p);
        var vendas = await Soma("SELECT SUM(valor_total) FROM fiscal.documento WHERE empresa_id = @e AND tipo_operacao = 'S' AND situacao = 'AUTORIZADA' AND data_entrada_saida BETWEEN @i AND @f");
        var compras = await Soma("SELECT SUM(valor_total) FROM fiscal.documento WHERE empresa_id = @e AND tipo_operacao = 'E' AND situacao = 'AUTORIZADA' AND data_entrada_saida BETWEEN @i AND @f");
        string Dup(string tipo, string cond) => $"SELECT SUM(saldo) FROM fiscal.duplicata WHERE empresa_id = @e AND tipo = '{tipo}' AND situacao IN ('ABERTA', 'PARCIAL') {cond}";
        var rec = await Soma(Dup("RECEBER", ""));
        var recV = await Soma(Dup("RECEBER", "AND vencimento < @h"));
        var rec7 = await Soma(Dup("RECEBER", "AND vencimento BETWEEN @h AND @h7"));
        var pag = await Soma(Dup("PAGAR", ""));
        var pagV = await Soma(Dup("PAGAR", "AND vencimento < @h"));
        var pag7 = await Soma(Dup("PAGAR", "AND vencimento BETWEEN @h AND @h7"));
        var estoque = await ProdutosAsync(empresa, usuario, ct);
        var abertos = await _db.ListarAsync("SELECT tipo, COUNT(*)::int FROM erp.pedido WHERE empresa_id = @e AND situacao = 'ABERTO' GROUP BY tipo",
                                            r => (r.GetString(0), r.GetInt32(1)), ct, p);
        var clientes = await _db.ListarAsync("""
            SELECT pa.nome, SUM(d.valor_total) FROM fiscal.documento d JOIN core.participante pa ON pa.id = d.participante_id
             WHERE d.empresa_id = @e AND d.tipo_operacao = 'S' AND d.situacao = 'AUTORIZADA' AND d.data_entrada_saida BETWEEN @i AND @f
             GROUP BY pa.nome ORDER BY 2 DESC LIMIT 5
            """, r => (r.GetString(0), r.GetDecimal(1)), ct, p);
        var maisVendidos = await _db.ListarAsync("""
            SELECT COALESCE(i.descricao, i.cod_produto, '?'), SUM(i.quantidade), SUM(i.valor_produto)
              FROM fiscal.documento_item i JOIN fiscal.documento d ON d.id = i.documento_id
             WHERE d.empresa_id = @e AND d.tipo_operacao = 'S' AND d.situacao = 'AUTORIZADA' AND d.data_entrada_saida BETWEEN @i AND @f
             GROUP BY 1 ORDER BY 3 DESC LIMIT 5
            """, r => (r.GetString(0), r.GetDecimal(1), r.GetDecimal(2)), ct, p);
        var fluxo = await _db.ListarAsync("""
            SELECT date_trunc('week', vencimento)::date, SUM(saldo) FILTER (WHERE tipo = 'RECEBER'), SUM(saldo) FILTER (WHERE tipo = 'PAGAR')
              FROM fiscal.duplicata WHERE empresa_id = @e AND situacao IN ('ABERTA', 'PARCIAL') AND vencimento BETWEEN @h AND @h + 55
             GROUP BY 1 ORDER BY 1
            """, r => (r.GetFieldValue<DateOnly>(0), r.IsDBNull(1) ? 0 : r.GetDecimal(1), r.IsDBNull(2) ? 0 : r.GetDecimal(2)), ct, p);
        return Resultado<PainelEmpresario>.Ok(new PainelEmpresario(ini, vendas, compras, rec, recV, pag, pagV, rec7, pag7, estoque.Sum(e => e.ValorEstoque),
            estoque.Count(e => e.AbaixoDoMinimo), abertos.Where(a => a.Item1 == "VENDA").Sum(a => a.Item2), abertos.Where(a => a.Item1 == "COMPRA").Sum(a => a.Item2),
            clientes, maisVendidos, fluxo));
    }
}

public static class ServicosErp
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VorticisApp, ServicoErp> Instancias = new();
    public static ServicoErp De(VorticisApp app) => Instancias.GetValue(app, a => new ServicoErp(a));
}
