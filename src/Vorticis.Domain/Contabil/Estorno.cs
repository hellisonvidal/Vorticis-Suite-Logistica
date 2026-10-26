using Vorticis.Domain.Comum;

namespace Vorticis.Domain.Contabil;

/// <summary>
/// Estorno (ITG 2000): novo lançamento com débitos e créditos invertidos. O original permanece
/// no Diário marcado como ESTORNADO — nada é apagado.
/// </summary>
public static class Estorno
{
    public static Resultado<LancamentoProposto> Gerar(LancamentoRegistrado original, DateOnly data, string motivo)
    {
        var erros = new List<string>();
        if (original.Status != StatusLancamento.Efetivado)
            erros.Add(original.Status == StatusLancamento.Estornado
                ? $"Lançamento nº {original.Numero} já foi estornado."
                : "Só lançamentos gravados podem ser estornados.");
        if (original.Origem == OrigemLancamento.Estorno)
            erros.Add("Não é possível estornar um estorno; faça um novo lançamento.");
        if (original.Origem == OrigemLancamento.Encerramento)
            erros.Add("Lançamentos de encerramento são desfeitos pela reabertura do exercício, não por estorno.");
        if (data < original.Data)
            erros.Add($"A data do estorno não pode ser anterior à do lançamento original ({original.Data:dd/MM/yyyy}).");
        if (string.IsNullOrWhiteSpace(motivo))
            erros.Add("Informe o motivo do estorno.");
        if (erros.Count > 0) return Resultado<LancamentoProposto>.Falha(erros);

        var prefixo = $"Estorno do lançamento nº {original.Numero} ({motivo.Trim()}) - ";
        var partidas = original.Partidas
            .Select(p => p with
            {
                DC = p.DC.Inverso(),
                Historico = Truncar(prefixo + p.Historico, ValidadorLancamento.TamanhoMaximoHistorico),
            })
            .ToArray();

        return Resultado<LancamentoProposto>.Ok(new LancamentoProposto(
            original.EmpresaId, data, OrigemLancamento.Estorno, partidas, EstornoDeId: original.Id));
    }

    private static string Truncar(string s, int max) => s.Length <= max ? s : s[..max];
}
