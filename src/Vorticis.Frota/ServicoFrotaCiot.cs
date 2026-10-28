using System.Text.Json;
using Vorticis.Application.Relatorios;
using Vorticis.Domain.Comum;
using Vorticis.Infra.Postgres;
using Vorticis.Infra.Relatorios;
using P = Vorticis.Domain.Seguranca.Permissao;

namespace Vorticis.Frota;

/// <summary>
/// CIOT (FROTA_TMS): operações de transporte com TAC ou ETC equiparada, retenções do TAC calculadas pelos parâmetros legais, CT-e ligados,
/// declaração (CIOT gerado pela IPEF), encerramento, cancelamento e o contrato/recibo de frete em PDF.
/// </summary>
public sealed partial class ServicoFrota
{
    private const string SelectCiot = """
        SELECT o.id, o.numero, o.ciot, o.codigo_verificador, o.ipef, o.tipo, o.contratado_id, o.rntrc, o.placa, o.motorista_nome, o.motorista_cpf, o.viagem_id,
               o.origem_uf, o.origem_ibge, o.origem_municipio, o.destino_uf, o.destino_ibge, o.destino_municipio, o.inicio, o.fim, o.produto, o.ncm, o.peso_kg,
               o.valor_frete, o.vale_pedagio, o.adiantamento, o.dependentes, o.inss, o.sest_senat, o.irrf, o.situacao, o.observacao,
               COALESCE((SELECT array_agg(x.cte_id ORDER BY x.cte_id) FROM tms.ciot_cte x WHERE x.ciot_id = o.id), '{}'), p.nome, p.cpf_cnpj
          FROM tms.ciot o JOIN core.participante p ON p.id = o.contratado_id
        """;

    private static OperacaoCiot LerCiot(Npgsql.NpgsqlDataReader r) =>
        new(r.GetInt64(0), r.GetInt32(1), r.Texto(2), r.Texto(3), r.Texto(4), r.GetString(5), r.GetInt64(6), r.GetString(7), r.GetString(8), r.Texto(9), r.Texto(10),
            r.Id(11), new ParadaRota(r.GetString(12), r.GetString(13), r.GetString(14)), new ParadaRota(r.GetString(15), r.GetString(16), r.GetString(17)),
            r.GetFieldValue<DateOnly>(18), r.GetFieldValue<DateOnly>(19), r.Texto(20), r.Texto(21), r.GetDecimal(22), r.GetDecimal(23), r.GetDecimal(24), r.GetDecimal(25),
            r.GetInt16(26), new RetencoesTac(r.GetDecimal(27), r.GetDecimal(28), r.GetDecimal(29)), r.GetString(30), r.Texto(31), r.GetFieldValue<long[]>(32),
            r.GetString(33), r.Texto(34));

    public async Task<List<OperacaoCiot>> CiotsAsync(long empresa, DateOnly inicio, DateOnly fim, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct)
            ? await _db.ListarAsync(SelectCiot + " WHERE o.empresa_id = @e AND o.inicio BETWEEN @i AND @f ORDER BY o.inicio DESC, o.numero DESC", LerCiot, ct,
                                    ("e", empresa), ("i", inicio), ("f", fim))
            : [];

    private async Task<OperacaoCiot?> ObterCiotAsync(long empresa, long id, CancellationToken ct)
        => (await _db.ListarAsync(SelectCiot + " WHERE o.empresa_id = @e AND o.id = @id", LerCiot, ct, ("e", empresa), ("id", id))).FirstOrDefault();

    /// <summary>Parâmetros das retenções do TAC vigentes na data (TAC_RETENCOES, teto do INSS e tabela do IRRF mensal).</summary>
    public async Task<ParametrosTac?> ParametrosTacAsync(DateOnly data, CancellationToken ct = default)
    {
        async Task<JsonElement?> Ler(string chave)
        {
            var json = (await _db.ListarAsync("""
                SELECT valor::text FROM core.parametro_legal WHERE chave = @k AND vigencia_inicio <= @d AND (vigencia_fim IS NULL OR vigencia_fim >= @d)
                 ORDER BY vigencia_inicio DESC LIMIT 1
                """, r => r.GetString(0), ct, ("k", chave), ("d", data))).FirstOrDefault();
            return json is null ? null : JsonDocument.Parse(json).RootElement.Clone();
        }
        if (await Ler("TAC_RETENCOES") is not { } tac || await Ler("FOLHA_INSS_EMPREGADO") is not { } inss || await Ler("FOLHA_IRRF_MENSAL") is not { } ir) return null;
        static decimal D(JsonElement e, string nome) => e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0;
        var teto = inss.GetProperty("faixas").EnumerateArray().Select(f => D(f, "ate")).Max();
        var faixas = ir.GetProperty("faixas").EnumerateArray()
                       .Select(f => new FaixaIr(f.GetProperty("ate").ValueKind == JsonValueKind.Null ? null : f.GetProperty("ate").GetDecimal(), D(f, "aliquota"), D(f, "deduzir")))
                       .ToList();
        ReducaoIr? reducao = ir.TryGetProperty("reducao", out var red) && red.ValueKind == JsonValueKind.Object
            ? new ReducaoIr(D(red, "limiteIsencao"), D(red, "reducaoMaxima"), D(red, "limiteSuperior"), D(red, "constante"), D(red, "coeficiente"))
            : null;
        return new ParametrosTac(D(tac, "baseInss"), D(tac, "aliquotaInss"), D(tac, "sestSenat"), D(tac, "baseIrrf"), teto, faixas, D(ir, "deducaoDependente"), reducao);
    }

