using Npgsql;
using Vorticis.Domain.Comum;
using Vorticis.Infra.Postgres;
using P = Vorticis.Domain.Seguranca.Permissao;

namespace Vorticis.Frota;

/// <summary>
/// Menu Manutenção: catálogo de peças e serviços, ordens de serviço com itens e autorização, e notas fiscais de compra.
/// Gravar e mexer nas ordens exige FROTA_MANUTENCAO; aprovar ou reprovar exige FROTA_APROVAR.
/// </summary>
public sealed partial class ServicoFrota
{
    private static string? Limpo(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Executa vários comandos numa transação (gravar a ordem e os itens juntos), com os erros do banco traduzidos.</summary>
    private Task<T> EmTransacaoAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> acao, CancellationToken ct) => BancoModulo.Traduzir(async () =>
    {
        await using var cx = await _db.Fonte.OpenConnectionAsync(ct);
        await using var tx = await cx.BeginTransactionAsync(ct);
        var r = await acao(cx, tx);
        await tx.CommitAsync(ct);
        return r;
    });

    private static NpgsqlCommand Cmd(NpgsqlConnection cx, NpgsqlTransaction tx, string sql, params (string Nome, object? Valor)[] p)
    {
        var c = new NpgsqlCommand(sql, cx, tx);
        foreach (var (n, v) in p) c.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return c;
    }

    // ------------------------------------------------------------------ peças e serviços

