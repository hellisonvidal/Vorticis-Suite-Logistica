namespace Vorticis.Infra.Sistema;

/// <summary>
/// Exclusão mútua entre processos (Contábil, Fiscal e Folha abrindo ao mesmo tempo): um arquivo aberto sem
/// compartilhamento. O sistema operacional libera a trava sozinho se o processo cair.
/// </summary>
public sealed class TravaEntreProcessos : IDisposable
{
    private FileStream? _arquivo;

    private TravaEntreProcessos(FileStream arquivo) => _arquivo = arquivo;

    /// <param name="aguardando">Chamado uma vez se a trava estiver com outro processo (para avisar o usuário).</param>
    /// <exception cref="TimeoutException">Outro processo manteve a trava por mais que <paramref name="limite"/>.</exception>
    public static async Task<TravaEntreProcessos> AdquirirAsync(string arquivo, TimeSpan limite, Action? aguardando = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(arquivo))!);
        var fim = DateTime.UtcNow + limite;
        var avisou = false;
        while (true)
        {
            try
            {
                return new TravaEntreProcessos(new FileStream(arquivo, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= fim)
                    throw new TimeoutException($"Outro módulo do Vorticis está preparando o banco de dados há mais de {limite.TotalSeconds:0} s ({arquivo}).");
                if (!avisou) { avisou = true; aguardando?.Invoke(); }
                await Task.Delay(300, ct);
            }
        }
    }

    public void Dispose()
    {
        _arquivo?.Dispose();
        _arquivo = null;
    }
}