    /// <summary>Retenções da operação: TAC (CPF) pelos parâmetros, somando os outros fretes do mesmo contratado no mês (não cancelados).</summary>
    public async Task<Resultado<RetencoesTac>> CalcularRetencoesAsync(long empresa, OperacaoCiot o, CancellationToken ct = default)
    {
        var documento = await _db.EscalarAsync<string>("SELECT cpf_cnpj FROM core.participante WHERE id = @p AND empresa_id = @e", ct, ("p", o.ContratadoId), ("e", empresa));
        if (!RegrasCiot.EhTac(documento)) return Resultado<RetencoesTac>.Ok(RetencoesTac.Nenhuma);
        if (await ParametrosTacAsync(o.Inicio, ct) is not { } p) return Resultado<RetencoesTac>.Falha("Parâmetros legais das retenções do TAC não cadastrados para a data.");
        var mes = new DateOnly(o.Inicio.Year, o.Inicio.Month, 1);
        var anteriores = await _db.EscalarAsync<decimal>("""
            SELECT COALESCE(sum(valor_frete), 0) FROM tms.ciot
             WHERE empresa_id = @e AND contratado_id = @p AND id <> @id AND situacao <> 'CANCELADO' AND inicio >= @m AND inicio < @m2
               AND (inicio < @i OR (inicio = @i AND (@id = 0 OR id < @id)))
            """, ct, ("e", empresa), ("p", o.ContratadoId), ("id", o.Id), ("m", mes), ("m2", mes.AddMonths(1)), ("i", o.Inicio));
        return Resultado<RetencoesTac>.Ok(RegrasCiot.Retencoes(documento, o.ValorFrete, anteriores, o.Dependentes, p));
    }

