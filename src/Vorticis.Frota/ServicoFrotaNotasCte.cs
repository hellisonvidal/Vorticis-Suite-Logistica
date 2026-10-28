using Vorticis.Domain.Comum;
using Vorticis.Infra.Postgres;
using P = Vorticis.Domain.Seguranca.Permissao;

namespace Vorticis.Frota;

/// <summary>NF-e transportadas pelo CT-e, anexadas pelo XML (FROTA_TMS): lista, gravação junto com o CT-e e as chaves no campo do CT-e.</summary>
public sealed partial class ServicoFrota
{
    public async Task<List<NotaTransportada>> NotasCteAsync(long empresa, long cte, long usuario, CancellationToken ct = default)
        => !await PodeVerAsync(usuario, empresa, ct) ? [] : await _db.ListarAsync("""
            SELECT n.chave, n.numero, n.serie, n.emissao, n.emitente_documento, n.emitente_nome, n.origem_uf, n.origem_ibge, n.origem_municipio,
                   n.destinatario_documento, n.destinatario_nome, n.destino_uf, n.destino_ibge, n.destino_municipio, n.valor, n.peso_bruto, n.peso_liquido,
                   n.volumes, n.produto
              FROM tms.cte_nfe n JOIN tms.cte c ON c.id = n.cte_id WHERE c.empresa_id = @e AND n.cte_id = @c ORDER BY n.numero, n.chave
            """, r => new NotaTransportada(r.GetString(0), r.GetInt32(1), r.GetInt16(2), r.GetFieldValue<DateOnly>(3), r.Texto(4), r.GetString(5),
                                           r.Texto(7) is { } oi ? new ParadaRota(r.Texto(6) ?? "", oi, r.Texto(8) ?? "") : null, r.Texto(9), r.GetString(10),
                                           r.Texto(12) is { } di ? new ParadaRota(r.Texto(11) ?? "", di, r.Texto(13) ?? "") : null,
                                           r.GetDecimal(14), r.GetDecimal(15), r.GetDecimal(16), r.GetInt32(17), r.Texto(18)), ct, ("e", empresa), ("c", cte));

    /// <summary>
    /// Substitui as notas do CT-e (só digitado: depois de emitido, as notas fazem parte do documento autorizado) e grava as chaves no CT-e.
    /// Uma NF-e não pode estar em dois CT-e ativos (não cancelados).
    /// </summary>
    public async Task<Resultado> GravarNotasCteAsync(long empresa, long cte, IReadOnlyList<NotaTransportada> notas, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaTms, ct) is { } n) return Resultado.Falha(n);
        var situacao = await _db.EscalarAsync<string>("SELECT situacao FROM tms.cte WHERE id = @c AND empresa_id = @e", ct, ("c", cte), ("e", empresa));
        if (situacao is null) return Resultado.Falha("CT-e não encontrado.");
        if (situacao != "DIGITADO") return Resultado.Falha("As notas só podem mudar enquanto o CT-e está digitado.");
        if (notas.GroupBy(x => x.Chave).FirstOrDefault(g => g.Count() > 1) is { } repetida) return Resultado.Falha($"A NF-e {repetida.Key} aparece duas vezes.");
        var chaves = notas.Select(x => x.Chave).ToArray();
        var outros = await _db.ListarAsync("""
            SELECT n.chave, c.serie || '-' || c.numero FROM tms.cte_nfe n JOIN tms.cte c ON c.id = n.cte_id
             WHERE c.empresa_id = @e AND c.id <> @c AND c.situacao <> 'CANCELADO' AND n.chave = ANY(@k)
            """, r => $"A NF-e nº {notas.First(x => x.Chave == r.GetString(0)).Numero} já está no CT-e {r.GetString(1)}.", ct, ("e", empresa), ("c", cte), ("k", chaves));
        if (outros.Count > 0) return Resultado.Falha(outros);
        await _db.ExecutarAsync("DELETE FROM tms.cte_nfe WHERE cte_id = @c", ct, ("c", cte));
        foreach (var x in notas)
            await _db.ExecutarAsync("""
                INSERT INTO tms.cte_nfe (cte_id, chave, numero, serie, emissao, emitente_documento, emitente_nome, origem_uf, origem_ibge, origem_municipio,
                       destinatario_documento, destinatario_nome, destino_uf, destino_ibge, destino_municipio, valor, peso_bruto, peso_liquido, volumes, produto)
                VALUES (@c, @k, @n, @s, @em, @ed, @en, @ou, @oi, @om, @dd, @dn, @du, @di, @dm, @v, @pb, @pl, @vol, @p)
                """, ct, ("c", cte), ("k", x.Chave), ("n", x.Numero), ("s", (short)x.Serie), ("em", x.Emissao), ("ed", x.EmitenteDocumento),
                ("en", Cortar(x.EmitenteNome, 150)), ("ou", x.Origem?.Uf), ("oi", x.Origem?.MunicipioIbge), ("om", Cortar(x.Origem?.Municipio, 100)),
                ("dd", x.DestinatarioDocumento), ("dn", Cortar(x.DestinatarioNome, 150)), ("du", x.Destino?.Uf), ("di", x.Destino?.MunicipioIbge),
                ("dm", Cortar(x.Destino?.Municipio, 100)), ("v", x.Valor), ("pb", x.PesoBruto), ("pl", x.PesoLiquido), ("vol", x.Volumes), ("p", Cortar(x.Produto, 120)));
        // as chaves digitadas à mão continuam; as das notas anexadas são acrescentadas
        var atuais = RegrasTms.ChavesNfe(await _db.EscalarAsync<string>("SELECT chaves_nfe FROM tms.cte WHERE id = @c", ct, ("c", cte)));
        var todas = atuais.Concat(chaves).Distinct().ToList();
        await _db.ExecutarAsync("UPDATE tms.cte SET chaves_nfe = @k WHERE id = @c", ct, ("k", todas.Count == 0 ? null : string.Join("\n", todas)), ("c", cte));
        return Resultado.Ok();
    }

    private static string? Cortar(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}
