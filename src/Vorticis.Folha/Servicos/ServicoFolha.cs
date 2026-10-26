using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Folha.Cadastros;
using Vorticis.Folha.Regras;

namespace Vorticis.Folha.Servicos;

public sealed record ResumoCalculo(int Calculados, IReadOnlyList<string> Avisos, decimal TotalProventos, decimal TotalDescontos, decimal Liquido);

/// <summary>Saldo de um período aquisitivo de férias do funcionário.</summary>
public sealed record SaldoFerias(PeriodoAquisitivo Periodo, int DiasDireito, int DiasUsados, bool Completo, bool ConcessivoVencido)
{
    public int Saldo => Math.Max(0, DiasDireito - DiasUsados);
}

/// <summary>
/// Cálculo da folha: mensal, férias, 13º salário, rescisões e provisões, com fechamento/reabertura da competência.
/// Toda operação verifica a permissão do usuário (FOLHA_*) na empresa; as contas e valores legais vêm do banco.
/// </summary>
public sealed class ServicoFolha(IRepositorioCadastrosFolha cadastros, IRepositorioMovimentoFolha movimento, IRepositorioContabilizacaoFolha contabilizacao,
                                 Autorizador autorizador)
{
    private static DateOnly Mes(DateOnly d) => new(d.Year, d.Month, 1);

    private sealed record Base(TabelasLegais Tabelas, IReadOnlyDictionary<string, RubricaCalc> Rubricas, ParametrosEncargos Encargos);

    private async Task<Base> CarregarAsync(long empresaId, DateOnly competencia, CancellationToken ct)
    {
        await cadastros.GarantirRubricasPadraoAsync(empresaId, ct);
        var tabelas = TabelasLegais.Em(competencia, await cadastros.ListarParametrosLegaisAsync(ct));
        var rubricas = (await cadastros.ListarRubricasAsync(empresaId, ct)).ToDictionary(r => r.Codigo, r => r.ParaCalculo());
        var p = await cadastros.ObterParametrosAsync(empresaId, ct);
        var regime = await cadastros.ObterRegimeTributarioAsync(empresaId, competencia, ct);
        return new Base(tabelas, rubricas, new ParametrosEncargos(p.RatAliquota, p.Fap, p.TerceirosAliquota, RegimeEncargosExtensoes.De(regime, p.SimplesAnexo)));
    }

    private async Task<string?> CompetenciaFechadaAsync(long empresaId, DateOnly competencia, CancellationToken ct)
        => (await movimento.ObterCompetenciaAsync(empresaId, competencia, ct)).Fechada
            ? $"A competência {competencia:MM/yyyy} da folha está fechada. Reabra-a para alterar."
            : null;

    private async Task<string?> ContabilizadaAsync(long empresaId, DateOnly competencia, CancellationToken ct)
        => await contabilizacao.ObterContabilizacaoAtivaAsync(empresaId, competencia, "FOLHA", ct) is not null
            ? $"A folha de {competencia:MM/yyyy} está contabilizada. Desfaça a contabilização antes."
            : null;

    // ------------------------------------------------------------------ competências

    public async Task<IReadOnlyList<StatusCompetencia>> ListarCompetenciasAsync(long empresaId, int ano, long usuarioId, CancellationToken ct = default)
        => await autorizador.PodeVerAsync(usuarioId, empresaId, ct) ? await movimento.ListarCompetenciasAsync(empresaId, ano, ct) : [];

    public async Task<StatusCompetencia> ObterCompetenciaAsync(long empresaId, DateOnly competencia, long usuarioId, CancellationToken ct = default)
        => await autorizador.PodeVerAsync(usuarioId, empresaId, ct) ? await movimento.ObterCompetenciaAsync(empresaId, Mes(competencia), ct) : new StatusCompetencia(Mes(competencia), false, null);

    public async Task<Resultado> FecharAsync(long empresaId, DateOnly competencia, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FolhaFechar, ct) is { } negado) return Resultado.Falha(negado);
        competencia = Mes(competencia);
        if ((await movimento.ObterCompetenciaAsync(empresaId, competencia, ct)).Fechada) return Resultado.Falha($"A competência {competencia:MM/yyyy} já está fechada.");
        var calculos = await movimento.ListarCalculosAsync(empresaId, competencia, null, false, ct);
        if (calculos.Count == 0 && await movimento.ContarProLaboreAsync(empresaId, competencia, ct) == 0)
            return Resultado.Falha($"Não há folha nem pró-labore calculados em {competencia:MM/yyyy}.");
        var anterior = competencia.AddMonths(-1);
        if (!(await movimento.ObterCompetenciaAsync(empresaId, anterior, ct)).Fechada && (await movimento.ListarCalculosAsync(empresaId, anterior, null, false, ct)).Count > 0)
            return Resultado.Falha($"Feche antes a competência {anterior:MM/yyyy}.");
        await movimento.AlterarCompetenciaAsync(empresaId, competencia, fechar: true, usuarioId, ct);
        return Resultado.Ok();
    }

    public async Task<Resultado> ReabrirAsync(long empresaId, DateOnly competencia, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FolhaReabrir, ct) is { } negado) return Resultado.Falha(negado);
        competencia = Mes(competencia);
        if (!(await movimento.ObterCompetenciaAsync(empresaId, competencia, ct)).Fechada) return Resultado.Falha($"A competência {competencia:MM/yyyy} já está aberta.");
        if (await ContabilizadaAsync(empresaId, competencia, ct) is { } contabilizada) return Resultado.Falha(contabilizada);
        var posterior = competencia.AddMonths(1);
        if ((await movimento.ObterCompetenciaAsync(empresaId, posterior, ct)).Fechada)
            return Resultado.Falha($"Reabra antes a competência {posterior:MM/yyyy}.");
        await movimento.AlterarCompetenciaAsync(empresaId, competencia, fechar: false, usuarioId, ct);
        return Resultado.Ok();
    }

    // ------------------------------------------------------------------ eventos variáveis

    public async Task<IReadOnlyList<EventoVariavel>> ListarEventosAsync(long empresaId, DateOnly competencia, long usuarioId, CancellationToken ct = default)
        => await autorizador.PodeVerAsync(usuarioId, empresaId, ct) ? await movimento.ListarEventosAsync(empresaId, Mes(competencia), ct) : [];

    /// <summary>Rubricas que o lançamento de eventos aceita: as de referência (horas/dias) e as de valor informado.</summary>
    public static readonly IReadOnlySet<string> RubricasPorReferencia = new HashSet<string>
    {
        RubricasPadrao.HoraExtra50, RubricasPadrao.HoraExtra100, RubricasPadrao.AdicionalNoturno, RubricasPadrao.Faltas, RubricasPadrao.DsrFaltas,
    };

    public async Task<Resultado<long>> LancarEventoAsync(long empresaId, long funcionarioId, DateOnly competencia, string rubricaCodigo, decimal referencia,
                                                         decimal? valor, string? observacao, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.FolhaEventosLancar, ct) is { } negado) return negado;
        competencia = Mes(competencia);
        if (await CompetenciaFechadaAsync(empresaId, competencia, ct) is { } fechada) return Resultado<long>.Falha(fechada);
        var f = await cadastros.ObterFuncionarioAsync(empresaId, funcionarioId, ct);
        if (f is null) return Resultado<long>.Falha("Funcionário não encontrado.");
        if (f.Situacao == "DESLIGADO") return Resultado<long>.Falha("O funcionário está desligado.");
        await cadastros.GarantirRubricasPadraoAsync(empresaId, ct);
        var rubrica = (await cadastros.ListarRubricasAsync(empresaId, ct)).FirstOrDefault(r => r.Codigo == rubricaCodigo);
        if (rubrica is null || !rubrica.Ativa) return Resultado<long>.Falha("Rubrica não encontrada ou inativa.");
        if (rubrica.Tipo == 'I') return Resultado<long>.Falha("Rubricas informativas não recebem lançamento.");
        if (RubricasAutomaticas.Contains(rubricaCodigo)) return Resultado<long>.Falha($"A rubrica {rubricaCodigo} – {rubrica.Descricao} é calculada automaticamente.");
        if (RubricasPorReferencia.Contains(rubricaCodigo))
        {
            if (referencia <= 0) return Resultado<long>.Falha($"Informe a quantidade (horas ou dias) de {rubrica.Descricao}.");
            valor = null;
        }
        else if (valor is not > 0) return Resultado<long>.Falha($"Informe o valor de {rubrica.Descricao}.");
        if (f.Admissao > new DateOnly(competencia.Year, competencia.Month, DateTime.DaysInMonth(competencia.Year, competencia.Month)))
            return Resultado<long>.Falha("O funcionário foi admitido depois desta competência.");
        try { return Resultado<long>.Ok(await movimento.GravarEventoAsync(empresaId, funcionarioId, competencia, rubricaCodigo, referencia, valor, observacao, ct)); }
        catch (ViolacaoRegraException ex) { return Resultado<long>.Falha(ex.Message); }
    }

    /// <summary>A rubrica é calculada pelo sistema (não recebe lançamento de evento).</summary>
    public static bool EhAutomatica(string codigo) => RubricasAutomaticas.Contains(codigo);

    /// <summary>O evento é informado por quantidade (horas/dias) e não por valor.</summary>
    public static bool UsaReferencia(string codigo) => RubricasPorReferencia.Contains(codigo);

    private static readonly HashSet<string> RubricasAutomaticas =
    [
        RubricasPadrao.Salario, RubricasPadrao.Dsr, RubricasPadrao.Insalubridade, RubricasPadrao.Periculosidade, RubricasPadrao.SalarioFamilia,
        RubricasPadrao.Inss, RubricasPadrao.Irrf, RubricasPadrao.InssDecimo, RubricasPadrao.IrrfDecimo, RubricasPadrao.Decimo1, RubricasPadrao.Decimo2,
        RubricasPadrao.FeriasGozadas, RubricasPadrao.TercoFerias, RubricasPadrao.AbonoPecuniario, RubricasPadrao.TercoAbono, RubricasPadrao.SaldoSalario,
        RubricasPadrao.AvisoPrevioIndenizado, RubricasPadrao.FeriasVencidas, RubricasPadrao.TercoFeriasVencidas, RubricasPadrao.FeriasProporcionais,
        RubricasPadrao.TercoFeriasProporcionais, RubricasPadrao.DecimoProporcional, RubricasPadrao.AvisoNaoCumprido, RubricasPadrao.AdiantamentoDecimo,
    ];

    public async Task<Resultado> ExcluirEventoAsync(long empresaId, DateOnly competencia, long eventoId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FolhaEventosLancar, ct) is { } negado) return Resultado.Falha(negado);
        if (await CompetenciaFechadaAsync(empresaId, Mes(competencia), ct) is { } fechada) return Resultado.Falha(fechada);
        await movimento.ExcluirEventoAsync(empresaId, eventoId, ct);
        return Resultado.Ok();
    }

    // ------------------------------------------------------------------ folha mensal

    private static ContextoCalculo Contexto(DateOnly competencia, Base b, AcumuladoMes? acum = null)
        => new(competencia, b.Tabelas, b.Rubricas, acum ?? AcumuladoMes.Nenhum);

    private async Task<AcumuladoMes> AcumuladoFeriasAsync(long empresaId, long funcionarioId, DateOnly competencia, long? exceto, CancellationToken ct)
    {
        var ferias = (await movimento.ListarCalculosAsync(empresaId, competencia, TipoFolha.Ferias, false, ct))
            .Where(c => c.FuncionarioId == funcionarioId && c.OrigemId != exceto).ToList();
        return new AcumuladoMes(ferias.Sum(c => c.BaseInss), ferias.Sum(c => c.Inss), ferias.Sum(c => c.BaseIrrf), ferias.Sum(c => c.Irrf));
    }

    private async Task<decimal> MediaVariaveisAsync(long empresaId, long funcionarioId, DateOnly competencia, CancellationToken ct)
    {
        var (soma, meses) = await movimento.VariaveisUltimosMesesAsync(empresaId, funcionarioId, competencia, ct);
        return PeriodosFerias.MediaSimples(soma, meses);
    }

    /// <summary>Calcula (ou recalcula) a folha mensal da competência para todos os funcionários elegíveis, ou só para um.</summary>
    public async Task<Resultado<ResumoCalculo>> CalcularMensalAsync(long empresaId, DateOnly competencia, long usuarioId, long? funcionarioId = null,
                                                                   CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<ResumoCalculo>(usuarioId, empresaId, Permissao.FolhaCalcular, ct) is { } negado) return negado;
        competencia = Mes(competencia);
        if (await CompetenciaFechadaAsync(empresaId, competencia, ct) is { } fechada) return Resultado<ResumoCalculo>.Falha(fechada);
        if (await ContabilizadaAsync(empresaId, competencia, ct) is { } contab) return Resultado<ResumoCalculo>.Falha(contab);

        var b = await CarregarAsync(empresaId, competencia, ct);
        var fimMes = competencia.AddMonths(1).AddDays(-1);
        var funcionarios = (await cadastros.ListarFuncionariosAsync(empresaId, ct))
            .Where(f => funcionarioId is null || f.Id == funcionarioId).ToList();
        var eventos = (await movimento.ListarEventosAsync(empresaId, competencia, ct)).GroupBy(e => e.FuncionarioId)
            .ToDictionary(g => g.Key, g => g.Select(e => new EventoCalc(e.RubricaCodigo, e.Referencia, e.Valor)).ToList());
        var avisos = new List<string>();
        int calculados = 0;
        decimal proventos = 0, descontos = 0;
        foreach (var f in funcionarios)
        {
            if (f.Admissao > fimMes) continue;
            if (f.Situacao == "DESLIGADO") { if (f.Desligamento is null || Mes(f.Desligamento.Value) >= competencia) avisos.Add($"{f.Nome}: desligado – use a rescisão."); continue; }
            if (f.Situacao == "AFASTADO") { avisos.Add($"{f.Nome}: afastado – não calculado (altere a situação para ATIVO para incluir)."); continue; }
            try
            {
                var calc = f.ParaCalculo();
                var acum = await AcumuladoFeriasAsync(empresaId, f.Id, competencia, null, ct);
                var r = CalculadoraFolhaMensal.Calcular(calc, eventos.GetValueOrDefault(f.Id) ?? [], Contexto(competencia, b, acum));
                var enc = CalculoEncargos.Calcular(r.BaseInss, r.Fgts, b.Encargos, !calc.Estagiario);
                await movimento.GravarCalculoAsync(empresaId, new NovoCalculo(f.Id, competencia, TipoFolha.Mensal, null, null, f.CentroCustoId, r, enc), usuarioId, ct);
                calculados++;
                proventos += r.TotalProventos;
                descontos += r.TotalDescontos;
            }
            catch (ErroCalculoFolhaException ex) { avisos.Add(ex.Message); }
        }
        return Resultado<ResumoCalculo>.Ok(new ResumoCalculo(calculados, avisos, proventos, descontos, proventos - descontos));
    }

    public async Task<IReadOnlyList<CalculoGravado>> ListarCalculosAsync(long empresaId, DateOnly competencia, TipoFolha? tipo, long usuarioId, bool comItens = false,
                                                                         CancellationToken ct = default)
        => await autorizador.PodeVerAsync(usuarioId, empresaId, ct) ? await movimento.ListarCalculosAsync(empresaId, Mes(competencia), tipo, comItens, ct) : [];

    public async Task<CalculoGravado?> ObterCalculoAsync(long empresaId, long calculoId, long usuarioId, CancellationToken ct = default)
        => await autorizador.PodeVerAsync(usuarioId, empresaId, ct) ? await movimento.ObterCalculoAsync(empresaId, calculoId, ct) : null;

    // ------------------------------------------------------------------ férias

    public async Task<IReadOnlyList<SaldoFerias>> ListarSaldosFeriasAsync(long empresaId, long funcionarioId, DateOnly em, long usuarioId, CancellationToken ct = default)
    {
        if (!await autorizador.PodeVerAsync(usuarioId, empresaId, ct)) return [];
        var f = await cadastros.ObterFuncionarioAsync(empresaId, funcionarioId, ct);
        if (f is null) return [];
        var usadas = (await movimento.ListarFeriasAsync(empresaId, funcionarioId, ct)).Where(x => x.Status == "CALCULADA").ToList();
        return PeriodosFerias.Calcular(f.Admissao, f.Desligamento is { } d && d < em ? d : em).Select(p =>
        {
            var doPeriodo = usadas.Where(u => u.AquisitivoInicio == p.Inicio).ToList();
            var faltas = doPeriodo.Select(u => u.Faltas).DefaultIfEmpty(0).Max();
            return new SaldoFerias(p, PeriodosFerias.DiasDireito(faltas), doPeriodo.Sum(u => u.DiasGozo + u.AbonoDias), p.Completo(em), p.ConcessivoVencido(em));
        }).ToList();
    }

    public async Task<IReadOnlyList<FeriasRegistro>> ListarFeriasAsync(long empresaId, long? funcionarioId, long usuarioId, CancellationToken ct = default)
        => await autorizador.PodeVerAsync(usuarioId, empresaId, ct) ? await movimento.ListarFeriasAsync(empresaId, funcionarioId, ct) : [];

    /// <summary>Calcula as férias de um período aquisitivo, registra e grava o cálculo na competência do pagamento.</summary>
    public async Task<Resultado<long>> CalcularFeriasAsync(long empresaId, long funcionarioId, DateOnly aquisitivoInicio, DateOnly gozoInicio, int diasGozo,
                                                           int abonoDias, int faltas, DateOnly pagamento, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.FolhaCalcular, ct) is { } negado) return negado;
        var competencia = Mes(pagamento);
        if (await CompetenciaFechadaAsync(empresaId, competencia, ct) is { } fechada) return Resultado<long>.Falha(fechada);
        var f = await cadastros.ObterFuncionarioAsync(empresaId, funcionarioId, ct);
        if (f is null) return Resultado<long>.Falha("Funcionário não encontrado.");
        if (f.Situacao == "DESLIGADO") return Resultado<long>.Falha("O funcionário está desligado.");
        var periodo = PeriodosFerias.Calcular(f.Admissao, gozoInicio).FirstOrDefault(p => p.Inicio == aquisitivoInicio);
        if (periodo is null) return Resultado<long>.Falha("Período aquisitivo não encontrado para o funcionário.");
        if (!periodo.Completo(gozoInicio)) return Resultado<long>.Falha($"O período aquisitivo {periodo.Inicio:dd/MM/yyyy} a {periodo.Fim:dd/MM/yyyy} só se completa em {periodo.Fim:dd/MM/yyyy}.");
        if (pagamento > gozoInicio) return Resultado<long>.Falha("O pagamento das férias deve ser feito até o início do gozo (CLT, art. 145).");

        var usadas = (await movimento.ListarFeriasAsync(empresaId, funcionarioId, ct)).Where(x => x.Status == "CALCULADA" && x.AquisitivoInicio == periodo.Inicio).ToList();
        var b = await CarregarAsync(empresaId, competencia, ct);
        var media = await MediaVariaveisAsync(empresaId, funcionarioId, competencia, ct);
        var acum = await AcumuladoFeriasAsync(empresaId, funcionarioId, competencia, null, ct);
        var solicitacao = new SolicitacaoFerias(gozoInicio, diasGozo, abonoDias, Math.Max(faltas, usadas.Select(u => u.Faltas).DefaultIfEmpty(0).Max()), media,
                                                usadas.Sum(u => u.DiasGozo + u.AbonoDias));
        try
        {
            var calc = f.ParaCalculo();
            var r = CalculadoraFerias.Calcular(calc, solicitacao, Contexto(competencia, b, acum));
            var enc = CalculoEncargos.Calcular(r.BaseInss, r.Fgts, b.Encargos, !calc.Estagiario);
            var id = await movimento.GravarFeriasAsync(empresaId, funcionarioId, periodo, gozoInicio, diasGozo, abonoDias, solicitacao.FaltasInjustificadas, pagamento, ct);
            await movimento.GravarCalculoAsync(empresaId, new NovoCalculo(funcionarioId, competencia, TipoFolha.Ferias, id, pagamento, f.CentroCustoId, r, enc), usuarioId, ct);
            return Resultado<long>.Ok(id);
        }
        catch (ErroCalculoFolhaException ex) { return Resultado<long>.Falha(ex.Message); }
        catch (ViolacaoRegraException ex) { return Resultado<long>.Falha(ex.Message); }
    }

    public async Task<Resultado> CancelarFeriasAsync(long empresaId, long feriasId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FolhaCalcular, ct) is { } negado) return Resultado.Falha(negado);
        var ferias = await movimento.ObterFeriasAsync(empresaId, feriasId, ct);
        if (ferias is null || ferias.Status != "CALCULADA") return Resultado.Falha("Férias não encontradas ou já canceladas.");
        var competencia = Mes(ferias.DataPagamento);
        if (await CompetenciaFechadaAsync(empresaId, competencia, ct) is { } fechada) return Resultado.Falha(fechada);
        if (await ContabilizadaAsync(empresaId, competencia, ct) is { } contab) return Resultado.Falha(contab);
        await movimento.ExcluirCalculosAsync(empresaId, competencia, TipoFolha.Ferias, feriasId, ct);
        await movimento.CancelarFeriasAsync(empresaId, feriasId, ct);
        return Resultado.Ok();
    }

    // ------------------------------------------------------------------ 13º salário

    /// <summary>
    /// 13º salário do ano: parcela 1 (competência novembro) ou 2 (dezembro) para todos os funcionários ativos com avos no ano.
    /// </summary>
    public async Task<Resultado<ResumoCalculo>> CalcularDecimoAsync(long empresaId, int ano, int parcela, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<ResumoCalculo>(usuarioId, empresaId, Permissao.FolhaCalcular, ct) is { } negado) return negado;
        if (parcela is not (1 or 2)) return Resultado<ResumoCalculo>.Falha("Informe a parcela 1 ou 2.");
        var competencia = new DateOnly(ano, parcela == 1 ? 11 : 12, 1);
        var tipo = parcela == 1 ? TipoFolha.Decimo1 : TipoFolha.Decimo2;
        if (await CompetenciaFechadaAsync(empresaId, competencia, ct) is { } fechada) return Resultado<ResumoCalculo>.Falha(fechada);
        if (await ContabilizadaAsync(empresaId, competencia, ct) is { } contab) return Resultado<ResumoCalculo>.Falha(contab);

        var b = await CarregarAsync(empresaId, competencia, ct);
        var avisos = new List<string>();
        int calculados = 0;
        decimal proventos = 0, descontos = 0;
        foreach (var f in await cadastros.ListarFuncionariosAsync(empresaId, ct))
        {
            if (f.Situacao != "ATIVO") { if (f.Situacao == "AFASTADO") avisos.Add($"{f.Nome}: afastado – não calculado."); continue; }
            var avos = CalculadoraDecimoTerceiro.Avos(f.Admissao, null, ano);
            if (avos == 0) continue;
            var calc = f.ParaCalculo();
            ResultadoFolha r;
            if (parcela == 1) r = CalculadoraDecimoTerceiro.PrimeiraParcela(calc, avos, Contexto(competencia, b));
            else
            {
                var media = await MediaVariaveisAsync(empresaId, f.Id, competencia, ct);
                var paga = await movimento.TotalDecimoPrimeiraParcelaAsync(empresaId, f.Id, ano, ct);
                r = CalculadoraDecimoTerceiro.SegundaParcela(calc, avos, media, paga, Contexto(competencia, b));
            }
            var enc = CalculoEncargos.Calcular(r.BaseInss, r.Fgts, b.Encargos, !calc.Estagiario);
            await movimento.GravarCalculoAsync(empresaId, new NovoCalculo(f.Id, competencia, tipo, null, null, f.CentroCustoId, r, enc), usuarioId, ct);
            calculados++;
            proventos += r.TotalProventos;
            descontos += r.TotalDescontos;
        }
        return Resultado<ResumoCalculo>.Ok(new ResumoCalculo(calculados, avisos, proventos, descontos, proventos - descontos));
    }

    // ------------------------------------------------------------------ rescisão

    public async Task<IReadOnlyList<RescisaoRegistro>> ListarRescisoesAsync(long empresaId, long usuarioId, CancellationToken ct = default)
        => await autorizador.PodeVerAsync(usuarioId, empresaId, ct) ? await movimento.ListarRescisoesAsync(empresaId, ct) : [];

    /// <summary>Saldo estimado do FGTS: soma dos depósitos calculados na folha (a base da multa deve ser conferida no extrato da Caixa).</summary>
    public async Task<decimal> SaldoFgtsEstimadoAsync(long empresaId, long funcionarioId, DateOnly ate, long usuarioId, CancellationToken ct = default)
        => await autorizador.PodeVerAsync(usuarioId, empresaId, ct) ? await movimento.FgtsAcumuladoAsync(empresaId, funcionarioId, ate, ct) : 0;

    public async Task<Resultado<long>> CalcularRescisaoAsync(long empresaId, long funcionarioId, MotivoRescisao motivo, DateOnly desligamento, TipoAviso aviso,
                                                             decimal? saldoFgts, DateOnly pagamento, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.FolhaCalcular, ct) is { } negado) return negado;
        var competencia = Mes(desligamento);
        if (await CompetenciaFechadaAsync(empresaId, competencia, ct) is { } fechada) return Resultado<long>.Falha(fechada);
        if (await ContabilizadaAsync(empresaId, competencia, ct) is { } contab) return Resultado<long>.Falha(contab);
        var f = await cadastros.ObterFuncionarioAsync(empresaId, funcionarioId, ct);
        if (f is null) return Resultado<long>.Falha("Funcionário não encontrado.");
        if (f.Situacao == "DESLIGADO") return Resultado<long>.Falha("O funcionário já está desligado.");
        if (pagamento < desligamento) return Resultado<long>.Falha("A data de pagamento não pode ser anterior ao desligamento.");

        var b = await CarregarAsync(empresaId, competencia, ct);
        var calc = f.ParaCalculo();
        var media = await MediaVariaveisAsync(empresaId, funcionarioId, competencia, ct);
        var saldo = saldoFgts ?? await movimento.FgtsAcumuladoAsync(empresaId, funcionarioId, competencia.AddMonths(-1), ct);
        var primeira = await movimento.TotalDecimoPrimeiraParcelaAsync(empresaId, funcionarioId, desligamento.Year, ct);

        // Férias vencidas e não gozadas (períodos anteriores ao período proporcional)
        var rascunho = new SolicitacaoRescisao(motivo, desligamento, aviso, media, saldo, [], primeira);
        var projetada = CalculadoraRescisao.DataProjetada(f.Admissao, rascunho);
        var inicioProporcional = f.Admissao.AddYears(Math.Max(0, CalculadoraRescisao.AnosCompletos(f.Admissao, projetada)));
        var gozadas = (await movimento.ListarFeriasAsync(empresaId, funcionarioId, ct)).Where(x => x.Status == "CALCULADA").ToList();
        var vencidas = new List<FeriasVencidas>();
        foreach (var p in PeriodosFerias.Calcular(f.Admissao, projetada).Where(p => p.Inicio < inicioProporcional))
        {
            var doPeriodo = gozadas.Where(g => g.AquisitivoInicio == p.Inicio).ToList();
            var direito = PeriodosFerias.DiasDireito(doPeriodo.Select(g => g.Faltas).DefaultIfEmpty(0).Max());
            var pendentes = direito - doPeriodo.Sum(g => g.DiasGozo + g.AbonoDias);
            if (pendentes > 0) vencidas.Add(new FeriasVencidas(p.Inicio, pendentes, desligamento > p.FimConcessivo));
        }
        var solicitacao = rascunho with { Vencidas = vencidas };
        try
        {
            var r = CalculadoraRescisao.Calcular(calc, solicitacao, Contexto(competencia, b));
            var enc = CalculoEncargos.Calcular(r.BaseInss + r.Itens.Where(i => i.Codigo == RubricasPadrao.DecimoProporcional).Sum(i => i.Valor), r.Fgts, b.Encargos, !calc.Estagiario);
            var dias = r.Itens.Where(i => i.Codigo == RubricasPadrao.AvisoPrevioIndenizado).Select(i => (int)i.Referencia).FirstOrDefault();
            var id = await movimento.GravarRescisaoAsync(empresaId, funcionarioId, solicitacao, dias, pagamento, ct);
            await movimento.ExcluirCalculosAsync(empresaId, competencia, TipoFolha.Mensal, null, ct, funcionarioId);
            await movimento.GravarCalculoAsync(empresaId, new NovoCalculo(funcionarioId, competencia, TipoFolha.Rescisao, id, pagamento, f.CentroCustoId, r, enc), usuarioId, ct);
            await cadastros.DefinirDesligamentoAsync(empresaId, funcionarioId, desligamento, motivo.Sql(), usuarioId, ct);
            return Resultado<long>.Ok(id);
        }
        catch (ErroCalculoFolhaException ex) { return Resultado<long>.Falha(ex.Message); }
        catch (ViolacaoRegraException ex) { return Resultado<long>.Falha(ex.Message); }
    }

    public async Task<Resultado> CancelarRescisaoAsync(long empresaId, long rescisaoId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FolhaCalcular, ct) is { } negado) return Resultado.Falha(negado);
        var r = await movimento.ObterRescisaoAsync(empresaId, rescisaoId, ct);
        if (r is null || r.Status != "CALCULADA") return Resultado.Falha("Rescisão não encontrada ou já cancelada.");
        var competencia = Mes(r.Desligamento);
        if (await CompetenciaFechadaAsync(empresaId, competencia, ct) is { } fechada) return Resultado.Falha(fechada);
        if (await ContabilizadaAsync(empresaId, competencia, ct) is { } contab) return Resultado.Falha(contab);
        await movimento.ExcluirCalculosAsync(empresaId, competencia, TipoFolha.Rescisao, rescisaoId, ct);
        await movimento.CancelarRescisaoAsync(empresaId, rescisaoId, ct);
        await cadastros.DefinirDesligamentoAsync(empresaId, r.FuncionarioId, null, null, usuarioId, ct);
        return Resultado.Ok();
    }

    // ------------------------------------------------------------------ provisões

    public async Task<Resultado<ResumoCalculo>> CalcularProvisoesAsync(long empresaId, DateOnly competencia, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<ResumoCalculo>(usuarioId, empresaId, Permissao.FolhaCalcular, ct) is { } negado) return negado;
        competencia = Mes(competencia);
        if (await contabilizacao.ObterContabilizacaoAtivaAsync(empresaId, competencia, "PROVISAO", ct) is not null)
            return Resultado<ResumoCalculo>.Falha($"As provisões de {competencia:MM/yyyy} estão contabilizadas. Desfaça a contabilização antes.");
        var b = await CarregarAsync(empresaId, competencia, ct);
        var provisoes = new List<(long, long?, ProvisaoMensal)>();
        var avisos = new List<string>();
        foreach (var f in await cadastros.ListarFuncionariosAsync(empresaId, ct))
        {
            var dias = Calendario.DiasTrabalhadosComerciais(competencia, f.Admissao, f.Desligamento);
            if (dias == 0 || f.Situacao == "DESLIGADO") continue;
            if (f.Estagiario()) { avisos.Add($"{f.Nome}: estagiário – sem provisão."); continue; }
            var calc = f.ParaCalculo();
            var media = await MediaVariaveisAsync(empresaId, f.Id, competencia, ct);
            var baseRem = PeriodosFerias.Remuneracao(calc, b.Tabelas.SalarioMinimo, media);
            var fracao = dias >= 15 ? 1m : 0m;
            provisoes.Add((f.Id, f.CentroCustoId, CalculoProvisoes.Calcular(calc, baseRem, fracao, b.Encargos, b.Tabelas.Fgts)));
        }
        await contabilizacao.SubstituirProvisoesAsync(empresaId, competencia, provisoes, ct);
        var total = provisoes.Sum(p => p.Item3.Total);
        return Resultado<ResumoCalculo>.Ok(new ResumoCalculo(provisoes.Count, avisos, total, 0, total));
    }

    public async Task<IReadOnlyList<ProvisaoGravada>> ListarProvisoesAsync(long empresaId, DateOnly competencia, long usuarioId, CancellationToken ct = default)
        => await autorizador.PodeVerAsync(usuarioId, empresaId, ct) ? await contabilizacao.ListarProvisoesAsync(empresaId, Mes(competencia), ct) : [];
}

internal static class FuncionarioExtensoes
{
    public static bool Estagiario(this Funcionario f) => f.Categoria == "901";
}
