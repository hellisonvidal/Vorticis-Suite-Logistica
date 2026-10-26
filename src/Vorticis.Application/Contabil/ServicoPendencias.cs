using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Application.Contabil;

/// <summary>Tipo de contabilização da empresa (Contábil › Parâmetros): como os módulos geram os lançamentos.</summary>
public enum ModoContabilizacao
{
    /// <summary>Padrão: cada documento, baixa ou folha gera o lançamento na hora.</summary>
    Online,
    /// <summary>Os módulos só registram pendências; a tela Contabilização em lote gera os lançamentos.</summary>
    Lote,
}

/// <summary>Origens das pendências de contabilização (as mesmas do CHECK da tabela).</summary>
public static class OrigensPendencia
{
    public const string Fiscal = "FISCAL";
    public const string Duplicatas = "DUPLICATAS";
    public const string Folha = "FOLHA";
    public const string Frota = "FROTA";
    public const string Gerencial = "GERENCIAL";

    public static readonly IReadOnlyList<string> Todas = [Fiscal, Duplicatas, Folha, Frota, Gerencial];

    public static string Nome(string origem) => origem switch
    {
        Fiscal => "Fiscal (notas e provisões)",
        Duplicatas => "Fiscal (baixas de duplicatas)",
        Folha => "Folha de pagamento",
        Frota => "Frota",
        Gerencial => "Gerencial",
        _ => origem,
    };
}

public static class SituacaoPendencia
{
    public const string Pendente = "PENDENTE";
    public const string Contabilizada = "CONTABILIZADA";
    public const string Cancelada = "CANCELADA";
}

public sealed record NovaPendencia(string Origem, string Tipo, string Referencia, DateOnly Periodo, string Descricao, decimal Valor);

public sealed record PendenciaContabil(long Id, long EmpresaId, string Origem, string Tipo, string Referencia, DateOnly Periodo, string Descricao, decimal Valor,
                                       string Situacao, long? LoteId, long? LancamentoId, string? Mensagem, DateTime CriadoEm);

public sealed record FiltroPendencias(string? Origem = null, DateOnly? Inicio = null, DateOnly? Fim = null, string? Situacao = SituacaoPendencia.Pendente, long? LoteId = null);

public sealed record LoteContabil(long Id, DateTime CriadoEm, int Quantidade, decimal Valor, DateTime? DesfeitoEm)
{
    public bool Desfeito => DesfeitoEm is not null;
}

public sealed record FalhaPendencia(string Descricao, IReadOnlyList<string> Erros);

public sealed record ResultadoLote(long? LoteId, int Contabilizadas, decimal Valor, IReadOnlyList<FalhaPendencia> Falhas)
{
    public string Resumo => $"{Contabilizadas} pendência(s) contabilizada(s)" + (Falhas.Count > 0 ? $"; {Falhas.Count} continuam pendentes." : ".");
}

public interface IRepositorioPendencias
{
    Task<ModoContabilizacao> ModoAsync(long empresaId, CancellationToken ct);
    Task GravarModoAsync(long empresaId, ModoContabilizacao modo, CancellationToken ct);
    /// <summary>Registra a pendência; false se já existe uma aberta para a mesma referência.</summary>
    Task<bool> RegistrarAsync(long empresaId, NovaPendencia pendencia, long usuarioId, CancellationToken ct);
    Task<IReadOnlyList<PendenciaContabil>> ListarAsync(long empresaId, FiltroPendencias filtro, CancellationToken ct);
    Task<PendenciaContabil?> ObterAsync(long empresaId, long id, CancellationToken ct);
    Task<long> CriarLoteAsync(long empresaId, long usuarioId, CancellationToken ct);
    Task MarcarAsync(long id, string situacao, long? loteId, long? lancamentoId, string? mensagem, CancellationToken ct);
    Task<IReadOnlyList<LoteContabil>> ListarLotesAsync(long empresaId, CancellationToken ct);
    Task<bool> LoteDesfeitoAsync(long empresaId, long loteId, CancellationToken ct);
    Task MarcarLoteDesfeitoAsync(long loteId, long usuarioId, CancellationToken ct);
}

public sealed record ResultadoProcessamento(long? LancamentoId, IReadOnlyList<string> Erros)
{
    public bool Sucesso => Erros.Count == 0;
    public static ResultadoProcessamento Ok(long? lancamentoId) => new(lancamentoId, []);
    public static ResultadoProcessamento Falha(params string[] erros) => new(null, erros);
    public static ResultadoProcessamento Falha(IReadOnlyList<string> erros) => new(null, erros);
    /// <summary>O que gerou a pendência não existe mais (nota excluída, baixa estornada): a pendência é cancelada.</summary>
    public static ResultadoProcessamento Cancelada(string motivo) => new(null, []) { Cancelamento = motivo };
    public string? Cancelamento { get; init; }
}

