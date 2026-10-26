using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Application.Contabil;

public sealed record LayoutSalvo(long Id, string Nome, LayoutImportacao Layout);

/// <summary>Layout de extrato em PDF/TXT/CSV salvo (compartilhado entre as empresas, como os de planilha).</summary>
public sealed record LayoutExtratoSalvo(long Id, string Nome, LayoutExtrato Layout)
{
    public override string ToString() => Nome;
}

/// <summary>Lançamento lido + o que impede a importação (vazio = pode importar) e se já foi importado antes.</summary>
public sealed record ItemImportacao(LancamentoLido Lancamento, IReadOnlyList<string> Erros, bool JaImportado)
{
    public bool Valido => Erros.Count == 0 && !JaImportado;
}

public interface IRepositorioImportacao
{
    Task<IReadOnlyList<LayoutSalvo>> ListarLayoutsAsync(CancellationToken ct);
    /// <summary>Inclui (id nulo) ou altera o layout. Retorna o id.</summary>
    Task<long> SalvarLayoutAsync(long? id, LayoutImportacao layout, CancellationToken ct);
    Task ExcluirLayoutAsync(long id, CancellationToken ct);
    Task<IReadOnlyList<LayoutExtratoSalvo>> ListarLayoutsExtratoAsync(CancellationToken ct);
    /// <summary>Inclui (id nulo) ou altera o layout de extrato. Retorna o id.</summary>
    Task<long> SalvarLayoutExtratoAsync(long? id, LayoutExtrato layout, CancellationToken ct);
    /// <summary>Das chaves informadas, as que já geraram lançamento ativo (não estornado) na empresa.</summary>
    Task<IReadOnlySet<string>> ChavesImportadasAsync(long empresaId, IReadOnlyCollection<string> chaves, CancellationToken ct);
}

/// <summary>
/// Integrações › Importar: layouts de planilha do usuário e importação de lançamentos (planilha, extrato, layout padrão).
/// Fluxo: ler → <see cref="PrepararAsync"/> (valida contra o plano e os períodos, aponta duplicidades) → <see cref="ImportarAsync"/>
/// (tudo ou nada, numa transação).
/// </summary>
public sealed class ServicoImportacao(IRepositorioImportacao repositorio, ServicoLancamento lancamentos, Autorizador autorizador)
{
    public Task<IReadOnlyList<LayoutSalvo>> ListarLayoutsAsync(CancellationToken ct = default) => repositorio.ListarLayoutsAsync(ct);

