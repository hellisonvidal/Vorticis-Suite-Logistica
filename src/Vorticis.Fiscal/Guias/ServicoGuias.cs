using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Comum;
using Vorticis.Fiscal.Lucro;
using Vorticis.Fiscal.Retencoes;
using Vorticis.Fiscal.Simples;

namespace Vorticis.Fiscal.Guias;

public interface IRepositorioGuias
{
    Task<IReadOnlyList<Guia>> ListarAsync(long empresaId, FiltroGuias filtro, CancellationToken ct);

    /// <summary>
    /// Substitui as guias geradas e ainda abertas com competência no intervalo pelas informadas; guias já pagas são preservadas
    /// (a mesma referência não é gerada de novo). Devolve quantas ficaram.
    /// </summary>
    Task<int> SubstituirGeradasAsync(long empresaId, DateOnly competenciaInicio, DateOnly competenciaFim, IReadOnlyList<NovaGuia> guias, long usuarioId, CancellationToken ct);
    Task<long> GravarManualAsync(long empresaId, NovaGuia guia, long usuarioId, CancellationToken ct);
    Task<bool> MarcarPagaAsync(long empresaId, long id, DateOnly data, decimal valorPago, CancellationToken ct);
    Task<bool> ReabrirAsync(long empresaId, long id, CancellationToken ct);
    Task<bool> ExcluirAsync(long empresaId, long id, CancellationToken ct);
}

/// <summary>Guias a pagar por competência e vencimento: DAS, DARF com código de receita, GPS/DCTFWeb (informativo) e ISS.</summary>
public sealed class ServicoGuias(IRepositorioGuias repositorio, IRepositorioSimples simples, IRepositorioLucro lucro, IRepositorioRetencoes retencoes,
                                 ServicoRetencoes servicoRetencoes, Autorizador autorizador)
{
    /// <summary>
    /// Gera as guias das competências do intervalo a partir das apurações salvas (Simples, Lucro Presumido/Real, PIS/COFINS) e das retenções
    /// efetuadas. Pode ser repetida: guias abertas são refeitas, as pagas ficam como estão.
    /// </summary>
    public async Task<Resultado<int>> GerarAsync(long empresaId, DateOnly competenciaInicio, DateOnly competenciaFim, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<int>(usuarioId, empresaId, Permissao.FiscalRetencoesManter, ct) is { } negado) return negado;
        var ini = new DateOnly(competenciaInicio.Year, competenciaInicio.Month, 1);
        var fim = new DateOnly(competenciaFim.Year, competenciaFim.Month, 1);
        if (fim < ini) return Resultado<int>.Falha("Competência final anterior à inicial.");
        var p = await servicoRetencoes.ParametrosAsync(ini, ct);

        var guias = new List<NovaGuia>();
        foreach (var ano in Enumerable.Range(ini.Year, fim.Year - ini.Year + 1))
        {
            guias.AddRange((await simples.ListarApuracoesAsync(empresaId, ano, ct)).Where(a => a.Competencia >= ini && a.Competencia <= fim).SelectMany(GeradorGuias.DoSimples));
            guias.AddRange((await lucro.ListarPisCofinsAsync(empresaId, ano, ct)).Where(a => a.Resultado.Competencia >= ini && a.Resultado.Competencia <= fim)
                                       .SelectMany(a => GeradorGuias.DePisCofins(a, p)));
            guias.AddRange((await lucro.ListarApuracoesAsync(empresaId, ano, ct))
                .Where(a => new DateOnly(a.Resultado.Fim.Year, a.Resultado.Fim.Month, 1) is var comp && comp >= ini && comp <= fim).SelectMany(a => GeradorGuias.DoLucro(a, p)));
        }
        var retidas = await retencoes.ListarAsync(empresaId, ini, fim.AddMonths(1).AddDays(-1), SentidoRetencao.Efetuada, null, ct);
        guias.AddRange(GeradorGuias.DasRetencoes(retidas, p));
        return Resultado<int>.Ok(await repositorio.SubstituirGeradasAsync(empresaId, ini, fim, guias, usuarioId, ct));
    }

    public async Task<Resultado<IReadOnlyList<Guia>>> ListarAsync(long empresaId, FiltroGuias filtro, long usuarioId, CancellationToken ct = default)
    {
        var perm = await autorizador.ObterAsync(usuarioId, empresaId, ct);
        if (!perm.Pode(Permissao.FiscalRelatorios) && !perm.Pode(Permissao.FiscalRetencoesManter))
            return Resultado<IReadOnlyList<Guia>>.Falha(await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FiscalRelatorios, ct) ?? "Acesso negado.");
        return Resultado<IReadOnlyList<Guia>>.Ok(await repositorio.ListarAsync(empresaId, filtro, ct));
    }

    public async Task<Resultado<long>> GravarManualAsync(long empresaId, NovaGuia g, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.FiscalRetencoesManter, ct) is { } negado) return negado;
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(g.Tributo)) erros.Add("Informe o tributo.");
        if (string.IsNullOrWhiteSpace(g.Descricao)) erros.Add("Informe a descrição.");
        if (g.Valor <= 0 || !Dinheiro.TemNoMaximoDuasCasas(g.Valor)) erros.Add("Informe um valor positivo com até 2 casas decimais.");
        if (g.Documento == DocumentoGuia.Darf && string.IsNullOrWhiteSpace(g.CodigoReceita)) erros.Add("DARF exige o código de receita.");
        if (erros.Count > 0) return Resultado<long>.Falha(erros);
        try
        {
            return Resultado<long>.Ok(await repositorio.GravarManualAsync(empresaId, g with
            {
                Tributo = g.Tributo.Trim().ToUpperInvariant(), Descricao = g.Descricao.Trim(), Competencia = new DateOnly(g.Competencia.Year, g.Competencia.Month, 1),
                Origem = "MANUAL", Referencia = null,
            }, usuarioId, ct));
        }
        catch (ViolacaoRegraException ex) { return Resultado<long>.Falha(ex.Message); }
    }

    public async Task<Resultado> MarcarPagaAsync(long empresaId, long id, DateOnly data, decimal valorPago, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FiscalRetencoesManter, ct) is { } negado) return Resultado.Falha(negado);
        if (valorPago <= 0) return Resultado.Falha("Informe o valor pago.");
        return await repositorio.MarcarPagaAsync(empresaId, id, data, valorPago, ct) ? Resultado.Ok() : Resultado.Falha("Guia não encontrada.");
    }

    public async Task<Resultado> ReabrirAsync(long empresaId, long id, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FiscalRetencoesManter, ct) is { } negado) return Resultado.Falha(negado);
        return await repositorio.ReabrirAsync(empresaId, id, ct) ? Resultado.Ok() : Resultado.Falha("Guia não encontrada.");
    }

    public async Task<Resultado> ExcluirAsync(long empresaId, long id, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FiscalRetencoesManter, ct) is { } negado) return Resultado.Falha(negado);
        return await repositorio.ExcluirAsync(empresaId, id, ct) ? Resultado.Ok() : Resultado.Falha("Guia não encontrada ou não é manual.");
    }
}
