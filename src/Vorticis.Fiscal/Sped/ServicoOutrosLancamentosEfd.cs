using Npgsql;
using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Postgres;

namespace Vorticis.Fiscal.Sped;

/// <summary>Conta de consumo como cadastrada (Lançamentos › SPED Fiscal › Contas de consumo).</summary>
public sealed record ContaConsumo(long Id, string Modelo, long ParticipanteId, string ParticipanteNome, string Numero, string Serie, DateOnly Emissao, DateOnly Entrada,
                                  string ClasseConsumo, decimal Valor, string Cfop, string CstIcms, decimal BaseIcms, decimal AliqIcms, decimal Icms, decimal Pis, decimal Cofins);

public sealed record ItemInventario(long ProdutoId, int ProdutoCodigo, string Descricao, string Unidade, decimal Quantidade, decimal ValorUnitario, decimal Valor);

/// <summary>Inventário de estoque (bloco H) com a competência da EFD em que é informado e a conta contábil do estoque.</summary>
public sealed record Inventario(long Id, DateOnly Data, string Motivo, DateOnly CompetenciaEfd, string ContaContabil, IReadOnlyList<ItemInventario> Itens)
{
    public decimal Total => Itens.Sum(i => i.Valor);
}

/// <summary>
/// Lançamentos › SPED Fiscal: ajustes da apuração do ICMS (E111), contas de consumo (C500/D500) e inventário (bloco H), que a EFD ICMS/IPI
/// usa ao ser gerada. Ver <see cref="OutrosLancamentosEfd"/> para as regras.
/// </summary>
public sealed class ServicoOutrosLancamentosEfd(NpgsqlDataSource ds, Autorizador autorizador)
{
    private async Task<string?> NegarVerAsync(long usuario, long empresa, CancellationToken ct) =>
        (await autorizador.ObterAsync(usuario, empresa, ct)).TemAcesso ? null : "Você não tem acesso a esta empresa.";

    private async Task<string> UfAsync(long empresa, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT uf FROM core.empresa WHERE id = @e").Com("e", empresa);
        return (await cmd.ExecuteScalarAsync(ct) as string ?? "").Trim();
    }

