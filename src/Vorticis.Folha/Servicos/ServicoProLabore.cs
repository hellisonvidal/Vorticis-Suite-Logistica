using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Folha.Cadastros;
using Vorticis.Folha.Dados;
using Vorticis.Folha.Regras;

namespace Vorticis.Folha.Servicos;

/// <summary>
/// Folha › Pró-labore: quais sócios retiram pró-labore e quanto (FOLHA_CADASTROS_MANTER) e o cálculo mensal com INSS,
/// IRRF e CPP patronal (FOLHA_CALCULAR). Com a competência fechada, nada é incluído, alterado ou excluído.
/// </summary>
public sealed class ServicoProLabore(RepositorioProLabore repositorio, IRepositorioCadastrosFolha cadastros, IRepositorioMovimentoFolha movimento,
                                     Autorizador autorizador)
{
    private static DateOnly Mes(DateOnly d) => new(d.Year, d.Month, 1);

    public async Task<IReadOnlyList<ProLaboreSocio>> ListarAsync(long empresaId, long usuarioId, CancellationToken ct = default)
        => await autorizador.PodeVerAsync(usuarioId, empresaId, ct) ? await repositorio.ListarAsync(empresaId, ct) : [];

    public async Task<IReadOnlyList<ProLaboreCalculo>> ListarCalculosAsync(long empresaId, DateOnly competencia, long usuarioId, CancellationToken ct = default)
        => await autorizador.PodeVerAsync(usuarioId, empresaId, ct) ? await repositorio.ListarCalculosAsync(empresaId, Mes(competencia), ct) : [];

    public async Task<Resultado> GravarAsync(long empresaId, ProLaboreSocio p, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FolhaCadastrosManter, ct) is { } negado) return Resultado.Falha(negado);
        var erros = new List<string>();
        if (p.Retira && p.Valor <= 0) erros.Add("Informe o valor mensal do pró-labore (ou desmarque \"Retira pró-labore\").");
        if (p.Valor < 0 || p.InssOutrasFontes < 0) erros.Add("Os valores não podem ser negativos.");
        if (p.DependentesIrrf is < 0 or > 99) erros.Add("Dependentes para o IRRF: de 0 a 99.");
        if (p.Retira && p.Inicio is null) erros.Add("Informe o início do pró-labore.");
        if (p.Fim is { } fim && p.Inicio is { } ini && fim < ini) erros.Add("O fim do pró-labore não pode ser anterior ao início.");
        if (erros.Count > 0) return Resultado.Falha(erros);
        try
        {
            await repositorio.GravarAsync(empresaId, p with { Inicio = p.Inicio ?? Mes(DateOnly.FromDateTime(DateTime.Today)), Valor = p.Retira ? p.Valor : 0 }, ct);
            return Resultado.Ok();
        }
        catch (ViolacaoRegraException ex) { return Resultado.Falha(ex.Message); }
    }

    /// <summary>Calcula o pró-labore da competência para os sócios que retiram e estão na vigência; substitui o cálculo anterior.</summary>
    public async Task<Resultado<IReadOnlyList<ProLaboreCalculo>>> CalcularAsync(long empresaId, DateOnly competencia, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<ProLaboreCalculo>>(usuarioId, empresaId, Permissao.FolhaCalcular, ct) is { } negado) return negado;
        competencia = Mes(competencia);
        if ((await movimento.ObterCompetenciaAsync(empresaId, competencia, ct)).Fechada)
            return Resultado<IReadOnlyList<ProLaboreCalculo>>.Falha($"A competência {competencia:MM/yyyy} da folha está fechada. Reabra-a para calcular o pró-labore.");
        var fimMes = competencia.AddMonths(1).AddDays(-1);
        var socios = (await repositorio.ListarAsync(empresaId, ct))
            .Where(s => s.Retira && s.Valor > 0 && (s.Inicio ?? competencia) <= fimMes && (s.Fim is null || s.Fim >= competencia)).ToList();
        if (socios.Count == 0)
            return Resultado<IReadOnlyList<ProLaboreCalculo>>.Falha("Nenhum sócio retira pró-labore nesta competência. Marque \"Retira pró-labore\" e informe o valor.");

        var legais = await cadastros.ListarParametrosLegaisAsync(ct);
        var tabelas = TabelasLegais.Em(competencia, legais);
        var parametros = ParametrosProLabore.Em(competencia, legais);
        var p = await cadastros.ObterParametrosAsync(empresaId, ct);
        var regime = RegimeEncargosExtensoes.De(await cadastros.ObterRegimeTributarioAsync(empresaId, competencia, ct), p.SimplesAnexo);
        var calculos = socios.Select(s =>
        {
            var r = CalculoProLabore.Calcular(s.Valor, s.InssOutrasFontes, s.DependentesIrrf, tabelas, parametros, regime);
            return new ProLaboreCalculo(0, s.SocioId, s.Nome, competencia, r.Bruto, r.Inss, r.Irrf, r.Liquido, r.InssPatronal, r.Memoria);
        }).ToList();
        try { await repositorio.GravarCalculosAsync(empresaId, competencia, calculos, usuarioId, ct); }
        catch (ViolacaoRegraException ex) { return Resultado<IReadOnlyList<ProLaboreCalculo>>.Falha(ex.Message); }
        return Resultado<IReadOnlyList<ProLaboreCalculo>>.Ok(await repositorio.ListarCalculosAsync(empresaId, competencia, ct));
    }

    public async Task<Resultado> ExcluirCalculosAsync(long empresaId, DateOnly competencia, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FolhaCalcular, ct) is { } negado) return Resultado.Falha(negado);
        competencia = Mes(competencia);
        if ((await movimento.ObterCompetenciaAsync(empresaId, competencia, ct)).Fechada)
            return Resultado.Falha($"A competência {competencia:MM/yyyy} da folha está fechada. Reabra-a para excluir o cálculo.");
        try { await repositorio.ExcluirCalculosAsync(empresaId, competencia, ct); return Resultado.Ok(); }
        catch (ViolacaoRegraException ex) { return Resultado.Falha(ex.Message); }
    }
}
