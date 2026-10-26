using Avalonia.Threading;

namespace Vorticis.Desktop.Comum;

/// <summary>
/// Rede de segurança: um erro não previsto na tela (ex.: um botão que lê arquivos) não fecha mais o módulo. O erro vai para a barra de
/// status e para o arquivo %LOCALAPPDATA%\Vorticis\logs\erros.log, e o usuário continua trabalhando.
/// </summary>
public static class ErrosInesperados
{
    public static string Arquivo => Path.Combine(Configuracao.PastaLocal, "logs", "erros.log");

    /// <summary>Instala o tratamento; <paramref name="mostrar"/> recebe a mensagem para o usuário (na thread da tela).</summary>
    public static void Instalar(Action<string> mostrar)
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            mostrar(Registrar(e.Exception));
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            var msg = Registrar(e.Exception);
            Dispatcher.UIThread.Post(() => mostrar(msg));
        };
    }

    /// <summary>Grava o erro no log e devolve o texto para o usuário.</summary>
    public static string Registrar(Exception ex)
    {
        var raiz = ex is AggregateException { InnerExceptions.Count: 1 } a ? a.InnerExceptions[0] : ex;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Arquivo)!);
            File.AppendAllText(Arquivo, $"{DateTime.Now:dd/MM/yyyy HH:mm:ss} {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception) { /* sem log, mas a mensagem ainda aparece */ }
        return $"Ocorreu um erro inesperado e a operação foi interrompida: {raiz.Message} (detalhes em {Arquivo}). O programa continua aberto.";
    }
}
