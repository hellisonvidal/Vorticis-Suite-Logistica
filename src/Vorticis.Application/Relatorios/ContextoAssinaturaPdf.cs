namespace Vorticis.Application.Relatorios;

/// <summary>
/// Assinatura digital "ambiente": quem chama um relatório (a tela) declara, no escopo da chamada, que o PDF gerado deve sair assinado,
/// sem que cada serviço de relatório precise repassar o certificado. Vale só para a cadeia assíncrona da chamada (AsyncLocal).
/// </summary>
public static class ContextoAssinaturaPdf
{
    private static readonly AsyncLocal<AssinaturaDigitalPdf?> Corrente = new();

    public static AssinaturaDigitalPdf? Atual => Corrente.Value;

    /// <summary>Usa a assinatura até o <c>Dispose</c> do retorno (use com <c>using</c>).</summary>
    public static IDisposable Usar(AssinaturaDigitalPdf? assinatura)
    {
        var anterior = Corrente.Value;
        Corrente.Value = assinatura;
        return new Escopo(anterior);
    }

    private sealed class Escopo(AssinaturaDigitalPdf? anterior) : IDisposable
    {
        public void Dispose() => Corrente.Value = anterior;
    }
}
