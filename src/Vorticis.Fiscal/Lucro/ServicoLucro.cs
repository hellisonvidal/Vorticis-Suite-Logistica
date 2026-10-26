using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Comum;
using Vorticis.Fiscal.Documentos;
using Vorticis.Fiscal.Retencoes;

namespace Vorticis.Fiscal.Lucro;

/// <summary>Lançamento da Parte A do LALUR (IRPJ, registro M300 da ECF) ou do LACS (CSLL, M350); "AMBOS" vale para os dois.</summary>
public sealed record LancamentoLalur(long Id, int Ano, int Trimestre, string Tributo, string Tipo, string Descricao, decimal Valor, string? CodigoEcf, string? ContaCodigo);

/// <summary>Movimento da conta da Parte B (saldo inicial, prejuízo gerado, compensação).</summary>
public sealed record MovimentoParteB(long Id, string Tributo, string Tipo, int? Ano, int? Trimestre, decimal Valor, string? Descricao);

public sealed record ApuracaoLucroSalva(long Id, ResultadoLucro Resultado, long? LancamentoId);

public sealed record ApuracaoPisCofinsSalva(long Id, ResultadoPisCofins Resultado, long? LancamentoId);

/// <summary>Dados de partida de uma apuração do Lucro Real: resultado contábil, LALUR, saldos de prejuízos e retenções.</summary>
public sealed record PreparoReal(EntradaReal Entrada, decimal ResultadoContabil, decimal AjusteTributosSobreLucro, IReadOnlyList<string> Avisos);

public sealed record PreparoPresumido(EntradaPresumido Entrada, IReadOnlyList<string> Avisos);

public sealed record PreparoPisCofins(EntradaPisCofins Entrada, IReadOnlyList<string> Avisos);

public interface IRepositorioLucro
{
    Task<IReadOnlyList<LancamentoLalur>> ListarLalurAsync(long empresaId, int ano, int? trimestre, CancellationToken ct);
    Task<long> GravarLalurAsync(long empresaId, LancamentoLalur l, long? contaId, long usuarioId, CancellationToken ct);
    Task ExcluirLalurAsync(long empresaId, long id, CancellationToken ct);

    /// <summary>Totais do trimestre: (adições IRPJ, exclusões IRPJ, adições CSLL, exclusões CSLL).</summary>
    Task<(decimal AdicoesIrpj, decimal ExclusoesIrpj, decimal AdicoesCsll, decimal ExclusoesCsll)> SomarLalurAsync(long empresaId, int ano, int trimestre, CancellationToken ct);

    /// <summary>Saldo de prejuízo (IRPJ) ou base negativa (CSLL) disponível: saldo inicial mais o gerado, menos o compensado, em períodos anteriores ao informado.</summary>
    Task<decimal> SaldoParteBAsync(long empresaId, string tributo, int ano, int trimestre, CancellationToken ct);
    Task<IReadOnlyList<MovimentoParteB>> ListarParteBAsync(long empresaId, string tributo, CancellationToken ct);
    Task DefinirSaldoInicialAsync(long empresaId, string tributo, decimal valor, long usuarioId, CancellationToken ct);

    /// <summary>Resultado contábil do período (receitas − despesas das contas de resultado) e movimento líquido das contas de despesa de IRPJ/CSLL configuradas.</summary>
    Task<(decimal Resultado, decimal DespesaTributosLucro)> ResultadoContabilAsync(long empresaId, DateOnly inicio, DateOnly fim, CancellationToken ct);

    /// <summary>Grava a apuração (substituindo a do mesmo período) e, no Lucro Real, o razão da Parte B (prejuízo gerado e compensações).</summary>
    Task<long> GravarApuracaoAsync(long empresaId, ResultadoLucro r, long usuarioId, CancellationToken ct);
    Task<IReadOnlyList<ApuracaoLucroSalva>> ListarApuracoesAsync(long empresaId, int ano, CancellationToken ct);
    Task<ApuracaoLucroSalva?> ObterApuracaoAsync(long empresaId, RegimeLucro regime, int ano, int trimestre, CancellationToken ct);
    Task ExcluirApuracaoAsync(long empresaId, long id, CancellationToken ct);

    Task<long> GravarPisCofinsAsync(long empresaId, ResultadoPisCofins r, long usuarioId, CancellationToken ct);
    Task<IReadOnlyList<ApuracaoPisCofinsSalva>> ListarPisCofinsAsync(long empresaId, int ano, CancellationToken ct);
    Task<ApuracaoPisCofinsSalva?> ObterPisCofinsAsync(long empresaId, DateOnly competencia, CancellationToken ct);
    Task ExcluirPisCofinsAsync(long empresaId, long id, CancellationToken ct);
}