    public async Task<Resultado<long>> SalvarLayoutAsync(long empresaId, long? id, LayoutImportacao layout, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.LancamentoIncluir, ct) is { } negado) return negado;
        var nome = layout.Nome.Trim();
        var erros = new List<string>();
        if (nome.Length is 0 or > 80) erros.Add("Dê um nome ao layout (até 80 caracteres).");
        if (string.IsNullOrWhiteSpace(layout.ColunaData)) erros.Add("Informe a coluna da data.");
        if (layout.Modelo == ModeloLayout.UmaLinhaPorLancamento)
        {
            if (string.IsNullOrWhiteSpace(layout.ColunaValor)) erros.Add("Informe a coluna do valor.");
            if (string.IsNullOrWhiteSpace(layout.ColunaContaDebito) && string.IsNullOrWhiteSpace(layout.ContaDebitoPadrao))
                erros.Add("Informe a coluna da conta a débito ou uma conta débito fixa.");
            if (string.IsNullOrWhiteSpace(layout.ColunaContaCredito) && string.IsNullOrWhiteSpace(layout.ContaCreditoPadrao))
                erros.Add("Informe a coluna da conta a crédito ou uma conta crédito fixa.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(layout.ColunaConta)) erros.Add("Informe a coluna da conta.");
            if (string.IsNullOrWhiteSpace(layout.ColunaValor) && string.IsNullOrWhiteSpace(layout.ColunaValorDebito) && string.IsNullOrWhiteSpace(layout.ColunaValorCredito))
                erros.Add("Informe a coluna do valor (ou as colunas de valor a débito e a crédito).");
        }
        if (!string.IsNullOrWhiteSpace(layout.MascaraContas) && MascaraPlano.Ler(layout.MascaraContas) is { Sucesso: false } m) erros.AddRange(m.Erros);
        if (erros.Count > 0) return Resultado<long>.Falha(erros);
        var existentes = await repositorio.ListarLayoutsAsync(ct);
        if (existentes.Any(l => l.Id != id && string.Equals(l.Nome, nome, StringComparison.CurrentCultureIgnoreCase)))
            return Resultado<long>.Falha($"Já existe um layout chamado \"{nome}\".");
        try
        {
            return Resultado<long>.Ok(await repositorio.SalvarLayoutAsync(id, layout with { Nome = nome }, ct));
        }
        catch (ViolacaoRegraException ex)
        {
            return Resultado<long>.Falha(ex.Message);
        }
    }

    public Task<IReadOnlyList<LayoutExtratoSalvo>> ListarLayoutsExtratoAsync(CancellationToken ct = default) => repositorio.ListarLayoutsExtratoAsync(ct);

    public static IReadOnlyList<string> ValidarLayoutExtrato(LayoutExtrato layout)
    {
        var erros = new List<string>();
        if (layout.Nome.Trim().Length is 0 or > 80) erros.Add("Dê um nome ao layout (até 80 caracteres), ex.: o nome do banco.");
        if (layout.ColunaData < 1) erros.Add("Informe a coluna da data (1 = primeira coluna).");
        if (layout.ColunaDescricao < 0 || layout.ColunaValor < 0 || layout.ColunaDocumento < 0) erros.Add("As colunas são números a partir de 1 (0 = automático/nenhuma).");
        if (layout.ColunaValor > 0 && layout.ColunaValor == layout.ColunaData) erros.Add("A coluna do valor não pode ser a mesma da data.");
        if (!LeitorExtratoTexto.FormatosData.Contains(layout.FormatoData)) erros.Add("Formato de data inválido.");
        return erros;
    }

    public async Task<Resultado<long>> SalvarLayoutExtratoAsync(long empresaId, long? id, LayoutExtrato layout, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.LancamentoIncluir, ct) is { } negado) return negado;
        if (ValidarLayoutExtrato(layout) is { Count: > 0 } erros) return Resultado<long>.Falha(erros);
        var nome = layout.Nome.Trim();
        if ((await repositorio.ListarLayoutsExtratoAsync(ct)).Any(l => l.Id != id && string.Equals(l.Nome, nome, StringComparison.CurrentCultureIgnoreCase)))
            return Resultado<long>.Falha($"Já existe um layout de extrato chamado \"{nome}\".");
        try
        {
            return Resultado<long>.Ok(await repositorio.SalvarLayoutExtratoAsync(id, layout with { Nome = nome, Ignorar = layout.Ignorar?.Trim() ?? "" }, ct));
        }
        catch (ViolacaoRegraException ex)
        {
            return Resultado<long>.Falha(ex.Message);
        }
    }

    public async Task<Resultado> ExcluirLayoutAsync(long empresaId, long id, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.LancamentoIncluir, ct) is { } negado) return Resultado.Falha(negado);
        await repositorio.ExcluirLayoutAsync(id, ct);
        return Resultado.Ok();
    }

    /// <summary>Valida cada lançamento lido (contas, períodos, partidas dobradas) e marca os já importados.</summary>
    public async Task<Resultado<IReadOnlyList<ItemImportacao>>> PrepararAsync(long empresaId, IReadOnlyList<LancamentoLido> lidos,
                                                                             OrigemLancamento origem, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<ItemImportacao>>(usuarioId, empresaId, Permissao.LancamentoIncluir, ct) is { } negado) return negado;
        var chaves = lidos.Select(l => l.Chave).OfType<string>().ToArray();
        var importadas = chaves.Length == 0 ? new HashSet<string>() : await repositorio.ChavesImportadasAsync(empresaId, chaves, ct);
        var validacoes = await lancamentos.ValidarLoteAsync(lidos.Select(l => Proposta(empresaId, l, origem)).ToList(), ct);
        return Resultado<IReadOnlyList<ItemImportacao>>.Ok(lidos.Select((l, i) =>
            new ItemImportacao(l, validacoes[i].Erros, l.Chave is { } c && importadas.Contains(c))).ToList());
    }

    /// <summary>
    /// Efetiva os lançamentos válidos ainda não importados, numa transação. Se algum tiver erro, nada é gravado.
    /// Retorna os números gerados.
    /// </summary>
    public async Task<Resultado<IReadOnlyList<LancamentoEfetivado>>> ImportarAsync(long empresaId, IReadOnlyList<LancamentoLido> lidos,
                                                                                  OrigemLancamento origem, long usuarioId, CancellationToken ct = default)
    {
        var preparo = await PrepararAsync(empresaId, lidos, origem, usuarioId, ct);
        if (!preparo.Sucesso) return Resultado<IReadOnlyList<LancamentoEfetivado>>.Falha(preparo.Erros);
        var comErro = preparo.Valor.Where(i => i.Erros.Count > 0 && !i.JaImportado)
                                   .Select(i => $"Linha {i.Lancamento.Linha}: {string.Join(" ", i.Erros)}").Take(30).ToArray();
        if (comErro.Length > 0) return Resultado<IReadOnlyList<LancamentoEfetivado>>.Falha(comErro);
        var novos = preparo.Valor.Where(i => i.Valido).Select(i => i.Lancamento).ToList();
        if (novos.Count == 0) return Resultado<IReadOnlyList<LancamentoEfetivado>>.Falha("Nada a importar: todos os lançamentos já foram importados.");
        return await lancamentos.EfetivarLoteAsync(novos.Select(l => Proposta(empresaId, l, origem)).ToList(),
                                                   novos.Select(l => l.Chave).ToList(), usuarioId, ct);
    }

    private static LancamentoProposto Proposta(long empresaId, LancamentoLido l, OrigemLancamento origem) =>
        new(empresaId, l.Data, origem, l.Partidas);

    /// <summary>
    /// Extrato bancário → lançamentos: entradas debitam a conta do banco e creditam a contrapartida; saídas o inverso.
    /// <paramref name="contrapartidas"/> traz a contrapartida de cada transação (mesma posição).
    /// </summary>
    public static IReadOnlyList<LancamentoLido> LancamentosDoExtrato(ExtratoBancario extrato, IReadOnlyList<TransacaoExtrato> transacoes,
                                                                   string contaBanco, IReadOnlyList<string> contrapartidas, string? prefixoHistorico)
    {
        var res = new List<LancamentoLido>(transacoes.Count);
        for (var i = 0; i < transacoes.Count; i++)
        {
            var t = transacoes[i];
            var hist = string.Join(" ", new[] { prefixoHistorico?.Trim(), t.Descricao, t.Documento is null ? null : $"Doc {t.Documento}" }
                                                .Where(s => !string.IsNullOrWhiteSpace(s)));
            if (hist.Length == 0) hist = "Movimento bancário";
            var v = Math.Abs(t.Valor);
            var (deb, cred) = t.Valor > 0 ? (contaBanco, contrapartidas[i]) : (contrapartidas[i], contaBanco);
            res.Add(new LancamentoLido(i + 1, t.Data,
                [new PartidaProposta(deb, IndicadorDC.D, v, hist, NumeroArquivo: t.Documento), new PartidaProposta(cred, IndicadorDC.C, v, hist, NumeroArquivo: t.Documento)],
                extrato.Chave(t)));
        }
        return res;
    }
}
