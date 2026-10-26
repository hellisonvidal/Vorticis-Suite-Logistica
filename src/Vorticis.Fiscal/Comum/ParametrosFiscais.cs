using System.Reflection;

namespace Vorticis.Fiscal.Comum;

/// <summary>Chaves dos parâmetros legais do módulo Fiscal em core.parametro_legal.</summary>
public static class ChavesParametro
{
    public const string SimplesNacional = "SIMPLES_NACIONAL";
    public const string LucroPresumidoReal = "LUCRO_PRESUMIDO_REAL";
    public const string Retencoes = "RETENCOES_GUIAS";
}

/// <summary>Leitura dos parâmetros legais vigentes em uma data (tabelas, alíquotas, códigos de receita).</summary>
public interface IParametrosFiscais
{
    /// <summary>JSON do parâmetro vigente na data. Sem registro no banco, usa (e semeia) o parâmetro embutido no programa.</summary>
    Task<string> ObterJsonAsync(string chave, DateOnly data, CancellationToken ct = default);
}

/// <summary>Parâmetros embutidos no assembly (Parametros\*.json): a semente de core.parametro_legal e o padrão dos testes.</summary>
public static class ParametrosEmbutidos
{
    public static string Json(string chave)
    {
        var assembly = typeof(ParametrosEmbutidos).Assembly;
        var nome = $"Vorticis.Fiscal.Parametros.{chave}.json";
        using var s = assembly.GetManifestResourceStream(nome) ?? throw new InvalidOperationException($"Parâmetro embutido ausente: {nome}.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    /// <summary>Provedor sem banco, só com os parâmetros embutidos.</summary>
    public sealed class Provedor : IParametrosFiscais
    {
        public Task<string> ObterJsonAsync(string chave, DateOnly data, CancellationToken ct = default) => Task.FromResult(Json(chave));
    }
}