/// <summary>
/// Apuração do Lucro Presumido e do Lucro Real (IRPJ e CSLL trimestrais), LALUR/LACS parte A e parte B básicos e PIS/COFINS mensais.
/// Os dados ficam guardados em colunas e tabelas próprias do esquema fiscal para a ECF ler depois.
/// </summary>
public sealed class ServicoLucro(IRepositorioLucro repositorio, IRepositorioDocumentos documentos, IRepositorioRetencoes retencoes,
                                 IParametrosFiscais parametros, Autorizador autorizador)
{
    public async Task<ParametrosLucro> ParametrosAsync(DateOnly data, CancellationToken ct = default) =>
        ParametrosLucro.Carregar(await parametros.ObterJsonAsync(ChavesParametro.LucroPresumidoReal, data, ct));

    private async Task<string?> NegarAsync(long empresaId, long usuarioId, Permissao permissao, CancellationToken ct) =>
        await autorizador.VerificarAsync(usuarioId, empresaId, permissao, ct);

    // ------------------------------------------------------------------ Lucro Presumido

    public async Task<Resultado<PreparoPresumido>> PrepararPresumidoAsync(long empresaId, int ano, int trimestre, long usuarioId, CancellationToken ct = default)
    {
        if (await NegarAsync(empresaId, usuarioId, Permissao.FiscalApurar, ct) is { } negado) return Resultado<PreparoPresumido>.Falha(negado);
        if (trimestre is < 1 or > 4) return Resultado<PreparoPresumido>.Falha("Trimestre inválido.");
        var (ini, fim) = CalculadoraLucro.Trimestre(ano, trimestre);
        var itens = await documentos.ListarItensAsync(empresaId, ini, fim, null, ct);
        var servicos = itens.Where(i => i.Situacao == SituacaoDocumento.Autorizada && i.Operacao == TipoOperacao.Saida && (i.Item.Cfop == Cfop.Servico || i.Item.Cfop is "5933" or "6933"))
                            .Sum(i => i.Item.ValorContabil);
        var total = LivrosFiscais.ReceitaLiquida(itens);
        var comercio = total - servicos;
        var receitas = new List<ReceitaPresumida>();
        if (comercio != 0) receitas.Add(new ReceitaPresumida("COMERCIO_INDUSTRIA", comercio));
        if (servicos != 0) receitas.Add(new ReceitaPresumida("SERVICOS_GERAIS", servicos));
        var irrf = await retencoes.SomarAsync(empresaId, SentidoRetencao.Sofrida, TributoRetido.IRRF, ini, fim, ct);
        var csll = await retencoes.SomarAsync(empresaId, SentidoRetencao.Sofrida, TributoRetido.CSLL, ini, fim, ct);
        var avisos = new List<string> { "Receita sugerida pelas notas fiscais (vendas e serviços, sem IPI e ICMS-ST, menos devoluções): revise o enquadramento por atividade e inclua as outras receitas." };
        if (receitas.Count == 0) avisos.Add("Não há notas de venda ou serviço no trimestre.");
        return Resultado<PreparoPresumido>.Ok(new PreparoPresumido(new EntradaPresumido(ano, trimestre, receitas, 0, irrf, csll), avisos));
    }

    public async Task<Resultado<ResultadoLucro>> CalcularPresumidoAsync(long empresaId, EntradaPresumido e, long usuarioId, CancellationToken ct = default)
    {
        if (await NegarAsync(empresaId, usuarioId, Permissao.FiscalApurar, ct) is { } negado) return Resultado<ResultadoLucro>.Falha(negado);
        if (e.Trimestre is < 1 or > 4) return Resultado<ResultadoLucro>.Falha("Trimestre inválido.");
        if (e.IrrfCompensavel < 0 || e.CsllRetida < 0 || e.OutrasDeducoesIrpj < 0 || e.OutrasReceitas < 0) return Resultado<ResultadoLucro>.Falha("Valores não podem ser negativos.");
        var (ini, _) = CalculadoraLucro.Trimestre(e.Ano, e.Trimestre);
        return Resultado<ResultadoLucro>.Ok(CalculadoraLucro.CalcularPresumido(await ParametrosAsync(ini, ct), e));
    }

    public async Task<Resultado<ApuracaoLucroSalva>> SalvarPresumidoAsync(long empresaId, EntradaPresumido e, long usuarioId, CancellationToken ct = default)
    {
        var existente = await repositorio.ObterApuracaoAsync(empresaId, RegimeLucro.Presumido, e.Ano, e.Trimestre, ct);
        if (existente?.LancamentoId is not null) return Resultado<ApuracaoLucroSalva>.Falha("A provisão desta apuração está contabilizada; desfaça-a antes de recalcular.");
        var calculo = await CalcularPresumidoAsync(empresaId, e, usuarioId, ct);
        if (!calculo.Sucesso) return Resultado<ApuracaoLucroSalva>.Falha(calculo.Erros);
        await repositorio.GravarApuracaoAsync(empresaId, calculo.Valor, usuarioId, ct);
        return Resultado<ApuracaoLucroSalva>.Ok((await repositorio.ObterApuracaoAsync(empresaId, RegimeLucro.Presumido, e.Ano, e.Trimestre, ct))!);
    }

    // ------------------------------------------------------------------ Lucro Real

    /// <summary>
    /// Monta a entrada do Lucro Real: resultado contábil do trimestre (contas de resultado, sem lançamentos de encerramento) antes dos
    /// tributos sobre o lucro, adições e exclusões do LALUR/LACS, saldos de prejuízo da Parte B e retenções sofridas.
    /// </summary>
    public async Task<Resultado<PreparoReal>> PrepararRealAsync(long empresaId, int ano, int trimestre, long usuarioId, CancellationToken ct = default)
    {
        if (await NegarAsync(empresaId, usuarioId, Permissao.FiscalApurar, ct) is { } negado) return Resultado<PreparoReal>.Falha(negado);
        if (trimestre is < 1 or > 4) return Resultado<PreparoReal>.Falha("Trimestre inválido.");
        var (ini, fim) = CalculadoraLucro.Trimestre(ano, trimestre);
        var (resultado, despesaTributos) = await repositorio.ResultadoContabilAsync(empresaId, ini, fim, ct);
        var lucroAntes = resultado + despesaTributos;
        var (adIrpj, exIrpj, adCsll, exCsll) = await repositorio.SomarLalurAsync(empresaId, ano, trimestre, ct);
        var saldoIrpj = await repositorio.SaldoParteBAsync(empresaId, "IRPJ", ano, trimestre, ct);
        var saldoCsll = await repositorio.SaldoParteBAsync(empresaId, "CSLL", ano, trimestre, ct);
        var irrf = await retencoes.SomarAsync(empresaId, SentidoRetencao.Sofrida, TributoRetido.IRRF, ini, fim, ct);
        var csllRetida = await retencoes.SomarAsync(empresaId, SentidoRetencao.Sofrida, TributoRetido.CSLL, ini, fim, ct);
        var receita = LivrosFiscais.ReceitaLiquida(await documentos.ListarItensAsync(empresaId, ini, fim, null, ct));
        var avisos = new List<string>();
        if (despesaTributos == 0)
            avisos.Add("Contas de despesa de IRPJ/CSLL não configuradas em Contas dos tributos: se o resultado já inclui a provisão do trimestre, ela deve ser lançada como adição.");
        return Resultado<PreparoReal>.Ok(new PreparoReal(
            new EntradaReal(ano, trimestre, lucroAntes, adIrpj, exIrpj, adCsll, exCsll, saldoIrpj, saldoCsll, irrf, csllRetida, 0, receita), resultado, despesaTributos, avisos));
    }

    public async Task<Resultado<ResultadoLucro>> CalcularRealAsync(long empresaId, EntradaReal e, long usuarioId, CancellationToken ct = default)
    {
        if (await NegarAsync(empresaId, usuarioId, Permissao.FiscalApurar, ct) is { } negado) return Resultado<ResultadoLucro>.Falha(negado);
        if (e.Trimestre is < 1 or > 4) return Resultado<ResultadoLucro>.Falha("Trimestre inválido.");
        if (e.SaldoPrejuizoIrpj < 0 || e.SaldoBaseNegativaCsll < 0 || e.IrrfCompensavel < 0 || e.CsllRetida < 0 || e.OutrasDeducoesIrpj < 0)
            return Resultado<ResultadoLucro>.Falha("Saldos, retenções e deduções não podem ser negativos.");
        var (ini, _) = CalculadoraLucro.Trimestre(e.Ano, e.Trimestre);
        return Resultado<ResultadoLucro>.Ok(CalculadoraLucro.CalcularReal(await ParametrosAsync(ini, ct), e));
    }

    public async Task<Resultado<ApuracaoLucroSalva>> SalvarRealAsync(long empresaId, EntradaReal e, long usuarioId, CancellationToken ct = default)
    {
        var existente = await repositorio.ObterApuracaoAsync(empresaId, RegimeLucro.Real, e.Ano, e.Trimestre, ct);
        if (existente?.LancamentoId is not null) return Resultado<ApuracaoLucroSalva>.Falha("A provisão desta apuração está contabilizada; desfaça-a antes de recalcular.");
        var calculo = await CalcularRealAsync(empresaId, e, usuarioId, ct);
        if (!calculo.Sucesso) return Resultado<ApuracaoLucroSalva>.Falha(calculo.Erros);
        await repositorio.GravarApuracaoAsync(empresaId, calculo.Valor, usuarioId, ct);
        return Resultado<ApuracaoLucroSalva>.Ok((await repositorio.ObterApuracaoAsync(empresaId, RegimeLucro.Real, e.Ano, e.Trimestre, ct))!);
    }

    public async Task<Resultado<IReadOnlyList<ApuracaoLucroSalva>>> ListarApuracoesAsync(long empresaId, int ano, long usuarioId, CancellationToken ct = default)
    {
        var p = await autorizador.ObterAsync(usuarioId, empresaId, ct);
        if (!p.Pode(Permissao.FiscalApurar) && !p.Pode(Permissao.FiscalRelatorios))
            return Resultado<IReadOnlyList<ApuracaoLucroSalva>>.Falha(await NegarAsync(empresaId, usuarioId, Permissao.FiscalRelatorios, ct) ?? "Acesso negado.");
        return Resultado<IReadOnlyList<ApuracaoLucroSalva>>.Ok(await repositorio.ListarApuracoesAsync(empresaId, ano, ct));
    }

    public async Task<Resultado> ExcluirApuracaoAsync(long empresaId, long id, long usuarioId, CancellationToken ct = default)
    {
        if (await NegarAsync(empresaId, usuarioId, Permissao.FiscalApurar, ct) is { } negado) return Resultado.Falha(negado);
        var alvo = (await repositorio.ListarApuracoesAsync(empresaId, 0, ct)).FirstOrDefault(a => a.Id == id);
        if (alvo is null) return Resultado.Falha("Apuração não encontrada.");
        if (alvo.LancamentoId is not null) return Resultado.Falha("A provisão desta apuração está contabilizada; desfaça-a antes de excluir.");
        await repositorio.ExcluirApuracaoAsync(empresaId, id, ct);
        return Resultado.Ok();
    }

    // ------------------------------------------------------------------ LALUR / LACS

    public async Task<Resultado<IReadOnlyList<LancamentoLalur>>> ListarLalurAsync(long empresaId, int ano, int? trimestre, long usuarioId, CancellationToken ct = default)
    {
        var p = await autorizador.ObterAsync(usuarioId, empresaId, ct);
        if (!p.Pode(Permissao.FiscalLalurManter) && !p.Pode(Permissao.FiscalApurar) && !p.Pode(Permissao.FiscalRelatorios))
            return Resultado<IReadOnlyList<LancamentoLalur>>.Falha(await NegarAsync(empresaId, usuarioId, Permissao.FiscalLalurManter, ct) ?? "Acesso negado.");
        return Resultado<IReadOnlyList<LancamentoLalur>>.Ok(await repositorio.ListarLalurAsync(empresaId, ano, trimestre, ct));
    }

    public async Task<Resultado<long>> GravarLalurAsync(long empresaId, LancamentoLalur l, long usuarioId, CancellationToken ct = default)
    {
        if (await NegarAsync(empresaId, usuarioId, Permissao.FiscalLalurManter, ct) is { } negado) return Resultado<long>.Falha(negado);
        var erros = new List<string>();
        if (l.Trimestre is < 1 or > 4) erros.Add("Trimestre inválido.");
        if (l.Ano is < 2000 or > 2100) erros.Add("Ano inválido.");
        if (l.Tributo is not ("IRPJ" or "CSLL" or "AMBOS")) erros.Add("Tributo: IRPJ, CSLL ou AMBOS.");
        if (l.Tipo is not ("ADICAO" or "EXCLUSAO")) erros.Add("Tipo: ADICAO ou EXCLUSAO.");
        if (string.IsNullOrWhiteSpace(l.Descricao)) erros.Add("Informe a descrição.");
        if (l.Valor <= 0 || !Dinheiro.TemNoMaximoDuasCasas(l.Valor)) erros.Add("Informe um valor positivo com até 2 casas decimais.");
        if (erros.Count > 0) return Resultado<long>.Falha(erros);
        try { return Resultado<long>.Ok(await repositorio.GravarLalurAsync(empresaId, l with { Descricao = l.Descricao.Trim() }, null, usuarioId, ct)); }
        catch (ViolacaoRegraException ex) { return Resultado<long>.Falha(ex.Message); }
    }

    public async Task<Resultado> ExcluirLalurAsync(long empresaId, long id, long usuarioId, CancellationToken ct = default)
    {
        if (await NegarAsync(empresaId, usuarioId, Permissao.FiscalLalurManter, ct) is { } negado) return Resultado.Falha(negado);
        await repositorio.ExcluirLalurAsync(empresaId, id, ct);
        return Resultado.Ok();
    }

    public async Task<Resultado<IReadOnlyList<MovimentoParteB>>> ParteBAsync(long empresaId, string tributo, long usuarioId, CancellationToken ct = default)
    {
        var p = await autorizador.ObterAsync(usuarioId, empresaId, ct);
        if (!p.Pode(Permissao.FiscalLalurManter) && !p.Pode(Permissao.FiscalApurar) && !p.Pode(Permissao.FiscalRelatorios))
            return Resultado<IReadOnlyList<MovimentoParteB>>.Falha(await NegarAsync(empresaId, usuarioId, Permissao.FiscalLalurManter, ct) ?? "Acesso negado.");
        return Resultado<IReadOnlyList<MovimentoParteB>>.Ok(await repositorio.ListarParteBAsync(empresaId, tributo, ct));
    }

    /// <summary>Saldo de prejuízo fiscal (IRPJ) ou base negativa (CSLL) trazido de antes do uso do sistema.</summary>
    public async Task<Resultado> DefinirSaldoInicialAsync(long empresaId, string tributo, decimal valor, long usuarioId, CancellationToken ct = default)
    {
        if (await NegarAsync(empresaId, usuarioId, Permissao.FiscalLalurManter, ct) is { } negado) return Resultado.Falha(negado);
        if (tributo is not ("IRPJ" or "CSLL")) return Resultado.Falha("Tributo: IRPJ ou CSLL.");
        if (valor < 0 || !Dinheiro.TemNoMaximoDuasCasas(valor)) return Resultado.Falha("Informe um valor não negativo com até 2 casas decimais.");
        await repositorio.DefinirSaldoInicialAsync(empresaId, tributo, valor, usuarioId, ct);
        return Resultado.Ok();
    }

    // ------------------------------------------------------------------ PIS e COFINS

    /// <summary>Receita e créditos do mês pelas notas: saídas de receita menos devoluções; créditos sobre entradas de CFOP que geram crédito (regime não cumulativo).</summary>
    public async Task<Resultado<PreparoPisCofins>> PrepararPisCofinsAsync(long empresaId, DateOnly competencia, RegimePisCofins regime, bool excluirIcmsDaBase, long usuarioId,
                                                                        CancellationToken ct = default)
    {
        if (await NegarAsync(empresaId, usuarioId, Permissao.FiscalApurar, ct) is { } negado) return Resultado<PreparoPisCofins>.Falha(negado);
        var comp = new DateOnly(competencia.Year, competencia.Month, 1);
        var fim = comp.AddMonths(1).AddDays(-1);
        var itens = (await documentos.ListarItensAsync(empresaId, comp, fim, null, ct)).Where(i => i.Situacao == SituacaoDocumento.Autorizada).ToList();
        decimal Base(LinhaItemFiscal i) => i.Item.ValorContabil - i.Item.Ipi - i.Item.IcmsSt - (excluirIcmsDaBase ? i.Item.Icms : 0m);
        var receita = Dinheiro.Arredondar(itens.Where(i => i.Operacao == TipoOperacao.Saida && Cfop.GeraReceita(i.Item.Cfop)).Sum(Base)
                                          - itens.Where(i => i.Operacao == TipoOperacao.Entrada && Cfop.EhDevolucaoDeVenda(i.Item.Cfop)).Sum(Base));
        var creditos = regime == RegimePisCofins.NaoCumulativo
            ? Dinheiro.Arredondar(itens.Where(i => i.Operacao == TipoOperacao.Entrada && Cfop.GeraCreditoPisCofins(i.Item.Cfop)).Sum(Base)
                                  - itens.Where(i => i.Operacao == TipoOperacao.Saida && (Cfop.Casa("52", i.Item.Cfop) || Cfop.Casa("62", i.Item.Cfop))).Sum(Base))
            : 0m;
        var pisRet = await retencoes.SomarAsync(empresaId, SentidoRetencao.Sofrida, TributoRetido.PIS, comp, fim, ct);
        var cofinsRet = await retencoes.SomarAsync(empresaId, SentidoRetencao.Sofrida, TributoRetido.COFINS, comp, fim, ct);
        var avisos = new List<string> { "Receita e créditos sugeridos pelas notas; revise e acrescente créditos de energia, aluguéis, depreciação e demais despesas permitidas." };
        if (excluirIcmsDaBase) avisos.Add("ICMS destacado excluído da base (tese do STF – Tema 69).");
        return Resultado<PreparoPisCofins>.Ok(new PreparoPisCofins(new EntradaPisCofins(comp, regime, receita, Math.Max(0, creditos), pisRet, cofinsRet), avisos));
    }

    public async Task<Resultado<ResultadoPisCofins>> CalcularPisCofinsAsync(long empresaId, EntradaPisCofins e, long usuarioId, CancellationToken ct = default)
    {
        if (await NegarAsync(empresaId, usuarioId, Permissao.FiscalApurar, ct) is { } negado) return Resultado<ResultadoPisCofins>.Falha(negado);
        if (e.ReceitaBase < 0 || e.BaseCreditos < 0 || e.PisRetido < 0 || e.CofinsRetido < 0) return Resultado<ResultadoPisCofins>.Falha("Valores não podem ser negativos.");
        var comp = new DateOnly(e.Competencia.Year, e.Competencia.Month, 1);
        return Resultado<ResultadoPisCofins>.Ok(CalculadoraLucro.CalcularPisCofins(await ParametrosAsync(comp, ct), e with { Competencia = comp }));
    }

    public async Task<Resultado<ApuracaoPisCofinsSalva>> SalvarPisCofinsAsync(long empresaId, EntradaPisCofins e, long usuarioId, CancellationToken ct = default)
    {
        var comp = new DateOnly(e.Competencia.Year, e.Competencia.Month, 1);
        var existente = await repositorio.ObterPisCofinsAsync(empresaId, comp, ct);
        if (existente?.LancamentoId is not null) return Resultado<ApuracaoPisCofinsSalva>.Falha("A provisão desta apuração está contabilizada; desfaça-a antes de recalcular.");
        var calculo = await CalcularPisCofinsAsync(empresaId, e, usuarioId, ct);
        if (!calculo.Sucesso) return Resultado<ApuracaoPisCofinsSalva>.Falha(calculo.Erros);
        await repositorio.GravarPisCofinsAsync(empresaId, calculo.Valor, usuarioId, ct);
        return Resultado<ApuracaoPisCofinsSalva>.Ok((await repositorio.ObterPisCofinsAsync(empresaId, comp, ct))!);
    }

    public async Task<Resultado<IReadOnlyList<ApuracaoPisCofinsSalva>>> ListarPisCofinsAsync(long empresaId, int ano, long usuarioId, CancellationToken ct = default)
    {
        var p = await autorizador.ObterAsync(usuarioId, empresaId, ct);
        if (!p.Pode(Permissao.FiscalApurar) && !p.Pode(Permissao.FiscalRelatorios))
            return Resultado<IReadOnlyList<ApuracaoPisCofinsSalva>>.Falha(await NegarAsync(empresaId, usuarioId, Permissao.FiscalRelatorios, ct) ?? "Acesso negado.");
        return Resultado<IReadOnlyList<ApuracaoPisCofinsSalva>>.Ok(await repositorio.ListarPisCofinsAsync(empresaId, ano, ct));
    }

    public async Task<Resultado> ExcluirPisCofinsAsync(long empresaId, long id, long usuarioId, CancellationToken ct = default)
    {
        if (await NegarAsync(empresaId, usuarioId, Permissao.FiscalApurar, ct) is { } negado) return Resultado.Falha(negado);
        var alvo = (await repositorio.ListarPisCofinsAsync(empresaId, 0, ct)).FirstOrDefault(a => a.Id == id);
        if (alvo is null) return Resultado.Falha("Apuração não encontrada.");
        if (alvo.LancamentoId is not null) return Resultado.Falha("A provisão desta apuração está contabilizada; desfaça-a antes de excluir.");
        await repositorio.ExcluirPisCofinsAsync(empresaId, id, ct);
        return Resultado.Ok();
    }
}
