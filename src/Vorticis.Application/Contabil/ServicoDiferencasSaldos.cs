using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Application.Contabil;

/// <summary>Saldo da conta antes do período e no fim dele, e a diferença (movimento líquido).</summary>
public sealed record DiferencaSaldo(LinhaBalancete Linha)
{
    public decimal Diferenca => Linha.SaldoFinal - Linha.SaldoAnterior;
}

/// <summary>Diferenças de saldos entre duas datas e as inconsistências encontradas no período.</summary>
public sealed record AnaliseDiferencas(IReadOnlyList<DiferencaSaldo> Contas, decimal TotalDebitos, decimal TotalCreditos,
                                       IReadOnlyList<string> Inconsistencias)
{
    public decimal DiferencaDebitoCredito => TotalDebitos - TotalCreditos;
}

/// <summary>
/// Consultas › Diferenças de saldos: compara o saldo de cada conta antes do período com o saldo no fim dele e aponta
/// inconsistências — débitos ≠ créditos, contas com saldo contrário à natureza e divergências entre as partidas e os
/// saldos mensais (verificação interna dos meses do período).
/// </summary>
public sealed class ServicoDiferencasSaldos(ServicoRelatorios relatorios, IRepositorioContabil repositorio, Autorizador autorizador)
{
    public async Task<Resultado<AnaliseDiferencas>> AnalisarAsync(long empresaId, DateOnly inicio, DateOnly fim, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<AnaliseDiferencas>(usuarioId, empresaId, Permissao.RelatorioBalancete, ct) is { } negado) return negado;
        if (fim < inicio) return Resultado<AnaliseDiferencas>.Falha("A data final deve ser posterior à inicial.");
        var balancete = await relatorios.BalanceteAsync(empresaId, inicio, fim, usuarioId, incluirZeradas: false, ct);
        if (!balancete.Sucesso) return Resultado<AnaliseDiferencas>.Falha(balancete.Erros);
        var linhas = balancete.Valor;
        var analiticas = linhas.Where(l => l.Analitica).ToList();
        var debitos = analiticas.Sum(l => l.Debitos);
        var creditos = analiticas.Sum(l => l.Creditos);
        var problemas = new List<string>();
        if (debitos != creditos)
            problemas.Add($"Débitos ({Formato.Moeda(debitos)}) diferentes dos créditos ({Formato.Moeda(creditos)}) no período: diferença de {Formato.Moeda(debitos - creditos)}.");
        foreach (var l in analiticas.Where(l => l.SaldoInvertido))
            problemas.Add($"Conta {l.Codigo} – {l.Descricao}: saldo {LinhaBalancete.Formatar(l.SaldoFinal)} contrário à natureza da conta.");
        for (var m = new DateOnly(inicio.Year, inicio.Month, 1); m <= fim; m = m.AddMonths(1))
            foreach (var i in await repositorio.VerificarInvariantesAsync(empresaId, m, ct))
                problemas.Add($"{m:MM/yyyy} [{i.Codigo}] {i.Descricao}");
        return Resultado<AnaliseDiferencas>.Ok(new AnaliseDiferencas(linhas.Select(l => new DiferencaSaldo(l)).ToList(), debitos, creditos, problemas));
    }
}
