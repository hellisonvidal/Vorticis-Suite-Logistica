using Vorticis.Application.Contabil;
using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Documentos;
using Vorticis.Fiscal.Duplicatas;

namespace Vorticis.Fiscal.Contabilizacao;

/// <summary>Conta do plano resolvida por código (completo ou reduzido).</summary>
public sealed record ContaResolvida(long Id, string Codigo, bool Analitica, bool Ativa);

public interface IRepositorioContabilizacao
{
    Task<IReadOnlyList<RegraContabilizacao>> ListarRegrasAsync(long empresaId, CancellationToken ct);
    Task<long> GravarRegraAsync(long empresaId, RegraContabilizacao regra, long contaDebitoId, long contaCreditoId, CancellationToken ct);
    Task ExcluirRegraAsync(long empresaId, long regraId, CancellationToken ct);

    /// <summary>Contas pelo código completo ou reduzido; as não encontradas ficam fora.</summary>
    Task<IReadOnlyDictionary<string, ContaResolvida>> ResolverContasAsync(long empresaId, IEnumerable<string> codigos, CancellationToken ct);

    /// <summary>Marca as notas como contabilizadas (documento → lançamento).</summary>
    Task MarcarContabilizadosAsync(IReadOnlyList<(long DocumentoId, long LancamentoId)> marcas, long usuarioId, CancellationToken ct);
    Task DesmarcarAsync(IReadOnlyCollection<long> documentoIds, CancellationToken ct);
    Task<DateOnly?> ObterDataLancamentoAsync(long lancamentoId, CancellationToken ct);

    /// <summary>"Usar centros de custo no fiscal" ligado e empresa com centros de custo habilitados.</summary>
    Task<bool> CentroCustoAtivoAsync(long empresaId, CancellationToken ct);
    /// <summary>Parâmetro do Fiscal: contabilizar as notas ao gravar/importar (on-line) em vez de pela função em lote.</summary>
    Task<bool> ContabilizacaoOnlineAsync(long empresaId, CancellationToken ct);
    Task GravarContabilizacaoOnlineAsync(long empresaId, bool online, CancellationToken ct);
}

public sealed record FalhaContabilizacao(string Documento, IReadOnlyList<string> Erros);

public sealed record ResultadoContabilizacao(int Contabilizados, decimal Valor, IReadOnlyList<FalhaContabilizacao> Falhas, int Registradas = 0)
{
    public string Resumo => $"{Contabilizados} nota(s) contabilizada(s), total {Formato.Moeda(Valor)}" +
                            (Falhas.Count > 0 ? $"; {Falhas.Count} com pendência." : ".");
}

