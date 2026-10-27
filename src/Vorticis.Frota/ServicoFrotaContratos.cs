using Vorticis.Application.Imobilizado;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Imobilizado;
using Vorticis.Infra.Postgres;
using P = Vorticis.Domain.Seguranca.Permissao;

namespace Vorticis.Frota;

/// <summary>Posição de um contrato numa data: parcelas pagas, saldo devedor em curto e longo prazo e o bem do imobilizado.</summary>
public sealed record PosicaoContrato(ContratoFinanceiro Contrato, int Pagas, decimal SaldoDevedor, DivisaoPrazos Prazos, string Placas);

/// <summary>
/// Financiamentos e consórcios (Financeiro › Contas): cadastro e parcelas (FISCAL_DUPLICATAS_MANTER), pagamento das parcelas
/// (FISCAL_DUPLICATAS_BAIXAR), ligação com o imobilizado e contabilização opcional (LANCAMENTO_INCLUIR, conferida pelo serviço de lançamentos):
/// contratação, pagamentos, contemplação e reclassificação do longo para o curto prazo. As despesas vão ao centro de custo da frota de
/// cada placa do rateio.
/// </summary>
public sealed partial class ServicoFrota
{
    private async Task<bool> PodeVerContratosAsync(long u, long e, CancellationToken ct)
    {
        var p = await _aut.ObterAsync(u, e, ct);
        return p.Administrador || new[] { P.FiscalDuplicatasManter, P.FiscalDuplicatasBaixar, P.LancamentoIncluir, P.FrotaRelatorios }.Any(p.Pode);
    }

    private const string SelectContrato = """
        SELECT c.id, c.tipo, c.numero, c.descricao, c.credor_id, c.data_contrato, c.valor_bem, c.entrada, c.encargos, c.taxa_juros, c.sistema,
               c.taxa_administracao, c.fundo_reserva, c.seguro_mensal, c.parcelas, c.primeiro_vencimento, c.data_contemplacao, c.bem_id, c.contabiliza,
               c.conta_bem, c.conta_pagamento, c.conta_curto_prazo, c.conta_longo_prazo, c.conta_despesa, c.conta_consorcio, c.situacao, c.observacao,
               p.nome, b.codigo::text || ' – ' || b.descricao
          FROM frota.contrato c LEFT JOIN core.participante p ON p.id = c.credor_id LEFT JOIN imobilizado.bem b ON b.id = c.bem_id
        """;

