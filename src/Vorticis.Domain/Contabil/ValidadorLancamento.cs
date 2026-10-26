using Vorticis.Domain.Comum;

namespace Vorticis.Domain.Contabil;

/// <summary>
/// Regras de aplicação de um lançamento contábil (ver docs/02-partidas-dobradas.md).
/// Acumula todos os erros para o usuário corrigir de uma vez. As mesmas regras essenciais
/// são reforçadas por triggers no banco (db/001_core_contabil.sql).
/// </summary>
public static class ValidadorLancamento
{
    public const int TamanhoMaximoHistorico = 500;

    public static Resultado<LancamentoValidado> Validar(LancamentoProposto l, ContextoValidacao ctx)
    {
        var erros = new List<string>();
        var partidas = l.Partidas;

        if (partidas.Count == 0)
            return Resultado<LancamentoValidado>.Falha("O lançamento não possui partidas.");
        if (partidas.Count > short.MaxValue)
            erros.Add("Quantidade de partidas excede o limite.");

        // R3 — valores
        for (int i = 0; i < partidas.Count; i++)
        {
            var p = partidas[i];
            var linha = $"Linha {i + 1}";
            if (p.Valor <= 0) erros.Add($"{linha}: o valor deve ser positivo (o lado é indicado por D/C).");
            else if (!Dinheiro.TemNoMaximoDuasCasas(p.Valor)) erros.Add($"{linha}: valor com mais de 2 casas decimais ({p.Valor}).");
            if (string.IsNullOrWhiteSpace(p.Historico)) erros.Add($"{linha}: histórico obrigatório.");
            else if (p.Historico.Length > TamanhoMaximoHistorico) erros.Add($"{linha}: histórico excede {TamanhoMaximoHistorico} caracteres.");
        }

        // R1 + R2 — partidas dobradas
        decimal debitos = partidas.Where(p => p.DC == IndicadorDC.D).Sum(p => p.Valor);
        decimal creditos = partidas.Where(p => p.DC == IndicadorDC.C).Sum(p => p.Valor);
        if (!partidas.Any(p => p.DC == IndicadorDC.D) || !partidas.Any(p => p.DC == IndicadorDC.C))
            erros.Add("O lançamento precisa de ao menos uma partida a débito e uma a crédito.");
        if (debitos != creditos)
            erros.Add($"Débitos ({Formato.Moeda(debitos)}) diferentes de créditos ({Formato.Moeda(creditos)}). Diferença: {Formato.Moeda(debitos - creditos)}.");

        // R6 + R9 — período e indicador
        var per = ctx.Periodo;
        if (per is null)
            erros.Add($"Não há exercício cadastrado que contenha {l.Data:dd/MM/yyyy}.");
        else
        {
            if (per.ExercicioEncerrado) erros.Add($"O exercício de {per.InicioExercicio.Year} está encerrado.");
            if (!per.PeriodoExiste) erros.Add($"O período {l.Data:MM/yyyy} não está cadastrado.");
            else if (!per.PeriodoAberto) erros.Add($"O período {l.Data:MM/yyyy} está fechado.");
            if (l.Indicador == IndicadorLancamento.Encerramento && l.Data != per.FimExercicio)
                erros.Add($"Lançamento de encerramento deve ter a data final do exercício ({per.FimExercicio:dd/MM/yyyy}).");
            // Regras da empresa (aba Contabilidade): bloqueio de datas e documento obrigatório. Saldos iniciais e encerramento ficam isentos.
            var isento = l.Origem == OrigemLancamento.SaldoInicial || l.Indicador == IndicadorLancamento.Encerramento;
            if (!isento && per.DataBloqueio is { } bloqueio && l.Data <= bloqueio)
                erros.Add($"Lançamentos até {bloqueio:dd/MM/yyyy} estão bloqueados nesta empresa (a data {l.Data:dd/MM/yyyy} é anterior ou igual à data de bloqueio).");
            if (!isento && l.Origem != OrigemLancamento.Estorno && per.ExigeDocumento)
                for (int i = 0; i < partidas.Count; i++)
                    if (string.IsNullOrWhiteSpace(partidas[i].NumeroArquivo))
                        erros.Add($"Linha {i + 1}: informe o nº do documento (a empresa exige documento em todos os lançamentos).");
        }
        if ((l.Indicador == IndicadorLancamento.Extemporaneo) != l.DataExtemporanea.HasValue)
            erros.Add("Lançamento extemporâneo exige a data do fato (e só ele a aceita).");
        if (l.Origem == OrigemLancamento.Estorno && l.EstornoDeId is null)
            erros.Add("Lançamento de estorno deve indicar o lançamento estornado.");

        // R4 + R5 — contas, centros de custo e participantes
        var validadas = new List<PartidaValidada>(partidas.Count);
        for (int i = 0; i < partidas.Count; i++)
        {
            var p = partidas[i];
            var linha = $"Linha {i + 1}";
            long? centroId = null, participanteId = null;

            if (!ctx.Contas.TryGetValue(p.CodigoConta, out var conta))
            {
                erros.Add($"{linha}: conta {p.CodigoConta} não encontrada.");
                continue;
            }
            if (!conta.Analitica) erros.Add($"{linha}: conta {conta.Codigo} é sintética e não aceita lançamentos.");
            if (!conta.Ativa) erros.Add($"{linha}: conta {conta.Codigo} está inativa.");
            if (conta.DataInclusao > l.Data) erros.Add($"{linha}: conta {conta.Codigo} foi incluída em {conta.DataInclusao:dd/MM/yyyy}, após a data do lançamento.");

            if (!string.IsNullOrWhiteSpace(p.CodigoCentroCusto))
            {
                if (ctx.CentrosCusto.TryGetValue(p.CodigoCentroCusto, out var cc)) centroId = cc;
                else erros.Add($"{linha}: centro de custo {p.CodigoCentroCusto} não encontrado (só centros analíticos e ativos aceitam lançamento).");
            }
            else if (conta.ExigeCentroCusto) erros.Add($"{linha}: conta {conta.Codigo} exige centro de custo.");

            if (!string.IsNullOrWhiteSpace(p.CodigoParticipante))
            {
                if (ctx.Participantes.TryGetValue(p.CodigoParticipante, out var pa)) participanteId = pa;
                else erros.Add($"{linha}: participante {p.CodigoParticipante} não encontrado.");
            }
            else if (conta.ExigeParticipante) erros.Add($"{linha}: conta {conta.Codigo} exige participante.");

            validadas.Add(new PartidaValidada((short)(i + 1), conta.Id, centroId, participanteId,
                p.DC, p.Valor, p.Historico.Trim(), p.NumeroArquivo));
        }

        if (erros.Count > 0) return Resultado<LancamentoValidado>.Falha(erros);

        return Resultado<LancamentoValidado>.Ok(new LancamentoValidado(
            l.EmpresaId, per!.ExercicioId, l.Data, l.Origem, l.Indicador, l.DataExtemporanea,
            l.DocumentoOrigemId, l.EstornoDeId, validadas));
    }
}