/// <summary>
/// Contabilização automática das notas fiscais: regras por CFOP/participante geram lançamentos pelo
/// <see cref="ServicoLancamento"/> (único ponto de gravação contábil). Exige a função "contabilizar notas fiscais" e, para gravar,
/// a de incluir (ou estornar) lançamentos, verificada pelo próprio serviço de lançamento.
/// </summary>
public sealed class ServicoContabilizacaoFiscal(IRepositorioContabilizacao repositorio, IRepositorioDocumentos documentos, ServicoLancamento lancamentos,
                                                Autorizador autorizador, IRepositorioDuplicatas? duplicatas = null, ServicoPendencias? pendencias = null)
{
    // ------------------------------------------------------------------ regras

    public async Task<Resultado<IReadOnlyList<RegraContabilizacao>>> ListarRegrasAsync(long empresaId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<RegraContabilizacao>>(usuarioId, empresaId, Permissao.FiscalDocumentosConsultar, ct) is { } negado) return negado;
        return Resultado<IReadOnlyList<RegraContabilizacao>>.Ok(await repositorio.ListarRegrasAsync(empresaId, ct));
    }

    public async Task<Resultado<long>> GravarRegraAsync(long empresaId, RegraContabilizacao regra, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.FiscalContabilizar, ct) is { } negado) return negado;
        var erros = new List<string>();
        var cfop = string.IsNullOrWhiteSpace(regra.Cfop) ? null : regra.Cfop.Trim().ToUpperInvariant();
        if (cfop is not null && cfop != Cfop.Servico && !(cfop.Length is >= 1 and <= 4 && cfop.All(char.IsAsciiDigit)))
            erros.Add("CFOP da regra: informe 1 a 4 dígitos (prefixo ou código completo) ou NFSE para serviços.");
        if (string.IsNullOrWhiteSpace(regra.Historico)) erros.Add("Informe o histórico.");
        else if (VariaveisHistorico.Desconhecidas(regra.Historico) is { Count: > 0 } desc)
            erros.Add($"Variável desconhecida no histórico: {string.Join(", ", desc.Select(d => "{" + d + "}"))}.");
        var contas = await repositorio.ResolverContasAsync(empresaId, [regra.ContaDebito, regra.ContaCredito], ct);
        ContaResolvida? Conta(string codigo, string papel)
        {
            if (!contas.TryGetValue(codigo.Trim(), out var c)) { erros.Add($"Conta de {papel} {codigo} não encontrada."); return null; }
            if (!c.Analitica) erros.Add($"Conta de {papel} {c.Codigo} é sintética.");
            if (!c.Ativa) erros.Add($"Conta de {papel} {c.Codigo} está inativa.");
            return c;
        }
        var debito = Conta(regra.ContaDebito, "débito");
        var credito = Conta(regra.ContaCredito, "crédito");
        if (debito is not null && credito is not null && debito.Id == credito.Id) erros.Add("As contas de débito e de crédito devem ser diferentes.");
        if (erros.Count > 0) return Resultado<long>.Falha(erros);
        try
        {
            return Resultado<long>.Ok(await repositorio.GravarRegraAsync(empresaId,
                regra with { Cfop = cfop, Historico = regra.Historico.Trim(), ContaDebito = debito!.Codigo, ContaCredito = credito!.Codigo }, debito.Id, credito.Id, ct));
        }
        catch (ViolacaoRegraException ex) { return Resultado<long>.Falha(ex.Message); }
    }

    public async Task<Resultado> ExcluirRegraAsync(long empresaId, long regraId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FiscalContabilizar, ct) is { } negado) return Resultado.Falha(negado);
        await repositorio.ExcluirRegraAsync(empresaId, regraId, ct);
        return Resultado.Ok();
    }

    // ------------------------------------------------------------------ contabilização

    /// <summary>Notas ainda não contabilizadas do período com o lançamento que seria gerado (ou o motivo de não ser possível).</summary>
    public async Task<Resultado<IReadOnlyList<PropostaContabilizacao>>> PrevisaoAsync(long empresaId, DateOnly inicio, DateOnly fim, TipoOperacao? operacao,
        IReadOnlySet<long>? ids, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<PropostaContabilizacao>>(usuarioId, empresaId, Permissao.FiscalContabilizar, ct) is { } negado) return negado;
        if (fim < inicio) return Resultado<IReadOnlyList<PropostaContabilizacao>>.Falha("Data final anterior à inicial.");
        return Resultado<IReadOnlyList<PropostaContabilizacao>>.Ok(await MontarPropostasAsync(empresaId, inicio, fim, operacao, ids, ct));
    }

    private async Task<IReadOnlyList<PropostaContabilizacao>> MontarPropostasAsync(long empresaId, DateOnly inicio, DateOnly fim, TipoOperacao? operacao,
                                                                                   IReadOnlySet<long>? ids, CancellationToken ct)
    {
        var docs = await documentos.ListarAsync(empresaId, new FiltroDocumentos(inicio, fim, operacao, Situacao: SituacaoDocumento.Autorizada, Contabilizado: false, Limite: 20000), ct);
        if (ids is not null) docs = docs.Where(d => ids.Contains(d.Id)).ToList();
        var itens = (await documentos.ListarItensAsync(empresaId, inicio, fim, operacao, ct)).ToLookup(i => i.DocumentoId, i => i.Item);
        var regras = await repositorio.ListarRegrasAsync(empresaId, ct);
        var usaCentro = await repositorio.CentroCustoAtivoAsync(empresaId, ct);
        // Nota com duplicatas: a contrapartida é a conta de fornecedores (entrada) ou de clientes (saída) dos Parâmetros do Fiscal.
        var comDuplicata = duplicatas is null || docs.Count == 0 ? new HashSet<long>() : await duplicatas.DocumentosComDuplicataAsync(empresaId, docs.Select(d => d.Id), ct);
        var config = comDuplicata.Count == 0 ? null : await duplicatas!.ObterConfiguracaoAsync(empresaId, ct);
        return docs.Select(d => MotorContabilizacao.Montar(empresaId, d, itens[d.Id].ToList(), regras, usaCentro,
            comDuplicata.Contains(d.Id) ? config?.ContaParticipante(d.Operacao == TipoOperacao.Entrada ? TipoDuplicata.Pagar : TipoDuplicata.Receber) : null)).ToList();
    }

    /// <summary>
    /// Gera os lançamentos das notas prontas (as demais voltam como pendências) e as marca como contabilizadas. Os lançamentos
    /// são gravados numa única transação pelo serviço de lançamentos; uma nota com conta ou período inválido não impede as outras.
    /// </summary>
    public async Task<Resultado<ResultadoContabilizacao>> ContabilizarAsync(long empresaId, DateOnly inicio, DateOnly fim, TipoOperacao? operacao,
        IReadOnlySet<long>? ids, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<ResultadoContabilizacao>(usuarioId, empresaId, Permissao.FiscalContabilizar, ct) is { } negado) return negado;
        if (fim < inicio) return Resultado<ResultadoContabilizacao>.Falha("Data final anterior à inicial.");
        var propostas = await MontarPropostasAsync(empresaId, inicio, fim, operacao, ids, ct);
        if (propostas.Count == 0) return Resultado<ResultadoContabilizacao>.Falha("Nenhuma nota pendente de contabilização no período.");

        var falhas = propostas.Where(p => !p.Pronta).Select(p => new FalhaContabilizacao(p.Documento.Numero, p.Erros)).ToList();
        var prontas = propostas.Where(p => p.Pronta).ToList();
        if (prontas.Count > 0)
        {
            var validacoes = await lancamentos.ValidarLoteAsync(prontas.Select(p => p.Lancamento!).ToList(), ct);
            var validas = new List<PropostaContabilizacao>();
            for (var i = 0; i < prontas.Count; i++)
                if (validacoes[i].Sucesso) validas.Add(prontas[i]);
                else falhas.Add(new FalhaContabilizacao(prontas[i].Documento.Numero, validacoes[i].Erros));
            prontas = validas;
        }
        if (prontas.Count == 0) return Resultado<ResultadoContabilizacao>.Ok(new ResultadoContabilizacao(0, 0, falhas));

        var gravados = await lancamentos.EfetivarLoteAsync(prontas.Select(p => p.Lancamento!).ToList(), prontas.Select(p => (string?)$"FISCAL:DOC:{p.Documento.Id}").ToList(),
                                                          usuarioId, ct);
        if (!gravados.Sucesso) return Resultado<ResultadoContabilizacao>.Falha(gravados.Erros);
        await repositorio.MarcarContabilizadosAsync(prontas.Select((p, i) => (p.Documento.Id, gravados.Valor[i].Id)).ToList(), usuarioId, ct);
        var valor = prontas.Sum(p => p.Lancamento!.Partidas.Where(x => x.DC == IndicadorDC.D).Sum(x => x.Valor));
        return Resultado<ResultadoContabilizacao>.Ok(new ResultadoContabilizacao(prontas.Count, valor, falhas));
    }

    public async Task<Resultado<bool>> ContabilizacaoOnlineAsync(long empresaId, long usuarioId, CancellationToken ct = default)
        => (await autorizador.ObterAsync(usuarioId, empresaId, ct)).TemAcesso
            ? Resultado<bool>.Ok(await repositorio.ContabilizacaoOnlineAsync(empresaId, ct))
            : Resultado<bool>.Falha("Você não tem acesso a esta empresa.");

    public async Task<Resultado> GravarContabilizacaoOnlineAsync(long empresaId, bool online, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FiscalContabilizar, ct) is { } negado) return Resultado.Falha(negado);
        await repositorio.GravarContabilizacaoOnlineAsync(empresaId, online, ct);
        return Resultado.Ok();
    }

    /// <summary>
    /// Contabilização on-line: com o parâmetro ligado, contabiliza as notas pendentes do período logo depois de gravadas ou importadas
    /// (as que não têm regra ou conta válida continuam pendentes e voltam em <see cref="ResultadoContabilizacao.Falhas"/>).
    /// Devolve null quando o parâmetro está desligado (contabilização em lote, pela função Contabilizar notas fiscais).
    /// </summary>
    public async Task<Resultado<ResultadoContabilizacao>?> ContabilizarSeOnlineAsync(long empresaId, DateOnly inicio, DateOnly fim, IReadOnlySet<long>? ids,
                                                                                     long usuarioId, CancellationToken ct = default)
    {
        if (pendencias is not null && await pendencias.ModoDaEmpresaAsync(empresaId, ct) == ModoContabilizacao.Lote)
            return await RegistrarPendenciasAsync(empresaId, inicio, fim, ids, usuarioId, ct);
        if (!await repositorio.ContabilizacaoOnlineAsync(empresaId, ct)) return null;
        var r = await ContabilizarAsync(empresaId, inicio, fim, null, ids, usuarioId, ct);
        // nada pendente não é erro na contabilização automática
        return !r.Sucesso && r.Erros.All(e => e.StartsWith("Nenhuma nota pendente", StringComparison.Ordinal))
            ? Resultado<ResultadoContabilizacao>.Ok(new ResultadoContabilizacao(0, 0, []))
            : r;
    }

    public const string TipoNota = "NOTA";

    /// <summary>Contabilização em lote: em vez de gerar o lançamento, registra uma pendência para cada nota pendente do período.</summary>
    private async Task<Resultado<ResultadoContabilizacao>> RegistrarPendenciasAsync(long empresaId, DateOnly inicio, DateOnly fim, IReadOnlySet<long>? ids, long usuarioId,
                                                                                    CancellationToken ct)
    {
        var docs = await documentos.ListarAsync(empresaId, new FiltroDocumentos(inicio, fim, null, Situacao: SituacaoDocumento.Autorizada, Contabilizado: false, Limite: 20000), ct);
        if (ids is not null) docs = docs.Where(d => ids.Contains(d.Id)).ToList();
        var n = 0;
        foreach (var d in docs)
        {
            var parte = d.Operacao == TipoOperacao.Entrada ? d.Emitente.Nome : d.Destinatario?.Nome;
            var descricao = $"NF {d.Numero} ({(d.Operacao == TipoOperacao.Entrada ? "entrada" : "saída")}){(string.IsNullOrWhiteSpace(parte) ? "" : " - " + parte)}";
            if (await pendencias!.RegistrarAsync(empresaId, new NovaPendencia(OrigensPendencia.Fiscal, TipoNota, d.Id.ToString(), d.DataLivro, descricao, d.Totais.Total), usuarioId, ct)) n++;
        }
        return Resultado<ResultadoContabilizacao>.Ok(new ResultadoContabilizacao(0, 0, [], n));
    }

    /// <summary>Processador da origem FISCAL (notas pendentes da Contabilização em lote): as mesmas regras e o mesmo motor da contabilização on-line.</summary>
    public IProcessadorPendencia ProcessadorPendencias => new ProcessadorNotas(this);

    private async Task<IReadOnlyList<string>> PreviaPendenciaAsync(long empresaId, PendenciaContabil p, long usuarioId, CancellationToken ct)
    {
        var r = await PrevisaoAsync(empresaId, p.Periodo, p.Periodo, null, new HashSet<long> { long.Parse(p.Referencia) }, usuarioId, ct);
        if (!r.Sucesso) return r.Erros;
        if (r.Valor.Count == 0) return ["A nota não está mais pendente de contabilização."];
        var prop = r.Valor[0];
        return prop.Lancamento is null || prop.Erros.Count > 0 ? prop.Erros : [.. prop.Lancamento.Partidas.Select(x => $"{x.DC} {x.CodigoConta}  {Formato.Moeda(x.Valor)}")];
    }

    private async Task<ResultadoProcessamento> ContabilizarPendenciaAsync(long empresaId, PendenciaContabil p, long usuarioId, CancellationToken ct)
    {
        var id = long.Parse(p.Referencia);
        var r = await ContabilizarAsync(empresaId, p.Periodo, p.Periodo, null, new HashSet<long> { id }, usuarioId, ct);
        if (!r.Sucesso)
            return r.Erros.All(e => e.StartsWith("Nenhuma nota pendente", StringComparison.Ordinal)) ? ResultadoProcessamento.Cancelada("a nota não está mais pendente.") : ResultadoProcessamento.Falha(r.Erros);
        if (r.Valor.Contabilizados == 0) return ResultadoProcessamento.Falha(r.Valor.Falhas.SelectMany(f => f.Erros).ToList());
        var doc = (await documentos.ListarAsync(empresaId, new FiltroDocumentos(p.Periodo, p.Periodo, Contabilizado: true), ct)).FirstOrDefault(d => d.Id == id);
        return ResultadoProcessamento.Ok(doc?.LancamentoId);
    }

    private async Task<Resultado> DesfazerPendenciaAsync(long empresaId, PendenciaContabil p, long usuarioId, CancellationToken ct)
    {
        var r = await DesfazerAsync(empresaId, [long.Parse(p.Referencia)], null, usuarioId, ct);
        if (!r.Sucesso) return Resultado.Falha(r.Erros);
        return r.Valor.Falhas.Count > 0 ? Resultado.Falha(r.Valor.Falhas.SelectMany(f => f.Erros)) : Resultado.Ok();
    }

    private sealed class ProcessadorNotas(ServicoContabilizacaoFiscal s) : IProcessadorPendencia
    {
        public string Origem => OrigensPendencia.Fiscal;
        public Task<IReadOnlyList<string>> PreviaAsync(long empresaId, PendenciaContabil p, long usuarioId, CancellationToken ct) => s.PreviaPendenciaAsync(empresaId, p, usuarioId, ct);
        public Task<ResultadoProcessamento> ContabilizarAsync(long empresaId, PendenciaContabil p, long usuarioId, CancellationToken ct) => s.ContabilizarPendenciaAsync(empresaId, p, usuarioId, ct);
        public Task<Resultado> DesfazerAsync(long empresaId, PendenciaContabil p, long usuarioId, CancellationToken ct) => s.DesfazerPendenciaAsync(empresaId, p, usuarioId, ct);
    }

    /// <summary>Estorna o lançamento de cada nota (na data dele, ou em <paramref name="dataEstorno"/>) e a devolve à situação de não contabilizada.</summary>
    public async Task<Resultado<ResultadoContabilizacao>> DesfazerAsync(long empresaId, IReadOnlyCollection<long> documentoIds, DateOnly? dataEstorno,
                                                                       long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<ResultadoContabilizacao>(usuarioId, empresaId, Permissao.FiscalContabilizar, ct) is { } negado) return negado;
        if (documentoIds.Count == 0) return Resultado<ResultadoContabilizacao>.Falha("Selecione ao menos uma nota contabilizada.");
        var docs = (await documentos.ListarAsync(empresaId, new FiltroDocumentos(Contabilizado: true, Limite: 100000), ct))
            .Where(d => documentoIds.Contains(d.Id) && d.LancamentoId is not null).ToList();
        if (docs.Count == 0) return Resultado<ResultadoContabilizacao>.Falha("As notas selecionadas não estão contabilizadas.");

        var falhas = new List<FalhaContabilizacao>();
        var desfeitos = new List<long>();
        foreach (var d in docs)
        {
            var data = dataEstorno ?? await repositorio.ObterDataLancamentoAsync(d.LancamentoId!.Value, ct) ?? d.DataLivro;
            var r = await lancamentos.EstornarAsync(empresaId, d.LancamentoId!.Value, data, $"Desfazer contabilização da NF {d.Numero}", usuarioId, ct);
            if (r.Sucesso) desfeitos.Add(d.Id);
            else falhas.Add(new FalhaContabilizacao(d.Numero, r.Erros));
        }
        if (desfeitos.Count > 0) await repositorio.DesmarcarAsync(desfeitos, ct);
        return Resultado<ResultadoContabilizacao>.Ok(new ResultadoContabilizacao(desfeitos.Count, 0, falhas));
    }

    /// <summary>
    /// Exclui notas (digitadas ou importadas de XML). Nota contabilizada só é excluída com <paramref name="desfazerContabilizacao"/>:
    /// o lançamento é estornado primeiro (exige também a função de contabilizar). Se o estorno falhar, nada é excluído.
    /// </summary>
    public async Task<Resultado<int>> ExcluirDocumentosAsync(long empresaId, IReadOnlyCollection<long> ids, bool desfazerContabilizacao, long usuarioId,
                                                             CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<int>(usuarioId, empresaId, Permissao.FiscalDocumentosImportar, ct) is { } negado) return negado;
        if (ids.Count == 0) return Resultado<int>.Falha("Selecione ao menos um documento.");
        if (duplicatas is not null && await duplicatas.DocumentosComBaixaAsync(empresaId, ids, ct) is { Count: > 0 })
            return Resultado<int>.Falha("Há nota(s) com duplicata baixada: estorne as baixas em Lançamentos › Duplicatas antes de excluir.");
        var contabilizados = (await documentos.ListarAsync(empresaId, new FiltroDocumentos(Contabilizado: true, Limite: 100000), ct))
            .Where(d => ids.Contains(d.Id)).Select(d => d.Id).ToList();
        if (contabilizados.Count > 0)
        {
            if (!desfazerContabilizacao)
                return Resultado<int>.Falha("Há nota(s) contabilizada(s): é preciso desfazer a contabilização antes de excluir (use \"Desfazer contabilização e excluir\").");
            var d = await DesfazerAsync(empresaId, contabilizados, null, usuarioId, ct);
            if (!d.Sucesso) return Resultado<int>.Falha(d.Erros);
            if (d.Valor.Falhas.Count > 0)
                return Resultado<int>.Falha(d.Valor.Falhas.SelectMany(f => f.Erros.Select(e => $"Nota {f.Documento}: {e}")).Append("Nenhuma nota foi excluída.").ToList());
        }
        int n;
        try { n = await documentos.ExcluirAsync(empresaId, ids, ct); }
        catch (ViolacaoRegraException ex) { return Resultado<int>.Falha(ex.Message); }
        return n == 0 ? Resultado<int>.Falha("Nenhum documento excluído.") : Resultado<int>.Ok(n);
    }
}
