using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Application.Contabil;

/// <summary>Uma linha da implantação de saldos: conta patrimonial e saldo com sinal (positivo = devedor).</summary>
public sealed record SaldoInicialConta(string CodigoConta, decimal Valor, IndicadorDC DC);

/// <summary>
/// Único ponto de gravação de lançamentos contábeis. Módulos integradores (fiscal, banco, folha)
/// produzem <see cref="LancamentoProposto"/> e chamam <see cref="EfetivarAsync"/>.
/// Toda operação verifica a permissão do usuário na empresa antes de qualquer outra regra.
/// </summary>
public sealed class ServicoLancamento(IRepositorioContabil repositorio, Autorizador autorizador)
{
    private static readonly string[] NaturezasPatrimoniais = ["01", "02", "03"];

    public async Task<Resultado<LancamentoEfetivado>> EfetivarAsync(LancamentoProposto proposta, long usuarioId, CancellationToken ct = default)
    {
        var permissao = proposta.Origem switch
        {
            OrigemLancamento.Estorno => Permissao.LancamentoEstornar,
            OrigemLancamento.SaldoInicial => Permissao.SaldosIniciais,
            _ => Permissao.LancamentoIncluir,
        };
        if (await autorizador.NegarAsync<LancamentoEfetivado>(usuarioId, proposta.EmpresaId, permissao, ct) is { } negado) return negado;
        return await GravarAsync(proposta, usuarioId, ct);
    }

    public async Task<Resultado<LancamentoEfetivado>> EstornarAsync(long empresaId, long lancamentoId, DateOnly data, string motivo,
                                                                   long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<LancamentoEfetivado>(usuarioId, empresaId, Permissao.LancamentoEstornar, ct) is { } negado) return negado;
        var original = await repositorio.ObterLancamentoAsync(empresaId, lancamentoId, ct);
        if (original is null) return Resultado<LancamentoEfetivado>.Falha("Lançamento não encontrado.");

        var estorno = Estorno.Gerar(original, data, motivo);
        if (!estorno.Sucesso) return Resultado<LancamentoEfetivado>.Falha(estorno.Erros);