    /// <summary>Grava a operação (só digitada): calcula as retenções, numera e liga os CT-e (cada CT-e numa operação ativa só).</summary>
    public async Task<Resultado<OperacaoCiot>> GravarCiotAsync(long empresa, OperacaoCiot o, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaTms, ct) is { } n) return Resultado<OperacaoCiot>.Falha(n);
        if (o.Id != 0 && await ObterCiotAsync(empresa, o.Id, ct) is { } atual && atual.Situacao != "DIGITADO")
            return Resultado<OperacaoCiot>.Falha("Só a operação digitada pode ser alterada (a declarada já foi registrada na IPEF).");
        static string? Dig(string? s) => new string([.. (s ?? "").Where(char.IsAsciiDigit)]) is { Length: > 0 } d ? d : null;
        o = o with
        {
            Rntrc = Dig(o.Rntrc) ?? "", Placa = RegrasFrota.NormalizarPlaca(o.Placa), MotoristaCpf = Dig(o.MotoristaCpf), MotoristaNome = N(o.MotoristaNome),
            Ncm = Dig(o.Ncm), Produto = N(o.Produto), Ipef = N(o.Ipef), Observacao = N(o.Observacao), Ciot = Dig(o.Ciot), CodigoVerificador = N(o.CodigoVerificador),
            Situacao = "DIGITADO",
        };
        var ret = await CalcularRetencoesAsync(empresa, o, ct);
        if (!ret.Sucesso) return Resultado<OperacaoCiot>.Falha(ret.Erros);
        o = o with { Retencoes = ret.Valor };
        if (RegrasCiot.Validar(o) is { Count: > 0 } erros) return Resultado<OperacaoCiot>.Falha(erros);
        var ocupados = await _db.ListarAsync("""
            SELECT c.serie || '-' || c.numero, o.numero FROM tms.ciot_cte x JOIN tms.ciot o ON o.id = x.ciot_id JOIN tms.cte c ON c.id = x.cte_id
             WHERE o.empresa_id = @e AND o.id <> @id AND o.situacao <> 'CANCELADO' AND x.cte_id = ANY(@c)
            """, r => $"O CT-e {r.GetString(0)} já está na operação nº {r.GetInt32(1)}.", ct, ("e", empresa), ("id", o.Id), ("c", o.Ctes.ToArray()));
        if (ocupados.Count > 0) return Resultado<OperacaoCiot>.Falha(ocupados);
        (string, object?)[] p =
        [
            ("e", empresa), ("id", o.Id), ("ci", o.Ciot), ("cv", o.CodigoVerificador), ("ip", o.Ipef), ("t", o.Tipo), ("ct", o.ContratadoId), ("rn", o.Rntrc),
            ("pl", o.Placa), ("mn", o.MotoristaNome), ("mc", o.MotoristaCpf), ("vi", o.ViagemId), ("ou", o.Origem.Uf), ("oi", o.Origem.MunicipioIbge),
            ("om", o.Origem.Municipio), ("du", o.Destino.Uf), ("di", o.Destino.MunicipioIbge), ("dm", o.Destino.Municipio), ("ini", o.Inicio), ("fim", o.Fim),
            ("pr", o.Produto), ("ncm", o.Ncm), ("pk", o.PesoKg), ("vf", o.ValorFrete), ("vp", o.ValePedagio), ("ad", o.Adiantamento), ("dep", (short)o.Dependentes),
            ("inss", o.Retencoes.Inss), ("sest", o.Retencoes.SestSenat), ("irrf", o.Retencoes.Irrf), ("obs", o.Observacao),
        ];
        var r = await Tentar(async () => o.Id == 0
            ? await _db.EscalarAsync<long>("""
                INSERT INTO tms.ciot (empresa_id, numero, ciot, codigo_verificador, ipef, tipo, contratado_id, rntrc, placa, motorista_nome, motorista_cpf, viagem_id,
                       origem_uf, origem_ibge, origem_municipio, destino_uf, destino_ibge, destino_municipio, inicio, fim, produto, ncm, peso_kg, valor_frete,
                       vale_pedagio, adiantamento, dependentes, inss, sest_senat, irrf, observacao)
                VALUES (@e, (SELECT COALESCE(max(numero), 0) + 1 FROM tms.ciot WHERE empresa_id = @e), @ci, @cv, @ip, @t, @ct, @rn, @pl, @mn, @mc, @vi, @ou, @oi,
                        @om, @du, @di, @dm, @ini, @fim, @pr, @ncm, @pk, @vf, @vp, @ad, @dep, @inss, @sest, @irrf, @obs) RETURNING id
                """, ct, p)
            : await _db.EscalarAsync<long?>("""
                UPDATE tms.ciot SET ciot = @ci, codigo_verificador = @cv, ipef = @ip, tipo = @t, contratado_id = @ct, rntrc = @rn, placa = @pl, motorista_nome = @mn,
                       motorista_cpf = @mc, viagem_id = @vi, origem_uf = @ou, origem_ibge = @oi, origem_municipio = @om, destino_uf = @du, destino_ibge = @di,
                       destino_municipio = @dm, inicio = @ini, fim = @fim, produto = @pr, ncm = @ncm, peso_kg = @pk, valor_frete = @vf, vale_pedagio = @vp,
                       adiantamento = @ad, dependentes = @dep, inss = @inss, sest_senat = @sest, irrf = @irrf, observacao = @obs
                 WHERE id = @id AND empresa_id = @e RETURNING id
                """, ct, p) ?? throw new ViolacaoRegraException("Operação não encontrada."));
        if (!r.Sucesso) return Resultado<OperacaoCiot>.Falha(r.Erros);
        await _db.ExecutarAsync("DELETE FROM tms.ciot_cte WHERE ciot_id = @o", ct, ("o", r.Valor));
        foreach (var cte in o.Ctes.Distinct())
            await _db.ExecutarAsync("INSERT INTO tms.ciot_cte (ciot_id, cte_id) SELECT @o, id FROM tms.cte WHERE id = @c AND empresa_id = @e AND situacao <> 'CANCELADO'", ct,
                                    ("o", r.Valor), ("c", cte), ("e", empresa));
        return Resultado<OperacaoCiot>.Ok((await ObterCiotAsync(empresa, r.Valor, ct))!);
    }

    /// <summary>Registra o CIOT gerado pela IPEF (12 dígitos e o código verificador): a operação passa a declarada e não muda mais.</summary>
    public async Task<Resultado> DeclararCiotAsync(long empresa, long id, string ciot, string? verificador, string? ipef, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaTms, ct) is { } n) return Resultado.Falha(n);
        if (await ObterCiotAsync(empresa, id, ct) is not { } o) return Resultado.Falha("Operação não encontrada.");
        if (o.Situacao != "DIGITADO") return Resultado.Falha("Só a operação digitada pode ser declarada.");
        var codigo = new string([.. (ciot ?? "").Where(char.IsAsciiDigit)]);
        if (codigo.Length != 12) return Resultado.Falha("O CIOT tem 12 dígitos.");
        return await Tentar(() => _db.ExecutarAsync("""
            UPDATE tms.ciot SET ciot = @c, codigo_verificador = @v, ipef = COALESCE(@i, ipef), situacao = 'DECLARADO' WHERE id = @id AND empresa_id = @e
            """, ct, ("c", codigo), ("v", N(verificador)), ("i", N(ipef)), ("id", id), ("e", empresa)));
    }

    /// <summary>Encerra (viagem concluída e encerrada na IPEF) ou cancela a operação.</summary>
    public async Task<Resultado> MudarSituacaoCiotAsync(long empresa, long id, string situacao, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaTms, ct) is { } n) return Resultado.Falha(n);
        if (await ObterCiotAsync(empresa, id, ct) is not { } o) return Resultado.Falha("Operação não encontrada.");
        var permitido = (o.Situacao, situacao) is ("DECLARADO", "ENCERRADO") or ("DIGITADO" or "DECLARADO", "CANCELADO");
        if (!permitido) return Resultado.Falha($"Uma operação {RegrasCiot.Rotulo(o.Situacao).ToLowerInvariant()} não pode passar a {RegrasCiot.Rotulo(situacao).ToLowerInvariant()}.");
        await _db.ExecutarAsync("UPDATE tms.ciot SET situacao = @s WHERE id = @id AND empresa_id = @e", ct, ("s", situacao), ("id", id), ("e", empresa));
        return Resultado.Ok();
    }

    public async Task<Resultado> ExcluirCiotAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaTms, ct) is { } n) return Resultado.Falha(n);
        if (await ObterCiotAsync(empresa, id, ct) is { Situacao: not "DIGITADO" }) return Resultado.Falha("Só a operação digitada pode ser excluída; a declarada deve ser cancelada.");
        return await Tentar(() => _db.ExecutarAsync("DELETE FROM tms.ciot WHERE id = @id AND empresa_id = @e", ct, ("id", id), ("e", empresa)));
    }

    /// <summary>Contrato de frete / recibo do contratado em PDF: partes, trecho, carga, valores, retenções, adiantamento e saldo.</summary>
    public static byte[] ContratoFretePdf(OperacaoCiot o, string empresaDescricao, string emitidoPor)
    {
        var linhas = new List<LinhaRelatorio>();
        void L(string campo, string? valor) => linhas.Add(new LinhaRelatorio([campo, string.IsNullOrWhiteSpace(valor) ? "-" : valor]));
        L("Operação nº / tipo", $"{o.Numero} / {RegrasCiot.Rotulo(o.Tipo)}");
        L("CIOT / código verificador", o.Ciot is null ? "a declarar na IPEF" : $"{o.Ciot} / {o.CodigoVerificador ?? "-"}");
        L("IPEF", o.Ipef);
        L("Contratado", $"{o.Contratado} ({o.ContratadoDocumento ?? "-"})");
        L("RNTRC / placa", $"{o.Rntrc} / {RegrasFrota.FormatarPlaca(o.Placa)}");
        L("Motorista", o.MotoristaNome is null ? null : $"{o.MotoristaNome} ({o.MotoristaCpf ?? "-"})");
        L("Trecho", o.Trecho);
        L("Período", $"{o.Inicio:dd/MM/yyyy} a {o.Fim:dd/MM/yyyy}");
        L("Carga", $"{o.Produto ?? "-"} · NCM {o.Ncm ?? "-"} · {o.PesoKg.ToString("N3", Formato.PtBr)} kg");
        L("Valor do frete", Formato.Moeda(o.ValorFrete));
        L("INSS / SEST-SENAT / IRRF", $"{Formato.Moeda(o.Retencoes.Inss)} / {Formato.Moeda(o.Retencoes.SestSenat)} / {Formato.Moeda(o.Retencoes.Irrf)}");
        L("Valor líquido", Formato.Moeda(o.ValorLiquido));
        L("Adiantamento", Formato.Moeda(o.Adiantamento));
        L("Saldo a pagar", Formato.Moeda(o.Saldo));
        L("Vale-pedágio (fora do frete)", Formato.Moeda(o.ValePedagio));
        L("Observação", o.Observacao);
        var tabela = new TabelaRelatorio($"Contrato de frete nº {o.Numero}", [new("Campo", LarguraCm: 5), new("Informação")], linhas,
                                         "Contratante: ______________________________      Contratado: ______________________________      Data: ____/____/______");
        var doc = ExportacaoRelatorio.ParaPdf($"Contrato de frete nº {o.Numero}", [empresaDescricao, $"Emitido em {DateTime.Now:dd/MM/yyyy HH:mm} por {emitidoPor}"],
                                              [tabela], false);
        return new GeradorPdf().Gerar(doc).Conteudo;
    }
}
