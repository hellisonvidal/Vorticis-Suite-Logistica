using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Vorticis.Domain.Cadastros;
using Vorticis.Domain.Comum;
using Vorticis.Fiscal.Sefaz;
using Vorticis.Infra.Postgres;
using P = Vorticis.Domain.Seguranca.Permissao;

namespace Vorticis.Frota;

/// <summary>
/// Configuração e transmissão direta de CT-e 4.00 (com IBS/CBS e contingência automática SVC-RS/SVC-SP)
/// e MDF-e 3.00 à SEFAZ, eventos de cancelamento (110111) e encerramento (110112), XML autorizado e histórico.
/// </summary>
public sealed partial class ServicoFrota
{
    public async Task<ConfiguracaoSefazTms> ConfiguracaoSefazAsync(long empresa, CancellationToken ct = default)
        => (await _db.ListarAsync("""
            SELECT ambiente, rntrc, contingencia_svc_automatica, seguradora_nome, seguradora_cnpj, apolice_seguro, averbacao_padrao
              FROM tms.configuracao_sefaz WHERE empresa_id = @e
            """, r => new ConfiguracaoSefazTms(r.GetInt16(0), r.Texto(1), r.GetBoolean(2), r.Texto(3), r.Texto(4), r.Texto(5), r.Texto(6)),
            ct, ("e", empresa))).FirstOrDefault() ?? new ConfiguracaoSefazTms();

