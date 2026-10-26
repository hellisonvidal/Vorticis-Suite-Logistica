using System.Text.Json;
using System.Text.Json.Serialization;
using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Comum;
using Vorticis.Fiscal.Documentos;

namespace Vorticis.Fiscal.Simples;

/// <summary>Receita bruta e folha de um mês: a base do RBT12 e do Fator R.</summary>
public sealed record BaseMensal(DateOnly Competencia, decimal? Receita, decimal? Folha, string OrigemReceita = "MANUAL", string OrigemFolha = "MANUAL");

public sealed record ApuracaoSimplesSalva(long Id, DateOnly Competencia, decimal ValorDas, DateOnly Vencimento, long? LancamentoId, ResultadoSimples Resultado);

public sealed record PreparoSimples(IReadOnlyList<ReceitaSimples> Sugestao, decimal Rbt12, bool Rbt12Proporcional, decimal Folha12, IReadOnlyList<string> Avisos);

public interface IRepositorioSimples
{
    Task<IReadOnlyList<BaseMensal>> ListarBaseMensalAsync(long empresaId, DateOnly de, DateOnly ate, CancellationToken ct);
    Task GravarBaseMensalAsync(long empresaId, BaseMensal b, long usuarioId, CancellationToken ct);
    Task ExcluirBaseMensalAsync(long empresaId, DateOnly competencia, CancellationToken ct);

    /// <summary>Grava (substituindo a existente) a apuração e a receita do mês na base mensal.</summary>
    Task<long> GravarApuracaoAsync(long empresaId, ResultadoSimples r, long usuarioId, CancellationToken ct);
    Task<IReadOnlyList<ApuracaoSimplesSalva>> ListarApuracoesAsync(long empresaId, int ano, CancellationToken ct);
    Task<ApuracaoSimplesSalva?> ObterApuracaoAsync(long empresaId, DateOnly competencia, CancellationToken ct);
    Task ExcluirApuracaoAsync(long empresaId, long id, CancellationToken ct);
}

/// <summary>Serialização do resultado das apurações (JSONB), compartilhada pelos repositórios.</summary>
public static class JsonFiscal
{
    public static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };

    public static string Serializar<T>(T valor) => JsonSerializer.Serialize(valor, Opcoes);

    public static T Ler<T>(string json) => JsonSerializer.Deserialize<T>(json, Opcoes)!;
}

/// <summary>
/// Apuração mensal do Simples Nacional: segregação da receita (sugerida pelas notas), RBT12, Fator R, DAS por anexo com memória
/// de cálculo. Exige a função "apurar tributos" (consultas: relatórios fiscais).
/// </summary>
public sealed class ServicoSimplesNacional(IRepositorioSimples repositorio, IRepositorioDocumentos documentos, IParametrosFiscais parametros, Autorizador autorizador)
{
    public async Task<TabelaSimples> TabelaAsync(DateOnly data, CancellationToken ct = default) =>
        TabelaSimples.Carregar(await parametros.ObterJsonAsync(ChavesParametro.SimplesNacional, data, ct));

    private static DateOnly Mes(DateOnly d) => new(d.Year, d.Month, 1);

    // ------------------------------------------------------------------ base mensal (histórico de receita e folha)

    public async Task<Resultado<IReadOnlyList<BaseMensal>>> BaseMensalAsync(long empresaId, int ano, long usuarioId, CancellationToken ct = default)
    {
        var p = await autorizador.ObterAsync(usuarioId, empresaId, ct);
        if (!p.Pode(Permissao.FiscalApurar) && !p.Pode(Permissao.FiscalRetencoesManter) && !p.Pode(Permissao.FiscalRelatorios))
            return await autorizador.NegarAsync<IReadOnlyList<BaseMensal>>(usuarioId, empresaId, Permissao.FiscalApurar, ct) ?? Resultado<IReadOnlyList<BaseMensal>>.Falha("Acesso negado.");
        return Resultado<IReadOnlyList<BaseMensal>>.Ok(await repositorio.ListarBaseMensalAsync(empresaId, new DateOnly(ano, 1, 1), new DateOnly(ano, 12, 1), ct));
    }