    private static ContratoFinanceiro LerContrato(Npgsql.NpgsqlDataReader r) =>
        new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.Id(4), r.GetFieldValue<DateOnly>(5), r.GetDecimal(6), r.GetDecimal(7), r.GetDecimal(8),
            r.GetDecimal(9), r.GetString(10), r.GetDecimal(11), r.GetDecimal(12), r.GetDecimal(13), r.GetInt16(14), r.GetFieldValue<DateOnly>(15), r.Data(16), r.Id(17),
            r.GetBoolean(18), r.Texto(19), r.Texto(20), r.Texto(21), r.Texto(22), r.Texto(23), r.Texto(24), r.GetString(25), r.Texto(26), r.Texto(27), r.Texto(28));

    public async Task<List<ContratoFinanceiro>> ContratosAsync(long empresa, string? tipo, long usuario, CancellationToken ct = default)
        => await PodeVerContratosAsync(usuario, empresa, ct)
            ? await _db.ListarAsync(SelectContrato + " WHERE c.empresa_id = @e AND (@t::text IS NULL OR c.tipo = @t) ORDER BY c.data_contrato DESC, c.numero", LerContrato, ct,
                                    ("e", empresa), ("t", tipo))
            : [];

    private async Task<ContratoFinanceiro?> ObterContratoAsync(long empresa, long id, CancellationToken ct)
        => (await _db.ListarAsync(SelectContrato + " WHERE c.empresa_id = @e AND c.id = @id", LerContrato, ct, ("e", empresa), ("id", id))).FirstOrDefault();

    public async Task<List<ParcelaContrato>> ParcelasContratoAsync(long empresa, long contrato, long usuario, CancellationToken ct = default)
        => await PodeVerContratosAsync(usuario, empresa, ct) ? await ListarParcelasAsync(empresa, contrato, ct) : [];

    private Task<List<ParcelaContrato>> ListarParcelasAsync(long empresa, long contrato, CancellationToken ct) => _db.ListarAsync("""
        SELECT p.id, p.numero, p.vencimento, p.amortizacao, p.juros, p.outros, p.seguro, p.saldo, p.pago_em, p.valor_pago,
               EXISTS (SELECT 1 FROM frota.contrato_lancamento l WHERE l.parcela_id = p.id)
          FROM frota.contrato_parcela p JOIN frota.contrato c ON c.id = p.contrato_id
         WHERE c.empresa_id = @e AND p.contrato_id = @c ORDER BY p.numero
        """, r => new ParcelaContrato(r.GetInt64(0), r.GetInt16(1), r.GetFieldValue<DateOnly>(2), r.GetDecimal(3), r.GetDecimal(4), r.GetDecimal(5), r.GetDecimal(6),
                                      r.GetDecimal(7), r.Data(8), r.Valor(9), r.GetBoolean(10)), ct, ("e", empresa), ("c", contrato));

    public async Task<List<RateioContrato>> RateioContratoAsync(long empresa, long contrato, long usuario, CancellationToken ct = default)
        => await PodeVerContratosAsync(usuario, empresa, ct) ? await ListarRateioAsync(empresa, contrato, ct) : [];

    private Task<List<RateioContrato>> ListarRateioAsync(long empresa, long contrato, CancellationToken ct) => _db.ListarAsync("""
        SELECT r.veiculo_id, r.percentual, v.placa FROM frota.contrato_rateio r JOIN frota.contrato c ON c.id = r.contrato_id JOIN frota.veiculo v ON v.id = r.veiculo_id
         WHERE c.empresa_id = @e AND r.contrato_id = @c ORDER BY v.placa
        """, r => new RateioContrato(r.GetInt64(0), r.GetDecimal(1), RegrasFrota.FormatarPlaca(r.GetString(2))), ct, ("e", empresa), ("c", contrato));

    /// <summary>Saldos já contabilizados (soma dos movimentos) e se a contratação já foi contabilizada.</summary>
    public async Task<SaldosContabeisContrato> SaldosContabeisAsync(long contrato, CancellationToken ct = default)
        => (await _db.ListarAsync("""
            SELECT COALESCE(SUM(curto_prazo), 0), COALESCE(SUM(longo_prazo), 0), COALESCE(SUM(ativo_consorcio), 0),
                   COALESCE(bool_or(tipo IN ('CONTRATACAO', 'CONTEMPLACAO')), FALSE)
              FROM frota.contrato_lancamento WHERE contrato_id = @c
            """, r => new SaldosContabeisContrato(r.GetDecimal(0), r.GetDecimal(1), r.GetDecimal(2), r.GetBoolean(3)), ct, ("c", contrato)))[0];

    public async Task<Resultado<long>> GravarContratoAsync(long empresa, ContratoFinanceiro c, IReadOnlyList<RateioContrato> rateio, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FiscalDuplicatasManter, ct) is { } n) return Resultado<long>.Falha(n);
        c = c with
        {
            Numero = (c.Numero ?? "").Trim(), Descricao = (c.Descricao ?? "").Trim(), ContaBem = N(c.ContaBem), ContaPagamento = N(c.ContaPagamento),
            ContaCurtoPrazo = N(c.ContaCurtoPrazo), ContaLongoPrazo = N(c.ContaLongoPrazo), ContaDespesa = N(c.ContaDespesa), ContaConsorcio = N(c.ContaConsorcio),
            Observacao = N(c.Observacao),
        };
        if (!c.Consorcio) c = c with { TaxaAdministracao = 0, FundoReserva = 0, SeguroMensal = 0, DataContemplacao = null };
        else c = c with { Entrada = 0, Encargos = 0, TaxaJuros = 0, Sistema = "PRICE" };
        if (RegrasContratos.Validar(c, rateio) is { Count: > 0 } erros) return Resultado<long>.Falha(erros);

        var gerarParcelas = true;
        if (c.Id != 0)
        {
            if (await ObterContratoAsync(empresa, c.Id, ct) is not { } atual) return Resultado<long>.Falha("Contrato não encontrado.");
            var parcelas = await ListarParcelasAsync(empresa, c.Id, ct);
            var movimentos = await _db.EscalarAsync<long>("SELECT count(*) FROM frota.contrato_lancamento WHERE contrato_id = @c", ct, ("c", c.Id));
            var financeiroIgual = (atual.DataContrato, atual.ValorBem, atual.Entrada, atual.Encargos, atual.TaxaJuros, atual.Sistema, atual.TaxaAdministracao,
                                   atual.FundoReserva, atual.SeguroMensal, atual.Parcelas, atual.PrimeiroVencimento)
                                  == (c.DataContrato, c.ValorBem, c.Entrada, c.Encargos, c.TaxaJuros, c.Sistema, c.TaxaAdministracao, c.FundoReserva,
                                      c.SeguroMensal, c.Parcelas, c.PrimeiroVencimento);
            gerarParcelas = !financeiroIgual;
            if (gerarParcelas && (movimentos > 0 || parcelas.Any(p => p.Paga)))
                return Resultado<long>.Falha("O contrato já tem parcelas pagas ou lançamentos contábeis: valores, taxas, prazos e datas não podem mudar (estorne os pagamentos antes).");
            if (atual.DataContemplacao != c.DataContemplacao && movimentos > 0)
                return Resultado<long>.Falha("A contemplação é registrada pelo botão Registrar contemplação.");
            if (atual.Contabiliza && !c.Contabiliza && movimentos > 0)
                return Resultado<long>.Falha("O contrato já tem lançamentos contábeis: não é possível desligar a contabilização.");
            if (!atual.Contabiliza && c.Contabiliza && parcelas.Any(p => p.Paga))
                return Resultado<long>.Falha("Há parcelas pagas sem contabilização: ligue a contabilização antes do primeiro pagamento (ou estorne os pagamentos).");
        }
        (string, object?)[] p =
        [
            ("e", empresa), ("id", c.Id), ("t", c.Tipo), ("n", c.Numero), ("d", c.Descricao), ("cr", c.CredorId), ("dc", c.DataContrato), ("vb", c.ValorBem),
            ("en", c.Entrada), ("ec", c.Encargos), ("tj", c.TaxaJuros), ("si", c.Sistema), ("ta", c.TaxaAdministracao), ("fr", c.FundoReserva),
            ("sm", c.SeguroMensal), ("np", (short)c.Parcelas), ("pv", c.PrimeiroVencimento), ("dct", c.DataContemplacao), ("b", c.BemId), ("cb", c.Contabiliza),
            ("k1", c.ContaBem), ("k2", c.ContaPagamento), ("k3", c.ContaCurtoPrazo), ("k4", c.ContaLongoPrazo), ("k5", c.ContaDespesa), ("k6", c.ContaConsorcio),
            ("s", c.Situacao), ("o", c.Observacao),
        ];
        var r = await Tentar(async () => c.Id == 0
            ? await _db.EscalarAsync<long>("""
                INSERT INTO frota.contrato (empresa_id, tipo, numero, descricao, credor_id, data_contrato, valor_bem, entrada, encargos, taxa_juros, sistema,
                       taxa_administracao, fundo_reserva, seguro_mensal, parcelas, primeiro_vencimento, data_contemplacao, bem_id, contabiliza, conta_bem,
                       conta_pagamento, conta_curto_prazo, conta_longo_prazo, conta_despesa, conta_consorcio, situacao, observacao)
                VALUES (@e, @t, @n, @d, @cr, @dc, @vb, @en, @ec, @tj, @si, @ta, @fr, @sm, @np, @pv, @dct, @b, @cb, @k1, @k2, @k3, @k4, @k5, @k6, @s, @o) RETURNING id
                """, ct, p)
            : await _db.EscalarAsync<long?>("""
                UPDATE frota.contrato SET numero = @n, descricao = @d, credor_id = @cr, data_contrato = @dc, valor_bem = @vb, entrada = @en, encargos = @ec,
                       taxa_juros = @tj, sistema = @si, taxa_administracao = @ta, fundo_reserva = @fr, seguro_mensal = @sm, parcelas = @np,
                       primeiro_vencimento = @pv, data_contemplacao = @dct, bem_id = @b, contabiliza = @cb, conta_bem = @k1, conta_pagamento = @k2,
                       conta_curto_prazo = @k3, conta_longo_prazo = @k4, conta_despesa = @k5, conta_consorcio = @k6, situacao = @s, observacao = @o
                 WHERE id = @id AND empresa_id = @e AND tipo = @t RETURNING id
                """, ct, p) ?? throw new ViolacaoRegraException("Contrato não encontrado."));
        if (!r.Sucesso)
            return r.Erros.Any(e => e.StartsWith("Já existe um cadastro", StringComparison.Ordinal))
                ? Resultado<long>.Falha($"Já existe {(c.Consorcio ? "o consórcio" : "o financiamento")} nº {c.Numero}.")
                : r;
        var id = r.Valor;
        if (gerarParcelas)
        {
            await _db.ExecutarAsync("DELETE FROM frota.contrato_parcela WHERE contrato_id = @c", ct, ("c", id));
            foreach (var x in RegrasContratos.Calcular(c))
                await _db.ExecutarAsync("""
                    INSERT INTO frota.contrato_parcela (contrato_id, numero, vencimento, amortizacao, juros, outros, seguro, saldo)
                    VALUES (@c, @n, @v, @a, @j, @o, @s, @sd)
                    """, ct, ("c", id), ("n", (short)x.Numero), ("v", x.Vencimento), ("a", x.Amortizacao), ("j", x.Juros), ("o", x.Outros), ("s", x.Seguro), ("sd", x.Saldo));
        }
        await _db.ExecutarAsync("DELETE FROM frota.contrato_rateio WHERE contrato_id = @c", ct, ("c", id));
        foreach (var x in rateio)
            await _db.ExecutarAsync("INSERT INTO frota.contrato_rateio (contrato_id, veiculo_id, percentual) VALUES (@c, @v, @p)", ct,
                                    ("c", id), ("v", x.VeiculoId), ("p", x.Percentual));
        return Resultado<long>.Ok(id);
    }

    public async Task<Resultado> ExcluirContratoAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FiscalDuplicatasManter, ct) is { } n) return Resultado.Falha(n);
        if (await _db.EscalarAsync<long>("SELECT count(*) FROM frota.contrato_lancamento l JOIN frota.contrato c ON c.id = l.contrato_id WHERE c.id = @c AND c.empresa_id = @e",
                                         ct, ("c", id), ("e", empresa)) > 0)
            return Resultado.Falha("O contrato tem lançamentos contábeis: estorne os pagamentos e a contratação (ou mude a situação para Cancelado).");
        return await Tentar(() => _db.ExecutarAsync("DELETE FROM frota.contrato WHERE id = @id AND empresa_id = @e", ct, ("id", id), ("e", empresa)));
    }

    // ------------------------------------------------------------------ contabilização

    /// <summary>Centro de custo de cada veículo: o da frota do veículo (Cadastros › Frotas) ou o centro de custo ligado à frota.</summary>
    private async Task<Dictionary<long, string?>> CentrosDosVeiculosAsync(long empresa, CancellationToken ct)
        => (await _db.ListarAsync("""
            SELECT v.id, COALESCE(cf.codigo, (SELECT cc.codigo FROM contabil.centro_custo cc WHERE cc.frota_id = f.id ORDER BY cc.id LIMIT 1))
              FROM frota.veiculo v LEFT JOIN frota.frota f ON f.id = v.frota_id LEFT JOIN contabil.centro_custo cf ON cf.id = f.centro_custo_id
             WHERE v.empresa_id = @e
            """, r => (r.GetInt64(0), r.Texto(1)), ct, ("e", empresa))).ToDictionary(x => x.Item1, x => x.Item2);

    /// <summary>Partidas de uma despesa rateada por placa (débito; valor negativo vira crédito), com o centro de custo de cada veículo.</summary>
    private async Task<List<PartidaProposta>> DespesaRateadaAsync(long empresa, ContratoFinanceiro c, decimal valor, string historico, CancellationToken ct)
    {
        if (valor == 0) return [];
        var rateio = await ListarRateioAsync(empresa, c.Id, ct);
        var centros = rateio.Count == 0 ? [] : await CentrosDosVeiculosAsync(empresa, ct);
        return [.. RegrasContratos.Ratear(Math.Abs(valor), rateio).Select(x => new PartidaProposta(c.ContaDespesa!, valor > 0 ? IndicadorDC.D : IndicadorDC.C, x.Valor,
                    historico + (x.VeiculoId is { } v && rateio.FirstOrDefault(r => r.VeiculoId == v)?.Placa is { } placa ? $" – {placa}" : ""),
                    x.VeiculoId is { } vid ? centros.GetValueOrDefault(vid) : null))];
    }

    private async Task<string?> ContaDoBemAsync(ContratoFinanceiro c, CancellationToken ct)
        => c.BemId is { } b
            ? await _db.EscalarAsync<string>("SELECT p.codigo FROM imobilizado.bem b JOIN contabil.plano_contas p ON p.id = b.conta_bem_id WHERE b.id = @b", ct, ("b", b)) ?? c.ContaBem
            : c.ContaBem;

    private static string Nome(ContratoFinanceiro c) => $"{RegrasContratos.Rotulo(c.Tipo)} nº {c.Numero} – {c.Descricao}";

    /// <summary>Efetiva o lançamento e guarda o movimento do contrato (CP, LP e ativo do consórcio).</summary>
    private async Task<Resultado<long>> LancarAsync(long empresa, ContratoFinanceiro c, string tipo, DateOnly data, long? parcela, List<PartidaProposta> partidas,
                                                     decimal curto, decimal longo, decimal ativo, long usuario, CancellationToken ct)
    {
        var r = await app.Lancamentos.EfetivarAsync(new LancamentoProposto(empresa, data, OrigemLancamento.Frota, [.. partidas.Where(x => x.Valor != 0)]), usuario, ct);
        if (!r.Sucesso) return Resultado<long>.Falha(r.Erros);
        await _db.ExecutarAsync("""
            INSERT INTO frota.contrato_lancamento (contrato_id, tipo, data, parcela_id, curto_prazo, longo_prazo, ativo_consorcio, lancamento_id)
            VALUES (@c, @t, @d, @p, @cp, @lp, @at, @l)
            """, ct, ("c", c.Id), ("t", tipo), ("d", data), ("p", parcela), ("cp", curto), ("lp", longo), ("at", ativo), ("l", r.Valor.Id));
        return Resultado<long>.Ok(r.Valor.Id);
    }

    /// <summary>
    /// Contratação do financiamento: D bem (valor do bem), D despesa (encargos, rateados por placa), C pagamento (entrada) e C curto/longo prazo
    /// (o valor financiado, pelas parcelas que vencem até 12 meses depois da data do contrato e as demais).
    /// </summary>
    public async Task<Resultado<long>> ContabilizarContratacaoAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await ObterContratoAsync(empresa, id, ct) is not { } c) return Resultado<long>.Falha("Contrato não encontrado.");
        if (c.Consorcio) return Resultado<long>.Falha("No consórcio não há lançamento na adesão: as parcelas pagas vão para o ativo e, na contemplação, use Registrar contemplação.");
        if (!c.Contabiliza) return Resultado<long>.Falha("Marque Contabilizar no Contábil e informe as contas do contrato.");
        if ((await SaldosContabeisAsync(id, ct)).ContratacaoContabilizada) return Resultado<long>.Falha("A contratação já está contabilizada.");
        var parcelas = await ListarParcelasAsync(empresa, id, ct);
        if (parcelas.Any(p => p.Paga)) return Resultado<long>.Falha("Há parcelas pagas: estorne os pagamentos, contabilize a contratação e pague de novo.");
        var prazos = RegrasContratos.Dividir(c, parcelas, c.DataContrato);
        var hist = $"Contratação – {Nome(c)}" + (c.Credor is null ? "" : $" ({c.Credor})");
        if (await ContaDoBemAsync(c, ct) is not { } contaBem) return Resultado<long>.Falha("Informe a conta do bem.");
        var partidas = new List<PartidaProposta> { new(contaBem, IndicadorDC.D, c.ValorBem, hist) };
        partidas.AddRange(await DespesaRateadaAsync(empresa, c, c.Encargos, hist + " – encargos", ct));
        partidas.Add(new(c.ContaPagamento!, IndicadorDC.C, c.Entrada, hist + " – entrada"));
        partidas.Add(new(c.ContaCurtoPrazo!, IndicadorDC.C, prazos.CurtoPrazo, hist + " – curto prazo"));
        partidas.Add(new(c.ContaLongoPrazo!, IndicadorDC.C, prazos.LongoPrazo, hist + " – longo prazo"));
        return await LancarAsync(empresa, c, "CONTRATACAO", c.DataContrato, null, partidas, prazos.CurtoPrazo, prazos.LongoPrazo, 0, usuario, ct);
    }

    /// <summary>Desfaz a contabilização da contratação (estorno), se ainda não houver outros lançamentos do contrato.</summary>
    public async Task<Resultado> DesfazerContratacaoAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await ObterContratoAsync(empresa, id, ct) is not { } c) return Resultado.Falha("Contrato não encontrado.");
        var movimentos = await _db.ListarAsync("SELECT id, tipo, lancamento_id FROM frota.contrato_lancamento WHERE contrato_id = @c", r => (r.GetInt64(0), r.GetString(1), r.Id(2)),
                                               ct, ("c", id));
        if (movimentos.Count == 0) return Resultado.Falha("A contratação não está contabilizada.");
        if (movimentos.Any(m => m.Item2 != "CONTRATACAO")) return Resultado.Falha("O contrato tem pagamentos ou reclassificações contabilizados: estorne-os antes.");
        foreach (var m in movimentos)
        {
            if (m.Item3 is { } l)
            {
                var r = await app.Lancamentos.EstornarAsync(empresa, l, c.DataContrato, $"desfazer contabilização da contratação – {Nome(c)}", usuario, ct);
                if (!r.Sucesso) return Resultado.Falha(r.Erros);
            }
            await _db.ExecutarAsync("DELETE FROM frota.contrato_lancamento WHERE id = @i", ct, ("i", m.Item1));
        }
        return Resultado.Ok();
    }

    /// <summary>
    /// Pagamento de uma parcela (data e valor pago; a diferença para o valor da parcela é juros/multa ou desconto). Contabilizando:
    /// D curto prazo (amortização; o que faltar no curto prazo sai do longo), ou no consórcio ainda não contemplado D consórcio (fundo comum);
    /// D consórcio (fundo de reserva); D despesa (juros, taxa de administração, seguro e diferença, rateados por placa); C pagamento (valor pago).
    /// </summary>
    public async Task<Resultado> PagarParcelaAsync(long empresa, long id, int numero, DateOnly data, decimal valorPago, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FiscalDuplicatasBaixar, ct) is { } n) return Resultado.Falha(n);
        if (await ObterContratoAsync(empresa, id, ct) is not { } c) return Resultado.Falha("Contrato não encontrado.");
        if (c.Situacao == "CANCELADO") return Resultado.Falha("O contrato está cancelado.");
        var parcelas = await ListarParcelasAsync(empresa, id, ct);
        if (parcelas.FirstOrDefault(p => p.Numero == numero) is not { } parcela) return Resultado.Falha("Parcela não encontrada.");
        if (parcela.Paga) return Resultado.Falha($"A parcela {numero} já está paga.");
        if (valorPago <= 0) return Resultado.Falha("Informe o valor pago.");
        if (data < c.DataContrato) return Resultado.Falha("A data do pagamento não pode ser anterior ao contrato.");
        if (c.Contabiliza)
        {
            var saldos = await SaldosContabeisAsync(id, ct);
            if (!c.Consorcio && !saldos.ContratacaoContabilizada) return Resultado.Falha("Contabilize a contratação antes de pagar as parcelas.");
            var hist = $"Parcela {numero}/{c.Parcelas} – {Nome(c)}";
            var partidas = new List<PartidaProposta>();
            decimal curto = 0, longo = 0, ativo = 0;
            if (c.TemDivida)
            {
                curto = Math.Min(parcela.Amortizacao, Math.Max(0, saldos.CurtoPrazo));
                longo = parcela.Amortizacao - curto;
                partidas.Add(new(c.ContaCurtoPrazo!, IndicadorDC.D, curto, hist));
                partidas.Add(new(c.ContaLongoPrazo!, IndicadorDC.D, longo, hist));
            }
            else ativo += parcela.Amortizacao;
            ativo += parcela.Outros;
            if (ativo > 0) partidas.Add(new(c.ContaConsorcio!, IndicadorDC.D, ativo, hist + (c.TemDivida ? " – fundo de reserva" : "")));
            partidas.AddRange(await DespesaRateadaAsync(empresa, c, parcela.Juros + parcela.Seguro + valorPago - parcela.Valor,
                                                        hist + (c.Consorcio ? " – taxa de administração e seguro" : " – juros"), ct));
            partidas.Add(new(c.ContaPagamento!, IndicadorDC.C, valorPago, hist));
            var r = await LancarAsync(empresa, c, "PAGAMENTO", data, parcela.Id, partidas, -curto, -longo, ativo, usuario, ct);
            if (!r.Sucesso) return Resultado.Falha(r.Erros);
        }
        await _db.ExecutarAsync("UPDATE frota.contrato_parcela SET pago_em = @d, valor_pago = @v WHERE id = @i", ct, ("d", data), ("v", valorPago), ("i", parcela.Id));
        if (parcelas.All(p => p.Paga || p.Id == parcela.Id))
            await _db.ExecutarAsync("UPDATE frota.contrato SET situacao = 'QUITADO' WHERE id = @c", ct, ("c", id));
        return Resultado.Ok();
    }

    /// <summary>Estorna o pagamento de uma parcela (e o lançamento contábil dele).</summary>
    public async Task<Resultado> EstornarPagamentoAsync(long empresa, long id, int numero, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FiscalDuplicatasBaixar, ct) is { } n) return Resultado.Falha(n);
        if (await ObterContratoAsync(empresa, id, ct) is not { } c) return Resultado.Falha("Contrato não encontrado.");
        if ((await ListarParcelasAsync(empresa, id, ct)).FirstOrDefault(p => p.Numero == numero) is not { Paga: true } parcela)
            return Resultado.Falha($"A parcela {numero} não está paga.");
        if (c.Consorcio && c.DataContemplacao is not null && parcela.PagoEm < c.DataContemplacao && parcela.Contabilizada)
            return Resultado.Falha("A parcela foi paga antes da contemplação, que já está contabilizada: não pode ser estornada.");
        foreach (var (mov, lanc) in await _db.ListarAsync("SELECT id, lancamento_id FROM frota.contrato_lancamento WHERE parcela_id = @p", r => (r.GetInt64(0), r.Id(1)),
                                                          ct, ("p", parcela.Id)))
        {
            if (lanc is { } l)
            {
                var r = await app.Lancamentos.EstornarAsync(empresa, l, parcela.PagoEm!.Value, $"estorno do pagamento da parcela {numero} – {Nome(c)}", usuario, ct);
                if (!r.Sucesso) return Resultado.Falha(r.Erros);
            }
            await _db.ExecutarAsync("DELETE FROM frota.contrato_lancamento WHERE id = @i", ct, ("i", mov));
        }
        await _db.ExecutarAsync("UPDATE frota.contrato_parcela SET pago_em = NULL, valor_pago = NULL WHERE id = @i", ct, ("i", parcela.Id));
        await _db.ExecutarAsync("UPDATE frota.contrato SET situacao = 'ATIVO' WHERE id = @c AND situacao = 'QUITADO'", ct, ("c", id));
        return Resultado.Ok();
    }

    /// <summary>
    /// Contemplação do consórcio (data em que o bem foi recebido). Contabilizando: D bem (carta de crédito), C consórcio (fundo comum já pago,
    /// que estava no ativo) e C curto/longo prazo (fundo comum das parcelas que faltam).
    /// </summary>
    public async Task<Resultado> RegistrarContemplacaoAsync(long empresa, long id, DateOnly data, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FiscalDuplicatasManter, ct) is { } n) return Resultado.Falha(n);
        if (await ObterContratoAsync(empresa, id, ct) is not { } c) return Resultado.Falha("Contrato não encontrado.");
        if (!c.Consorcio) return Resultado.Falha("Só consórcios têm contemplação.");
        if (c.DataContemplacao is not null) return Resultado.Falha("A contemplação já foi registrada.");
        if (data < c.DataContrato) return Resultado.Falha("A contemplação não pode ser anterior à adesão.");
        var parcelas = await ListarParcelasAsync(empresa, id, ct);
        if (parcelas.Any(p => p.Paga && p.PagoEm > data)) return Resultado.Falha("Há parcelas pagas depois desta data: estorne-as ou informe a data certa.");
        if (c.Contabiliza)
        {
            if (parcelas.Any(p => p.Paga && !p.Contabilizada)) return Resultado.Falha("Há parcelas pagas sem contabilização.");
            if (await ContaDoBemAsync(c, ct) is not { } contaBem) return Resultado.Falha("Informe a conta do bem.");
            var contemplado = c with { DataContemplacao = data };
            var prazos = RegrasContratos.Dividir(contemplado, parcelas, data);
            var pago = parcelas.Where(p => p.Paga).Sum(p => p.Amortizacao);
            var hist = $"Contemplação – {Nome(c)}" + (c.Credor is null ? "" : $" ({c.Credor})");
            List<PartidaProposta> partidas =
            [
                new(contaBem, IndicadorDC.D, c.ValorBem, hist),
                new(c.ContaConsorcio!, IndicadorDC.C, pago, hist + " – parcelas pagas"),
                new(c.ContaCurtoPrazo!, IndicadorDC.C, prazos.CurtoPrazo, hist + " – curto prazo"),
                new(c.ContaLongoPrazo!, IndicadorDC.C, prazos.LongoPrazo, hist + " – longo prazo"),
            ];
            var r = await LancarAsync(empresa, contemplado, "CONTEMPLACAO", data, null, partidas, prazos.CurtoPrazo, prazos.LongoPrazo, -pago, usuario, ct);
            if (!r.Sucesso) return Resultado.Falha(r.Erros);
        }
        await _db.ExecutarAsync("UPDATE frota.contrato SET data_contemplacao = @d WHERE id = @c", ct, ("d", data), ("c", id));
        return Resultado.Ok();
    }

    /// <summary>
    /// Reclassificação na data-base (normalmente no fim do mês ou do exercício): passa do longo para o curto prazo o principal das parcelas que
    /// passaram a vencer nos próximos 12 meses (D longo prazo, C curto prazo). Devolve o valor reclassificado.
    /// </summary>
    public async Task<Resultado<decimal>> ReclassificarAsync(long empresa, long id, DateOnly dataBase, long usuario, CancellationToken ct = default)
    {
        if (await ObterContratoAsync(empresa, id, ct) is not { } c) return Resultado<decimal>.Falha("Contrato não encontrado.");
        if (!c.Contabiliza) return Resultado<decimal>.Falha("O contrato não é contabilizado.");
        var saldos = await SaldosContabeisAsync(id, ct);
        if (!saldos.ContratacaoContabilizada) return Resultado<decimal>.Falha(c.Consorcio ? "Registre a contemplação antes." : "Contabilize a contratação antes.");
        var alvo = RegrasContratos.Dividir(c, await ListarParcelasAsync(empresa, id, ct), dataBase).CurtoPrazo;
        var diferenca = Math.Min(alvo - saldos.CurtoPrazo, saldos.LongoPrazo);
        if (diferenca == 0) return Resultado<decimal>.Falha("Nada a reclassificar: o curto prazo já está certo nesta data.");
        var hist = $"Reclassificação longo → curto prazo em {dataBase:dd/MM/yyyy} – {Nome(c)}";
        List<PartidaProposta> partidas = diferenca > 0
            ? [new(c.ContaLongoPrazo!, IndicadorDC.D, diferenca, hist), new(c.ContaCurtoPrazo!, IndicadorDC.C, diferenca, hist)]
            : [new(c.ContaCurtoPrazo!, IndicadorDC.D, -diferenca, hist), new(c.ContaLongoPrazo!, IndicadorDC.C, -diferenca, hist)];
        var r = await LancarAsync(empresa, c, "RECLASSIFICACAO", dataBase, null, partidas, diferenca, -diferenca, 0, usuario, ct);
        return r.Sucesso ? Resultado<decimal>.Ok(diferenca) : Resultado<decimal>.Falha(r.Erros);
    }

    // ------------------------------------------------------------------ imobilizado

    /// <summary>
    /// Cadastra o bem do contrato no imobilizado (grupo escolhido, com a vida útil e as contas do grupo; aquisição na data do contrato ou da
    /// contemplação; valor do bem ou da carta; plaqueta = placa do rateio) e liga o contrato a ele.
    /// </summary>
    public async Task<Resultado<long>> GerarBemAsync(long empresa, long id, long grupoId, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FiscalDuplicatasManter, ct) is { } n) return Resultado<long>.Falha(n);
        if (await ObterContratoAsync(empresa, id, ct) is not { } c) return Resultado<long>.Falha("Contrato não encontrado.");
        if (c.BemId is not null) return Resultado<long>.Falha("O contrato já está ligado a um bem do imobilizado.");
        if (c.Consorcio && c.DataContemplacao is null) return Resultado<long>.Falha("O bem do consórcio entra no imobilizado na contemplação: registre-a antes.");
        if ((await app.Imobilizado.ListarGruposAsync(ct)).FirstOrDefault(g => g.Id == grupoId) is not { } grupo) return Resultado<long>.Falha("Escolha o grupo do bem.");
        var placas = (await ListarRateioAsync(empresa, id, ct)).Select(r => r.Placa).ToList();
        var aquisicao = c.DataContemplacao ?? c.DataContrato;
        var bem = new BemImobilizado(0, 0, c.Descricao, grupo.Id, placas.Count == 1 ? placas[0] : null, null, null, null, aquisicao, aquisicao, c.ValorBem, 0,
                                     grupo.VidaUtilFiscalMeses, MetodoDepreciacao.Linear, 1, 0, c.ContaBem ?? grupo.ContaBem ?? "", grupo.ContaAcumulada, grupo.ContaDespesa,
                                     null, null);
        var r = await app.Imobilizado.GravarBemAsync(empresa, bem, usuario, ct);
        if (!r.Sucesso) return r;
        await _db.ExecutarAsync("UPDATE frota.contrato SET bem_id = @b WHERE id = @c", ct, ("b", r.Valor), ("c", id));
        return r;
    }

    public async Task<List<GrupoBem>> GruposImobilizadoAsync(CancellationToken ct = default) => [.. await app.Imobilizado.ListarGruposAsync(ct)];

    /// <summary>Imobilizado › Bens financiados e consorciados: cada contrato com o bem, as placas, as parcelas pagas e o saldo devedor em CP/LP.</summary>
    public async Task<List<PosicaoContrato>> PosicaoContratosAsync(long empresa, DateOnly dataBase, long usuario, CancellationToken ct = default)
    {
        if (!await PodeVerContratosAsync(usuario, empresa, ct)) return [];
        var lista = new List<PosicaoContrato>();
        foreach (var c in await ContratosAsync(empresa, null, usuario, ct))
        {
            var parcelas = await ListarParcelasAsync(empresa, c.Id, ct);
            var prazos = RegrasContratos.Dividir(c, parcelas, dataBase);
            var saldo = parcelas.Where(p => !p.Paga).Sum(p => p.Amortizacao);
            lista.Add(new PosicaoContrato(c, parcelas.Count(p => p.Paga), saldo, prazos,
                                          string.Join(", ", (await ListarRateioAsync(empresa, c.Id, ct)).Select(r => r.Placa))));
        }
        return lista;
    }
}
