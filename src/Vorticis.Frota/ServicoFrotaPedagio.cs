using Vorticis.Domain.Comum;
using Vorticis.Fiscal;
using Vorticis.Fiscal.Documentos;
using Vorticis.Infra.Postgres;
using P = Vorticis.Domain.Seguranca.Permissao;

namespace Vorticis.Frota;

/// <summary>Pedágio: layouts dos arquivos das operadoras, importação das passagens e geração da nota fiscal de entrada da operadora.</summary>
public sealed partial class ServicoFrota
{
    // ------------------------------------------------------------------ layouts

    public async Task<List<LayoutPedagio>> LayoutsPedagioAsync(long empresa, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await ListarLayoutsPedagioAsync(empresa, ct) : [];

    private Task<List<LayoutPedagio>> ListarLayoutsPedagioAsync(long empresa, CancellationToken ct) => _db.ListarAsync("""
        SELECT l.id, l.nome, l.operadora_id, l.coluna_data, l.coluna_hora, l.coluna_placa, l.coluna_praca, l.coluna_valor, l.cfop, l.ativo, p.nome
          FROM frota.pedagio_layout l JOIN core.participante p ON p.id = l.operadora_id WHERE l.empresa_id = @e ORDER BY l.nome
        """, r => new LayoutPedagio(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.GetString(3), r.Texto(4), r.GetString(5), r.Texto(6), r.GetString(7),
                                    r.GetString(8), r.GetBoolean(9), r.GetString(10)), ct, ("e", empresa));

    public async Task<Resultado<long>> GravarLayoutPedagioAsync(long empresa, LayoutPedagio l, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaOperacao, ct) is { } n) return Resultado<long>.Falha(n);
        l = l with { Nome = l.Nome?.Trim() ?? "", ColunaHora = N(l.ColunaHora), ColunaPraca = N(l.ColunaPraca), Cfop = (l.Cfop ?? "").Trim() };
        if (RegrasPedagio.Validar(l) is { Count: > 0 } erros) return Resultado<long>.Falha(erros);
        (string, object?)[] p =
        [
            ("e", empresa), ("id", l.Id), ("n", l.Nome), ("o", l.OperadoraId), ("cd", l.ColunaData.Trim()), ("ch", l.ColunaHora), ("cp", l.ColunaPlaca.Trim()),
            ("cr", l.ColunaPraca), ("cv", l.ColunaValor.Trim()), ("cf", l.Cfop), ("a", l.Ativo),
        ];
        return await Tentar(async () => l.Id == 0
            ? await _db.EscalarAsync<long>("""
                INSERT INTO frota.pedagio_layout (empresa_id, nome, operadora_id, coluna_data, coluna_hora, coluna_placa, coluna_praca, coluna_valor, cfop, ativo)
                VALUES (@e, @n, @o, @cd, @ch, @cp, @cr, @cv, @cf, @a) RETURNING id
                """, ct, p)
            : await _db.EscalarAsync<long?>("""
                UPDATE frota.pedagio_layout SET nome = @n, operadora_id = @o, coluna_data = @cd, coluna_hora = @ch, coluna_placa = @cp, coluna_praca = @cr,
                       coluna_valor = @cv, cfop = @cf, ativo = @a WHERE id = @id AND empresa_id = @e RETURNING id
                """, ct, p) ?? throw new ViolacaoRegraException("Layout não encontrado."));
    }

    public async Task<Resultado> ExcluirLayoutPedagioAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaOperacao, ct) is { } n) return Resultado.Falha(n);
        return await Tentar(() => _db.ExecutarAsync("DELETE FROM frota.pedagio_layout WHERE id = @id AND empresa_id = @e", ct, ("id", id), ("e", empresa)));
    }

    // ------------------------------------------------------------------ passagens

    public async Task<List<PassagemPedagio>> PassagensPedagioAsync(long empresa, DateOnly inicio, DateOnly fim, long? layoutId, bool soSemNota, long usuario,
                                                                   CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await ListarPassagensAsync(empresa, inicio, fim, layoutId, soSemNota, ct) : [];

    private Task<List<PassagemPedagio>> ListarPassagensAsync(long empresa, DateOnly inicio, DateOnly fim, long? layoutId, bool soSemNota, CancellationToken ct) => _db.ListarAsync("""
        SELECT p.id, p.data_hora, p.placa, p.veiculo_id, p.praca, p.valor, p.documento_id, p.layout_id, v.codigo::text || ' – ' || COALESCE(v.marca || ' ' || v.modelo, v.modelo, v.tipo), d.serie || '/' || d.numero
          FROM frota.pedagio p LEFT JOIN frota.veiculo v ON v.id = p.veiculo_id LEFT JOIN fiscal.documento d ON d.id = p.documento_id
         WHERE p.empresa_id = @e AND p.data_hora::date BETWEEN @i AND @f AND (@l::bigint IS NULL OR p.layout_id = @l) AND (NOT @s OR p.documento_id IS NULL)
         ORDER BY p.data_hora, p.placa
        """, r => new PassagemPedagio(r.GetInt64(0), r.GetDateTime(1), r.GetString(2), r.Id(3), r.GetString(4), r.GetDecimal(5), r.Id(6), r.Id(7), r.Texto(8), r.Texto(9)),
        ct, ("e", empresa), ("i", inicio), ("f", fim), ("l", layoutId), ("s", soSemNota));

    /// <summary>Importa as passagens da planilha pelo layout; a placa é ligada ao veículo cadastrado e a mesma passagem não entra duas vezes.</summary>
    public async Task<Resultado<ResultadoImportacaoPedagio>> ImportarPedagioAsync(long empresa, long layoutId, TabelaImportada tabela, string arquivo, long usuario,
                                                                                 CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaOperacao, ct) is { } n) return Resultado<ResultadoImportacaoPedagio>.Falha(n);
        if ((await ListarLayoutsPedagioAsync(empresa, ct)).FirstOrDefault(l => l.Id == layoutId) is not { } layout) return Resultado<ResultadoImportacaoPedagio>.Falha("Escolha o layout da operadora.");
        var (passagens, erros) = RegrasPedagio.Ler(tabela, layout);
        if (passagens.Count == 0) return Resultado<ResultadoImportacaoPedagio>.Falha(erros.Count > 0 ? erros : ["O arquivo não tem passagens."]);
        var veiculos = (await ListarVeiculosAsync(empresa, ct)).ToDictionary(v => RegrasFrota.NormalizarPlaca(v.Placa), v => v.Id);
        int novas = 0, duplicadas = 0, semVeiculo = 0;
        decimal valor = 0;
        return await Tentar(() => EmTransacaoAsync(async (con, tx) =>
        {
            foreach (var p in passagens)
            {
                long? veiculo = veiculos.TryGetValue(p.Placa, out var v) ? v : null;
                var inseriu = await Exec(con, tx, """
                    INSERT INTO frota.pedagio (empresa_id, layout_id, data_hora, placa, veiculo_id, praca, valor, arquivo) VALUES (@e, @l, @d, @p, @v, @pr, @val, @a)
                    ON CONFLICT ON CONSTRAINT uq_pedagio_passagem DO NOTHING
                    """, ct, ("e", empresa), ("l", layoutId), ("d", p.DataHora), ("p", p.Placa), ("v", veiculo), ("pr", p.Praca), ("val", p.Valor),
                    ("a", arquivo.Length > 200 ? arquivo[^200..] : arquivo));
                if (inseriu == 0) { duplicadas++; continue; }
                novas++;
                valor += p.Valor;
                if (veiculo is null) semVeiculo++;
            }
            return new ResultadoImportacaoPedagio(novas, duplicadas, semVeiculo, valor, erros);
        }, ct));
    }

    /// <summary>Exclui passagens que ainda não estão em nota fiscal (ex.: importadas com o layout errado).</summary>
    public async Task<Resultado<int>> ExcluirPassagensAsync(long empresa, IReadOnlyList<long> ids, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaOperacao, ct) is { } n) return Resultado<int>.Falha(n);
        return await Tentar(() => _db.ExecutarAsync("DELETE FROM frota.pedagio WHERE empresa_id = @e AND id = ANY(@ids) AND documento_id IS NULL", ct,
                                                    ("e", empresa), ("ids", ids.ToArray())));
    }

    /// <summary>
    /// Gera a nota fiscal de entrada (NF-e, modelo 55) da operadora com as passagens sem nota do layout no período: um item por placa, com o
    /// CFOP do layout. A nota é gravada pela digitação do Fiscal (mesmas regras, duplicata e contabilização on-line) e as passagens ficam ligadas a ela.
    /// </summary>
    public async Task<Resultado<(long DocumentoId, int Passagens, decimal Valor)>> GerarNotaPedagioAsync(long empresa, long layoutId, DateOnly inicio, DateOnly fim,
        string numero, string serie, DateOnly emissao, string? chave, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaOperacao, ct) is { } n) return Resultado<(long, int, decimal)>.Falha(n);
        if ((await ListarLayoutsPedagioAsync(empresa, ct)).FirstOrDefault(l => l.Id == layoutId) is not { } layout) return Resultado<(long, int, decimal)>.Falha("Escolha o layout da operadora.");
        var passagens = await ListarPassagensAsync(empresa, inicio, fim, layoutId, true, ct);
        if (passagens.Count == 0) return Resultado<(long, int, decimal)>.Falha("Não há passagens sem nota fiscal neste período para esta operadora.");
        var itens = RegrasPedagio.ItensNota(passagens).Select((x, k) => new ItemFiscal(k + 1, null, x.Descricao, null, null, layout.Cfop, "UN", 1, x.Valor, x.Valor,
            0, 0, 0, 0, x.Valor, null, null, 0, 0, 0, 0, 0, null, 0, 0, 0, null, 0, 0, 0, null, 0, 0, 0, 0, 0, 0)).ToList();
        var zero = new TotaisDocumento(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0, 0, 0, 0, 0);
        var doc = new DocumentoDigitado(0, TipoOperacao.Entrada, ModeloDocumento.NFe55, serie, numero, string.IsNullOrWhiteSpace(chave) ? null : chave, emissao, emissao,
            "Pedágio", layout.OperadoraId, null, CalculadoraNota.Totalizar(itens, zero), itens,
            Observacoes: $"Pedágio {layout.Nome}: {passagens.Count} passagem(ns) de {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}.");
        var r = await ServicosFiscais.De(app).Digitacao.GravarAsync(empresa, doc, usuario, ct);
        if (!r.Sucesso) return Resultado<(long, int, decimal)>.Falha(r.Erros);
        await _db.ExecutarAsync("UPDATE frota.pedagio SET documento_id = @d WHERE empresa_id = @e AND id = ANY(@ids) AND documento_id IS NULL", ct,
                                ("d", r.Valor), ("e", empresa), ("ids", passagens.Select(p => p.Id).ToArray()));
        return Resultado<(long, int, decimal)>.Ok((r.Valor, passagens.Count, passagens.Sum(p => p.Valor)));
    }
}