/// <summary>
/// Processa as pendências de uma origem com as mesmas regras de contabilização on-line do módulo (cada módulo registra o seu).
/// </summary>
public interface IProcessadorPendencia
{
    string Origem { get; }
    /// <summary>Linhas do lançamento que a pendência vai gerar (ou o motivo de não ser possível).</summary>
    Task<IReadOnlyList<string>> PreviaAsync(long empresaId, PendenciaContabil pendencia, long usuarioId, CancellationToken ct);
    Task<ResultadoProcessamento> ContabilizarAsync(long empresaId, PendenciaContabil pendencia, long usuarioId, CancellationToken ct);
    /// <summary>Estorna o lançamento da pendência contabilizada e devolve o documento à situação de não contabilizado.</summary>
    Task<Resultado> DesfazerAsync(long empresaId, PendenciaContabil pendencia, long usuarioId, CancellationToken ct);
}

/// <summary>
/// Contabilização em lote: guarda o tipo de contabilização da empresa, recebe as pendências dos módulos e as processa por período e
/// origem. O lançamento continua sendo gravado pelo <see cref="ServicoLancamento"/>, dentro de cada módulo (os processadores).
/// </summary>
public sealed class ServicoPendencias(IRepositorioPendencias repositorio, Autorizador autorizador)
{
    private readonly Dictionary<string, IProcessadorPendencia> _processadores = [];

    /// <summary>Cada módulo registra o processador da sua origem ao compor os seus serviços (idempotente).</summary>
    public void RegistrarProcessador(IProcessadorPendencia processador)
    {
        lock (_processadores) _processadores[processador.Origem] = processador;
    }

    public bool TemProcessador(string origem)
    {
        lock (_processadores) return _processadores.ContainsKey(origem);
    }

    // ------------------------------------------------------------------ tipo de contabilização

    public async Task<Resultado<ModoContabilizacao>> ModoAsync(long empresaId, long usuarioId, CancellationToken ct = default)
        => (await autorizador.ObterAsync(usuarioId, empresaId, ct)).TemAcesso
            ? Resultado<ModoContabilizacao>.Ok(await repositorio.ModoAsync(empresaId, ct))
            : Resultado<ModoContabilizacao>.Falha("Você não tem acesso a esta empresa.");

    /// <summary>Uso interno dos módulos (as permissões já foram conferidas pela operação que os chamou).</summary>
    public Task<ModoContabilizacao> ModoDaEmpresaAsync(long empresaId, CancellationToken ct = default) => repositorio.ModoAsync(empresaId, ct);

