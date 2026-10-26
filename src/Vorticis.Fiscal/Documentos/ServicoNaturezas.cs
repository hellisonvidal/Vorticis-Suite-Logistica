using Npgsql;
using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra.Postgres;

namespace Vorticis.Fiscal.Documentos;

/// <summary>Cadastro das naturezas das notas de entrada (CFOP de saída → CFOP de entrada). Manter exige "Importar XML e digitar notas fiscais".</summary>
public sealed class ServicoNaturezas(NpgsqlDataSource ds, Autorizador autorizador)
{
    private readonly BancoModulo _db = new(ds);

    private async Task<bool> PodeVerAsync(long usuario, long empresa, CancellationToken ct) => (await autorizador.ObterAsync(usuario, empresa, ct)).TemAcesso;

    public async Task<List<NaturezaEntrada>> ListarAsync(long empresa, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await RegrasAsync(empresa, false, ct) : [];

    /// <summary>Naturezas da empresa (só as ativas, para aplicar nas notas).</summary>
    public Task<List<NaturezaEntrada>> RegrasAsync(long empresa, bool soAtivas = true, CancellationToken ct = default) => _db.ListarAsync("""
        SELECT id, codigo, descricao, cfop_saida, cfop_entrada, ativa FROM fiscal.natureza_entrada
         WHERE empresa_id = @e AND (ativa OR NOT @a) ORDER BY codigo
        """, r => new NaturezaEntrada(r.GetInt64(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetBoolean(5)), ct, ("e", empresa), ("a", soAtivas));

    public async Task<Resultado<long>> GravarAsync(long empresa, NaturezaEntrada n, long usuario, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuario, empresa, Permissao.FiscalDocumentosImportar, ct) is { } neg) return Resultado<long>.Falha(neg);
        n = n with { Descricao = n.Descricao?.Trim() ?? "", CfopSaida = new string([.. (n.CfopSaida ?? "").Where(char.IsAsciiDigit)]),
                     CfopEntrada = new string([.. (n.CfopEntrada ?? "").Where(char.IsAsciiDigit)]) };
        if (RegrasNaturezas.Validar(n) is { Count: > 0 } erros) return Resultado<long>.Falha(erros);
        try
        {
            (string, object?)[] p = [("e", empresa), ("id", n.Id), ("c", n.Codigo), ("d", n.Descricao), ("s", n.CfopSaida), ("en", n.CfopEntrada), ("a", n.Ativa)];
            return Resultado<long>.Ok(n.Id == 0
                ? await _db.EscalarAsync<long>("""
                    INSERT INTO fiscal.natureza_entrada (empresa_id, codigo, descricao, cfop_saida, cfop_entrada, ativa) VALUES (@e, @c, @d, @s, @en, @a) RETURNING id
                    """, ct, p)
                : await _db.EscalarAsync<long?>("""
                    UPDATE fiscal.natureza_entrada SET codigo = @c, descricao = @d, cfop_saida = @s, cfop_entrada = @en, ativa = @a WHERE id = @id AND empresa_id = @e RETURNING id
                    """, ct, p) ?? throw new ViolacaoRegraException("Natureza não encontrada."));
        }
        catch (ViolacaoRegraException ex)
        {
            return Resultado<long>.Falha(ex.Message.StartsWith("Registro repetido", StringComparison.Ordinal)
                ? "Já existe uma natureza com este código ou com este CFOP de saída." : ex.Message);
        }
    }

    public async Task<Resultado> ExcluirAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuario, empresa, Permissao.FiscalDocumentosImportar, ct) is { } neg) return Resultado.Falha(neg);
        await _db.ExecutarAsync("DELETE FROM fiscal.natureza_entrada WHERE id = @id AND empresa_id = @e", ct, ("id", id), ("e", empresa));
        return Resultado.Ok();
    }

    /// <summary>Inclui as naturezas sugeridas cujo CFOP de saída ainda não está cadastrado; devolve quantas entraram.</summary>
    public async Task<Resultado<int>> IncluirSugeridasAsync(long empresa, long usuario, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuario, empresa, Permissao.FiscalDocumentosImportar, ct) is { } neg) return Resultado<int>.Falha(neg);
        var atuais = await RegrasAsync(empresa, false, ct);
        var codigo = atuais.Count == 0 ? 0 : atuais.Max(n => n.Codigo);
        var novas = 0;
        foreach (var s in RegrasNaturezas.Sugeridas.Where(s => atuais.All(a => a.CfopSaida != s.CfopSaida)))
        {
            await _db.ExecutarAsync("INSERT INTO fiscal.natureza_entrada (empresa_id, codigo, descricao, cfop_saida, cfop_entrada) VALUES (@e, @c, @d, @s, @en)", ct,
                                    ("e", empresa), ("c", ++codigo), ("d", s.Descricao), ("s", s.CfopSaida), ("en", s.CfopEntrada));
            novas++;
        }
        return Resultado<int>.Ok(novas);
    }
}
