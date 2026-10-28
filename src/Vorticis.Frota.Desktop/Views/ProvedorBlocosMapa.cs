using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Vorticis.Frota.Desktop.Views;

/// <summary>
/// Blocos (imagens de 256×256) do mapa de ruas e estradas do OpenStreetMap, baixados sob demanda e guardados em memória e em disco
/// (pasta do usuário), para não baixar de novo. Sem internet, o mapa do roteirizador mostra só o desenho próprio (cidades em pontos).
/// Os dados são © colaboradores do OpenStreetMap (licença ODbL); a atribuição aparece no canto do mapa.
/// </summary>
public static class ProvedorBlocosMapa
{
    /// <summary>Endereço dos blocos ({z}/{x}/{y}); pode apontar para outro servidor de mapas compatível.</summary>
    public static string Url { get; set; } = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    /// <summary>Sem baixar blocos (testes, ou quando o usuário prefere o mapa sem internet).</summary>
    public static bool Desligado { get; set; }

    /// <summary>Fonte alternativa dos blocos (testes): devolve a imagem do bloco ou null.</summary>
    public static Func<int, int, int, Bitmap?>? Fonte { get; set; }

    /// <summary>Avisa os mapas abertos que chegaram blocos novos.</summary>
    public static event Action? BlocosChegaram;

    private static readonly ConcurrentDictionary<(int, int, int), Bitmap?> Memoria = new();
    private static readonly ConcurrentDictionary<(int, int, int), byte> Baixando = new();
    private static DateTime _semInternetAte = DateTime.MinValue;
    private static readonly SemaphoreSlim Limite = new(4);

    private static readonly Lazy<HttpClient> Http = new(() =>
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("VorticisSuiteLogistica/1.0 (roteirizador; +https://www.openstreetmap.org/copyright)");
        return h;
    });

    private static string PastaCache => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vorticis", "mapa");

    /// <summary>O bloco já disponível (memória ou disco) ou null; se faltar, pede o download e avisa quando chegar.</summary>
    public static Bitmap? Obter(int z, int x, int y)
    {
        if (Fonte is { } f) return f(z, x, y);
        if (Desligado) return null;
        var n = 1 << z;
        if (y < 0 || y >= n) return null;
        x = ((x % n) + n) % n;
        var chave = (z, x, y);
        if (Memoria.TryGetValue(chave, out var bmp)) return bmp;
        var arquivo = Path.Combine(PastaCache, z.ToString(), x.ToString(), $"{y}.png");
        if (File.Exists(arquivo))
        {
            try { bmp = new Bitmap(arquivo); Memoria[chave] = bmp; return bmp; }
            catch (Exception) { /* arquivo corrompido: baixa de novo */ }
        }
        if (DateTime.UtcNow < _semInternetAte || !Baixando.TryAdd(chave, 0)) return null;
        _ = Task.Run(async () =>
        {
            await Limite.WaitAsync();
            try
            {
                var bytes = await Http.Value.GetByteArrayAsync(Url.Replace("{z}", z.ToString()).Replace("{x}", x.ToString()).Replace("{y}", y.ToString()));
                Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
                await File.WriteAllBytesAsync(arquivo, bytes);
                using var ms = new MemoryStream(bytes);
                Memoria[chave] = new Bitmap(ms);
                Dispatcher.UIThread.Post(() => BlocosChegaram?.Invoke());
            }
            catch (Exception)
            {
                _semInternetAte = DateTime.UtcNow.AddMinutes(1);       // sem internet: tenta de novo daqui a pouco
            }
            finally
            {
                Baixando.TryRemove(chave, out _);
                Limite.Release();
            }
        });
        return null;
    }
}
