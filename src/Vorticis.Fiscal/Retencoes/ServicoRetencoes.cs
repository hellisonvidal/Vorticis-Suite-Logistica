using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Comum;
using Vorticis.Fiscal.Documentos;

namespace Vorticis.Fiscal.Retencoes;

public interface IRepositorioRetencoes
{
    Task<IReadOnlyList<Retencao>> ListarAsync(long empresaId, DateOnly inicio, DateOnly fim, SentidoRetencao? sentido, TributoRetido? tributo, CancellationToken ct);

    /// <summary>Substitui as retenções de origem NOTA dos documentos do período pelas informadas (idempotente).</summary>
    Task<int> RegerarDeNotasAsync(long empresaId, DateOnly inicio, DateOnly fim, IReadOnlyList<NovaRetencao> retencoes, long usuarioId, CancellationToken ct);
    Task<long> GravarManualAsync(long empresaId, NovaRetencao retencao, long? participanteId, long usuarioId, CancellationToken ct);
    Task ExcluirAsync(long empresaId, long id, CancellationToken ct);
    Task<decimal> SomarAsync(long empresaId, SentidoRetencao sentido, TributoRetido tributo, DateOnly inicio, DateOnly fim, CancellationToken ct);
}

/// <summary>
/// Controle de retenções (IRRF, CSRF de PIS/COFINS/CSLL, INSS e ISS) a partir das notas e lançadas à mão. As sofridas viram crédito nas
/// apurações; as efetuadas viram guias a recolher.
/// </summary>
public sealed class ServicoRetencoes(IRepositorioRetencoes repositorio, IRepositorioDocumentos documentos, IParametrosFiscais parametros, Autorizador autorizador)
{
    public async Task<ParametrosGuias> ParametrosAsync(DateOnly data, CancellationToken ct = default) =>
        ParametrosGuias.Carregar(await parametros.ObterJsonAsync(ChavesParametro.Retencoes, data, ct));

    public async Task<Resultado<IReadOnlyList<Retencao>>> ListarAsync(long empresaId, DateOnly inicio, DateOnly fim, SentidoRetencao? sentido, TributoRetido? tributo,
                                                                    long usuarioId, CancellationToken ct = default)
    {
        var p = await autorizador.ObterAsync(usuarioId, empresaId, ct);
        if (!p.Pode(Permissao.FiscalRelatorios) && !p.Pode(Permissao.FiscalRetencoesManter) && !p.Pode(Permissao.FiscalApurar))
            return await autorizador.NegarAsync<IReadOnlyList<Retencao>>(usuarioId, empresaId, Permissao.FiscalRelatorios, ct) ?? Resultado<IReadOnlyList<Retencao>>.Falha("Acesso negado.");
        if (fim < inicio) return Resultado<IReadOnlyList<Retencao>>.Falha("Data final anterior à inicial.");
        return Resultado<IReadOnlyList<Retencao>>.Ok(await repositorio.ListarAsync(empresaId, inicio, fim, sentido, tributo, ct));
    }

    /// <summary>Gera (ou regera) as retenções informadas nas notas do período; devolve quantas ficaram registradas.</summary>
    public async Task<Resultado<int>> GerarDasNotasAsync(long empresaId, DateOnly inicio, DateOnly fim, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<int>(usuarioId, empresaId, Permissao.FiscalRetencoesManter, ct) is { } negado) return negado;
        if (fim < inicio) return Resultado<int>.Falha("Data final anterior à inicial.");
        var p = await ParametrosAsync(inicio, ct);
        var docs = await documentos.ListarAsync(empresaId, new FiltroDocumentos(inicio, fim, Situacao: SituacaoDocumento.Autorizada, Limite: 100000), ct);
        var novas = docs.SelectMany(d => GeradorRetencoes.DeDocumento(d, p)).ToList();
        return Resultado<int>.Ok(await repositorio.RegerarDeNotasAsync(empresaId, inicio, fim, novas, usuarioId, ct));
    }

    public async Task<Resultado<long>> GravarManualAsync(long empresaId, NovaRetencao r, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.FiscalRetencoesManter, ct) is { } negado) return negado;
        var erros = new List<string>();
        if (r.Valor <= 0) erros.Add("Informe o valor retido.");
        if (!Dinheiro.TemNoMaximoDuasCasas(r.Valor) || !Dinheiro.TemNoMaximoDuasCasas(r.BaseCalculo)) erros.Add("Valores com no máximo 2 casas decimais.");
        if (r.BaseCalculo < 0 || r.Aliquota < 0) erros.Add("Base e alíquota não podem ser negativas.");
        if (r.Sentido == SentidoRetencao.Efetuada && r.Tributo != TributoRetido.ISS && string.IsNullOrWhiteSpace(r.CodigoReceita))
        {
            var p = await ParametrosAsync(r.Competencia, ct);
            r = r with { CodigoReceita = r.Tributo switch { TributoRetido.IRRF => p.CodigoIrrfServicos, TributoRetido.INSS => p.CodigoInss, _ => p.CodigoCsrf } };
        }
        if (erros.Count > 0) return Resultado<long>.Falha(erros);
        var comp = new DateOnly(r.Competencia.Year, r.Competencia.Month, 1);
        try { return Resultado<long>.Ok(await repositorio.GravarManualAsync(empresaId, r with { Competencia = comp, Origem = "MANUAL" }, r.ParticipanteId, usuarioId, ct)); }
        catch (ViolacaoRegraException ex) { return Resultado<long>.Falha(ex.Message); }
    }

    public async Task<Resultado> ExcluirAsync(long empresaId, long id, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FiscalRetencoesManter, ct) is { } negado) return Resultado.Falha(negado);
        await repositorio.ExcluirAsync(empresaId, id, ct);
        return Resultado.Ok();
    }
}
