using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Xml;
using Vorticis.Application.Cadastros;
using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Folha.Esocial;

namespace Vorticis.Folha.Servicos;

public enum PapelTransmissor { Empregador, Procurador, Contador }

public sealed record ResumoEnvioEsocial(int Assinados, int Enviados, int Lotes, IReadOnlyList<string> Avisos);
public sealed record ResumoConsultaEsocial(int Consultados, int Processados, int Aceitos, int Rejeitados, int Aguardando, IReadOnlyList<string> Avisos);

/// <summary>
/// Assinatura e transmissão dos eventos do eSocial. Sequência: eventos assinados → lotes de até 50 eventos por grupo (tabelas → não
/// periódicos → periódicos) → protocolo → consulta do lote → recibo ou ocorrências por evento. O transmissor pode ser o empregador
/// (certificado e-CNPJ da empresa), um procurador ou o contador — nesses dois casos o certificado é de outra pessoa/empresa e o eSocial
/// só aceita se houver procuração eletrônica (e-CAC ou módulo de procurações do eSocial) com o perfil dos eventos do lote.
/// Por padrão o ambiente é a produção restrita; a produção exige confirmação explícita.
/// </summary>
public sealed class ServicoTransmissaoEsocial(IRepositorioCadastrosFolha cadastros, IRepositorioESocial eventos, IRepositorioTransmissaoEsocial repositorio,
                                              ServicoResponsaveis responsaveis, Autorizador autorizador, TimeProvider? relogio = null)
{
    private static readonly string[] Sinalizaveis = ["GERADO", "ASSINADO", "EXPORTADO"];

    /// <summary>Fábrica do cliente SOAP (certificado + ambiente). Os testes trocam por um cliente apontado para um servidor simulado.</summary>
    public Func<X509Certificate2, AmbienteEsocial, IClienteEsocial> FabricaCliente { get; set; } = (c, a) => ClienteEsocial.Criar(c, a);

    private DateOnly Hoje => DateOnly.FromDateTime((relogio?.GetLocalNow() ?? DateTimeOffset.Now).DateTime);

    public async Task<IReadOnlyList<LoteEsocial>> ListarLotesAsync(long empresaId, long usuarioId, int limite = 50, CancellationToken ct = default)
        => await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FolhaESocial, ct) is null ? await repositorio.ListarLotesAsync(empresaId, limite, ct) : [];

    public async Task<LoteEsocial?> ObterLoteAsync(long empresaId, long loteId, long usuarioId, CancellationToken ct = default)
        => await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FolhaESocial, ct) is null ? await repositorio.ObterLoteAsync(empresaId, loteId, ct) : null;

    // ------------------------------------------------------------------ transmissor

    /// <summary>
    /// Confere se o certificado serve para o papel escolhido e devolve o ideTransmissor. Empregador: e-CNPJ da mesma raiz do CNPJ da empresa.
    /// Procurador: CPF do certificado = CPF do contador procurador, com procuração vigente hoje e abrangendo o eSocial. Contador: documento do certificado =
    /// documento do contador/escritório com vínculo vigente.
    /// </summary>
    public async Task<Resultado<TransmissorEsocial>> ResolverTransmissorAsync(long empresaId, PapelTransmissor papel, X509Certificate2 certificado,
                                                                              CancellationToken ct = default)
    {
        var empresa = await cadastros.ObterEmpresaAsync(empresaId, ct);
        if (empresa is null) return Resultado<TransmissorEsocial>.Falha("Empresa não encontrada.");
        var cnpj = Digitos(empresa.Cnpj);
        var doc = AssinadorEsocial.DocumentoDoCertificado(certificado);
        if (doc is null) return Resultado<TransmissorEsocial>.Falha("Não foi possível ler o CPF/CNPJ do certificado. Use um certificado e-CPF ou e-CNPJ ICP-Brasil.");
        if (certificado.NotAfter < (relogio?.GetUtcNow().UtcDateTime ?? DateTime.UtcNow)) return Resultado<TransmissorEsocial>.Falha("O certificado está vencido.");

        switch (papel)
        {
            case PapelTransmissor.Empregador:
                if (doc.Length != 14 || doc[..8] != cnpj[..8])
                    return Resultado<TransmissorEsocial>.Falha($"O certificado ({Formatar(doc)}) não é do empregador ({Formatar(cnpj)}). Para transmitir com o certificado de outra pessoa, escolha Procurador ou Contador.");
                return Resultado<TransmissorEsocial>.Ok(new("EMPREGADOR", 1, doc));
            case PapelTransmissor.Procurador:
            {
                var vigente = await responsaveis.ProcuradorVigenteAsync(empresaId, Hoje, AbrangenciaProcuracao.Esocial, ct);
                if (vigente is null)
                {
                    var ultimo = await responsaveis.UltimoProcuradorAsync(empresaId, ct);
                    return Resultado<TransmissorEsocial>.Falha(ultimo is null
                        ? "Não há procurador cadastrado para a empresa. Marque o contador como procurador (com a vigência da procuração) em Cadastros › Contadores, seção Procuração eletrônica."
                        : ultimo.VigenteEm(Hoje)
                            ? $"A procuração de {ultimo.Nome} não abrange o eSocial. Marque eSocial na abrangência da procuração, em Cadastros › Contadores."
                            : $"A procuração de {ultimo.Nome} não está vigente hoje (vigência {ultimo.VigenciaInicio:dd/MM/yyyy} a {(ultimo.VigenciaFim is { } f ? f.ToString("dd/MM/yyyy") : "indeterminada")}). Atualize a procuração no cadastro do contador.");
                }
                if (doc != Digitos(vigente.Cpf))
                    return Resultado<TransmissorEsocial>.Falha($"O certificado ({Formatar(doc)}) não é do procurador cadastrado ({vigente.Nome}, CPF {Formatar(Digitos(vigente.Cpf))}).");
                return Resultado<TransmissorEsocial>.Ok(new("PROCURADOR", 2, doc));
            }
            default:
            {
                var vinculos = (await responsaveis.ListarVinculosParaTransmissaoAsync(empresaId, ct)).Where(v => v.Vigente).ToList();
                if (vinculos.Count == 0) return Resultado<TransmissorEsocial>.Falha("A empresa não tem contador/escritório contábil com vínculo vigente.");
                var v = vinculos.FirstOrDefault(x => Digitos(x.Documento) == doc);
                if (v is null) return Resultado<TransmissorEsocial>.Falha($"O certificado ({Formatar(doc)}) não é de nenhum contador/escritório vinculado à empresa.");
                return Resultado<TransmissorEsocial>.Ok(new("CONTADOR", doc.Length == 14 ? 1 : 2, doc));
            }
        }
    }

    // ------------------------------------------------------------------ assinar e enviar

    public async Task<Resultado<ResumoEnvioEsocial>> AssinarEEnviarAsync(long empresaId, DateOnly competencia, IReadOnlyCollection<long> ids, AmbienteEsocial ambiente,
                                                                         PapelTransmissor papel, X509Certificate2 certificado, long usuarioId,
                                                                         bool confirmaProducao = false, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<ResumoEnvioEsocial>(usuarioId, empresaId, Permissao.FolhaESocial, ct) is { } negado) return negado;
        if (ambiente == AmbienteEsocial.Producao && !confirmaProducao)
            return Resultado<ResumoEnvioEsocial>.Falha("A transmissão em PRODUÇÃO é definitiva e vale perante a Receita/FGTS. Confirme explicitamente (recomenda-se testar antes na produção restrita).");
        var empresa = await cadastros.ObterEmpresaAsync(empresaId, ct);
        if (empresa is null) return Resultado<ResumoEnvioEsocial>.Falha("Empresa não encontrada.");
        var transmissor = await ResolverTransmissorAsync(empresaId, papel, certificado, ct);
        if (!transmissor.Sucesso) return Resultado<ResumoEnvioEsocial>.Falha(transmissor.Erros);

        competencia = new DateOnly(competencia.Year, competencia.Month, 1);
        var todos = await eventos.ListarAsync(empresaId, competencia, ct);
        var escolhidos = todos.Where(e => ids.Contains(e.Id)).ToList();
        if (escolhidos.Count == 0) return Resultado<ResumoEnvioEsocial>.Falha("Selecione ao menos um evento.");
        var avisos = new List<string>();
        foreach (var e in escolhidos.Where(e => !Enviavel(e)))
            avisos.Add($"{e.Tipo} {e.Referencia}: status {e.Status} — não é reenviado (apenas gerados, assinados ou com falha de lote).");
        escolhidos = escolhidos.Where(Enviavel).ToList();

        // Assinatura (e conferência no XSD oficial antes de assinar)
        int assinados = 0;
        var prontos = new List<(EventoEsocial Evento, string Assinado)>();
        foreach (var e in escolhidos)
        {
            string assinado;
            if (e.Status == "ASSINADO" && e.Ambiente == (int)ambiente && e.XmlAssinado is { } ja && AssinadorEsocial.Verificar(ja, out var cert) && cert?.Thumbprint == certificado.Thumbprint)
                assinado = ja;   // já assinado com este certificado e para este ambiente
            else
            {
                var xml = DefinirAmbiente(e.Xml, (int)ambiente);
                var erros = ValidadorXsdEsocial.ValidarEvento(xml, exigirAssinatura: false);
                if (erros.Count > 0)
                {
                    await repositorio.RegistrarFalhaPreparoAsync(empresaId, e.Id, Json(erros.Take(10).Select(m => new OcorrenciaEsocial(1, "XSD", ServicoESocial.ResumirErroXsd(m), null))), ct);
                    avisos.Add($"{e.Tipo} {e.Referencia}: fora do leiaute — {ServicoESocial.ResumirErroXsd(erros[0])}");
                    continue;
                }
                try { assinado = AssinadorEsocial.Assinar(xml, certificado); }
                catch (Exception ex) when (ex is InvalidOperationException or System.Security.Cryptography.CryptographicException)
                { return Resultado<ResumoEnvioEsocial>.Falha($"Não foi possível assinar com o certificado: {ex.Message}"); }
                await repositorio.MarcarAssinadoAsync(empresaId, e.Id, assinado, (int)ambiente, usuarioId, ct);
                assinados++;
            }
            prontos.Add((e, assinado));
        }
        if (prontos.Count == 0) return Resultado<ResumoEnvioEsocial>.Ok(new(assinados, 0, 0, avisos));

        // Ordem do envio: só vai o menor grupo; os demais esperam o processamento (tabelas → não periódicos → periódicos).
        var pendentes = (await eventos.ListarAsync(empresaId, null, ct)).Where(x => x.Status == "ENVIADO" && x.Ambiente == (int)ambiente).ToList();
        var menorPendente = pendentes.Count == 0 ? 99 : pendentes.Min(x => MontadorLoteEsocial.Grupo(x.Tipo));
        var gruposEnvio = prontos.Select(p => MontadorLoteEsocial.Grupo(p.Evento.Tipo)).Distinct().Order().ToList();
        var grupoAtual = gruposEnvio[0];
        if (menorPendente < grupoAtual)
        {
            avisos.Add($"Há eventos de {MontadorLoteEsocial.NomeGrupo(menorPendente)} aguardando processamento: consulte o retorno antes de enviar {MontadorLoteEsocial.NomeGrupo(grupoAtual)}.");
            return Resultado<ResumoEnvioEsocial>.Ok(new(assinados, 0, 0, avisos));
        }
        foreach (var g in gruposEnvio.Skip(1))
            avisos.Add($"{MontadorLoteEsocial.NomeGrupo(g)}: enviar depois do processamento de {MontadorLoteEsocial.NomeGrupo(grupoAtual)} (consulte o retorno e repita).");
        // S-1299 só depois de processados todos os eventos periódicos da competência.
        var doGrupo = prontos.Where(p => MontadorLoteEsocial.Grupo(p.Evento.Tipo) == grupoAtual).ToList();
        if (doGrupo.Any(p => p.Evento.Tipo == "S-1299"))
        {
            var abertos = todos.Where(x => x.Competencia == competencia && x.Tipo is "S-1200" or "S-1210" && x.Status != "PROCESSADO" && !doGrupo.Any(p => p.Evento.Id == x.Id)).ToList();
            if (abertos.Count > 0 || doGrupo.Any(p => p.Evento.Tipo is "S-1200" or "S-1210"))
            {
                avisos.Add("S-1299 (fechamento) fica para depois do processamento de todos os S-1200/S-1210 da competência.");
                doGrupo = doGrupo.Where(p => p.Evento.Tipo != "S-1299").ToList();
            }
        }
        if (doGrupo.Count == 0) return Resultado<ResumoEnvioEsocial>.Ok(new(assinados, 0, 0, avisos));

        IClienteEsocial cliente = FabricaCliente(certificado, ambiente);
        int enviados = 0, lotes = 0;
        foreach (var (grupo, parte) in MontadorLoteEsocial.Dividir(doGrupo, p => MontadorLoteEsocial.Grupo(p.Evento.Tipo)))
        {
            var xmlLote = MontadorLoteEsocial.Montar(empresa.Cnpj, transmissor.Valor, grupo, parte.Select(p => (p.Evento.IdEvento, p.Assinado)).ToList());
            if (ValidadorXsdEsocial.ValidarLote(xmlLote) is { Count: > 0 } errosLote)
                return Resultado<ResumoEnvioEsocial>.Falha("Lote fora do leiaute de comunicação: " + errosLote[0]);
            RespostaEnvioLote resp;
            try { resp = await cliente.EnviarLoteAsync(xmlLote, ct); }
            catch (ErroComunicacaoEsocial ex) { avisos.Add(ex.Message); break; }
            var recebido = resp.Recebido && resp.Protocolo is not null;
            var ocorrencias = Json(resp.Ocorrencias.Select(o => o with { Descricao = Explicar(o) }));
            var loteId = await repositorio.CriarLoteAsync(empresaId, new NovoLoteEsocial((int)ambiente, grupo, transmissor.Valor.Papel, transmissor.Valor.TpInsc, transmissor.Valor.NrInsc,
                resp.Protocolo, recebido ? "ENVIADO" : "ERRO", resp.Codigo, resp.Descricao, ocorrencias, xmlLote, resp.XmlBruto), usuarioId, ct);
            await repositorio.VincularEventosAsync(empresaId, parte.Select(p => p.Evento.Id).ToArray(), loteId, recebido ? "ENVIADO" : "ERRO", recebido ? null : ocorrencias, resp.Codigo, ct);
            lotes++;
            if (recebido) enviados += parte.Count;
            else avisos.Add($"Lote {MontadorLoteEsocial.NomeGrupo(grupo)} recusado: {resp.Codigo} — {resp.Descricao}" + (resp.Ocorrencias.Count > 0 ? $" ({Explicar(resp.Ocorrencias[0])})" : ""));
        }
        return Resultado<ResumoEnvioEsocial>.Ok(new(assinados, enviados, lotes, avisos));
    }

    private static bool Enviavel(EventoEsocial e) => Sinalizaveis.Contains(e.Status) || (e.Status == "ERRO" && e.CodigoResposta is null);

    // ------------------------------------------------------------------ consultar retorno

    /// <summary>Consulta os lotes enviados e ainda sem resultado (ou o lote indicado) e grava recibo/ocorrências por evento.</summary>
    public async Task<Resultado<ResumoConsultaEsocial>> ConsultarRetornoAsync(long empresaId, X509Certificate2 certificado, long usuarioId, long? loteId = null,
                                                                               CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<ResumoConsultaEsocial>(usuarioId, empresaId, Permissao.FolhaESocial, ct) is { } negado) return negado;
        var lotes = (await repositorio.ListarLotesAsync(empresaId, 200, ct)).Where(l => l.Protocolo is not null && (loteId is null ? l.Status == "ENVIADO" : l.Id == loteId)).ToList();
        if (lotes.Count == 0) return Resultado<ResumoConsultaEsocial>.Ok(new(0, 0, 0, 0, 0, ["Não há lotes aguardando retorno."]));
        var avisos = new List<string>();
        int processados = 0, aceitos = 0, rejeitados = 0, aguardando = 0;
        foreach (var lote in lotes)
        {
            var cliente = FabricaCliente(certificado, (AmbienteEsocial)lote.Ambiente);
            RespostaConsultaLote r;
            try { r = await cliente.ConsultarLoteAsync(lote.Protocolo!, ct); }
            catch (ErroComunicacaoEsocial ex) { avisos.Add($"Lote {lote.Protocolo}: {ex.Message}"); continue; }
            var ocLote = Json(r.Ocorrencias.Select(o => o with { Descricao = Explicar(o) }));
            if (r.EmProcessamento)
            {
                aguardando++;
                await repositorio.AtualizarLoteAsync(empresaId, lote.Id, "ENVIADO", r.Codigo, r.Descricao, ocLote, r.XmlBruto, ct);
                continue;
            }
            if (!r.Processado)   // 301, 4xx: lote recusado
            {
                await repositorio.AtualizarLoteAsync(empresaId, lote.Id, "ERRO", r.Codigo, r.Descricao, ocLote, r.XmlBruto, ct);
                await repositorio.RejeitarEventosPendentesDoLoteAsync(empresaId, lote.Id, r.Codigo, ocLote, ct);
                avisos.Add($"Lote {lote.Protocolo}: {r.Codigo} — {r.Descricao}");
                continue;
            }
            await repositorio.AtualizarLoteAsync(empresaId, lote.Id, "PROCESSADO", r.Codigo, r.Descricao, ocLote, r.XmlBruto, ct);
            processados++;
            foreach (var ev in r.Eventos)
            {
                await repositorio.RegistrarResultadoEventoAsync(empresaId, lote.Id, ev.IdEvento, ev.Aceito ? "PROCESSADO" : "ERRO", ev.Recibo, ev.Codigo,
                    Json(ev.Ocorrencias.Select(o => o with { Descricao = Explicar(o) })), ct);
                if (ev.Aceito) aceitos++; else { rejeitados++; avisos.Add($"{ev.IdEvento}: {ev.Codigo} — {ev.Ocorrencias.FirstOrDefault()?.Descricao ?? ev.Descricao}"); }
            }
        }
        return Resultado<ResumoConsultaEsocial>.Ok(new(lotes.Count, processados, aceitos, rejeitados, aguardando, avisos));
    }

    // ------------------------------------------------------------------ utilidades

    /// <summary>Acerta o tpAmb do evento (1 = produção, 2 = produção restrita) antes de assinar.</summary>
    public static string DefinirAmbiente(string xml, int ambiente)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);
        foreach (XmlNode n in doc.GetElementsByTagName("tpAmb", doc.DocumentElement!.NamespaceURI)) n.InnerText = ambiente.ToString();
        using var sw = new StringWriter();
        using (var xw = XmlWriter.Create(sw, new XmlWriterSettings { Indent = false, Encoding = new System.Text.UTF8Encoding(false) })) doc.Save(xw);
        return sw.ToString().Replace("encoding=\"utf-16\"", "encoding=\"UTF-8\"");
    }

    /// <summary>Acrescenta a orientação do Vorticis às ocorrências mais comuns (certificado, procuração, ambiente).</summary>
    public static string Explicar(OcorrenciaEsocial o) => o.Codigo switch
    {
        "411" => o.Descricao + " — O certificado usado não tem procuração eletrônica (e-CAC/eSocial) para estes eventos; confira o perfil outorgado ao procurador/contador.",
        "404" => o.Descricao + " — Problema com o certificado: confira se é ICP-Brasil, se está válido e se o CPF/CNPJ do ideTransmissor é o dele.",
        _ => o.Descricao,
    };

    private static string Json(IEnumerable<OcorrenciaEsocial> o) => JsonSerializer.Serialize(o);

    public static IReadOnlyList<OcorrenciaEsocial> LerOcorrencias(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<OcorrenciaEsocial>>(json) ?? []; } catch (JsonException) { return []; }
    }

    private static string Digitos(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());

    private static string Formatar(string d) => d.Length == 14 ? Convert.ToUInt64(d).ToString(@"00\.000\.000\/0000\-00") : d.Length == 11 ? Convert.ToUInt64(d).ToString(@"000\.000\.000\-00") : d;
}
