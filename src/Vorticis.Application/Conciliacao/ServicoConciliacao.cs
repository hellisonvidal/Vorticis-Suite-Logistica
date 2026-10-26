using Vorticis.Application.Contabil;
using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Application.Conciliacao;

/// <summary>Partida de uma conta, para conciliar (lançamentos efetivados que não foram estornados).</summary>
public sealed record PartidaConciliavel(long PartidaId, long LancamentoId, long Numero, DateOnly Data, IndicadorDC DC, decimal Valor,
                                        string Historico, string? Documento, long? ConciliacaoId,
                                        long ContaId = 0, string? ContaCodigo = null, string? ContaDescricao = null)
{
    public bool Conciliada => ConciliacaoId is not null;
    public decimal ValorComSinal => DC == IndicadorDC.D ? Valor : -Valor;
}

/// <summary>Resultado da conciliação automática.</summary>
public sealed record ResultadoConciliacao(int Conciliacoes, int Partidas, int DocumentosEmAberto);

public interface IRepositorioConciliacao
{
    /// <summary>
    /// Partidas até a data (efetivadas; sem os lançamentos estornados e seus estornos), da conta ou de todas (<paramref name="contaId"/> nulo);
    /// com <paramref name="somenteComDocumento"/>, só as que têm informação na coluna documento.
    /// </summary>
    Task<IReadOnlyList<PartidaConciliavel>> ListarAsync(long empresaId, long? contaId, DateOnly ate, bool somenteComDocumento, CancellationToken ct);
    Task<long> CriarAsync(long empresaId, long contaId, string? documento, bool automatica, IReadOnlyList<long> partidas, long usuarioId, CancellationToken ct);
    Task<bool> DesfazerAsync(long empresaId, long conciliacaoId, CancellationToken ct);
}

/// <summary>
/// Lançamentos › Conciliações. Numa conta (fornecedores, clientes, adiantamentos, bancos...), partidas que se compensam
/// — débitos = créditos — são conciliadas. A automática agrupa pelo nº do documento (ex.: a NF lançada a crédito na compra
/// e o pagamento da mesma NF a débito); a manual concilia as partidas escolhidas. O que sobra em aberto é a composição
/// do saldo da conta.
/// </summary>
public sealed class ServicoConciliacao(IRepositorioConciliacao repositorio, IRepositorioPlanoContas plano, Autorizador autorizador)
{
    private async Task<ContaDetalhe?> ContaAsync(long empresaId, string conta, CancellationToken ct)
    {
        var t = conta.Trim();
        return (await plano.ListarAsync(empresaId, ct)).FirstOrDefault(c => c.Analitica && (c.Codigo == t || c.CodigoReduzido?.ToString() == t));
    }