    private static async Task<Resultado> Tentar(Func<Task> acao)
    {
        try { await acao(); return Resultado.Ok(); }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UniqueViolation)
        { return Resultado.Falha("Já existe um lançamento igual (mesmo documento ou mesma data)."); }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.CheckViolation or PostgresErrorCodes.ForeignKeyViolation)
        { return Resultado.Falha($"Dados recusados pelo banco: {ex.MessageText}"); }
    }

    // ------------------------------------------------------------------ ajustes da apuração do ICMS (E111)

    public async Task<Resultado<IReadOnlyList<AjusteIcmsEfd>>> AjustesAsync(long empresa, DateOnly competencia, long usuario, CancellationToken ct = default)
    {
        if (await NegarVerAsync(usuario, empresa, ct) is { } n) return Resultado<IReadOnlyList<AjusteIcmsEfd>>.Falha(n);
        return Resultado<IReadOnlyList<AjusteIcmsEfd>>.Ok(await new RepositorioEfd(ds).AjustesIcmsAsync(empresa, competencia, ct));
    }

    public async Task<Resultado> GravarAjusteAsync(long empresa, AjusteIcmsEfd a, long usuario, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuario, empresa, Permissao.FiscalApurar, ct) is { } negado) return Resultado.Falha(negado);
        var codigo = (a.Codigo ?? "").Trim().ToUpperInvariant();
        if (OutrosLancamentosEfd.ValidarAjuste(codigo, a.Descricao, a.Valor, await UfAsync(empresa, ct)) is { Count: > 0 } erros) return Resultado.Falha(erros);
        var comp = new DateOnly(a.Competencia.Year, a.Competencia.Month, 1);
        return await Tentar(async () =>
        {
            await using var cmd = ds.CreateCommand(a.Id == 0
                ? "INSERT INTO fiscal.efd_ajuste_icms (empresa_id, competencia, codigo, descricao, valor) VALUES (@e, @c, @cod, @d, @v)"
                : "UPDATE fiscal.efd_ajuste_icms SET competencia = @c, codigo = @cod, descricao = @d, valor = @v WHERE id = @id AND empresa_id = @e")
                .Com("e", empresa).Com("c", comp).Com("cod", codigo).Com("d", a.Descricao.Trim()).Com("v", a.Valor).Com("id", a.Id);
            await cmd.ExecuteNonQueryAsync(ct);
        });
    }

    public async Task<Resultado> ExcluirAjusteAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuario, empresa, Permissao.FiscalApurar, ct) is { } negado) return Resultado.Falha(negado);
        await using var cmd = ds.CreateCommand("DELETE FROM fiscal.efd_ajuste_icms WHERE id = @id AND empresa_id = @e").Com("id", id).Com("e", empresa);
        return await cmd.ExecuteNonQueryAsync(ct) == 0 ? Resultado.Falha("Ajuste não encontrado.") : Resultado.Ok();
    }

    // ------------------------------------------------------------------ contas de consumo (C500/D500)

    public async Task<Resultado<IReadOnlyList<ContaConsumo>>> ContasAsync(long empresa, DateOnly inicio, DateOnly fim, long usuario, CancellationToken ct = default)
    {
        if (await NegarVerAsync(usuario, empresa, ct) is { } n) return Resultado<IReadOnlyList<ContaConsumo>>.Falha(n);
        await using var cmd = ds.CreateCommand("""
            SELECT c.id, c.modelo, c.participante_id, p.nome, c.numero, c.serie, c.data_emissao, c.data_entrada, c.classe_consumo, c.valor, c.cfop, c.cst_icms,
                   c.base_icms, c.aliq_icms, c.valor_icms, c.valor_pis, c.valor_cofins
              FROM fiscal.conta_consumo c JOIN core.participante p ON p.id = c.participante_id
             WHERE c.empresa_id = @e AND c.data_entrada BETWEEN @i AND @f ORDER BY c.data_entrada, c.id
            """).Com("e", empresa).Com("i", inicio).Com("f", fim);
        var res = new List<ContaConsumo>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new ContaConsumo(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetFieldValue<DateOnly>(6),
                r.GetFieldValue<DateOnly>(7), r.GetString(8), r.GetDecimal(9), r.GetString(10), r.GetString(11), r.GetDecimal(12), r.GetDecimal(13), r.GetDecimal(14),
                r.GetDecimal(15), r.GetDecimal(16)));
        return Resultado<IReadOnlyList<ContaConsumo>>.Ok(res);
    }

    public async Task<Resultado> GravarContaAsync(long empresa, ContaConsumo c, long usuario, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuario, empresa, Permissao.FiscalDocumentosImportar, ct) is { } negado) return Resultado.Falha(negado);
        var erros = OutrosLancamentosEfd.ValidarConta(c.Modelo, c.Numero, c.Emissao, c.Entrada, c.Valor, c.Cfop, c.CstIcms, c.BaseIcms, c.AliqIcms, c.Icms).ToList();
        if (c.ParticipanteId == 0) erros.Insert(0, "Escolha o fornecedor (concessionária ou operadora).");
        if (erros.Count > 0) return Resultado.Falha(erros);
        return await Tentar(async () =>
        {
            await using var cmd = ds.CreateCommand(c.Id == 0
                ? """
                  INSERT INTO fiscal.conta_consumo (empresa_id, modelo, participante_id, numero, serie, data_emissao, data_entrada, classe_consumo, valor, cfop, cst_icms,
                                                    base_icms, aliq_icms, valor_icms, valor_pis, valor_cofins)
                  SELECT @e, @m, p.id, @n, @s, @em, @en, @cl, @v, @cfop, @cst, @b, @a, @i, @pis, @cof FROM core.participante p WHERE p.id = @p AND p.empresa_id = @e
                  """
                : """
                  UPDATE fiscal.conta_consumo SET modelo = @m, participante_id = @p, numero = @n, serie = @s, data_emissao = @em, data_entrada = @en, classe_consumo = @cl,
                         valor = @v, cfop = @cfop, cst_icms = @cst, base_icms = @b, aliq_icms = @a, valor_icms = @i, valor_pis = @pis, valor_cofins = @cof
                   WHERE id = @id AND empresa_id = @e
                  """)
                .Com("e", empresa).Com("m", c.Modelo).Com("p", c.ParticipanteId).Com("n", c.Numero.Trim()).Com("s", (c.Serie ?? "").Trim()).Com("em", c.Emissao)
                .Com("en", c.Entrada).Com("cl", string.IsNullOrWhiteSpace(c.ClasseConsumo) ? "01" : c.ClasseConsumo.Trim()).Com("v", c.Valor).Com("cfop", c.Cfop.Trim())
                .Com("cst", c.CstIcms.Trim()).Com("b", c.BaseIcms).Com("a", c.AliqIcms).Com("i", c.Icms).Com("pis", c.Pis).Com("cof", c.Cofins).Com("id", c.Id);
            if (await cmd.ExecuteNonQueryAsync(ct) == 0) throw new PostgresException("Fornecedor não encontrado nesta empresa.", "ERROR", "ERROR", PostgresErrorCodes.ForeignKeyViolation);
        });
    }

    public async Task<Resultado> ExcluirContaAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuario, empresa, Permissao.FiscalDocumentosImportar, ct) is { } negado) return Resultado.Falha(negado);
        await using var cmd = ds.CreateCommand("DELETE FROM fiscal.conta_consumo WHERE id = @id AND empresa_id = @e").Com("id", id).Com("e", empresa);
        return await cmd.ExecuteNonQueryAsync(ct) == 0 ? Resultado.Falha("Nota não encontrada.") : Resultado.Ok();
    }

    // ------------------------------------------------------------------ inventário (bloco H)

    public async Task<Resultado<IReadOnlyList<Inventario>>> InventariosAsync(long empresa, long usuario, CancellationToken ct = default)
    {
        if (await NegarVerAsync(usuario, empresa, ct) is { } n) return Resultado<IReadOnlyList<Inventario>>.Falha(n);
        await using var cmd = ds.CreateCommand("""
            SELECT i.id, i.data, i.motivo, i.competencia_efd, i.conta_contabil, it.produto_id, p.codigo, p.descricao, it.unidade, it.quantidade, it.valor_unitario, it.valor
              FROM fiscal.inventario i
              LEFT JOIN fiscal.inventario_item it ON it.inventario_id = i.id
              LEFT JOIN fiscal.produto p ON p.id = it.produto_id
             WHERE i.empresa_id = @e
             ORDER BY i.data DESC, i.id, p.codigo
            """).Com("e", empresa);
        var cab = new List<Inventario>();
        var itens = new Dictionary<long, List<ItemInventario>>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var id = r.GetInt64(0);
            if (!itens.TryGetValue(id, out var lista))
            {
                itens[id] = lista = [];
                cab.Add(new Inventario(id, r.GetFieldValue<DateOnly>(1), r.GetString(2), r.GetFieldValue<DateOnly>(3), r.GetString(4), lista));
            }
            if (!r.IsDBNull(5))
                lista.Add(new ItemInventario(r.GetInt64(5), r.GetInt32(6), r.GetString(7), r.GetString(8), r.GetDecimal(9), r.GetDecimal(10), r.GetDecimal(11)));
        }
        return Resultado<IReadOnlyList<Inventario>>.Ok(cab);
    }

    /// <summary>Grava o inventário inteiro (cabeçalho e itens, substituindo os itens anteriores). Devolve o id.</summary>
    public async Task<Resultado<long>> GravarInventarioAsync(long empresa, Inventario inv, long usuario, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuario, empresa, Permissao.FiscalApurar, ct) is { } negado) return negado;
        var erros = new List<string>();
        if (inv.Itens.Count == 0) erros.Add("Inclua ao menos um produto no inventário.");
        if (inv.Itens.Any(i => i.Quantidade <= 0)) erros.Add("A quantidade de cada item deve ser maior que zero.");
        if (inv.Itens.Any(i => i.ValorUnitario < 0)) erros.Add("O valor unitário não pode ser negativo.");
        if (inv.Itens.GroupBy(i => i.ProdutoId).Any(g => g.Count() > 1)) erros.Add("Há produto repetido no inventário.");
        if (string.IsNullOrWhiteSpace(inv.ContaContabil)) erros.Add("Informe a conta contábil do estoque (exigida no H010).");
        if (inv.CompetenciaEfd < new DateOnly(inv.Data.Year, inv.Data.Month, 1)) erros.Add("A EFD em que o inventário é informado não pode ser anterior à data do inventário.");
        if (erros.Count > 0) return Resultado<long>.Falha(erros);
        try
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            long id;
            await using (var cmd = new NpgsqlCommand(inv.Id == 0
                ? "INSERT INTO fiscal.inventario (empresa_id, data, motivo, competencia_efd, conta_contabil) VALUES (@e, @d, @m, @c, @cc) RETURNING id"
                : "UPDATE fiscal.inventario SET data = @d, motivo = @m, competencia_efd = @c, conta_contabil = @cc WHERE id = @id AND empresa_id = @e RETURNING id", con, tx))
            {
                cmd.Com("e", empresa).Com("d", inv.Data).Com("m", inv.Motivo).Com("c", new DateOnly(inv.CompetenciaEfd.Year, inv.CompetenciaEfd.Month, 1))
                   .Com("cc", inv.ContaContabil.Trim()).Com("id", inv.Id);
                id = (long)(await cmd.ExecuteScalarAsync(ct) ?? throw new ViolacaoRegraException("Inventário não encontrado."));
            }
            await using (var del = new NpgsqlCommand("DELETE FROM fiscal.inventario_item WHERE inventario_id = @i", con, tx)) { del.Com("i", id); await del.ExecuteNonQueryAsync(ct); }
            foreach (var i in inv.Itens)
            {
                await using var ins = new NpgsqlCommand("""
                    INSERT INTO fiscal.inventario_item (inventario_id, produto_id, unidade, quantidade, valor_unitario, valor)
                    SELECT @i, p.id, @u, @q, @vu, @v FROM fiscal.produto p WHERE p.id = @p AND p.empresa_id = @e
                    """, con, tx);
                ins.Com("i", id).Com("u", string.IsNullOrWhiteSpace(i.Unidade) ? "UN" : i.Unidade.Trim().ToUpperInvariant()).Com("q", i.Quantidade)
                   .Com("vu", i.ValorUnitario).Com("v", Math.Round(i.Quantidade * i.ValorUnitario, 2)).Com("p", i.ProdutoId).Com("e", empresa);
                if (await ins.ExecuteNonQueryAsync(ct) == 0) throw new ViolacaoRegraException($"Produto {i.ProdutoCodigo} não encontrado nesta empresa.");
            }
            await tx.CommitAsync(ct);
            return Resultado<long>.Ok(id);
        }
        catch (ViolacaoRegraException ex) { return Resultado<long>.Falha(ex.Message); }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UniqueViolation)
        { return Resultado<long>.Falha("Já existe um inventário nesta data com o mesmo motivo."); }
    }

    public async Task<Resultado> ExcluirInventarioAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuario, empresa, Permissao.FiscalApurar, ct) is { } negado) return Resultado.Falha(negado);
        await using var cmd = ds.CreateCommand("DELETE FROM fiscal.inventario WHERE id = @id AND empresa_id = @e").Com("id", id).Com("e", empresa);
        return await cmd.ExecuteNonQueryAsync(ct) == 0 ? Resultado.Falha("Inventário não encontrado.") : Resultado.Ok();
    }
}
