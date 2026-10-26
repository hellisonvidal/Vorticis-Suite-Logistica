using System.Diagnostics;

namespace Vorticis.Desktop.Comum;

/// <summary>Grava relatórios gerados em Documentos\Vorticis Contabil\Relatorios e os abre no programa padrão.</summary>
public static class ArquivosGerados
{
    /// <summary>Desligado nos testes automatizados.</summary>
    public static bool AbrirAposGerar { get; set; } = true;

    public static string Pasta { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Vorticis Contabil", "Relatorios");

    public static string Salvar(string nome, byte[] conteudo)
    {
        Directory.CreateDirectory(Pasta);
        var invalidos = Path.GetInvalidFileNameChars();
        var arquivo = Path.Combine(Pasta, new string(nome.Select(c => invalidos.Contains(c) ? '_' : c).ToArray()));
        File.WriteAllBytes(arquivo, conteudo);
        if (AbrirAposGerar)
        {
            try { Process.Start(new ProcessStartInfo(arquivo) { UseShellExecute = true }); }
            catch (Exception) { /* sem leitor de PDF associado: o caminho é mostrado na tela */ }
        }
        return arquivo;
    }
}