        return await GravarAsync(estorno.Valor, usuarioId, ct);
    }

    /// <summary>Lançamento gravado (partidas por código) para ser editado na tela de lançamento.</summary>
    public async Task<Resultado<LancamentoRegistrado>> ObterAsync(long empresaId, long lancamentoId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<LancamentoRegistrado>(usuarioId, empresaId, Permissao.LancamentoIncluir, ct) is { } negado) return negado;
        return await repositorio.ObterLancamentoAsync(empresaId, lancamentoId, ct) is { } l
            ? Resultado<LancamentoRegistrado>.Ok(l)
            : Resultado<LancamentoRegistrado>.Falha("Lançamento não encontrado.");
    }

    /// <summary>
    /// Alteração de lançamento efetuado: como lançamentos efetivados são imutáveis, o original é estornado (na data
    /// dele) e o lançamento corrigido é gravado, tudo numa só transação. Devolve (estorno, novo).
    /// </summary>
    public async Task<Resultado<IReadOnlyList<LancamentoEfetivado>>> SubstituirAsync(long lancamentoId, LancamentoProposto novo, long usuarioId,
                                                                                    CancellationToken ct = default)
    {
        if (novo.Origem is OrigemLancamento.Estorno or OrigemLancamento.SaldoInicial)
            return Resultado<IReadOnlyList<LancamentoEfetivado>>.Falha("Lançamento inválido.");
        foreach (var p in new[] { Permissao.LancamentoIncluir, Permissao.LancamentoEstornar })
            if (await autorizador.NegarAsync<IReadOnlyList<LancamentoEfetivado>>(usuarioId, novo.EmpresaId, p, ct) is { } negado) return negado;
        var original = await repositorio.ObterLancamentoAsync(novo.EmpresaId, lancamentoId, ct);
        if (original is null) return Resultado<IReadOnlyList<LancamentoEfetivado>>.Falha("Lançamento não encontrado.");
        if (original.Origem != OrigemLancamento.Manual)
            return Resultado<IReadOnlyList<LancamentoEfetivado>>.Falha("Só lançamentos manuais podem ser alterados; os demais são corrigidos por estorno.");
        if (!await repositorio.UsaEstornoAsync(novo.EmpresaId, ct)) return await AlterarSemEstornoAsync(original, novo, usuarioId, ct);

        var estorno = Estorno.Gerar(original, original.Data, $"alteração do lançamento nº {original.Numero}");
        if (!estorno.Sucesso) return Resultado<IReadOnlyList<LancamentoEfetivado>>.Falha(estorno.Erros);
        var validacoes = await ValidarLoteAsync([estorno.Valor, novo], ct);
        var erros = validacoes.Where(v => !v.Sucesso).SelectMany(v => v.Erros).ToArray();
        if (erros.Length > 0) return Resultado<IReadOnlyList<LancamentoEfetivado>>.Falha(erros);
        try
        {
            return Resultado<IReadOnlyList<LancamentoEfetivado>>.Ok(
                await repositorio.GravarEfetivadosAsync(validacoes.Select(v => v.Valor).ToList(), usuarioId, ct));
        }
        catch (ViolacaoRegraException ex)
        {
            return Resultado<IReadOnlyList<LancamentoEfetivado>>.Falha(ex.Message);
        }
    }

    /// <summary>A empresa corrige lançamentos por estorno (padrão) ou altera/exclui o próprio lançamento? (aba Contabilidade do cadastro da empresa).</summary>
    public async Task<bool> UsaEstornoAsync(long empresaId, long usuarioId, CancellationToken ct = default)
        => !(await autorizador.ObterAsync(usuarioId, empresaId, ct)).TemAcesso || await repositorio.UsaEstornoAsync(empresaId, ct);

    /// <summary>Empresa sem estorno: regrava data e partidas no mesmo lançamento e número, com saldo e auditoria.</summary>
    private async Task<Resultado<IReadOnlyList<LancamentoEfetivado>>> AlterarSemEstornoAsync(LancamentoRegistrado original, LancamentoProposto novo,
                                                                                           long usuarioId, CancellationToken ct)
    {
        static Resultado<IReadOnlyList<LancamentoEfetivado>> Falha(params string[] e) => Resultado<IReadOnlyList<LancamentoEfetivado>>.Falha(e);
        var sit = await repositorio.ObterSituacaoLancamentoAsync(novo.EmpresaId, original.Id, ct);
        if (sit is null) return Falha("Lançamento não encontrado.");
        var impedimento = ImpedimentoManutencao(sit, "alterado");
        if (impedimento is not null) return Falha(impedimento);
        var validacao = (await ValidarLoteAsync([novo], ct))[0];
        if (!validacao.Sucesso) return Falha(validacao.Erros.ToArray());
        if (validacao.Valor.ExercicioId != sit.ExercicioId) return Falha("A nova data precisa continuar no mesmo exercício do lançamento.");
        try
        {
            await repositorio.AlterarLancamentoAsync(original.Id, validacao.Valor, usuarioId, ct);
            return Resultado<IReadOnlyList<LancamentoEfetivado>>.Ok([new LancamentoEfetivado(original.Id, sit.Numero ?? 0)]);
        }
        catch (ViolacaoRegraException ex)
        {
            return Falha(ex.Message);
        }
    }

    /// <summary>
    /// Exclui um lançamento gravado (a tela pede a confirmação). Recusa período fechado, exercício encerrado, ECD já gerada,
    /// data bloqueada, partida conciliada e lançamentos gerados por outros módulos (esses se desfazem no módulo de origem).
    /// Se o lançamento faz par com um estorno (ou é o estorno), os dois são excluídos juntos. Devolve os lançamentos removidos.
    /// </summary>
    public async Task<Resultado<IReadOnlyList<LancamentoEfetivado>>> ExcluirAsync(long empresaId, long lancamentoId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<LancamentoEfetivado>>(usuarioId, empresaId, Permissao.LancamentoEstornar, ct) is { } negado) return negado;
        var sit = await repositorio.ObterSituacaoLancamentoAsync(empresaId, lancamentoId, ct);
        if (sit is null) return Resultado<IReadOnlyList<LancamentoEfetivado>>.Falha("Lançamento não encontrado.");
        var impedimento = ImpedimentoManutencao(sit, "excluído");
        if (impedimento is not null) return Resultado<IReadOnlyList<LancamentoEfetivado>>.Falha(impedimento);
        try
        {
            return Resultado<IReadOnlyList<LancamentoEfetivado>>.Ok(await repositorio.ExcluirLancamentoAsync(empresaId, lancamentoId, usuarioId, ct));
        }
        catch (ViolacaoRegraException ex)
        {
            return Resultado<IReadOnlyList<LancamentoEfetivado>>.Falha(ex.Message);
        }
    }

    /// <summary>Mensagem clara do que impede excluir/alterar o lançamento (null = pode). <paramref name="verbo"/>: "excluído" ou "alterado".</summary>
    internal static string? ImpedimentoManutencao(SituacaoLancamento s, string verbo)
    {
        var nome = $"O lançamento nº {s.Numero}";
        if (s.Status == StatusLancamento.Rascunho) return $"{nome} é um rascunho.";
        foreach (var o in s.Vinculados.Select(v => v.Origem).Append(s.Origem).Distinct())
        {
            var origem = o switch
            {
                OrigemLancamento.NFe or OrigemLancamento.NFSe or OrigemLancamento.CTe =>
                    "pelo módulo Fiscal; desfaça a contabilização em Documentos fiscais › Contabilizar notas fiscais",
                OrigemLancamento.ApuracaoTributo => "pelo módulo Fiscal; desfaça a provisão na tela de Apuração correspondente",
                OrigemLancamento.Folha => "pelo módulo Folha; desfaça a contabilização em Cálculos › Folha › Contabilização",
                OrigemLancamento.Depreciacao => "pelo módulo Imobilizado; desfaça o lançamento em Imobilizado › Cálculo e lançamento da depreciação",
                OrigemLancamento.Frota => "pelo módulo Frota; desfaça a contabilização em Contabilização em lote (ou estorne o pagamento da diária)",
                OrigemLancamento.Encerramento => "pelo encerramento do exercício; desfaça-o em Lançamentos › Encerramento do exercício",
                _ => null,
            };
            if (origem is not null) return $"{nome} foi gerado {origem}. Ele não pode ser {verbo} por aqui.";
        }
        if (s.Indicador == IndicadorLancamento.Encerramento)
            return $"{nome} é de encerramento do exercício: desfaça-o em Lançamentos › Encerramento do exercício.";
        if (s.ExercicioEncerrado) return $"O exercício {s.Ano} está encerrado: o lançamento nº {s.Numero} não pode ser {verbo}.";
        if (s.EcdGerada) return $"Já existe ECD gerada para {s.Ano}: o lançamento nº {s.Numero} não pode ser {verbo}.";
        if (!s.PeriodoAberto) return $"O período {s.Data:MM/yyyy} está fechado: reabra-o para que o lançamento nº {s.Numero} possa ser {verbo}.";
        if (s.DataBloqueio is { } b && s.Data <= b && s.Origem != OrigemLancamento.SaldoInicial)
            return $"Lançamentos até {b:dd/MM/yyyy} estão bloqueados nesta empresa: o lançamento nº {s.Numero} não pode ser {verbo}.";
        if (s.Conciliado) return $"{nome} tem partida conciliada: desfaça a conciliação antes.";
        if (verbo == "alterado" && (s.Status == StatusLancamento.Estornado || s.EstornoDeId is not null || s.Vinculados.Count > 0))
            return $"{nome} faz parte de um estorno e não pode ser alterado.";
        return null;
    }

    /// <summary>
    /// Implantação: um único lançamento na data de início do exercício com os saldos das contas patrimoniais
    /// (Ativo, Passivo e PL). Os saldos devem fechar (débitos = créditos), como em qualquer balanço.
    /// </summary>
    public async Task<Resultado<LancamentoEfetivado>> LancarSaldosIniciaisAsync(long empresaId, int anoExercicio,
        IReadOnlyList<SaldoInicialConta> saldos, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<LancamentoEfetivado>(usuarioId, empresaId, Permissao.SaldosIniciais, ct) is { } negado) return negado;

        var periodo = await repositorio.ObterPeriodoAsync(empresaId, new DateOnly(anoExercicio, 12, 31), ct);
        if (periodo is null) return Resultado<LancamentoEfetivado>.Falha($"Exercício {anoExercicio} não cadastrado.");
        if (await repositorio.ObterNumeroLancamentoAtivoAsync(empresaId, periodo.ExercicioId, OrigemLancamento.SaldoInicial, ct) is long n)
            return Resultado<LancamentoEfetivado>.Falha($"Já existem saldos iniciais no exercício {anoExercicio} (lançamento nº {n}). Estorne-o para relançar.");

        var linhas = saldos.Where(s => s.Valor != 0).ToArray();
        if (linhas.Length == 0) return Resultado<LancamentoEfetivado>.Falha("Informe ao menos um saldo.");

        var contas = await repositorio.ObterContasAsync(empresaId, linhas.Select(l => l.CodigoConta).Distinct(), ct);
        var naoPatrimoniais = linhas.Where(l => contas.TryGetValue(l.CodigoConta, out var c) && !NaturezasPatrimoniais.Contains(c.CodNat))
                                    .Select(l => l.CodigoConta).ToArray();
        if (naoPatrimoniais.Length > 0)
            return Resultado<LancamentoEfetivado>.Falha(
                $"Saldos iniciais só em contas patrimoniais (Ativo, Passivo, PL). Contas de resultado informadas: {string.Join(", ", naoPatrimoniais)}.");

        var proposta = new LancamentoProposto(empresaId, periodo.InicioExercicio, OrigemLancamento.SaldoInicial,
            linhas.Select(l => new PartidaProposta(l.CodigoConta, l.Valor < 0 ? l.DC.Inverso() : l.DC, Math.Abs(l.Valor),
                                                   $"Saldo inicial em {periodo.InicioExercicio:dd/MM/yyyy}")).ToArray());
        return await GravarAsync(proposta, usuarioId, ct);
    }

    /// <summary>
    /// Valida vários lançamentos de uma vez (importação), carregando contas e períodos uma só vez.
    /// Não grava nada; o resultado de cada proposta vem na mesma posição.
    /// </summary>
    public async Task<IReadOnlyList<Resultado<LancamentoValidado>>> ValidarLoteAsync(IReadOnlyList<LancamentoProposto> propostas, CancellationToken ct = default)
    {
        if (propostas.Count == 0) return [];
        var empresaId = propostas[0].EmpresaId;
        var partidas = propostas.SelectMany(p => p.Partidas).ToArray();
        var contas = await repositorio.ObterContasAsync(empresaId, partidas.Select(x => x.CodigoConta).Distinct(), ct);
        var centros = partidas.Select(x => x.CodigoCentroCusto).OfType<string>().Where(s => s.Length > 0).Distinct().ToArray();
        var participantes = partidas.Select(x => x.CodigoParticipante).OfType<string>().Where(s => s.Length > 0).Distinct().ToArray();
        var mapaCentros = centros.Length == 0 ? new Dictionary<string, long>() : await repositorio.ObterCentrosCustoAsync(empresaId, centros, ct);
        var mapaPart = participantes.Length == 0 ? new Dictionary<string, long>() : await repositorio.ObterParticipantesAsync(empresaId, participantes, ct);
        var periodos = new Dictionary<(int, int), PeriodoInfo?>();
        var res = new List<Resultado<LancamentoValidado>>(propostas.Count);
        foreach (var p in propostas)
        {
            if (!periodos.TryGetValue((p.Data.Year, p.Data.Month), out var periodo))
                periodos[(p.Data.Year, p.Data.Month)] = periodo = await repositorio.ObterPeriodoAsync(empresaId, p.Data, ct);
            res.Add(ValidadorLancamento.Validar(p, new ContextoValidacao(contas, mapaCentros, mapaPart, periodo)));
        }
        return res;
    }

    /// <summary>
    /// Efetiva os lançamentos numa única transação (tudo ou nada). <paramref name="chaves"/> identifica a origem de cada
    /// um (ex.: transação do extrato) para não importar duas vezes.
    /// </summary>
    public async Task<Resultado<IReadOnlyList<LancamentoEfetivado>>> EfetivarLoteAsync(IReadOnlyList<LancamentoProposto> propostas,
        IReadOnlyList<string?>? chaves, long usuarioId, CancellationToken ct = default)
    {
        if (propostas.Count == 0) return Resultado<IReadOnlyList<LancamentoEfetivado>>.Falha("Nenhum lançamento para importar.");
        if (propostas.Any(p => p.EmpresaId != propostas[0].EmpresaId || p.Origem is OrigemLancamento.Estorno or OrigemLancamento.SaldoInicial))
            return Resultado<IReadOnlyList<LancamentoEfetivado>>.Falha("Lote inválido.");
        if (await autorizador.NegarAsync<IReadOnlyList<LancamentoEfetivado>>(usuarioId, propostas[0].EmpresaId, Permissao.LancamentoIncluir, ct) is { } negado)
            return negado;
        var validacoes = await ValidarLoteAsync(propostas, ct);
        var erros = validacoes.Select((v, i) => (v, i)).Where(x => !x.v.Sucesso)
                              .Select(x => $"Lançamento {x.i + 1}: {string.Join(" ", x.v.Erros)}").Take(30).ToArray();
        if (erros.Length > 0) return Resultado<IReadOnlyList<LancamentoEfetivado>>.Falha(erros);
        try
        {
            return Resultado<IReadOnlyList<LancamentoEfetivado>>.Ok(
                await repositorio.GravarEfetivadosAsync(validacoes.Select(v => v.Valor).ToList(), usuarioId, ct, chaves));
        }
        catch (ViolacaoRegraException ex)
        {
            return Resultado<IReadOnlyList<LancamentoEfetivado>>.Falha(ex.Message);
        }
    }

    private async Task<Resultado<LancamentoEfetivado>> GravarAsync(LancamentoProposto proposta, long usuarioId, CancellationToken ct)
    {
        var ctx = await CarregarContextoAsync(proposta, ct);
        var validacao = ValidadorLancamento.Validar(proposta, ctx);
        if (!validacao.Sucesso) return Resultado<LancamentoEfetivado>.Falha(validacao.Erros);

        try
        {
            return Resultado<LancamentoEfetivado>.Ok(await repositorio.GravarEfetivadoAsync(validacao.Valor, usuarioId, ct));
        }
        catch (ViolacaoRegraException ex)
        {
            return Resultado<LancamentoEfetivado>.Falha(ex.Message);
        }
    }

    private async Task<ContextoValidacao> CarregarContextoAsync(LancamentoProposto p, CancellationToken ct)
    {
        var contas = p.Partidas.Select(x => x.CodigoConta).Distinct().ToArray();
        var centros = p.Partidas.Select(x => x.CodigoCentroCusto).OfType<string>().Where(s => s.Length > 0).Distinct().ToArray();
        var participantes = p.Partidas.Select(x => x.CodigoParticipante).OfType<string>().Where(s => s.Length > 0).Distinct().ToArray();

        return new ContextoValidacao(
            await repositorio.ObterContasAsync(p.EmpresaId, contas, ct),
            centros.Length == 0 ? new Dictionary<string, long>() : await repositorio.ObterCentrosCustoAsync(p.EmpresaId, centros, ct),
            participantes.Length == 0 ? new Dictionary<string, long>() : await repositorio.ObterParticipantesAsync(p.EmpresaId, participantes, ct),
            await repositorio.ObterPeriodoAsync(p.EmpresaId, p.Data, ct));
    }
}