    public async Task<Resultado> GravarBaseMensalAsync(long empresaId, BaseMensal b, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FiscalRetencoesManter, ct) is { } negado) return Resultado.Falha(negado);
        if (b.Receita is < 0 || b.Folha is < 0) return Resultado.Falha("Receita e folha não podem ser negativas.");
        if (b.Receita is null && b.Folha is null) { await repositorio.ExcluirBaseMensalAsync(empresaId, Mes(b.Competencia), ct); return Resultado.Ok(); }
        await repositorio.GravarBaseMensalAsync(empresaId, b with { Competencia = Mes(b.Competencia), OrigemReceita = "MANUAL", OrigemFolha = "MANUAL" }, usuarioId, ct);
        return Resultado.Ok();
    }

    // ------------------------------------------------------------------ apuração

    /// <summary>Sugestão de receitas do mês (pelas notas), RBT12 e folha dos 12 meses (pela base mensal e início de atividade da empresa).</summary>
    public async Task<Resultado<PreparoSimples>> PrepararAsync(long empresaId, DateOnly competencia, AtividadeSimples atividadeServicos, long usuarioId,
                                                              CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<PreparoSimples>(usuarioId, empresaId, Permissao.FiscalApurar, ct) is { } negado) return negado;
        var comp = Mes(competencia);
        var itens = await documentos.ListarItensAsync(empresaId, comp, comp.AddMonths(1).AddDays(-1), null, ct);
        var sugestao = ClassificadorReceita.Sugerir(itens, atividadeServicos);
        var (rbt12, proporcional, folha12, avisos) = await Acumulados(empresaId, comp, sugestao.Sum(s => s.Valor), ct);
        if (sugestao.Count == 0) avisos.Add("Não há notas fiscais de venda ou serviço no mês: informe as receitas manualmente.");
        return Resultado<PreparoSimples>.Ok(new PreparoSimples(sugestao, rbt12, proporcional, folha12, avisos));
    }

    private async Task<(decimal Rbt12, bool Proporcional, decimal Folha12, List<string> Avisos)> Acumulados(long empresaId, DateOnly comp, decimal receitaMes, CancellationToken ct)
    {
        var empresa = await documentos.ObterEmpresaAsync(empresaId, ct) ?? throw new InvalidOperationException("Empresa não encontrada.");
        var historico = await repositorio.ListarBaseMensalAsync(empresaId, comp.AddMonths(-12), comp, ct);
        var receitas = historico.Where(b => b.Receita is not null && b.Competencia < comp).ToDictionary(b => b.Competencia, b => b.Receita!.Value);
        var folhas = historico.Where(b => b.Folha is not null && b.Competencia < comp).ToDictionary(b => b.Competencia, b => b.Folha!.Value);
        var folhaMes = historico.FirstOrDefault(b => b.Competencia == comp)?.Folha ?? 0m;
        var r = CalculadoraSimples.Acumular12Meses(comp, empresa.InicioAtividade, receitas, receitaMes);
        var f = CalculadoraSimples.Acumular12Meses(comp, empresa.InicioAtividade, folhas, folhaMes);
        var avisos = new List<string>();
        if (r.MesesSemDado.Count > 0)
            avisos.Add($"Receita bruta sem lançamento em {string.Join(", ", r.MesesSemDado.Select(m => m.ToString("MM/yyyy")))}: o RBT12 pode estar subestimado. Complete em Histórico de receita e folha.");
        if (f.MesesSemDado.Count > 0)
            avisos.Add($"Folha sem lançamento em {string.Join(", ", f.MesesSemDado.Select(m => m.ToString("MM/yyyy")))}: o Fator R pode estar subestimado.");
        if (r.Proporcional) avisos.Add("Empresa com menos de 12 meses de atividade: RBT12 proporcional (média dos meses de atividade × 12).");
        return (r.Valor, r.Proporcional, f.Valor, avisos);
    }

    /// <summary>Calcula sem gravar. <paramref name="rbt12"/> e <paramref name="folha12"/> nulos são obtidos da base mensal.</summary>
    public async Task<Resultado<ResultadoSimples>> CalcularAsync(long empresaId, DateOnly competencia, IReadOnlyList<ReceitaSimples> receitas, decimal? rbt12,
                                                                decimal? folha12, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<ResultadoSimples>(usuarioId, empresaId, Permissao.FiscalApurar, ct) is { } negado) return negado;
        return await CalcularSemPermissaoAsync(empresaId, competencia, receitas, rbt12, folha12, ct);
    }

    private async Task<Resultado<ResultadoSimples>> CalcularSemPermissaoAsync(long empresaId, DateOnly competencia, IReadOnlyList<ReceitaSimples> receitas, decimal? rbt12,
                                                                             decimal? folha12, CancellationToken ct)
    {
        var comp = Mes(competencia);
        if (receitas.Count == 0 || receitas.All(r => r.Valor == 0)) return Resultado<ResultadoSimples>.Falha("Informe ao menos uma receita do mês.");
        if (rbt12 is < 0 || folha12 is < 0) return Resultado<ResultadoSimples>.Falha("RBT12 e folha não podem ser negativos.");
        var acumulado = await Acumulados(empresaId, comp, receitas.Sum(r => r.Valor), ct);
        var tabela = await TabelaAsync(comp, ct);
        var resultado = CalculadoraSimples.Calcular(tabela, new EntradaSimples(comp, receitas, rbt12 ?? acumulado.Rbt12, folha12 ?? acumulado.Folha12));
        var avisos = rbt12 is null && folha12 is null ? resultado.Avisos.Concat(acumulado.Avisos).ToList() : resultado.Avisos;
        return Resultado<ResultadoSimples>.Ok(resultado with { Avisos = avisos });
    }

    public async Task<Resultado<ApuracaoSimplesSalva>> SalvarAsync(long empresaId, DateOnly competencia, IReadOnlyList<ReceitaSimples> receitas, decimal? rbt12,
                                                                  decimal? folha12, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<ApuracaoSimplesSalva>(usuarioId, empresaId, Permissao.FiscalApurar, ct) is { } negado) return negado;
        var comp = Mes(competencia);
        var existente = await repositorio.ObterApuracaoAsync(empresaId, comp, ct);
        if (existente?.LancamentoId is not null)
            return Resultado<ApuracaoSimplesSalva>.Falha($"A apuração de {comp:MM/yyyy} já tem a provisão contabilizada; desfaça a contabilização antes de recalcular.");
        var calculo = await CalcularSemPermissaoAsync(empresaId, comp, receitas, rbt12, folha12, ct);
        if (!calculo.Sucesso) return Resultado<ApuracaoSimplesSalva>.Falha(calculo.Erros);
        await repositorio.GravarApuracaoAsync(empresaId, calculo.Valor, usuarioId, ct);
        return Resultado<ApuracaoSimplesSalva>.Ok((await repositorio.ObterApuracaoAsync(empresaId, comp, ct))!);
    }

    public async Task<Resultado<IReadOnlyList<ApuracaoSimplesSalva>>> ListarAsync(long empresaId, int ano, long usuarioId, CancellationToken ct = default)
    {
        var p = await autorizador.ObterAsync(usuarioId, empresaId, ct);
        if (!p.Pode(Permissao.FiscalApurar) && !p.Pode(Permissao.FiscalRelatorios))
            return await autorizador.NegarAsync<IReadOnlyList<ApuracaoSimplesSalva>>(usuarioId, empresaId, Permissao.FiscalRelatorios, ct) ?? Resultado<IReadOnlyList<ApuracaoSimplesSalva>>.Falha("Acesso negado.");
        return Resultado<IReadOnlyList<ApuracaoSimplesSalva>>.Ok(await repositorio.ListarApuracoesAsync(empresaId, ano, ct));
    }

    public async Task<Resultado> ExcluirAsync(long empresaId, long id, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FiscalApurar, ct) is { } negado) return Resultado.Falha(negado);
        var todas = await repositorio.ListarApuracoesAsync(empresaId, 0, ct);   // ano 0 = todas
        var alvo = todas.FirstOrDefault(a => a.Id == id);
        if (alvo is null) return Resultado.Falha("Apuração não encontrada.");
        if (alvo.LancamentoId is not null) return Resultado.Falha("A provisão desta apuração está contabilizada; desfaça-a antes de excluir.");
        await repositorio.ExcluirApuracaoAsync(empresaId, id, ct);
        return Resultado.Ok();
    }
}