    public async Task<Resultado> GravarConfiguracaoSefazAsync(long empresa, ConfiguracaoSefazTms c, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaCadastros, ct) is not null && await Negar(usuario, empresa, P.FrotaTms, ct) is { } n)
            return Resultado.Falha(n);
        if (c.Ambiente is not (1 or 2))
            return Resultado.Falha("Ambiente da SEFAZ inválido (use 1 = Produção ou 2 = Homologação).");

        var rntrc = N(RegrasTms.Digitos(c.Rntrc));
        if (rntrc is not null && rntrc.Length is not (8 or 9))
            return Resultado.Falha("RNTRC inválido: informe 8 ou 9 dígitos (ou deixe em branco).");

        var segCnpj = N(RegrasTms.Digitos(c.SeguradoraCnpj));
        if (segCnpj is not null && (segCnpj.Length != 14 || !Cnpj.Valido(segCnpj)))
            return Resultado.Falha("CNPJ da seguradora inválido.");

        var segNome = N(c.SeguradoraNome);
        if (segNome is { Length: > 60 })
            return Resultado.Falha("Nome da seguradora: máximo de 60 caracteres.");

        var apolice = N(c.ApoliceSeguro);
        if (apolice is { Length: > 40 })
            return Resultado.Falha("Número da apólice: máximo de 40 caracteres.");

        var averbacao = N(c.AverbacaoPadrao);
        if (averbacao is { Length: > 40 })
            return Resultado.Falha("Averbação padrão: máximo de 40 caracteres.");

        return await Tentar(() => _db.ExecutarAsync("""
            INSERT INTO tms.configuracao_sefaz (empresa_id, ambiente, rntrc, contingencia_svc_automatica, seguradora_nome, seguradora_cnpj, apolice_seguro, averbacao_padrao)
            VALUES (@e, @amb, @rntrc, @svc, @sn, @sc, @ap, @av)
            ON CONFLICT (empresa_id) DO UPDATE SET
                ambiente = EXCLUDED.ambiente, rntrc = EXCLUDED.rntrc, contingencia_svc_automatica = EXCLUDED.contingencia_svc_automatica,
                seguradora_nome = EXCLUDED.seguradora_nome, seguradora_cnpj = EXCLUDED.seguradora_cnpj,
                apolice_seguro = EXCLUDED.apolice_seguro, averbacao_padrao = EXCLUDED.averbacao_padrao
            """, ct, ("e", empresa), ("amb", (short)c.Ambiente), ("rntrc", rntrc), ("svc", c.ContingenciaSvcAutomatica),
            ("sn", segNome), ("sc", segCnpj), ("ap", apolice), ("av", averbacao)));
    }

    public async Task<ParametrosIbsCbs?> ParametrosIbsCbsAsync(DateOnly data, CancellationToken ct = default)
    {
        var json = (await _db.ListarAsync("""
            SELECT valor::text FROM core.parametro_legal
             WHERE chave = 'IBS_CBS_TRANSPORTE' AND vigencia_inicio <= @d AND (vigencia_fim IS NULL OR vigencia_fim >= @d)
             ORDER BY vigencia_inicio DESC LIMIT 1
            """, r => r.GetString(0), ct, ("d", data))).FirstOrDefault();
        if (json is null) return null;

        using var doc = JsonDocument.Parse(json);
        var e = doc.RootElement;
        static string S(JsonElement el, string prop, string padrao)
            => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? padrao) : padrao;
        static decimal D(JsonElement el, string prop)
            => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0m;
        var incluir = !e.TryGetProperty("incluirNoXml", out var inc) || inc.ValueKind != JsonValueKind.False;
        return new ParametrosIbsCbs(S(e, "cst", "000"), S(e, "cClassTrib", "000001"), D(e, "ibsUfPct"), D(e, "ibsMunPct"), D(e, "cbsPct"), incluir);
    }

    private async Task<(ParteFiscal? Emitente, int Crt)> CarregarEmitenteFiscalAsync(long empresa, CancellationToken ct)
        => (await _db.ListarAsync<(ParteFiscal? Emitente, int Crt)>("""
            SELECT e.cnpj, e.razao_social, e.nome_fantasia, e.inscricao_estadual, e.uf, e.cod_municipio_ibge, e.municipio,
                   e.cep, e.logradouro, e.numero, e.complemento, e.bairro, e.telefone, e.email,
                   (SELECT r.regime::text FROM core.empresa_regime r WHERE r.empresa_id = e.id ORDER BY r.vigencia_inicio DESC LIMIT 1)
              FROM core.empresa e WHERE e.id = @e
            """, r =>
        {
            var pf = new ParteFiscal(r.GetString(0), r.GetString(1), r.Texto(2), r.Texto(3), r.GetString(4), r.GetString(5), r.Texto(6) ?? "",
                                     r.Texto(7), r.Texto(8), r.Texto(9), r.Texto(10), r.Texto(11), r.Texto(12), r.Texto(13));
            var regime = r.Texto(14) ?? "";
            var crt = regime.Contains("SIMPLES", StringComparison.OrdinalIgnoreCase) ? 1 : 3;
            return (pf, crt);
        }, ct, ("e", empresa))).FirstOrDefault();

    private async Task<ParteFiscal?> CarregarParticipanteFiscalAsync(long empresa, long id, CancellationToken ct)
        => (await _db.ListarAsync("""
            SELECT cpf_cnpj, nome, nome_fantasia, inscricao_estadual, uf, cod_municipio_ibge, municipio,
                   cep, logradouro, numero, complemento, bairro, telefone, email
              FROM core.participante WHERE id = @id AND empresa_id = @e
            """, r => new ParteFiscal(r.Texto(0) ?? "", r.GetString(1), r.Texto(2), r.Texto(3), r.Texto(4) ?? "", r.Texto(5) ?? "", r.Texto(6) ?? "",
                                      r.Texto(7), r.Texto(8), r.Texto(9), r.Texto(10), r.Texto(11), r.Texto(12), r.Texto(13)),
            ct, ("id", id), ("e", empresa))).FirstOrDefault();

    private Task RegistrarTransmissaoAsync(
        long empresa, string modelo, long docId, string acao, int ambiente, int tipoEmissao, bool svc,
        string? chave, int? cstat, string? motivo, string? protocolo, string xmlEnvio, string? xmlRetorno, long usuario, CancellationToken ct)
    {
        var mot = motivo is { Length: > 500 } ? motivo[..500] : motivo;
        return _db.ExecutarAsync("""
            INSERT INTO tms.transmissao_sefaz (empresa_id, modelo, documento_id, acao, ambiente, tipo_emissao, contingencia_svc, chave, cstat, motivo, protocolo, xml_envio, xml_retorno, enviado_por)
            VALUES (@e, @mod, @doc, @ac, @amb, @tp, @svc, @ch, @cs, @mot, @pr, @xe, @xr, @u)
            """, ct,
            ("e", empresa), ("mod", modelo), ("doc", docId), ("ac", acao), ("amb", (short)ambiente), ("tp", (short)tipoEmissao),
            ("svc", svc), ("ch", N(chave)), ("cs", cstat), ("mot", N(mot)), ("pr", N(protocolo)), ("xe", xmlEnvio), ("xr", N(xmlRetorno)), ("u", usuario));
    }

    private static DateTimeOffset DataHoraEmissao(DateOnly emissao)
        => emissao == DateOnly.FromDateTime(DateTime.Today)
            ? DateTimeOffset.Now
            : new DateTimeOffset(emissao.ToDateTime(new TimeOnly(12, 0)), TimeZoneInfo.Local.GetUtcOffset(DateTime.Now));

    /// <summary>
    /// Gera, assina (ICP-Brasil) e transmite o CT-e 4.00 à SEFAZ, incluindo IBS/CBS (core.parametro_legal)
    /// e acionando contingência automática SVC-RS/SVC-SP quando a SEFAZ normal estiver indisponível (erro de rede ou cStat 108/109).
    /// </summary>
    public async Task<Resultado<ResultadoTransmissaoCte>> TransmitirCteAsync(
        long empresa, long cteId, X509Certificate2 certificado, long usuario, IClienteSefazTms? cliente = null, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaTms, ct) is { } n) return Resultado<ResultadoTransmissaoCte>.Falha(n);
        var c = await CteAsync(empresa, cteId, ct);
        if (c is null) return Resultado<ResultadoTransmissaoCte>.Falha("CT-e não encontrado.");
        if (c.Situacao != "DIGITADO") return Resultado<ResultadoTransmissaoCte>.Falha("Só o CT-e digitado pode ser transmitido à SEFAZ.");

        var cfg = await ConfiguracaoSefazAsync(empresa, ct);
        if (string.IsNullOrWhiteSpace(cfg.Rntrc))
            return Resultado<ResultadoTransmissaoCte>.Falha("Informe o RNTRC da transportadora em Cadastros › Parâmetros (Frota › SEFAZ) antes de transmitir.");

        var (emitente, crt) = await CarregarEmitenteFiscalAsync(empresa, ct);
        if (emitente is null) return Resultado<ResultadoTransmissaoCte>.Falha("Empresa emitente não encontrada.");
        if (!Cnpj.Valido(emitente.Documento) || !Uf.Valida(emitente.Uf) || RegrasTms.Digitos(emitente.MunicipioIbge).Length != 7)
            return Resultado<ResultadoTransmissaoCte>.Falha("Complete CNPJ, UF e código IBGE do município da empresa antes de transmitir.");

        var rem = await CarregarParticipanteFiscalAsync(empresa, c.RemetenteId, ct);
        var dest = await CarregarParticipanteFiscalAsync(empresa, c.DestinatarioId, ct);
        var tom = c.TomadorId == c.RemetenteId ? rem : c.TomadorId == c.DestinatarioId ? dest : await CarregarParticipanteFiscalAsync(empresa, c.TomadorId, ct);
        if (rem is null || dest is null || tom is null) return Resultado<ResultadoTransmissaoCte>.Falha("Remetente, destinatário ou tomador não encontrados.");
        if (RegrasTms.Digitos(rem.Documento).Length is not (11 or 14) || RegrasTms.Digitos(dest.Documento).Length is not (11 or 14))
            return Resultado<ResultadoTransmissaoCte>.Falha("Informe o CPF/CNPJ do remetente e do destinatário antes de transmitir o CT-e.");

        var tipoTomador = c.TomadorId == c.RemetenteId ? 0 : c.TomadorId == c.DestinatarioId ? 3 : 4;
        if (tipoTomador == 4 && RegrasTms.Digitos(tom.Documento).Length is not (11 or 14))
            return Resultado<ResultadoTransmissaoCte>.Falha("Informe o CPF/CNPJ do tomador antes de transmitir o CT-e.");

        var cfop = !string.IsNullOrWhiteSpace(c.Cfop)
            ? c.Cfop
            : string.Equals(c.Origem.Uf, c.Destino.Uf, StringComparison.OrdinalIgnoreCase) ? "5353" : "6353";
        var natOp = string.Equals(c.Origem.Uf, c.Destino.Uf, StringComparison.OrdinalIgnoreCase)
            ? "PRESTACAO DE SERVICO DE TRANSPORTE ESTADUAL"
            : "PRESTACAO DE SERVICO DE TRANSPORTE INTERESTADUAL";

        var ibsCfg = await ParametrosIbsCbsAsync(c.Emissao, ct);
        TributosIbsCbs? ibsCbs = ibsCfg is { IncluirNoXml: true } pIbs ? GeradorXmlSefazTms.CalcularIbsCbs(c.Frete.Total, pIbs) : null;
        var dhEmi = DataHoraEmissao(c.Emissao);
        var chavesNfe = RegrasTms.ChavesNfe(c.ChavesNfe);
        var notasAnexadas = chavesNfe.Count == 0 ? await NotasCteAsync(empresa, cteId, usuario, ct) : [];
        if (chavesNfe.Count == 0 && notasAnexadas.Count > 0) chavesNfe = [.. notasAnexadas.Select(nt => nt.Chave)];

        (string Chave, string XmlAssinado) GerarEAssinar(int tpEmis)
        {
            var dados = new DadosXmlCte(cfg.Ambiente, tpEmis, dhEmi, GeradorXmlSefazTms.GerarCodigoNumerico(), c.Serie, c.Numero, cfop, natOp,
                                        tipoTomador, c.Origem, c.Destino, emitente, rem, dest, tipoTomador == 4 ? tom : null,
                                        c.Produto ?? "CARGA GERAL", c.PesoKg, c.VolumeM3, c.Volumes, c.ValorMercadoria, c.Frete, chavesNfe,
                                        cfg.Rntrc, ibsCbs, c.Observacao, crt);
            var (ch, xml) = GeradorXmlSefazTms.GerarXmlCte(dados);
            return (ch, GeradorXmlSefazTms.AssinarXml(xml, "infCte", certificado));
        }

        using var ownClient = cliente is null ? ClienteSefazTms.Criar(certificado) : null;
        var cli = cliente ?? ownClient!;

        var tipoEmissao = EndpointsSefazTms.EmissaoNormal;
        var usouSvc = false;
        var (chave, xmlAssinado) = GerarEAssinar(tipoEmissao);
        RespostaAutorizacaoSefaz? resp = null;

        try
        {
            resp = await cli.AutorizarCteAsync(emitente.Uf, cfg.Ambiente, tipoEmissao, xmlAssinado, ct);
            if (!resp.Autorizado && resp.CStat is 108 or 109 && cfg.ContingenciaSvcAutomatica)
            {
                await RegistrarTransmissaoAsync(empresa, RegrasTms.ModeloCte, cteId, "AUTORIZACAO", cfg.Ambiente, tipoEmissao, false,
                                                 chave, resp.CStat, resp.Motivo, resp.Protocolo, xmlAssinado, resp.XmlRetorno, usuario, ct);
                tipoEmissao = EndpointsSefazTms.TipoContingenciaSvc(emitente.Uf);
                usouSvc = true;
                (chave, xmlAssinado) = GerarEAssinar(tipoEmissao);
                resp = await cli.AutorizarCteAsync(emitente.Uf, cfg.Ambiente, tipoEmissao, xmlAssinado, ct);
            }
        }
        catch (ErroComunicacaoSefaz ex)
        {
            await RegistrarTransmissaoAsync(empresa, RegrasTms.ModeloCte, cteId, "AUTORIZACAO", cfg.Ambiente, tipoEmissao, false,
                                             chave, null, ex.Message, null, xmlAssinado, null, usuario, ct);
            if (!cfg.ContingenciaSvcAutomatica)
                return Resultado<ResultadoTransmissaoCte>.Falha(ex.Message);

            tipoEmissao = EndpointsSefazTms.TipoContingenciaSvc(emitente.Uf);
            usouSvc = true;
            (chave, xmlAssinado) = GerarEAssinar(tipoEmissao);
            try
            {
                resp = await cli.AutorizarCteAsync(emitente.Uf, cfg.Ambiente, tipoEmissao, xmlAssinado, ct);
            }
            catch (ErroComunicacaoSefaz exSvc)
            {
                await RegistrarTransmissaoAsync(empresa, RegrasTms.ModeloCte, cteId, "AUTORIZACAO", cfg.Ambiente, tipoEmissao, true,
                                                 chave, null, exSvc.Message, null, xmlAssinado, null, usuario, ct);
                return Resultado<ResultadoTransmissaoCte>.Falha($"Falha na transmissão normal e em contingência {EndpointsSefazTms.NomeAutorizador(emitente.Uf, tipoEmissao)}: {exSvc.Message}");
            }
        }

        await RegistrarTransmissaoAsync(empresa, RegrasTms.ModeloCte, cteId, "AUTORIZACAO", cfg.Ambiente, tipoEmissao, usouSvc,
                                         resp.Chave ?? chave, resp.CStat, resp.Motivo, resp.Protocolo, xmlAssinado, resp.XmlRetorno, usuario, ct);

        if (!resp.Autorizado)
            return Resultado<ResultadoTransmissaoCte>.Falha($"SEFAZ rejeitou o CT-e (cStat {resp.CStat}): {resp.Motivo}");

        var chaveFinal = resp.Chave ?? chave;
        var protocoloFinal = resp.Protocolo ?? "";
        var xmlAutorizado = resp.ProtXml is not null ? GeradorXmlSefazTms.MontarCteProc(xmlAssinado, resp.ProtXml) : null;
        var autorizadoEm = resp.RecebidoEm ?? DateTimeOffset.UtcNow;

        return await Tentar(async () =>
        {
            var linhas = await _db.ExecutarAsync("""
                UPDATE tms.cte
                   SET chave = @ch, protocolo = @pr, situacao = 'EMITIDO', ambiente = @amb, tipo_emissao = @tp,
                       ibs_uf_pct = @iufp, ibs_mun_pct = @imunp, ibs = @ibs, cbs_pct = @cbsp, cbs = @cbs,
                       cst_ibs_cbs = @cst, class_trib_ibs_cbs = @cls,
                       xml_assinado = @xa, xml_autorizado = @xaut, autorizado_em = @aem
                 WHERE id = @id AND empresa_id = @e AND situacao = 'DIGITADO'
                """, ct,
                ("ch", chaveFinal), ("pr", N(protocoloFinal)), ("amb", (short)cfg.Ambiente), ("tp", (short)tipoEmissao),
                ("iufp", ibsCbs?.IbsUfPct ?? 0m), ("imunp", ibsCbs?.IbsMunPct ?? 0m), ("ibs", ibsCbs?.IbsTotal ?? 0m),
                ("cbsp", ibsCbs?.CbsPct ?? 0m), ("cbs", ibsCbs?.Cbs ?? 0m), ("cst", ibsCbs?.Cst), ("cls", ibsCbs?.ClassTrib),
                ("xa", xmlAssinado), ("xaut", xmlAutorizado), ("aem", autorizadoEm), ("id", cteId), ("e", empresa));
            if (linhas == 0) throw new ViolacaoRegraException("O CT-e foi alterado por outro usuário antes de concluir a gravação.");
            return new ResultadoTransmissaoCte(chaveFinal, protocoloFinal, tipoEmissao, usouSvc, ibsCbs?.IbsTotal ?? 0m, ibsCbs?.Cbs ?? 0m, resp.Motivo);
        });
    }

    /// <summary>Transmite o evento de cancelamento (110111) do CT-e autorizado na SEFAZ.</summary>
    public async Task<Resultado<string>> CancelarCteSefazAsync(
        long empresa, long cteId, string justificativa, X509Certificate2 certificado, long usuario, IClienteSefazTms? cliente = null, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaTms, ct) is { } n) return Resultado<string>.Falha(n);
        var just = (justificativa ?? "").Trim();
        if (just.Length is < 15 or > 255) return Resultado<string>.Falha("A justificativa do cancelamento na SEFAZ deve ter entre 15 e 255 caracteres.");

        var c = await CteAsync(empresa, cteId, ct);
        if (c is null) return Resultado<string>.Falha("CT-e não encontrado.");
        if (c.Situacao != "EMITIDO") return Resultado<string>.Falha($"O CT-e está {RegrasTms.Rotulo(c.Situacao).ToLowerInvariant()} e não pode ser cancelado na SEFAZ.");
        if (c.FaturaId is not null) return Resultado<string>.Falha($"O CT-e está na fatura nº {c.FaturaNumero}: cancele a fatura antes.");
        if (await _db.EscalarAsync<int>("""
                SELECT count(*)::int FROM tms.manifesto_cte mc JOIN tms.manifesto m ON m.id = mc.manifesto_id WHERE mc.cte_id = @id AND m.situacao = 'ABERTO'
                """, ct, ("id", cteId)) > 0)
            return Resultado<string>.Falha("O CT-e está num manifesto aberto: retire-o do manifesto antes.");
        if (string.IsNullOrWhiteSpace(c.Chave) || string.IsNullOrWhiteSpace(c.Protocolo))
            return Resultado<string>.Falha("O CT-e não possui chave e protocolo de autorização.");

        var (emitente, _) = await CarregarEmitenteFiscalAsync(empresa, ct);
        if (emitente is null) return Resultado<string>.Falha("Empresa emitente não encontrada.");
        var cfg = await ConfiguracaoSefazAsync(empresa, ct);
        var amb = await _db.EscalarAsync<short?>("SELECT ambiente FROM tms.cte WHERE id = @id AND empresa_id = @e", ct, ("id", cteId), ("e", empresa))
                  ?? (short)cfg.Ambiente;

        var evt = GeradorXmlSefazTms.GerarEventoCancelamentoCte(emitente.Uf, amb, emitente.Documento, c.Chave, c.Protocolo, just, DateTimeOffset.Now);
        var evtAssinado = GeradorXmlSefazTms.AssinarXml(evt, "infEvento", certificado);

        using var ownClient = cliente is null ? ClienteSefazTms.Criar(certificado) : null;
        var cli = cliente ?? ownClient!;
        RespostaEventoSefaz resp;
        try
        {
            resp = await cli.EnviarEventoCteAsync(emitente.Uf, amb, c.TipoEmissao, evtAssinado, ct);
        }
        catch (ErroComunicacaoSefaz ex)
        {
            await RegistrarTransmissaoAsync(empresa, RegrasTms.ModeloCte, cteId, "CANCELAMENTO", amb, c.TipoEmissao, c.TipoEmissao != 1,
                                             c.Chave, null, ex.Message, null, evtAssinado, null, usuario, ct);
            return Resultado<string>.Falha(ex.Message);
        }

        await RegistrarTransmissaoAsync(empresa, RegrasTms.ModeloCte, cteId, "CANCELAMENTO", amb, c.TipoEmissao, c.TipoEmissao != 1,
                                         c.Chave, resp.CStat, resp.Motivo, resp.Protocolo, evtAssinado, resp.XmlRetorno, usuario, ct);
        if (!resp.Registrado) return Resultado<string>.Falha($"SEFAZ rejeitou o cancelamento (cStat {resp.CStat}): {resp.Motivo}");

        return await Tentar(() => EmTransacaoAsync(async (con, tx) =>
        {
            await Exec(con, tx, "UPDATE tms.cte SET situacao = 'CANCELADO', protocolo_cancelamento = @pr WHERE id = @id AND empresa_id = @e",
                       ct, ("pr", N(resp.Protocolo)), ("id", cteId), ("e", empresa));
            await Exec(con, tx, "UPDATE tms.cotacao SET situacao = 'APROVADA' WHERE id = @c AND situacao = 'CONVERTIDA'", ct, ("c", c.CotacaoId));
            return resp.Protocolo ?? resp.Motivo;
        }, ct));
    }

    /// <summary>Gera, assina e transmite o MDF-e 3.00 à SEFAZ/SVRS vinculando os CT-e e os CIOT declarados.</summary>
    public async Task<Resultado<ResultadoTransmissaoMdfe>> TransmitirMdfeAsync(
        long empresa, long manifestoId, X509Certificate2 certificado, long usuario, IClienteSefazTms? cliente = null, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaTms, ct) is { } n) return Resultado<ResultadoTransmissaoMdfe>.Falha(n);
        var m = (await ListarManifestosAsync(empresa, DateOnly.MinValue, DateOnly.MaxValue, manifestoId, ct)).FirstOrDefault();
        if (m is null) return Resultado<ResultadoTransmissaoMdfe>.Falha("Manifesto não encontrado.");
        if (m.Situacao != "ABERTO") return Resultado<ResultadoTransmissaoMdfe>.Falha("Só o manifesto aberto pode ser transmitido à SEFAZ.");
        if (m.TemXmlAutorizado) return Resultado<ResultadoTransmissaoMdfe>.Falha("Este manifesto já foi autorizado na SEFAZ.");
        if (m.Ctes.Count == 0) return Resultado<ResultadoTransmissaoMdfe>.Falha("Inclua ao menos um CT-e no manifesto.");

        var ctes = await _db.ListarAsync(SelectCte + "\n WHERE c.empresa_id = @e AND c.id = ANY(@ids) ORDER BY c.serie, c.numero",
                                         LerCte, ct, ("e", empresa), ("ids", m.Ctes.ToArray()));
        var chavesCte = ctes.Select(c => RegrasTms.Digitos(c.Chave)).Where(ch => RegrasTms.ChaveValida(ch, RegrasTms.ModeloCte)).ToList();
        if (chavesCte.Count != ctes.Count)
            return Resultado<ResultadoTransmissaoMdfe>.Falha("Todos os CT-e do manifesto devem ter chave de acesso de 44 dígitos antes da transmissão do MDF-e.");

        var cfg = await ConfiguracaoSefazAsync(empresa, ct);
        if (string.IsNullOrWhiteSpace(cfg.Rntrc))
            return Resultado<ResultadoTransmissaoMdfe>.Falha("Informe o RNTRC da transportadora em Cadastros › Parâmetros (Frota › SEFAZ) antes de transmitir.");

        var (emitente, _) = await CarregarEmitenteFiscalAsync(empresa, ct);
        if (emitente is null) return Resultado<ResultadoTransmissaoMdfe>.Falha("Empresa emitente não encontrada.");
        if (!Cnpj.Valido(emitente.Documento) || !Uf.Valida(emitente.Uf) || RegrasTms.Digitos(emitente.MunicipioIbge).Length != 7)
            return Resultado<ResultadoTransmissaoMdfe>.Falha("Complete CNPJ, UF e código IBGE do município da empresa antes de transmitir.");

        var veic = (await _db.ListarAsync<(string? Placa, string? Renavam, string? Tipo, decimal CapKg)>(
            "SELECT placa, renavam, tipo, capacidade_kg FROM frota.veiculo WHERE id = @id AND empresa_id = @e",
            r => (r.GetString(0), r.Texto(1), r.GetString(2), r.Valor(3) ?? 0m),
            ct, ("id", m.VeiculoId), ("e", empresa))).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(veic.Placa)) return Resultado<ResultadoTransmissaoMdfe>.Falha("Veículo do manifesto não encontrado.");

        var mot = (await _db.ListarAsync<(string? Nome, string? Cpf)>(
            "SELECT nome, cpf FROM frota.motorista WHERE id = @id AND empresa_id = @e",
            r => (r.GetString(0), r.Texto(1)), ct, ("id", m.MotoristaId), ("e", empresa))).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(mot.Nome) || !Cpf.Valido(mot.Cpf))
            return Resultado<ResultadoTransmissaoMdfe>.Falha("Informe um motorista com CPF válido para transmitir o MDF-e.");

        var ciots = await _db.ListarAsync("""
            SELECT DISTINCT o.ciot, p.cpf_cnpj, o.rntrc, o.vale_pedagio
              FROM tms.ciot o
              JOIN tms.ciot_cte cc ON cc.ciot_id = o.id
              JOIN core.participante p ON p.id = o.contratado_id
             WHERE o.empresa_id = @e AND o.situacao = 'DECLARADO' AND o.ciot IS NOT NULL AND cc.cte_id = ANY(@c)
            """, r => new CiotParaMdfe(r.GetString(0), r.Texto(1) ?? "", r.GetString(2), r.GetDecimal(3)),
            ct, ("e", empresa), ("c", m.Ctes.ToArray()));

        var (tpRod, tpCar) = veic.Tipo switch
        {
            "CAVALO" => ("01", "00"),
            "TRUCK" or "TOCO" => ("02", "00"),
            "VUC" or "UTILITARIO" => ("04", "00"),
            "REBOQUE" or "SEMIRREBOQUE" => ("01", "02"),
            _ => ("01", "00"),
        };

        var ufsPercurso = (m.UfsPercurso ?? "").Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries)
                                               .Select(u => u.Trim().ToUpperInvariant()).ToList();
        var dados = new DadosXmlMdfe(cfg.Ambiente, DataHoraEmissao(m.Emissao), GeradorXmlSefazTms.GerarCodigoNumerico(), m.Serie, m.Numero,
                                     m.UfInicio, m.UfFim, ufsPercurso, ctes[0].Origem, ctes[^1].Destino, emitente, cfg.Rntrc,
                                     veic.Placa, veic.Renavam, veic.CapKg, 0, tpRod, tpCar, mot.Nome, mot.Cpf!, chavesCte,
                                     ctes.Sum(c => c.PesoKg), ctes.Sum(c => c.ValorMercadoria), ciots,
                                     cfg.SeguradoraNome, cfg.SeguradoraCnpj, cfg.ApoliceSeguro, cfg.AverbacaoPadrao, m.Observacao);
        var (chave, xml) = GeradorXmlSefazTms.GerarXmlMdfe(dados);
        var xmlAssinado = GeradorXmlSefazTms.AssinarXml(xml, "infMDFe", certificado);

        using var ownClient = cliente is null ? ClienteSefazTms.Criar(certificado) : null;
        var cli = cliente ?? ownClient!;
        RespostaAutorizacaoSefaz resp;
        try
        {
            resp = await cli.AutorizarMdfeAsync(cfg.Ambiente, xmlAssinado, ct);
        }
        catch (ErroComunicacaoSefaz ex)
        {
            await RegistrarTransmissaoAsync(empresa, RegrasTms.ModeloMdfe, manifestoId, "AUTORIZACAO", cfg.Ambiente, 1, false,
                                             chave, null, ex.Message, null, xmlAssinado, null, usuario, ct);
            return Resultado<ResultadoTransmissaoMdfe>.Falha(ex.Message);
        }

        await RegistrarTransmissaoAsync(empresa, RegrasTms.ModeloMdfe, manifestoId, "AUTORIZACAO", cfg.Ambiente, 1, false,
                                         resp.Chave ?? chave, resp.CStat, resp.Motivo, resp.Protocolo, xmlAssinado, resp.XmlRetorno, usuario, ct);
        if (!resp.Autorizado)
            return Resultado<ResultadoTransmissaoMdfe>.Falha($"SEFAZ rejeitou o MDF-e (cStat {resp.CStat}): {resp.Motivo}");

        var chaveFinal = resp.Chave ?? chave;
        var protocoloFinal = resp.Protocolo ?? "";
        var xmlAutorizado = resp.ProtXml is not null ? GeradorXmlSefazTms.MontarMdfeProc(xmlAssinado, resp.ProtXml) : null;
        var autorizadoEm = resp.RecebidoEm ?? DateTimeOffset.UtcNow;

        return await Tentar(async () =>
        {
            var linhas = await _db.ExecutarAsync("""
                UPDATE tms.manifesto
                   SET chave = @ch, protocolo = @pr, ambiente = @amb, averbacao = @av,
                       xml_assinado = @xa, xml_autorizado = @xaut, autorizado_em = @aem
                 WHERE id = @id AND empresa_id = @e AND situacao = 'ABERTO'
                """, ct,
                ("ch", chaveFinal), ("pr", N(protocoloFinal)), ("amb", (short)cfg.Ambiente), ("av", cfg.AverbacaoPadrao),
                ("xa", xmlAssinado), ("xaut", xmlAutorizado), ("aem", autorizadoEm), ("id", manifestoId), ("e", empresa));
            if (linhas == 0) throw new ViolacaoRegraException("O manifesto não está mais aberto para gravação.");
            return new ResultadoTransmissaoMdfe(chaveFinal, protocoloFinal, resp.Motivo);
        });
    }

    /// <summary>Transmite o evento de encerramento (110112) do MDF-e autorizado e marca o manifesto como ENCERRADO.</summary>
    public async Task<Resultado<string>> EncerrarMdfeSefazAsync(
        long empresa, long manifestoId, DateOnly dataEncerramento, X509Certificate2 certificado, long usuario, IClienteSefazTms? cliente = null, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaTms, ct) is { } n) return Resultado<string>.Falha(n);
        var m = (await ListarManifestosAsync(empresa, DateOnly.MinValue, DateOnly.MaxValue, manifestoId, ct)).FirstOrDefault();
        if (m is null) return Resultado<string>.Falha("Manifesto não encontrado.");
        if (m.Situacao != "ABERTO") return Resultado<string>.Falha("Só o manifesto aberto pode ser encerrado na SEFAZ.");
        if (dataEncerramento < m.Emissao) return Resultado<string>.Falha("A data de encerramento não pode ser anterior à emissão.");
        if (string.IsNullOrWhiteSpace(m.Chave) || string.IsNullOrWhiteSpace(m.Protocolo))
            return Resultado<string>.Falha("O manifesto não possui chave e protocolo de autorização na SEFAZ.");

        var (emitente, _) = await CarregarEmitenteFiscalAsync(empresa, ct);
        if (emitente is null) return Resultado<string>.Falha("Empresa emitente não encontrada.");
        var cfg = await ConfiguracaoSefazAsync(empresa, ct);
        var amb = await _db.EscalarAsync<short?>("SELECT ambiente FROM tms.manifesto WHERE id = @id AND empresa_id = @e", ct, ("id", manifestoId), ("e", empresa))
                  ?? (short)cfg.Ambiente;
        var destino = (await _db.ListarAsync<(string? Uf, string? Ibge)>("""
            SELECT c.destino_uf, c.destino_ibge FROM tms.cte c JOIN tms.manifesto_cte mc ON mc.cte_id = c.id
             WHERE mc.manifesto_id = @m AND c.empresa_id = @e ORDER BY c.numero DESC LIMIT 1
            """, r => (r.GetString(0), r.GetString(1)), ct, ("m", manifestoId), ("e", empresa))).FirstOrDefault();
        var ufEnc = string.IsNullOrWhiteSpace(destino.Uf) ? m.UfFim : destino.Uf;
        var ibgeEnc = string.IsNullOrWhiteSpace(destino.Ibge) ? emitente.MunicipioIbge : destino.Ibge;

        var evt = GeradorXmlSefazTms.GerarEventoEncerramentoMdfe(emitente.Uf, amb, emitente.Documento, m.Chave, m.Protocolo,
                                                                 dataEncerramento, ufEnc, ibgeEnc, DateTimeOffset.Now);
        var evtAssinado = GeradorXmlSefazTms.AssinarXml(evt, "infEvento", certificado);

        using var ownClient = cliente is null ? ClienteSefazTms.Criar(certificado) : null;
        var cli = cliente ?? ownClient!;
        RespostaEventoSefaz resp;
        try
        {
            resp = await cli.EnviarEventoMdfeAsync(amb, evtAssinado, ct);
        }
        catch (ErroComunicacaoSefaz ex)
        {
            await RegistrarTransmissaoAsync(empresa, RegrasTms.ModeloMdfe, manifestoId, "ENCERRAMENTO", amb, 1, false,
                                             m.Chave, null, ex.Message, null, evtAssinado, null, usuario, ct);
            return Resultado<string>.Falha(ex.Message);
        }

        await RegistrarTransmissaoAsync(empresa, RegrasTms.ModeloMdfe, manifestoId, "ENCERRAMENTO", amb, 1, false,
                                         m.Chave, resp.CStat, resp.Motivo, resp.Protocolo, evtAssinado, resp.XmlRetorno, usuario, ct);
        if (!resp.Registrado) return Resultado<string>.Falha($"SEFAZ rejeitou o encerramento (cStat {resp.CStat}): {resp.Motivo}");

        return await Tentar(async () =>
        {
            var linhas = await _db.ExecutarAsync("""
                UPDATE tms.manifesto SET situacao = 'ENCERRADO', encerrado_em = @d, protocolo_encerramento = @pr
                 WHERE id = @id AND empresa_id = @e AND situacao = 'ABERTO' AND emissao <= @d
                """, ct, ("d", dataEncerramento), ("pr", N(resp.Protocolo)), ("id", manifestoId), ("e", empresa));
            if (linhas == 0) throw new ViolacaoRegraException("Não foi possível encerrar o manifesto.");
            return resp.Protocolo ?? resp.Motivo;
        });
    }

    public async Task<Resultado<(string NomeArquivo, string Xml)>> ObterXmlAutorizadoAsync(
        long empresa, string modelo, long documentoId, long usuario, CancellationToken ct = default)
    {
        if (!await PodeVerAsync(usuario, empresa, ct))
            return Resultado<(string NomeArquivo, string Xml)>.Falha("Sem permissão para consultar a Frota nesta empresa.");

        var sql = modelo == RegrasTms.ModeloMdfe
            ? "SELECT chave, xml_autorizado FROM tms.manifesto WHERE id = @id AND empresa_id = @e"
            : "SELECT chave, xml_autorizado FROM tms.cte WHERE id = @id AND empresa_id = @e";
        var doc = (await _db.ListarAsync<(string? Chave, string? Xml)>(sql, r => (r.Texto(0), r.Texto(1)), ct, ("id", documentoId), ("e", empresa))).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(doc.Xml) || string.IsNullOrWhiteSpace(doc.Chave))
            return Resultado<(string NomeArquivo, string Xml)>.Falha("Este documento ainda não possui XML autorizado pela SEFAZ.");

        var sufixo = modelo == RegrasTms.ModeloMdfe ? "procMDFe" : "procCTe";
        return Resultado<(string NomeArquivo, string Xml)>.Ok(($"{doc.Chave}-{sufixo}.xml", doc.Xml));
    }

    public async Task<List<RegistroTransmissaoSefaz>> TransmissoesSefazAsync(
        long empresa, string modelo, long documentoId, long usuario, CancellationToken ct = default)
        => !await PodeVerAsync(usuario, empresa, ct)
            ? []
            : await _db.ListarAsync("""
                SELECT t.id, t.modelo, t.documento_id, t.acao, t.ambiente, t.tipo_emissao, t.contingencia_svc,
                       t.chave, t.cstat, t.motivo, t.protocolo, t.enviado_em, u.nome
                  FROM tms.transmissao_sefaz t
                  LEFT JOIN core.usuario u ON u.id = t.enviado_por
                 WHERE t.empresa_id = @e AND t.modelo = @mod AND t.documento_id = @doc
                 ORDER BY t.enviado_em DESC, t.id DESC
                """, r => new RegistroTransmissaoSefaz(
                    r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.GetString(3), r.GetInt16(4), r.GetInt16(5), r.GetBoolean(6),
                    r.Texto(7), r.IsDBNull(8) ? null : r.GetInt32(8), r.Texto(9), r.Texto(10),
                    r.GetFieldValue<DateTimeOffset>(11), r.Texto(12)),
                ct, ("e", empresa), ("mod", modelo), ("doc", documentoId));
}
