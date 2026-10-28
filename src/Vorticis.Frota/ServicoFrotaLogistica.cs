using Vorticis.Application.Contabil;
using Vorticis.Application.Relatorios;
using Vorticis.Domain.Comum;
using Vorticis.Infra.Postgres;
using Vorticis.Infra.Relatorios;
using P = Vorticis.Domain.Seguranca.Permissao;

namespace Vorticis.Frota;

/// <summary>Frotas, rotas, diárias e ordens de coleta.</summary>
public sealed partial class ServicoFrota
{
    private static readonly HttpClient HttpRotas = CriarHttp();

    private static HttpClient CriarHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("VorticisSuiteLogistica/1.0");
        return c;
    }

    private static string? N(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private Task<int> ProximoNumeroAsync(string tabela, long empresa, CancellationToken ct)
        => _db.EscalarAsync<int>($"SELECT COALESCE(max(numero), 0) + 1 FROM frota.{tabela} WHERE empresa_id = @e", ct, ("e", empresa));

    // ------------------------------------------------------------------ frotas

    public async Task<List<FrotaCadastro>> FrotasAsync(long empresa, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await ListarFrotasAsync(empresa, ct) : [];

    private Task<List<FrotaCadastro>> ListarFrotasAsync(long empresa, CancellationToken ct) => _db.ListarAsync("""
        SELECT f.id, f.codigo, f.descricao, f.tipo, f.responsavel, f.centro_custo_id, f.ativa, c.codigo || ' – ' || c.descricao,
               (SELECT count(*) FROM frota.veiculo v WHERE v.frota_id = f.id)
          FROM frota.frota f LEFT JOIN contabil.centro_custo c ON c.id = f.centro_custo_id WHERE f.empresa_id = @e ORDER BY f.codigo
        """, r => new FrotaCadastro(r.GetInt64(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.Texto(4), r.Id(5), r.GetBoolean(6), r.Texto(7), (int)r.GetInt64(8)),
        ct, ("e", empresa));

    public async Task<Resultado<long>> GravarFrotaAsync(long empresa, FrotaCadastro f, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaCadastros, ct) is { } n) return Resultado<long>.Falha(n);
        f = f with { Descricao = f.Descricao?.Trim() ?? "", Responsavel = N(f.Responsavel) };
        if (RegrasLogistica.Validar(f) is { Count: > 0 } erros) return Resultado<long>.Falha(erros);
        (string, object?)[] p = [("e", empresa), ("id", f.Id), ("cod", f.Codigo), ("d", f.Descricao), ("t", f.Tipo), ("r", f.Responsavel), ("cc", f.CentroCustoId), ("a", f.Ativa)];
        return await Tentar(async () => f.Id == 0
            ? await _db.EscalarAsync<long>("""
                INSERT INTO frota.frota (empresa_id, codigo, descricao, tipo, responsavel, centro_custo_id, ativa) VALUES (@e, @cod, @d, @t, @r, @cc, @a) RETURNING id
                """, ct, p)
            : await _db.EscalarAsync<long?>("""
                UPDATE frota.frota SET descricao = @d, tipo = @t, responsavel = @r, centro_custo_id = @cc, ativa = @a WHERE id = @id AND empresa_id = @e RETURNING id
                """, ct, p) ?? throw new ViolacaoRegraException("Frota não encontrada."));
    }

    public async Task<Resultado> ExcluirFrotaAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaCadastros, ct) is { } n) return Resultado.Falha(n);
        return await Tentar(() => _db.ExecutarAsync("DELETE FROM frota.frota WHERE id = @id AND empresa_id = @e", ct, ("id", id), ("e", empresa)));
    }

    /// <summary>Centros de custo analíticos ativos da empresa (para escolher o padrão da frota e o da diária).</summary>
    public async Task<List<(long Id, string Texto, long? FrotaId)>> CentrosCustoAsync(long empresa, long usuario, CancellationToken ct = default)
        => !await PodeVerAsync(usuario, empresa, ct) ? [] : await _db.ListarAsync("""
            SELECT id, codigo || ' – ' || descricao, frota_id FROM contabil.centro_custo WHERE empresa_id = @e AND ativo AND tipo = 'A' ORDER BY codigo
            """, r => (r.GetInt64(0), r.GetString(1), r.Id(2)), ct, ("e", empresa));

    // ------------------------------------------------------------------ rotas

    public async Task<List<Rota>> RotasAsync(long empresa, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await ListarRotasAsync(empresa, ct) : [];

    private async Task<List<Rota>> ListarRotasAsync(long empresa, CancellationToken ct)
    {
        var paradas = (await _db.ListarAsync("""
            SELECT p.rota_id, p.uf, p.municipio_ibge, p.municipio FROM frota.rota_parada p JOIN frota.rota r ON r.id = p.rota_id
             WHERE r.empresa_id = @e ORDER BY p.rota_id, p.ordem
            """, r => (Rota: r.GetInt64(0), Parada: new ParadaRota(r.GetString(1).Trim(), r.GetString(2).Trim(), r.GetString(3))), ct, ("e", empresa)))
            .ToLookup(x => x.Rota, x => x.Parada);
        return await _db.ListarAsync("""
            SELECT id, codigo, descricao, distancia_km, distancia_origem, tempo_horas, pedagios, ativa FROM frota.rota WHERE empresa_id = @e ORDER BY codigo
            """, r => new Rota(r.GetInt64(0), r.GetInt32(1), r.GetString(2), r.GetDecimal(3), r.GetString(4), r.GetDecimal(5), r.GetDecimal(6), r.GetBoolean(7),
                               [.. paradas[r.GetInt64(0)]]), ct, ("e", empresa));
    }

    /// <summary>
    /// Grava a rota. Distância ESTIMADA: recalculada na hora (Haversine entre cidades consecutivas x fator rodoviário da configuração) e o tempo pela velocidade
    /// média; RODOVIARIA e MANUAL: mantém a distância informada.
    /// </summary>
    public async Task<Resultado<long>> GravarRotaAsync(long empresa, Rota r, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaCadastros, ct) is { } n) return Resultado<long>.Falha(n);
        r = r with { Descricao = r.Descricao?.Trim() ?? "" };
        if (RegrasLogistica.Validar(r) is { Count: > 0 } erros) return Resultado<long>.Falha(erros);
        var cfg = await ConfiguracaoAsync(empresa, ct);
        if (r.DistanciaOrigem == "ESTIMADA")
        {
            var km = RegrasLogistica.EstimarKm(r.Paradas, cfg.FatorRodoviario) ?? 0;
            r = r with { DistanciaKm = km, TempoHoras = RegrasLogistica.EstimarHoras(km, cfg.VelocidadeMedia) };
        }
        else if (r.TempoHoras <= 0) r = r with { TempoHoras = RegrasLogistica.EstimarHoras(r.DistanciaKm, cfg.VelocidadeMedia) };
        return await Tentar(async () =>
        {
            await using var con = await _db.Fonte.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            long id;
            Npgsql.NpgsqlCommand Cmd(string sql) => new(sql, con, tx);
            if (r.Id == 0)
            {
                await using var c = Cmd("""
                    INSERT INTO frota.rota (empresa_id, codigo, descricao, distancia_km, distancia_origem, tempo_horas, pedagios, ativa)
                    VALUES (@e, @cod, @d, @km, @o, @h, @p, @a) RETURNING id
                    """);
                Preencher(c, empresa, r);
                id = (long)(await c.ExecuteScalarAsync(ct))!;
            }
            else
            {
                id = r.Id;
                await using var c = Cmd("""
                    UPDATE frota.rota SET descricao = @d, distancia_km = @km, distancia_origem = @o, tempo_horas = @h, pedagios = @p, ativa = @a
                     WHERE id = @id AND empresa_id = @e
                    """);
                Preencher(c, empresa, r);
                c.Parameters.AddWithValue("id", id);
                if (await c.ExecuteNonQueryAsync(ct) == 0) throw new ViolacaoRegraException("Rota não encontrada.");
                await using var del = Cmd("DELETE FROM frota.rota_parada WHERE rota_id = @id");
                del.Parameters.AddWithValue("id", id);
                await del.ExecuteNonQueryAsync(ct);
            }
            for (var i = 0; i < r.Paradas.Count; i++)
            {
                await using var c = Cmd("INSERT INTO frota.rota_parada (rota_id, ordem, uf, municipio_ibge, municipio) VALUES (@r, @o, @uf, @ibge, @m)");
                c.Parameters.AddWithValue("r", id);
                c.Parameters.AddWithValue("o", (short)i);
                c.Parameters.AddWithValue("uf", r.Paradas[i].Uf.ToUpperInvariant());
                c.Parameters.AddWithValue("ibge", r.Paradas[i].MunicipioIbge);
                c.Parameters.AddWithValue("m", r.Paradas[i].Municipio);
                await c.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            return id;
        });

        static void Preencher(Npgsql.NpgsqlCommand c, long empresa, Rota r)
        {
            c.Parameters.AddWithValue("e", empresa);
            c.Parameters.AddWithValue("cod", r.Codigo);
            c.Parameters.AddWithValue("d", r.Descricao);
            c.Parameters.AddWithValue("km", r.DistanciaKm);
            c.Parameters.AddWithValue("o", r.DistanciaOrigem);
            c.Parameters.AddWithValue("h", r.TempoHoras);
            c.Parameters.AddWithValue("p", r.Pedagios);
            c.Parameters.AddWithValue("a", r.Ativa);
        }
    }

    public async Task<Resultado> ExcluirRotaAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaCadastros, ct) is { } n) return Resultado.Falha(n);
        return await Tentar(() => _db.ExecutarAsync("DELETE FROM frota.rota WHERE id = @id AND empresa_id = @e", ct, ("id", id), ("e", empresa)));
    }

    /// <summary>Botão "Buscar distância rodoviária": consulta o serviço público de rotas. Em caso de falha, devolve a mensagem e a tela mantém a estimativa.</summary>
    public async Task<Resultado<DistanciaRodoviaria>> BuscarDistanciaRodoviariaAsync(long empresa, IReadOnlyList<ParadaRota> paradas, long usuario,
                                                                                      HttpClient? http = null, string? urlBase = null, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaCadastros, ct) is { } n) return Resultado<DistanciaRodoviaria>.Falha(n);
        return await ServicoRotasOnline.ConsultarAsync(http ?? HttpRotas, paradas, urlBase, ct);
    }

    // ------------------------------------------------------------------ diárias

    public async Task<Dictionary<string, decimal>> ValoresDiariaAsync(long empresa, CancellationToken ct = default)
    {
        var d = RegrasLogistica.TiposDiaria.ToDictionary(t => t, _ => 0m);
        foreach (var (tipo, valor) in await _db.ListarAsync("SELECT tipo, valor FROM frota.diaria_valor WHERE empresa_id = @e", r => (r.GetString(0), r.GetDecimal(1)), ct, ("e", empresa)))
            d[tipo] = valor;
        return d;
    }

    public async Task<Resultado> GravarValoresDiariaAsync(long empresa, IReadOnlyDictionary<string, decimal> valores, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaCadastros, ct) is { } n) return Resultado.Falha(n);
        if (valores.Any(v => !RegrasLogistica.TiposDiaria.Contains(v.Key) || v.Value < 0)) return Resultado.Falha("Valores de diária inválidos.");
        return await Tentar(async () =>
        {
            foreach (var (tipo, valor) in valores)
                await _db.ExecutarAsync("""
                    INSERT INTO frota.diaria_valor (empresa_id, tipo, valor) VALUES (@e, @t, @v)
                    ON CONFLICT (empresa_id, tipo) DO UPDATE SET valor = EXCLUDED.valor
                    """, ct, ("e", empresa), ("t", tipo), ("v", valor));
        });
    }

    public async Task<List<Diaria>> DiariasAsync(long empresa, DateOnly inicio, DateOnly fim, long? motoristaId, long? frotaId, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await ListarDiariasAsync(empresa, inicio, fim, motoristaId, frotaId, ct) : [];

    private Task<List<Diaria>> ListarDiariasAsync(long empresa, DateOnly inicio, DateOnly fim, long? motoristaId, long? frotaId, CancellationToken ct) => _db.ListarAsync("""
        SELECT d.id, d.numero, d.data, d.motorista_id, d.funcao, d.veiculo_id, d.frota_id, d.viagem_id, d.rota_id, d.tipo, d.quantidade, d.valor_unitario, d.adiantamento,
               d.valor_gasto, d.situacao, d.centro_custo_id, d.duplicata_id, d.observacao, m.nome, v.placa, f.descricao, r.descricao, c.codigo || ' – ' || c.descricao
          FROM frota.diaria d JOIN frota.motorista m ON m.id = d.motorista_id LEFT JOIN frota.veiculo v ON v.id = d.veiculo_id LEFT JOIN frota.frota f ON f.id = d.frota_id
               LEFT JOIN frota.rota r ON r.id = d.rota_id LEFT JOIN contabil.centro_custo c ON c.id = d.centro_custo_id
         WHERE d.empresa_id = @e AND d.data BETWEEN @i AND @f AND (@m::bigint IS NULL OR d.motorista_id = @m) AND (@fr::bigint IS NULL OR d.frota_id = @fr)
         ORDER BY d.data DESC, d.numero DESC
        """, r => new Diaria(r.GetInt64(0), r.GetInt32(1), r.GetFieldValue<DateOnly>(2), r.GetInt64(3), r.GetString(4), r.Id(5), r.Id(6), r.Id(7), r.Id(8), r.GetString(9),
                             r.GetDecimal(10), r.GetDecimal(11), r.GetDecimal(12), r.Valor(13), r.GetString(14), r.Id(15), r.Id(16), r.Texto(17), r.GetString(18), r.Texto(19),
                             r.Texto(20), r.Texto(21), r.Texto(22)),
        ct, ("e", empresa), ("i", inicio), ("f", fim), ("m", motoristaId), ("fr", frotaId));

    /// <summary>
    /// Lança a diária. Valor unitário em branco (0) vem da tabela de valores (Parâmetros › Frota). A frota é a informada, ou a do centro de custo, ou a do veículo;
    /// o centro de custo em branco assume o padrão da frota. Diária paga ou acertada não pode mais ser alterada.
    /// </summary>
    public async Task<Resultado<int>> GravarDiariaAsync(long empresa, Diaria d, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaOperacao, ct) is { } n) return Resultado<int>.Falha(n);
        d = d with { Observacao = N(d.Observacao) };
        if (RegrasLogistica.Validar(d) is { Count: > 0 } erros) return Resultado<int>.Falha(erros);
        return await Tentar(async () =>
        {
            if (d.Id != 0 && await _db.EscalarAsync<string>("SELECT situacao FROM frota.diaria WHERE id = @id AND empresa_id = @e", ct, ("id", d.Id), ("e", empresa)) is { } atual and not "LANCADA")
                throw new ViolacaoRegraException($"A diária já está {RegrasFrota.Rotulo(atual).ToLowerInvariant()} e não pode mais ser alterada.");
            if (d.ValorUnitario <= 0) d = d with { ValorUnitario = (await ValoresDiariaAsync(empresa, ct)).GetValueOrDefault(d.Tipo) };
            if (d.ValorUnitario <= 0) throw new ViolacaoRegraException("Informe o valor unitário da diária ou cadastre o valor do tipo em Cadastros › Parâmetros › Frota.");
            var frotaCentro = d.CentroCustoId is { } cc ? await _db.EscalarAsync<long?>("SELECT frota_id FROM contabil.centro_custo WHERE id = @c AND empresa_id = @e", ct, ("c", cc), ("e", empresa)) : null;
            var frotaVeiculo = d.VeiculoId is { } vv ? await _db.EscalarAsync<long?>("SELECT frota_id FROM frota.veiculo WHERE id = @v AND empresa_id = @e", ct, ("v", vv), ("e", empresa)) : null;
            var frota = d.FrotaId ?? frotaCentro ?? frotaVeiculo;
            var centro = d.CentroCustoId ?? (frota is { } fr ? await _db.EscalarAsync<long?>("SELECT centro_custo_id FROM frota.frota WHERE id = @f AND empresa_id = @e", ct, ("f", fr), ("e", empresa)) : null);
            var numero = d.Id == 0 ? await ProximoNumeroAsync("diaria", empresa, ct) : d.Numero;
            (string, object?)[] p =
            [
                ("e", empresa), ("id", d.Id), ("n", numero), ("dt", d.Data), ("m", d.MotoristaId), ("fu", d.Funcao), ("v", d.VeiculoId), ("fr", frota), ("vi", d.ViagemId),
                ("ro", d.RotaId), ("t", d.Tipo), ("q", d.Quantidade), ("u", d.ValorUnitario), ("a", d.Adiantamento), ("cc", centro), ("o", d.Observacao),
            ];
            if (d.Id == 0)
                await _db.ExecutarAsync("""
                    INSERT INTO frota.diaria (empresa_id, numero, data, motorista_id, funcao, veiculo_id, frota_id, viagem_id, rota_id, tipo, quantidade, valor_unitario,
                                              adiantamento, centro_custo_id, observacao)
                    VALUES (@e, @n, @dt, @m, @fu, @v, @fr, @vi, @ro, @t, @q, @u, @a, @cc, @o)
                    """, ct, p);
            else if (await _db.ExecutarAsync("""
                    UPDATE frota.diaria SET data = @dt, motorista_id = @m, funcao = @fu, veiculo_id = @v, frota_id = @fr, viagem_id = @vi, rota_id = @ro, tipo = @t,
                           quantidade = @q, valor_unitario = @u, adiantamento = @a, centro_custo_id = @cc, observacao = @o WHERE id = @id AND empresa_id = @e
                    """, ct, p) == 0) throw new ViolacaoRegraException("Diária não encontrada.");
            return numero;
        });
    }

    /// <summary>
    /// Paga a diária: gera a conta a pagar (duplicata do Financeiro) com o adiantamento (ou o total, sem adiantamento) e, se a empresa contabiliza em lote,
    /// registra a pendência de contabilização da Frota. Com a contabilização on-line, só a conta a pagar é gerada.
    /// </summary>
    public async Task<Resultado<ResultadoPagamentoDiaria>> PagarDiariaAsync(long empresa, long id, DateOnly vencimento, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaOperacao, ct) is { } n) return Resultado<ResultadoPagamentoDiaria>.Falha(n);
        var d = (await ListarDiariasAsync(empresa, DateOnly.MinValue, DateOnly.MaxValue, null, null, ct)).FirstOrDefault(x => x.Id == id);
        if (d is null) return Resultado<ResultadoPagamentoDiaria>.Falha("Diária não encontrada.");
        if (d.Situacao != "LANCADA") return Resultado<ResultadoPagamentoDiaria>.Falha("Só diárias lançadas podem ser pagas.");
        if (d.ValorPagamento <= 0) return Resultado<ResultadoPagamentoDiaria>.Falha("O valor da diária é zero.");
        return await Tentar(async () =>
        {
            var hoje = DateOnly.FromDateTime(DateTime.Today);
            var duplicata = await _db.EscalarAsync<long>("""
                INSERT INTO fiscal.duplicata (empresa_id, tipo, parcela, numero_documento, emissao, vencimento, valor, saldo, observacao, origem, criado_por)
                VALUES (@e, 'PAGAR', '1/1', @doc, @em, @v, @val, @val, @obs, 'MANUAL', @u) RETURNING id
                """, ct, ("e", empresa), ("doc", $"DIA-{d.Numero}"), ("em", vencimento < hoje ? vencimento : hoje), ("v", vencimento), ("val", d.ValorPagamento),
                ("obs", $"Diária nº {d.Numero} – {d.Colaborador}"), ("u", usuario));
            await _db.ExecutarAsync("UPDATE frota.diaria SET situacao = 'PAGA', duplicata_id = @dp WHERE id = @id", ct, ("dp", duplicata), ("id", id));
            // contabilização: em lote, só a pendência; on-line, o lançamento na hora (se falhar, a pendência guarda o pagamento para contabilizar depois)
            var pendencia = false;
            long? lancamento = null;
            string? aviso = null;
            var referencia = new NovaPendencia(OrigensPendencia.Frota, "DIARIA", $"DIARIA:{id}", d.Data, $"Diária nº {d.Numero} – {d.Colaborador}", d.ValorPagamento);
            if (await app.Pendencias.ModoDaEmpresaAsync(empresa, ct) == ModoContabilizacao.Lote)
                pendencia = await app.Pendencias.RegistrarAsync(empresa, referencia, usuario, ct);
            else if (!(await ConfiguracaoAsync(empresa, ct)).ContabilizaDiarias)
                aviso = "O pagamento não foi contabilizado: informe as contas das diárias em Cadastros › Parâmetros › Frota.";
            else
            {
                var r = await ContabilizarDiariaAsync(empresa, d with { Situacao = "PAGA" }, usuario, ct);
                if (r.Sucesso) lancamento = r.LancamentoId;
                else
                {
                    pendencia = await app.Pendencias.RegistrarAsync(empresa, referencia, usuario, ct);
                    aviso = "A diária foi paga, mas não foi contabilizada (" + string.Join(" ", r.Erros) + "). Ela ficou nas pendências de contabilização.";
                }
            }
            return new ResultadoPagamentoDiaria(duplicata, d.ValorPagamento, pendencia, lancamento, aviso);
        });
    }

    /// <summary>Acerto (prestação de contas) da diária com adiantamento: informa o valor gasto; o saldo (adiantamento - gasto) é a devolver ou a pagar.</summary>
    public async Task<Resultado<decimal>> AcertarDiariaAsync(long empresa, long id, decimal valorGasto, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaOperacao, ct) is { } n) return Resultado<decimal>.Falha(n);
        if (valorGasto < 0) return Resultado<decimal>.Falha("O valor gasto não pode ser negativo.");
        var d = (await ListarDiariasAsync(empresa, DateOnly.MinValue, DateOnly.MaxValue, null, null, ct)).FirstOrDefault(x => x.Id == id);
        if (d is null) return Resultado<decimal>.Falha("Diária não encontrada.");
        if (d.Adiantamento <= 0) return Resultado<decimal>.Falha("Esta diária não teve adiantamento: não há o que acertar.");
        if (d.Situacao != "PAGA") return Resultado<decimal>.Falha(d.Situacao == "LANCADA" ? "Pague o adiantamento antes de acertar." : "A diária já foi acertada.");
        return await Tentar(async () =>
        {
            await _db.ExecutarAsync("UPDATE frota.diaria SET valor_gasto = @g, situacao = 'ACERTADA' WHERE id = @id", ct, ("g", valorGasto), ("id", id));
            return d.Adiantamento - valorGasto;
        });
    }

    public async Task<Resultado> ExcluirDiariaAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaOperacao, ct) is { } n) return Resultado.Falha(n);
        return await Tentar(async () =>
        {
            if (await _db.ExecutarAsync("DELETE FROM frota.diaria WHERE id = @id AND empresa_id = @e AND situacao = 'LANCADA'", ct, ("id", id), ("e", empresa)) == 0)
                throw new ViolacaoRegraException("Só diárias lançadas (ainda não pagas) podem ser excluídas.");
        });
    }

    // ------------------------------------------------------------------ coletas

    public async Task<List<ParticipanteResumo>> ParticipantesAsync(long empresa, long usuario, CancellationToken ct = default)
        => !await PodeVerAsync(usuario, empresa, ct) ? [] : await _db.ListarAsync("SELECT id, codigo, nome, cpf_cnpj FROM core.participante WHERE empresa_id = @e ORDER BY nome",
                                                                                 r => new ParticipanteResumo(r.GetInt64(0), r.GetString(1), r.GetString(2), r.Texto(3)), ct, ("e", empresa));

    public async Task<List<Coleta>> ColetasAsync(long empresa, DateOnly inicio, DateOnly fim, string? situacao, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await ListarColetasAsync(empresa, inicio, fim, situacao, ct) : [];

    private Task<List<Coleta>> ListarColetasAsync(long empresa, DateOnly inicio, DateOnly fim, string? situacao, CancellationToken ct) => _db.ListarAsync("""
        SELECT c.id, c.numero, c.agendada_em, c.remetente_id, c.destinatario_id, c.endereco_coleta, c.mercadoria, c.volumes, c.peso_kg, c.valor_mercadoria, c.veiculo_id,
               c.motorista_id, c.rota_id, c.situacao, c.observacao, rm.nome, ds.nome, v.placa, m.nome, ro.descricao
          FROM frota.coleta c LEFT JOIN core.participante rm ON rm.id = c.remetente_id LEFT JOIN core.participante ds ON ds.id = c.destinatario_id
               LEFT JOIN frota.veiculo v ON v.id = c.veiculo_id LEFT JOIN frota.motorista m ON m.id = c.motorista_id LEFT JOIN frota.rota ro ON ro.id = c.rota_id
         WHERE c.empresa_id = @e AND c.agendada_em::date BETWEEN @i AND @f AND (@s::text IS NULL OR c.situacao = @s) ORDER BY c.agendada_em DESC, c.numero DESC
        """, r => new Coleta(r.GetInt64(0), r.GetInt32(1), r.GetDateTime(2), r.Id(3), r.Id(4), r.GetString(5), r.Texto(6), r.GetInt32(7), r.GetDecimal(8), r.GetDecimal(9),
                             r.Id(10), r.Id(11), r.Id(12), r.GetString(13), r.Texto(14), r.Texto(15), r.Texto(16), r.Texto(17), r.Texto(18), r.Texto(19)),
        ct, ("e", empresa), ("i", inicio), ("f", fim), ("s", situacao));

    public async Task<Resultado<int>> GravarColetaAsync(long empresa, Coleta c, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaOperacao, ct) is { } n) return Resultado<int>.Falha(n);
        c = c with { EnderecoColeta = c.EnderecoColeta?.Trim() ?? "", Mercadoria = N(c.Mercadoria), Observacao = N(c.Observacao) };
        if (RegrasLogistica.Validar(c) is { Count: > 0 } erros) return Resultado<int>.Falha(erros);
        return await Tentar(async () =>
        {
            if (c.Id != 0)
            {
                var atual = await _db.EscalarAsync<string>("SELECT situacao FROM frota.coleta WHERE id = @id AND empresa_id = @e", ct, ("id", c.Id), ("e", empresa))
                            ?? throw new ViolacaoRegraException("Coleta não encontrada.");
                if (!RegrasLogistica.ColetaEditavel(atual)) throw new ViolacaoRegraException($"A coleta já está {RegrasFrota.Rotulo(atual).ToLowerInvariant()} e não pode mais ser alterada.");
                if (!RegrasLogistica.TransicaoColetaValida(atual, c.Situacao))
                    throw new ViolacaoRegraException($"Não é possível mudar a coleta de {RegrasFrota.Rotulo(atual).ToLowerInvariant()} para {RegrasFrota.Rotulo(c.Situacao).ToLowerInvariant()}.");
            }
            else if (c.Situacao is "COLETADA") throw new ViolacaoRegraException("Uma coleta nova começa agendada.");
            var numero = c.Id == 0 ? await ProximoNumeroAsync("coleta", empresa, ct) : c.Numero;
            (string, object?)[] p =
            [
                ("e", empresa), ("id", c.Id), ("n", numero), ("ag", c.AgendadaEm), ("rm", c.RemetenteId), ("ds", c.DestinatarioId), ("end", c.EnderecoColeta), ("mer", c.Mercadoria),
                ("vol", c.Volumes), ("peso", c.PesoKg), ("val", c.ValorMercadoria), ("v", c.VeiculoId), ("m", c.MotoristaId), ("ro", c.RotaId), ("s", c.Situacao), ("o", c.Observacao),
            ];
            if (c.Id == 0)
                await _db.ExecutarAsync("""
                    INSERT INTO frota.coleta (empresa_id, numero, agendada_em, remetente_id, destinatario_id, endereco_coleta, mercadoria, volumes, peso_kg, valor_mercadoria,
                                              veiculo_id, motorista_id, rota_id, situacao, observacao)
                    VALUES (@e, @n, @ag, @rm, @ds, @end, @mer, @vol, @peso, @val, @v, @m, @ro, @s, @o)
                    """, ct, p);
            else await _db.ExecutarAsync("""
                    UPDATE frota.coleta SET agendada_em = @ag, remetente_id = @rm, destinatario_id = @ds, endereco_coleta = @end, mercadoria = @mer, volumes = @vol, peso_kg = @peso,
                           valor_mercadoria = @val, veiculo_id = @v, motorista_id = @m, rota_id = @ro, situacao = @s, observacao = @o WHERE id = @id AND empresa_id = @e
                    """, ct, p);
            return numero;
        });
    }

    public async Task<Resultado> ExcluirColetaAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaOperacao, ct) is { } n) return Resultado.Falha(n);
        return await Tentar(async () =>
        {
            if (await _db.ExecutarAsync("DELETE FROM frota.coleta WHERE id = @id AND empresa_id = @e AND situacao IN ('AGENDADA', 'CANCELADA')", ct, ("id", id), ("e", empresa)) == 0)
                throw new ViolacaoRegraException("Só coletas agendadas ou canceladas podem ser excluídas.");
        });
    }

    /// <summary>Ordem de coleta pronta para imprimir (PDF, retrato) com os dados da coleta e campos para assinatura.</summary>
    public static byte[] OrdemColetaPdf(Coleta c, string empresaDescricao, string emitidoPor)
    {
        var linhas = new List<LinhaRelatorio>();
        void L(string campo, string? valor) => linhas.Add(new LinhaRelatorio([campo, string.IsNullOrWhiteSpace(valor) ? "-" : valor]));
        L("Data e hora agendadas", c.AgendadaEm.ToString("dd/MM/yyyy HH:mm"));
        L("Situação", RegrasFrota.Rotulo(c.Situacao));
        L("Remetente", c.Remetente);
        L("Destinatário", c.Destinatario);
        L("Endereço de coleta", c.EnderecoColeta);
        L("Mercadoria", c.Mercadoria);
        L("Volumes", c.Volumes.ToString());
        L("Peso (kg)", c.PesoKg.ToString("N3", Formato.PtBr));
        L("Valor da mercadoria (R$)", Formato.Moeda(c.ValorMercadoria));
        L("Veículo", c.Placa is null ? null : RegrasFrota.FormatarPlaca(c.Placa));
        L("Motorista", c.MotoristaNome);
        L("Rota", c.RotaNome);
        L("Observação", c.Observacao);
        var tabela = new TabelaRelatorio($"Ordem de coleta nº {c.Numero}", [new("Campo", LarguraCm: 5), new("Informação")], linhas,
                                         "Assinatura do motorista: ______________________________      Assinatura de quem entregou: ______________________________");
        var doc = ExportacaoRelatorio.ParaPdf($"Ordem de coleta nº {c.Numero}", [empresaDescricao, $"Emitida em {DateTime.Now:dd/MM/yyyy HH:mm} por {emitidoPor}"], [tabela], false);
        return new GeradorPdf().Gerar(doc).Conteudo;
    }
}