    public async Task<List<ItemManutencao>> ItensManutencaoAsync(long empresa, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct)
            ? await _db.ListarAsync("""
                SELECT id, codigo, tipo, descricao, unidade, valor_referencia, ativo FROM frota.item_manutencao WHERE empresa_id = @e ORDER BY codigo
                """, r => new ItemManutencao(r.GetInt64(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetString(4), r.Valor(5), r.GetBoolean(6)), ct, ("e", empresa))
            : [];

    public async Task<Resultado<long>> GravarItemManutencaoAsync(long empresa, ItemManutencao i, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaManutencao, ct) is { } n) return Resultado<long>.Falha(n);
        if (i.Codigo <= 0) return Resultado<long>.Falha("Informe o código.");
        if (!RegrasFrota.TiposItem.Contains(i.Tipo)) return Resultado<long>.Falha("Escolha se é peça ou serviço.");
        if (string.IsNullOrWhiteSpace(i.Descricao)) return Resultado<long>.Falha("Informe a descrição.");
        if (i.ValorReferencia is < 0) return Resultado<long>.Falha("O valor de referência não pode ser negativo.");
        (string, object?)[] p =
        [
            ("e", empresa), ("id", i.Id), ("c", i.Codigo), ("t", i.Tipo), ("d", i.Descricao.Trim()), ("u", Limpo(i.Unidade)?.ToUpperInvariant() ?? "UN"),
            ("v", i.ValorReferencia), ("a", i.Ativo),
        ];
        return await Tentar(async () => i.Id == 0
            ? await _db.EscalarAsync<long>("""
                INSERT INTO frota.item_manutencao (empresa_id, codigo, tipo, descricao, unidade, valor_referencia, ativo) VALUES (@e, @c, @t, @d, @u, @v, @a) RETURNING id
                """, ct, p)
            : await _db.EscalarAsync<long?>("""
                UPDATE frota.item_manutencao SET tipo = @t, descricao = @d, unidade = @u, valor_referencia = @v, ativo = @a WHERE id = @id AND empresa_id = @e RETURNING id
                """, ct, p) ?? throw new ViolacaoRegraException("Item não encontrado."));
    }

    public async Task<Resultado> ExcluirItemManutencaoAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaManutencao, ct) is { } n) return Resultado.Falha(n);
        return await Tentar(() => _db.ExecutarAsync("DELETE FROM frota.item_manutencao WHERE id = @id AND empresa_id = @e", ct, ("id", id), ("e", empresa)));
    }

    // ------------------------------------------------------------------ ordens de serviço

    public async Task<List<OrdemServico>> OrdensAsync(long empresa, DateOnly inicio, DateOnly fim, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await ListarOrdensAsync(empresa, inicio, fim, ct) : [];

    /// <summary>Ordens abertas no período e todas as que ainda não terminaram (aguardando, autorizadas, reprovadas), em qualquer data.</summary>
    private Task<List<OrdemServico>> ListarOrdensAsync(long empresa, DateOnly inicio, DateOnly fim, CancellationToken ct) => _db.ListarAsync("""
        SELECT o.id, o.numero, o.veiculo_id, o.plano_id, o.tipo, o.data, o.km, o.descricao, o.oficina, o.valor_pecas, o.valor_mao_obra, o.situacao, v.placa,
               o.solicitante, o.previsao, o.conclusao
          FROM frota.ordem_servico o JOIN frota.veiculo v ON v.id = o.veiculo_id
         WHERE o.empresa_id = @e AND (o.data BETWEEN @i AND @f OR o.situacao NOT IN ('CONCLUIDA', 'CANCELADA')) ORDER BY o.data DESC, o.numero DESC
        """, r => new OrdemServico(r.GetInt64(0), r.GetInt32(1), r.GetInt64(2), r.Id(3), r.GetString(4), r.GetFieldValue<DateOnly>(5), r.Valor(6), r.GetString(7),
                                   r.Texto(8), r.GetDecimal(9), r.GetDecimal(10), r.GetString(11), r.GetString(12), r.Texto(13), r.Data(14), r.Data(15)),
        ct, ("e", empresa), ("i", inicio), ("f", fim));

    public async Task<List<ItemOrdem>> ItensOrdemAsync(long empresa, long ordem, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct)
            ? await _db.ListarAsync("""
                SELECT i.id, i.item_id, i.tipo, i.descricao, i.quantidade, i.valor_unitario
                  FROM frota.ordem_item i JOIN frota.ordem_servico o ON o.id = i.ordem_id WHERE i.ordem_id = @o AND o.empresa_id = @e ORDER BY i.id
                """, r => new ItemOrdem(r.GetInt64(0), r.Id(1), r.GetString(2), r.GetString(3), r.GetDecimal(4), r.GetDecimal(5)), ct, ("o", ordem), ("e", empresa))
            : [];

    /// <summary>
    /// Grava a ordem e os itens (número automático; nova sempre começa aberta). Com itens, peças e serviços são a soma deles; sem itens, valem os
    /// valores digitados. Só se mexe na ordem aberta ou reprovada (a reprovada volta a ficar aberta, para pedir nova autorização).
    /// </summary>
    public async Task<Resultado<int>> GravarOrdemAsync(long empresa, OrdemServico o, IReadOnlyList<ItemOrdem> itens, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaManutencao, ct) is { } n) return Resultado<int>.Falha(n);
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(o.Descricao)) erros.Add("Descreva o serviço.");
        if (o.ValorPecas < 0 || o.ValorMaoObra < 0) erros.Add("Valores não podem ser negativos.");
        if (o.Tipo == "PREVENTIVA" && o.PlanoId is null) erros.Add("Na preventiva, escolha o plano de manutenção atendido.");
        if (o.Previsao is { } prev && prev < o.Data) erros.Add("A previsão de entrega não pode ser antes da abertura.");
        erros.AddRange(itens.SelectMany(RegrasFrota.Validar));
        if (erros.Count > 0) return Resultado<int>.Falha(erros);
        if (itens.Count > 0)
        {
            var (pecas, servicos) = RegrasFrota.Totais(itens);
            o = o with { ValorPecas = pecas, ValorMaoObra = servicos };
        }
        return await Tentar(() => EmTransacaoAsync(async (cx, tx) =>
        {
            int numero;
            long id;
            if (o.Id == 0)
            {
                await using (var c = Cmd(cx, tx, "SELECT COALESCE(max(numero), 0) + 1 FROM frota.ordem_servico WHERE empresa_id = @e", ("e", empresa)))
                    numero = (int)(await c.ExecuteScalarAsync(ct))!;
                await using var ins = Cmd(cx, tx, """
                    INSERT INTO frota.ordem_servico (empresa_id, numero, veiculo_id, plano_id, tipo, data, km, descricao, oficina, valor_pecas, valor_mao_obra,
                                                     situacao, solicitante, previsao)
                    VALUES (@e, @n, @v, @pl, @t, @d, @km, @desc, @of, @vp, @vm, 'ABERTA', @sol, @prev) RETURNING id
                    """, ParametrosOrdem(empresa, o, numero));
                id = (long)(await ins.ExecuteScalarAsync(ct))!;
            }
            else
            {
                await using (var c = Cmd(cx, tx, "SELECT situacao, numero FROM frota.ordem_servico WHERE id = @id AND empresa_id = @e FOR UPDATE", ("id", o.Id), ("e", empresa)))
                await using (var r = await c.ExecuteReaderAsync(ct))
                {
                    if (!await r.ReadAsync(ct)) throw new ViolacaoRegraException("Ordem de serviço não encontrada.");
                    if (!RegrasFrota.OrdemEditavel(r.GetString(0)))
                        throw new ViolacaoRegraException($"A ordem está {RegrasFrota.RotuloManutencao(r.GetString(0)).ToLowerInvariant()}: para alterar, use Reabrir.");
                    numero = r.GetInt32(1);
                }
                await using var up = Cmd(cx, tx, """
                    UPDATE frota.ordem_servico SET veiculo_id = @v, plano_id = @pl, tipo = @t, data = @d, km = @km, descricao = @desc, oficina = @of, valor_pecas = @vp,
                           valor_mao_obra = @vm, situacao = 'ABERTA', solicitante = @sol, previsao = @prev WHERE id = @id AND empresa_id = @e
                    """, ParametrosOrdem(empresa, o, numero));
                await up.ExecuteNonQueryAsync(ct);
                id = o.Id;
                await using var del = Cmd(cx, tx, "DELETE FROM frota.ordem_item WHERE ordem_id = @o", ("o", id));
                await del.ExecuteNonQueryAsync(ct);
            }
            foreach (var i in itens)
            {
                await using var c = Cmd(cx, tx, """
                    INSERT INTO frota.ordem_item (ordem_id, item_id, tipo, descricao, quantidade, valor_unitario) VALUES (@o, @it, @t, @d, @q, @vu)
                    """, ("o", id), ("it", i.ItemId), ("t", i.Tipo), ("d", i.Descricao.Trim()), ("q", i.Quantidade), ("vu", i.ValorUnitario));
                await c.ExecuteNonQueryAsync(ct);
            }
            if (o.Km is { } km)
            {
                await using var c = Cmd(cx, tx, "UPDATE frota.veiculo SET km_atual = GREATEST(km_atual, @km) WHERE id = @v", ("v", o.VeiculoId), ("km", km));
                await c.ExecuteNonQueryAsync(ct);
            }
            return numero;
        }, ct));
    }

    private static (string, object?)[] ParametrosOrdem(long empresa, OrdemServico o, int numero) =>
    [
        ("e", empresa), ("id", o.Id), ("n", numero), ("v", o.VeiculoId), ("pl", o.PlanoId), ("t", o.Tipo), ("d", o.Data), ("km", o.Km), ("desc", o.Descricao.Trim()),
        ("of", Limpo(o.Oficina)), ("vp", o.ValorPecas), ("vm", o.ValorMaoObra), ("sol", Limpo(o.Solicitante)), ("prev", o.Previsao),
    ];

    /// <summary>
    /// Muda a situação da ordem: pedir autorização (até o limite da configuração já sai autorizada), aprovar, reprovar (com motivo), concluir
    /// (data de conclusão; com plano, informe o km e o plano passa a contar dali), cancelar ou reabrir. Devolve a nova situação.
    /// </summary>
    public async Task<Resultado<string>> MudarSituacaoAsync(long empresa, long ordem, AcaoOrdem acao, long usuario, string? observacao = null,
                                                           DateOnly? conclusao = null, decimal? km = null, CancellationToken ct = default)
    {
        var permissao = acao is AcaoOrdem.Aprovar or AcaoOrdem.Reprovar ? P.FrotaAprovar : P.FrotaManutencao;
        if (await Negar(usuario, empresa, permissao, ct) is { } n) return Resultado<string>.Falha(n);
        if (acao == AcaoOrdem.Reprovar && string.IsNullOrWhiteSpace(observacao)) return Resultado<string>.Falha("Informe o motivo da reprovação.");
        var limite = (await ConfiguracaoAsync(empresa, ct)).LimiteAutorizacao;
        return await Tentar(() => EmTransacaoAsync(async (cx, tx) =>
        {
            string atual;
            decimal total;
            long? plano;
            long veiculo;
            DateOnly data;
            decimal? kmOrdem;
            await using (var c = Cmd(cx, tx, """
                SELECT situacao, valor_pecas + valor_mao_obra, plano_id, veiculo_id, data, km FROM frota.ordem_servico WHERE id = @id AND empresa_id = @e FOR UPDATE
                """, ("id", ordem), ("e", empresa)))
            await using (var r = await c.ExecuteReaderAsync(ct))
            {
                if (!await r.ReadAsync(ct)) throw new ViolacaoRegraException("Ordem de serviço não encontrada.");
                (atual, total, plano, veiculo, data, kmOrdem) = (r.GetString(0), r.GetDecimal(1), r.Id(2), r.GetInt64(3), r.GetFieldValue<DateOnly>(4), r.Valor(5));
            }
            var (nova, erro) = RegrasFrota.Transicao(atual, acao);
            if (nova is null) throw new ViolacaoRegraException(erro!);
            var automatica = acao == AcaoOrdem.SolicitarAutorizacao && !RegrasFrota.ExigeAutorizacao(total, limite);
            if (automatica) nova = "APROVADA";
            DateOnly? fim = null;
            if (acao == AcaoOrdem.Concluir)
            {
                fim = conclusao ?? DateOnly.FromDateTime(DateTime.Today);
                if (fim < data) throw new ViolacaoRegraException("A conclusão não pode ser antes da abertura da ordem.");
                kmOrdem = km ?? kmOrdem;
                if (plano is not null && kmOrdem is null) throw new ViolacaoRegraException("Para concluir a preventiva, informe o km do veículo.");
            }
            await using (var up = Cmd(cx, tx, "UPDATE frota.ordem_servico SET situacao = @s, conclusao = @c, km = @km WHERE id = @id",
                                      ("s", nova), ("c", fim), ("km", kmOrdem), ("id", ordem)))
                await up.ExecuteNonQueryAsync(ct);
            if (acao is AcaoOrdem.Aprovar or AcaoOrdem.Reprovar || automatica)
            {
                await using var aut = Cmd(cx, tx, """
                    INSERT INTO frota.autorizacao (ordem_id, usuario_id, decisao, valor, automatica, observacao) VALUES (@o, @u, @d, @v, @a, @obs)
                    """, ("o", ordem), ("u", usuario), ("d", nova), ("v", total), ("a", automatica),
                    ("obs", automatica ? "Dentro do limite de autorização automática" : Limpo(observacao)));
                await aut.ExecuteNonQueryAsync(ct);
            }
            if (acao == AcaoOrdem.Concluir)
            {
                if (kmOrdem is { } k)
                {
                    await using var c = Cmd(cx, tx, "UPDATE frota.veiculo SET km_atual = GREATEST(km_atual, @km) WHERE id = @v", ("v", veiculo), ("km", k));
                    await c.ExecuteNonQueryAsync(ct);
                }
                if (plano is { } pl)
                {
                    await using var c = Cmd(cx, tx, """
                        UPDATE frota.plano_manutencao SET ultima_km = GREATEST(COALESCE(ultima_km, 0), @km), ultima_data = GREATEST(COALESCE(ultima_data, @d), @d)
                         WHERE id = @p AND empresa_id = @e
                        """, ("p", pl), ("e", empresa), ("km", kmOrdem ?? 0), ("d", fim!.Value));
                    await c.ExecuteNonQueryAsync(ct);
                }
            }
            return nova;
        }, ct));
    }

    /// <summary>Exclui a ordem aberta ou cancelada (as notas de compra ficam, sem o vínculo). A que já passou por autorização é cancelada, não excluída.</summary>
    public async Task<Resultado> ExcluirOrdemAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaManutencao, ct) is { } n) return Resultado.Falha(n);
        var situacao = await _db.EscalarAsync<string>("SELECT situacao FROM frota.ordem_servico WHERE id = @id AND empresa_id = @e", ct, ("id", id), ("e", empresa));
        if (situacao is not ("ABERTA" or "CANCELADA"))
            return Resultado.Falha("Só se exclui ordem aberta ou cancelada. Esta já passou pela autorização: use Cancelar.");
        return await Tentar(() => _db.ExecutarAsync("DELETE FROM frota.ordem_servico WHERE id = @id AND empresa_id = @e", ct, ("id", id), ("e", empresa)));
    }

    // ------------------------------------------------------------------ autorizações

    public async Task<List<Autorizacao>> AutorizacoesAsync(long empresa, DateOnly inicio, DateOnly fim, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await ListarAutorizacoesAsync(empresa, inicio, fim, ct) : [];

    private Task<List<Autorizacao>> ListarAutorizacoesAsync(long empresa, DateOnly inicio, DateOnly fim, CancellationToken ct) => _db.ListarAsync("""
        SELECT a.id, a.ordem_id, o.numero, v.placa, a.momento, u.nome, a.decisao, a.valor, a.automatica, a.observacao
          FROM frota.autorizacao a JOIN frota.ordem_servico o ON o.id = a.ordem_id JOIN frota.veiculo v ON v.id = o.veiculo_id
          LEFT JOIN core.usuario u ON u.id = a.usuario_id
         WHERE o.empresa_id = @e AND a.momento::date BETWEEN @i AND @f ORDER BY a.momento, a.id
        """, r => new Autorizacao(r.GetInt64(0), r.GetInt64(1), r.GetInt32(2), r.GetString(3), r.GetFieldValue<DateTimeOffset>(4).ToLocalTime().DateTime, r.Texto(5),
                                  r.GetString(6), r.GetDecimal(7), r.GetBoolean(8), r.Texto(9)), ct, ("e", empresa), ("i", inicio), ("f", fim));

    // ------------------------------------------------------------------ notas de compra

    public async Task<List<NotaCompra>> NotasCompraAsync(long empresa, DateOnly inicio, DateOnly fim, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await ListarNotasAsync(empresa, inicio, fim, ct) : [];

    private Task<List<NotaCompra>> ListarNotasAsync(long empresa, DateOnly inicio, DateOnly fim, CancellationToken ct) => _db.ListarAsync("""
        SELECT n.id, n.ordem_id, n.fornecedor, n.cnpj, n.numero, n.serie, n.emissao, n.chave, n.valor_pecas, n.valor_servicos, n.observacao, o.numero, v.placa
          FROM frota.nota_compra n LEFT JOIN frota.ordem_servico o ON o.id = n.ordem_id LEFT JOIN frota.veiculo v ON v.id = o.veiculo_id
         WHERE n.empresa_id = @e AND n.emissao BETWEEN @i AND @f ORDER BY n.emissao, n.fornecedor, n.numero
        """, r => new NotaCompra(r.GetInt64(0), r.Id(1), r.GetString(2), r.Texto(3), r.GetString(4), r.GetString(5), r.GetFieldValue<DateOnly>(6), r.Texto(7),
                                 r.GetDecimal(8), r.GetDecimal(9), r.Texto(10), r.IsDBNull(11) ? null : r.GetInt32(11), r.Texto(12)),
        ct, ("e", empresa), ("i", inicio), ("f", fim));

    public async Task<Resultado<long>> GravarNotaCompraAsync(long empresa, NotaCompra nota, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaManutencao, ct) is { } n) return Resultado<long>.Falha(n);
        static string? Digitos(string? s) => new string([.. (s ?? "").Where(char.IsAsciiDigit)]) is { Length: > 0 } d ? d : null;
        nota = nota with { Fornecedor = nota.Fornecedor?.Trim() ?? "", Cnpj = Digitos(nota.Cnpj), Numero = nota.Numero?.Trim().TrimStart('0') is { Length: > 0 } num ? num : nota.Numero?.Trim() ?? "",
                           Serie = Limpo(nota.Serie) ?? "1", Chave = Digitos(nota.Chave) };
        if (RegrasFrota.Validar(nota) is { Count: > 0 } erros) return Resultado<long>.Falha(erros);
        (string, object?)[] p =
        [
            ("e", empresa), ("id", nota.Id), ("o", nota.OrdemId), ("f", nota.Fornecedor), ("c", nota.Cnpj), ("n", nota.Numero), ("s", nota.Serie), ("em", nota.Emissao),
            ("ch", nota.Chave), ("vp", nota.ValorPecas), ("vs", nota.ValorServicos), ("obs", Limpo(nota.Observacao)),
        ];
        var r = await Tentar(async () => nota.Id == 0
            ? await _db.EscalarAsync<long>("""
                INSERT INTO frota.nota_compra (empresa_id, ordem_id, fornecedor, cnpj, numero, serie, emissao, chave, valor_pecas, valor_servicos, observacao)
                VALUES (@e, @o, @f, @c, @n, @s, @em, @ch, @vp, @vs, @obs) RETURNING id
                """, ct, p)
            : await _db.EscalarAsync<long?>("""
                UPDATE frota.nota_compra SET ordem_id = @o, fornecedor = @f, cnpj = @c, numero = @n, serie = @s, emissao = @em, chave = @ch, valor_pecas = @vp,
                       valor_servicos = @vs, observacao = @obs WHERE id = @id AND empresa_id = @e RETURNING id
                """, ct, p) ?? throw new ViolacaoRegraException("Nota não encontrada."));
        return r.Sucesso || !r.Erros[0].StartsWith("Já existe", StringComparison.Ordinal) ? r
            : Resultado<long>.Falha("Esta nota (fornecedor, número e série) já foi lançada.");
    }

    public async Task<Resultado> ExcluirNotaCompraAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaManutencao, ct) is { } n) return Resultado.Falha(n);
        return await Tentar(() => _db.ExecutarAsync("DELETE FROM frota.nota_compra WHERE id = @id AND empresa_id = @e", ct, ("id", id), ("e", empresa)));
    }
}
