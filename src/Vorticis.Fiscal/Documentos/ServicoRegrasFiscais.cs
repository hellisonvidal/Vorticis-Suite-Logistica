using Npgsql;
using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra.Postgres;

namespace Vorticis.Fiscal.Documentos;

/// <summary>Cadastro das regras fiscais (de/para de CFOP de entrada e de saída). Manter exige "Importar XML e digitar notas fiscais".</summary>
public sealed class ServicoRegrasFiscais(NpgsqlDataSource ds, Autorizador autorizador)
{
    private readonly BancoModulo _db = new(ds);

    private async Task<bool> PodeVerAsync(long usuario, long empresa, CancellationToken ct) => (await autorizador.ObterAsync(usuario, empresa, ct)).TemAcesso;

    public async Task<List<RegraFiscal>> ListarAsync(long empresa, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await RegrasAsync(empresa, false, ct) : [];

    /// <summary>Regras da empresa (só as ativas, para aplicar nas notas).</summary>
    public Task<List<RegraFiscal>> RegrasAsync(long empresa, bool soAtivas = true, CancellationToken ct = default) => _db.ListarAsync("""
        SELECT id, codigo, descricao, operacao, cfop_origem, cfop_destino, cst_icms, cst_pis, cst_cofins, ativa FROM fiscal.regra_fiscal
         WHERE empresa_id = @e AND (ativa OR NOT @a) ORDER BY codigo
        """, r => new RegraFiscal(r.GetInt64(0), r.GetInt32(1), r.GetString(2), r.GetString(3) == "S" ? TipoOperacao.Saida : TipoOperacao.Entrada,
                                  r.GetString(4), r.GetString(5).Trim(), r.Texto(6), r.Texto(7), r.Texto(8), r.GetBoolean(9)), ct, ("e", empresa), ("a", soAtivas));

    public async Task<Resultado<long>> GravarAsync(long empresa, RegraFiscal regra, long usuario, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuario, empresa, Permissao.FiscalDocumentosImportar, ct) is { } neg) return Resultado<long>.Falha(neg);
        var n = RegrasFiscais.Normalizar(regra);
        if (RegrasFiscais.Validar(n) is { Count: > 0 } erros) return Resultado<long>.Falha(erros);
        try
        {
            (string, object?)[] p = [("e", empresa), ("id", n.Id), ("c", n.Codigo), ("d", n.Descricao), ("o", n.Operacao == TipoOperacao.Saida ? "S" : "E"),
                                     ("de", n.CfopOrigem), ("para", n.CfopDestino), ("ci", n.CstIcms), ("cp", n.CstPis), ("cc", n.CstCofins), ("a", n.Ativa)];
            return Resultado<long>.Ok(n.Id == 0
                ? await _db.EscalarAsync<long>("""
                    INSERT INTO fiscal.regra_fiscal (empresa_id, codigo, descricao, operacao, cfop_origem, cfop_destino, cst_icms, cst_pis, cst_cofins, ativa)
                    VALUES (@e, @c, @d, @o, @de, @para, @ci, @cp, @cc, @a) RETURNING id
                    """, ct, p)
                : await _db.EscalarAsync<long?>("""
                    UPDATE fiscal.regra_fiscal SET codigo = @c, descricao = @d, operacao = @o, cfop_origem = @de, cfop_destino = @para,
                           cst_icms = @ci, cst_pis = @cp, cst_cofins = @cc, ativa = @a
                     WHERE id = @id AND empresa_id = @e RETURNING id
                    """, ct, p) ?? throw new ViolacaoRegraException("Regra não encontrada."));
        }
        catch (ViolacaoRegraException ex)
        {
            return Resultado<long>.Falha(ex.Message.StartsWith("Registro repetido", StringComparison.Ordinal)
                ? "Já existe uma regra com este código, ou com este CFOP do XML na mesma operação (entrada ou saída)." : ex.Message);
        }
    }

    public async Task<Resultado> ExcluirAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuario, empresa, Permissao.FiscalDocumentosImportar, ct) is { } neg) return Resultado.Falha(neg);
        await _db.ExecutarAsync("DELETE FROM fiscal.regra_fiscal WHERE id = @id AND empresa_id = @e", ct, ("id", id), ("e", empresa));
        return Resultado.Ok();
    }

    /// <summary>Inclui as regras sugeridas cujo CFOP do XML ainda não está cadastrado na operação; devolve quantas entraram.</summary>
    public async Task<Resultado<int>> IncluirSugeridasAsync(long empresa, long usuario, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuario, empresa, Permissao.FiscalDocumentosImportar, ct) is { } neg) return Resultado<int>.Falha(neg);
        var atuais = await RegrasAsync(empresa, false, ct);
        var codigo = atuais.Count == 0 ? 0 : atuais.Max(n => n.Codigo);
        var novas = 0;
        foreach (var s in RegrasFiscais.Sugeridas.Where(s => atuais.All(a => a.Operacao != s.Operacao || a.CfopOrigem != s.CfopOrigem)))
        {
            await _db.ExecutarAsync("INSERT INTO fiscal.regra_fiscal (empresa_id, codigo, descricao, operacao, cfop_origem, cfop_destino) VALUES (@e, @c, @d, @o, @de, @para)", ct,
                                    ("e", empresa), ("c", ++codigo), ("d", s.Descricao), ("o", s.Operacao == TipoOperacao.Saida ? "S" : "E"), ("de", s.CfopOrigem), ("para", s.CfopDestino));
            novas++;
        }
        return Resultado<int>.Ok(novas);
    }
}
