using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Application.Sistema;

public sealed record ArquivoBackup(string Caminho, DateTimeOffset CriadoEm, long Bytes)
{
    public string Nome => Path.GetFileName(Caminho);
}

/// <summary>Cópia de segurança do banco inteiro (todas as empresas).</summary>
public interface IBackupBanco
{
    /// <summary>Gera um arquivo de backup em <paramref name="pasta"/> e devolve o caminho.</summary>
    Task<string> GerarAsync(string pasta, string prefixo, CancellationToken ct);

    /// <summary>Substitui o conteúdo do banco pelo do arquivo.</summary>
    Task RestaurarAsync(string arquivo, CancellationToken ct);
}

/// <summary>
/// Configurações › Backup e restauração (somente administrador). Antes de restaurar, gera automaticamente
/// um backup de segurança do estado atual, para que a restauração possa ser desfeita.
/// </summary>
public sealed class ServicoBackup(IBackupBanco backup)
{
    public const string Extensao = ".backup";

    /// <summary>
    /// Pasta padrão: "Backups" dentro do diretório de instalação do programa. Se ela não puder ser gravada
    /// (instalação em Arquivos de Programas para todos os usuários), usa Documentos\Vorticis Contabil\Backups.
    /// </summary>
    public static string PastaPadrao => _pastaPadrao.Value;

    private static readonly Lazy<string> _pastaPadrao = new(() =>
    {
        var naInstalacao = Path.Combine(AppContext.BaseDirectory, "Backups");
        return PodeGravar(naInstalacao)
            ? naInstalacao
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Vorticis Contabil", "Backups");
    });

    /// <summary>Verifica se é possível criar a pasta e gravar nela.</summary>
    public static bool PodeGravar(string pasta)
    {
        try
        {
            Directory.CreateDirectory(pasta);
            var teste = Path.Combine(pasta, $".vorticis_{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(teste, []);
            File.Delete(teste);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Exclui um arquivo de backup (somente administrador; só arquivos .backup).</summary>
    public static Resultado Excluir(SessaoUsuario sessao, string arquivo)
    {
        if (!sessao.Administrador) return Resultado.Falha("Somente administradores excluem backups.");
        if (!string.Equals(Path.GetExtension(arquivo), Extensao, StringComparison.OrdinalIgnoreCase))
            return Resultado.Falha("Só arquivos de backup do Vorticis (.backup) podem ser excluídos aqui.");
        if (!File.Exists(arquivo)) return Resultado.Falha($"Arquivo não encontrado: {arquivo}");
        try
        {
            File.Delete(arquivo);
            return Resultado.Ok();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Resultado.Falha($"Não foi possível excluir: {e.Message}");
        }
    }

    public static IReadOnlyList<ArquivoBackup> Listar(string pasta) =>
        Directory.Exists(pasta)
            ? new DirectoryInfo(pasta).GetFiles("*" + Extensao)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => new ArquivoBackup(f.FullName, new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero), f.Length))
                .ToArray()
            : [];

    public async Task<Resultado<string>> GerarAsync(SessaoUsuario sessao, string pasta, CancellationToken ct = default)
    {
        if (!sessao.Administrador) return Resultado<string>.Falha("Somente administradores fazem backup.");
        try
        {
            Directory.CreateDirectory(pasta);
            return Resultado<string>.Ok(await backup.GerarAsync(pasta, "vorticis", ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Resultado<string>.Falha($"Falha no backup: {ex.Message}");
        }
    }

    /// <returns>Caminho do backup de segurança gerado antes da restauração.</returns>
    public async Task<Resultado<string>> RestaurarAsync(SessaoUsuario sessao, string arquivo, string pastaSeguranca, CancellationToken ct = default)
    {
        if (!sessao.Administrador) return Resultado<string>.Falha("Somente administradores restauram backups.");
        if (!File.Exists(arquivo)) return Resultado<string>.Falha($"Arquivo não encontrado: {arquivo}");
        string seguranca;
        try
        {
            Directory.CreateDirectory(pastaSeguranca);
            seguranca = await backup.GerarAsync(pastaSeguranca, "antes_da_restauracao", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Resultado<string>.Falha($"Restauração cancelada: não foi possível gerar o backup de segurança ({ex.Message}).");
        }
        try
        {
            await backup.RestaurarAsync(arquivo, ct);
            return Resultado<string>.Ok(seguranca);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Resultado<string>.Falha($"Falha na restauração: {ex.Message}. O estado anterior está salvo em {seguranca}.");
        }
    }
}