/// <summary>Roteirizador: trajeto pela estrada e custo por km sugerido para o veículo.</summary>
public sealed partial class ServicoFrota
{
    /// <summary>Botão "Distâncias pela estrada" do roteirizador: o serviço público de rotas devolve o km de cada trecho e o desenho do caminho.</summary>
    public async Task<Resultado<TrajetoEstrada>> BuscarTrajetoAsync(long empresa, IReadOnlyList<ParadaRota> paradas, long usuario, HttpClient? http = null,
                                                                    string? urlBase = null, CancellationToken ct = default)
    {
        if (!await PodeVerAsync(usuario, empresa, ct)) return Resultado<TrajetoEstrada>.Falha("Sem permissão para o roteirizador.");
        return await ServicoRotasOnline.ConsultarTrajetoAsync(http ?? HttpRotas, paradas, urlBase, ct);
    }

    /// <summary>
    /// Custo por km do veículo nos últimos meses: abastecimentos e ordens de serviço (não canceladas nem reprovadas) divididos pelo km rodado
    /// entre o menor e o maior hodômetro dos abastecimentos; null se não houver dados suficientes.
    /// </summary>
    public async Task<decimal?> CustoPorKmAsync(long empresa, long veiculo, DateOnly hoje, long usuario, int meses = 6, CancellationToken ct = default)
    {
        if (!await PodeVerAsync(usuario, empresa, ct)) return null;
        var inicio = hoje.AddMonths(-meses);
        var abastecimentos = await ListarAbastecimentosAsync(empresa, inicio, hoje, veiculo, ct);
        var manutencao = (await ListarOrdensAsync(empresa, inicio, hoje, ct))
            .Where(o => o.VeiculoId == veiculo && o.Data >= inicio && o.Situacao is not ("CANCELADA" or "REPROVADA")).Sum(o => o.Total);
        return RegrasRoteirizacao.CustoPorKm(abastecimentos, manutencao);
    }
}