    public async Task<Resultado> GravarModoAsync(long empresaId, ModoContabilizacao modo, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.PlanoContasManter, ct) is { } negado) return Resultado.Falha(negado);
        await repositorio.GravarModoAsync(empresaId, modo, ct);
        return Resultado.Ok();
    }

    // ------------------------------------------------------------------ pendências

    /// <summary>Registra a pendência (chamado pelos módulos no lugar de gerar o lançamento). Devolve false se já havia uma aberta igual.</summary>
    public Task<bool> RegistrarAsync(long empresaId, NovaPendencia pendencia, long usuarioId, CancellationToken ct = default)
        => repositorio.RegistrarAsync(empresaId, pendencia, usuarioId, ct);

    public async Task<Resultado<IReadOnlyList<PendenciaContabil>>> ListarAsync(long empresaId, FiltroPendencias filtro, long usuarioId, CancellationToken ct = default)
        => (await autorizador.ObterAsync(usuarioId, empresaId, ct)).TemAcesso
            ? Resultado<IReadOnlyList<PendenciaContabil>>.Ok(await repositorio.ListarAsync(empresaId, filtro, ct))
            : Resultado<IReadOnlyList<PendenciaContabil>>.Falha("Você não tem acesso a esta empresa.");

    public async Task<Resultado<IReadOnlyList<LoteContabil>>> ListarLotesAsync(long empresaId, long usuarioId, CancellationToken ct = default)
        => (await autorizador.ObterAsync(usuarioId, empresaId, ct)).TemAcesso
            ? Resultado<IReadOnlyList<LoteContabil>>.Ok(await repositorio.ListarLotesAsync(empresaId, ct))
            : Resultado<IReadOnlyList<LoteContabil>>.Falha("Você não tem acesso a esta empresa.");

    private IProcessadorPendencia? Processador(string origem)
    {
        lock (_processadores) return _processadores.GetValueOrDefault(origem);
    }

    /// <summary>Prévia: as linhas do lançamento que cada pendência selecionada vai gerar.</summary>
    public async Task<Resultado<IReadOnlyList<string>>> PreviaAsync(long empresaId, IReadOnlyCollection<long> ids, long usuarioId, CancellationToken ct = default)
    {
        if (!(await autorizador.ObterAsync(usuarioId, empresaId, ct)).TemAcesso) return Resultado<IReadOnlyList<string>>.Falha("Você não tem acesso a esta empresa.");
        var linhas = new List<string>();
        foreach (var id in ids)
        {
            if (await repositorio.ObterAsync(empresaId, id, ct) is not { Situacao: SituacaoPendencia.Pendente } p) continue;
            linhas.Add($"{p.Descricao}:");
            if (Processador(p.Origem) is not { } proc) { linhas.Add("   Origem não disponível neste programa."); continue; }
            linhas.AddRange((await proc.PreviaAsync(empresaId, p, usuarioId, ct)).Select(l => "   " + l));
        }
        return linhas.Count == 0 ? Resultado<IReadOnlyList<string>>.Falha("Selecione ao menos uma pendência.") : Resultado<IReadOnlyList<string>>.Ok(linhas);
    }

    /// <summary>
    /// Contabiliza as pendências selecionadas (um lote). Cada uma passa pelas regras do seu módulo; as que não puderem ser
    /// contabilizadas continuam pendentes, com o motivo, e não impedem as outras.
    /// </summary>
    public async Task<Resultado<ResultadoLote>> ContabilizarAsync(long empresaId, IReadOnlyCollection<long> ids, long usuarioId, CancellationToken ct = default)
    {
        if (!(await autorizador.ObterAsync(usuarioId, empresaId, ct)).TemAcesso) return Resultado<ResultadoLote>.Falha("Você não tem acesso a esta empresa.");
        if (ids.Count == 0) return Resultado<ResultadoLote>.Falha("Selecione ao menos uma pendência.");
        var pendencias = new List<PendenciaContabil>();
        foreach (var id in ids)
            if (await repositorio.ObterAsync(empresaId, id, ct) is { Situacao: SituacaoPendencia.Pendente } p) pendencias.Add(p);
        if (pendencias.Count == 0) return Resultado<ResultadoLote>.Falha("As pendências selecionadas já foram contabilizadas ou canceladas.");

        long? lote = null;
        var falhas = new List<FalhaPendencia>();
        var ok = 0;
        var total = 0m;
        foreach (var p in pendencias.OrderBy(p => p.Periodo).ThenBy(p => p.Id))
        {
            if (Processador(p.Origem) is not { } proc)
            {
                falhas.Add(new FalhaPendencia(p.Descricao, ["Esta origem não está disponível neste programa."]));
                continue;
            }
            ResultadoProcessamento r;
            try { r = await proc.ContabilizarAsync(empresaId, p, usuarioId, ct); }
            catch (ViolacaoRegraException ex) { r = ResultadoProcessamento.Falha(ex.Message); }
            if (r.Cancelamento is { } motivo)
            {
                await repositorio.MarcarAsync(p.Id, SituacaoPendencia.Cancelada, null, null, motivo, ct);
                falhas.Add(new FalhaPendencia(p.Descricao, [$"Cancelada: {motivo}"]));
            }
            else if (!r.Sucesso)
            {
                await repositorio.MarcarAsync(p.Id, SituacaoPendencia.Pendente, null, null, string.Join(" ", r.Erros), ct);
                falhas.Add(new FalhaPendencia(p.Descricao, r.Erros));
            }
            else
            {
                lote ??= await repositorio.CriarLoteAsync(empresaId, usuarioId, ct);
                await repositorio.MarcarAsync(p.Id, SituacaoPendencia.Contabilizada, lote, r.LancamentoId, null, ct);
                ok++;
                total += p.Valor;
            }
        }
        return Resultado<ResultadoLote>.Ok(new ResultadoLote(lote, ok, total, falhas));
    }

    /// <summary>
    /// Desfaz o lote: estorna o lançamento de cada pendência contabilizada nele e a devolve à lista de pendentes (pode ser
    /// contabilizada de novo). Se algum estorno falhar (ex.: período fechado), o restante é desfeito e o lote continua parcialmente ativo.
    /// </summary>
    public async Task<Resultado<ResultadoLote>> DesfazerLoteAsync(long empresaId, long loteId, long usuarioId, CancellationToken ct = default)
    {
        if (!(await autorizador.ObterAsync(usuarioId, empresaId, ct)).TemAcesso) return Resultado<ResultadoLote>.Falha("Você não tem acesso a esta empresa.");
        if (await repositorio.LoteDesfeitoAsync(empresaId, loteId, ct)) return Resultado<ResultadoLote>.Falha("Este lote já foi desfeito.");
        var itens = await repositorio.ListarAsync(empresaId, new FiltroPendencias(Situacao: SituacaoPendencia.Contabilizada, LoteId: loteId), ct);
        if (itens.Count == 0) return Resultado<ResultadoLote>.Falha("Lote não encontrado.");
        var falhas = new List<FalhaPendencia>();
        var ok = 0;
        var total = 0m;
        foreach (var p in itens)
        {
            if (Processador(p.Origem) is not { } proc) { falhas.Add(new FalhaPendencia(p.Descricao, ["Esta origem não está disponível neste programa."])); continue; }
            var r = await proc.DesfazerAsync(empresaId, p, usuarioId, ct);
            if (!r.Sucesso) { falhas.Add(new FalhaPendencia(p.Descricao, r.Erros)); continue; }
            await repositorio.MarcarAsync(p.Id, SituacaoPendencia.Pendente, loteId, null, $"Lote {loteId} desfeito.", ct);
            ok++;
            total += p.Valor;
        }
        if (falhas.Count == 0) await repositorio.MarcarLoteDesfeitoAsync(loteId, usuarioId, ct);
        return Resultado<ResultadoLote>.Ok(new ResultadoLote(loteId, ok, total, falhas));
    }
}
