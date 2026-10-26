using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Application.Contabil;

/// <summary>Histórico padrão (ECD I075): texto fixo que o lançamento completa com uma parte livre.</summary>
public sealed record HistoricoPadrao(long Id, string Codigo, string Descricao);

public interface IRepositorioHistoricos
{
    Task<IReadOnlyList<HistoricoPadrao>> ListarAsync(long empresaId, CancellationToken ct);
    Task<long> IncluirAsync(long empresaId, string codigo, string descricao, CancellationToken ct);
    Task AlterarAsync(long id, string descricao, CancellationToken ct);
    Task ExcluirAsync(long id, CancellationToken ct);
}

/// <summary>
/// Cadastros › Históricos. O código é numérico e sequencial por empresa (sugerido automaticamente); na digitação
/// do lançamento, informar o código preenche o texto fixo e deixa o cursor no fim para o complemento.
/// </summary>
public sealed class ServicoHistoricos(IRepositorioHistoricos repositorio, Autorizador autorizador)
{
    public async Task<IReadOnlyList<HistoricoPadrao>> ListarAsync(long empresaId, long usuarioId, CancellationToken ct = default)
        => (await autorizador.ObterAsync(usuarioId, empresaId, ct)).TemAcesso
            ? (await repositorio.ListarAsync(empresaId, ct)).OrderBy(h => h.Codigo.Length).ThenBy(h => h.Codigo, StringComparer.Ordinal).ToList()
            : [];

    /// <summary>Próximo código livre (menor número livre, preenchendo lacunas).</summary>
    public static string ProximoCodigo(IEnumerable<HistoricoPadrao> existentes) =>
        Vorticis.Domain.Comum.Sequencia.MenorLivre(existentes.Select(h => h.Codigo)).ToString();

    public async Task<Resultado<long>> IncluirAsync(long empresaId, string? codigo, string descricao, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.LancamentoIncluir, ct) is { } negado) return negado;
        var lista = await repositorio.ListarAsync(empresaId, ct);
        var cod = string.IsNullOrWhiteSpace(codigo) ? ProximoCodigo(lista) : codigo.Trim();
        var erros = Validar(cod, descricao);
        if (lista.Any(h => h.Codigo == cod)) erros.Add($"Já existe o histórico {cod}.");
        if (erros.Count > 0) return Resultado<long>.Falha(erros);
        try
        {
            return Resultado<long>.Ok(await repositorio.IncluirAsync(empresaId, cod, descricao.Trim(), ct));
        }
        catch (ViolacaoRegraException ex) { return Resultado<long>.Falha(ex.Message); }
    }

    public async Task<Resultado> AlterarAsync(long empresaId, long id, string descricao, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.LancamentoIncluir, ct) is { } negado) return Resultado.Falha(negado);
        var lista = await repositorio.ListarAsync(empresaId, ct);
        if (lista.All(h => h.Id != id)) return Resultado.Falha("Histórico não encontrado.");
        var erros = Validar("1", descricao);
        if (erros.Count > 0) return Resultado.Falha(erros);
        await repositorio.AlterarAsync(id, descricao.Trim(), ct);
        return Resultado.Ok();
    }

    public async Task<Resultado> ExcluirAsync(long empresaId, long id, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.LancamentoIncluir, ct) is { } negado) return Resultado.Falha(negado);
        if ((await repositorio.ListarAsync(empresaId, ct)).All(h => h.Id != id)) return Resultado.Falha("Histórico não encontrado.");
        await repositorio.ExcluirAsync(id, ct);
        return Resultado.Ok();
    }

    private static List<string> Validar(string codigo, string descricao)
    {
        var erros = new List<string>();
        if (codigo.Length > 20) erros.Add("O código pode ter até 20 caracteres.");
        if (string.IsNullOrWhiteSpace(descricao)) erros.Add("Informe o texto do histórico.");
        else if (descricao.Trim().Length > 250) erros.Add("O texto do histórico pode ter até 250 caracteres.");
        foreach (var v in VariaveisHistorico.Desconhecidas(descricao))
            erros.Add($"Variável desconhecida {{{v}}} no texto do histórico. Use as variáveis listadas na tela.");
        return erros;
    }
}