    /// <summary>
    /// Todas as partidas com informação na coluna documento — conciliadas ou não, casando ou não —, da conta informada ou de
    /// todas as contas quando <paramref name="conta"/> vem vazia, para o usuário ver e conciliar.
    /// </summary>
    public async Task<Resultado<IReadOnlyList<PartidaConciliavel>>> ListarComDocumentoAsync(long empresaId, string? conta, DateOnly ate, long usuarioId,
                                                                                           CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<PartidaConciliavel>>(usuarioId, empresaId, Permissao.LancamentoIncluir, ct) is { } negado) return negado;
        long? contaId = null;
        if (!string.IsNullOrWhiteSpace(conta))
        {
            if (await ContaAsync(empresaId, conta, ct) is not { } c) return Resultado<IReadOnlyList<PartidaConciliavel>>.Falha($"Conta analítica {conta} não encontrada.");
            contaId = c.Id;
        }
        return Resultado<IReadOnlyList<PartidaConciliavel>>.Ok(await repositorio.ListarAsync(empresaId, contaId, ate, somenteComDocumento: true, ct));
    }

    public async Task<Resultado<IReadOnlyList<PartidaConciliavel>>> ListarAsync(long empresaId, string conta, DateOnly ate, long usuarioId,
                                                                               CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<PartidaConciliavel>>(usuarioId, empresaId, Permissao.LancamentoIncluir, ct) is { } negado) return negado;
        if (await ContaAsync(empresaId, conta, ct) is not { } c) return Resultado<IReadOnlyList<PartidaConciliavel>>.Falha($"Conta analítica {conta} não encontrada.");
        return Resultado<IReadOnlyList<PartidaConciliavel>>.Ok(await repositorio.ListarAsync(empresaId, c.Id, ate, somenteComDocumento: false, ct));
    }

    /// <summary>
    /// Concilia, por nº do documento, os grupos de partidas em aberto cujos débitos e créditos se anulam. Com a conta vazia,
    /// age em todas as contas (o documento é comparado dentro de cada conta).
    /// </summary>
    public async Task<Resultado<ResultadoConciliacao>> ConciliarAutomaticoAsync(long empresaId, string? conta, DateOnly ate, long usuarioId,
                                                                               CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<ResultadoConciliacao>(usuarioId, empresaId, Permissao.LancamentoIncluir, ct) is { } negado) return negado;
        long? contaId = null;
        if (!string.IsNullOrWhiteSpace(conta))
        {
            if (await ContaAsync(empresaId, conta, ct) is not { } c) return Resultado<ResultadoConciliacao>.Falha($"Conta analítica {conta} não encontrada.");
            contaId = c.Id;
        }
        var abertas = (await repositorio.ListarAsync(empresaId, contaId, ate, somenteComDocumento: true, ct)).Where(p => !p.Conciliada).ToList();
        int conciliacoes = 0, partidas = 0, emAberto = 0;
        foreach (var g in abertas.GroupBy(p => (p.ContaId, Doc: Normalizar(p.Documento!))))
        {
            var lista = g.ToList();
            if (lista.Count >= 2 && lista.Any(p => p.DC == IndicadorDC.D) && lista.Any(p => p.DC == IndicadorDC.C) && lista.Sum(p => p.ValorComSinal) == 0)
            {
                await repositorio.CriarAsync(empresaId, g.Key.ContaId, lista[0].Documento!.Trim(), true, lista.Select(p => p.PartidaId).ToList(), usuarioId, ct);
                conciliacoes++;
                partidas += lista.Count;
            }
            else emAberto++;
        }
        return Resultado<ResultadoConciliacao>.Ok(new ResultadoConciliacao(conciliacoes, partidas, emAberto));
    }

    /// <summary>Concilia as partidas escolhidas (precisam ser de uma mesma conta, estar em aberto e somar zero).</summary>
    public async Task<Resultado<long>> ConciliarManualAsync(long empresaId, string? conta, IReadOnlyList<long> partidaIds, long usuarioId,
                                                           CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.LancamentoIncluir, ct) is { } negado) return negado;
        long? contaId = null;
        if (!string.IsNullOrWhiteSpace(conta))
        {
            if (await ContaAsync(empresaId, conta, ct) is not { } c) return Resultado<long>.Falha($"Conta analítica {conta} não encontrada.");
            contaId = c.Id;
        }
        var todas = (await repositorio.ListarAsync(empresaId, contaId, DateOnly.MaxValue, somenteComDocumento: false, ct)).ToDictionary(p => p.PartidaId);
        var escolhidas = partidaIds.Distinct().Select(id => todas.GetValueOrDefault(id)).ToList();
        if (escolhidas.Count < 2 || escolhidas.Any(p => p is null)) return Resultado<long>.Falha("Escolha ao menos duas partidas" + (contaId is null ? "." : " desta conta."));
        if (escolhidas.Select(p => p!.ContaId).Distinct().Count() > 1) return Resultado<long>.Falha("As partidas escolhidas são de contas diferentes; concilie uma conta por vez.");
        if (escolhidas.Any(p => p!.Conciliada)) return Resultado<long>.Falha("Há partidas já conciliadas na seleção.");
        var diferenca = escolhidas.Sum(p => p!.ValorComSinal);
        if (diferenca != 0) return Resultado<long>.Falha($"Débitos e créditos selecionados não se anulam (diferença de {Formato.Moeda(Math.Abs(diferenca))}).");
        var doc = escolhidas.Select(p => p!.Documento).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d));
        return Resultado<long>.Ok(await repositorio.CriarAsync(empresaId, escolhidas[0]!.ContaId, doc, false, escolhidas.Select(p => p!.PartidaId).ToList(), usuarioId, ct));
    }

    public async Task<Resultado> DesfazerAsync(long empresaId, long conciliacaoId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.LancamentoIncluir, ct) is { } negado) return Resultado.Falha(negado);
        return await repositorio.DesfazerAsync(empresaId, conciliacaoId, ct) ? Resultado.Ok() : Resultado.Falha("Conciliação não encontrada.");
    }

    /// <summary>Documento comparado sem espaços, zeros à esquerda e diferença de maiúsculas ("NF 000123" = "nf123").</summary>
    public static string Normalizar(string documento)
    {
        var t = new string(documento.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        var letras = new string(t.TakeWhile(char.IsLetter).ToArray());
        var resto = t[letras.Length..].TrimStart('0');
        return letras + resto;
    }
}
