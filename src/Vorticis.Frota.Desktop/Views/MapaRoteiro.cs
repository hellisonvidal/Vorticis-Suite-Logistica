using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Vorticis.Frota.Desktop.Views;

/// <summary>Ponto do mapa: latitude, longitude e o rótulo (número da parada e cidade).</summary>
public sealed record PontoMapa(double Lat, double Lon, string Rotulo);

/// <summary>
/// Mapa do roteirizador, desenhado sem internet: as cidades da região aparecem como pontos claros (o desenho do Brasil sai da base de
/// municípios), o caminho em linha (pela estrada, quando consultado; senão em linha reta entre as paradas) e as paradas numeradas.
/// </summary>
public sealed class MapaRoteiro : Control
{
    public static readonly StyledProperty<IReadOnlyList<PontoMapa>?> ParadasProperty = AvaloniaProperty.Register<MapaRoteiro, IReadOnlyList<PontoMapa>?>(nameof(Paradas));
    public static readonly StyledProperty<IReadOnlyList<(double Lat, double Lon)>?> CaminhoProperty =
        AvaloniaProperty.Register<MapaRoteiro, IReadOnlyList<(double Lat, double Lon)>?>(nameof(Caminho));

    static MapaRoteiro() => AffectsRender<MapaRoteiro>(ParadasProperty, CaminhoProperty);

    public IReadOnlyList<PontoMapa>? Paradas { get => GetValue(ParadasProperty); set => SetValue(ParadasProperty, value); }
    public IReadOnlyList<(double Lat, double Lon)>? Caminho { get => GetValue(CaminhoProperty); set => SetValue(CaminhoProperty, value); }

    private static readonly IReadOnlyList<(double Lat, double Lon)> Cidades =
        [.. Vorticis.Frota.BaseMunicipios.Todos.Select(m => (m.Latitude, m.Longitude))];

    public override void Render(DrawingContext ctx)
    {
        var area = new Rect(Bounds.Size);
        ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(0xEE, 0xF4, 0xF8)), area);
        var paradas = Paradas ?? [];
        var caminho = Caminho is { Count: > 1 } c ? c : [.. paradas.Select(p => (p.Lat, p.Lon))];
        // enquadramento: as paradas e o caminho com folga; sem paradas, o Brasil inteiro
        var todos = paradas.Select(p => (p.Lat, p.Lon)).Concat(caminho).ToList();
        double latMin, latMax, lonMin, lonMax;
        if (todos.Count == 0) (latMin, latMax, lonMin, lonMax) = (-34, 5.5, -74, -34.5);
        else
        {
            (latMin, latMax, lonMin, lonMax) = (todos.Min(p => p.Lat), todos.Max(p => p.Lat), todos.Min(p => p.Lon), todos.Max(p => p.Lon));
            var folga = Math.Max(0.6, Math.Max(latMax - latMin, lonMax - lonMin) * 0.15);
            (latMin, latMax, lonMin, lonMax) = (latMin - folga, latMax + folga, lonMin - folga, lonMax + folga);
        }
        var cos = Math.Cos((latMin + latMax) / 2 * Math.PI / 180);
        // à direita fica espaço para os nomes das cidades
        const double margem = 20, espacoNomes = 170;
        var escala = Math.Min((area.Width - margem - espacoNomes) / ((lonMax - lonMin) * cos), (area.Height - 2 * margem) / (latMax - latMin));
        var largura = (lonMax - lonMin) * cos * escala;
        var altura = (latMax - latMin) * escala;
        var (x0, y0) = (margem + (area.Width - margem - espacoNomes - largura) / 2, (area.Height - altura) / 2);
        Point P(double lat, double lon) => new(x0 + (lon - lonMin) * cos * escala, y0 + (latMax - lat) * escala);

        // as cidades da região (contorno do Brasil e referência de escala)
        var ponto = new SolidColorBrush(Color.FromRgb(0xB8, 0xC7, 0xD3));
        var raio = escala > 60 ? 1.6 : 1.0;
        foreach (var (lat, lon) in Cidades)
            if (lat >= latMin && lat <= latMax && lon >= lonMin && lon <= lonMax) ctx.DrawEllipse(ponto, null, P(lat, lon), raio, raio);

        // o caminho
        if (caminho.Count > 1)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(P(caminho[0].Lat, caminho[0].Lon), false);
                foreach (var (lat, lon) in caminho.Skip(1)) g.LineTo(P(lat, lon));
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0x0F, 0x76, 0x6E)), 7, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geo);
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(0x0F, 0x76, 0x6E)), 3, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geo);
        }

        // as paradas numeradas (origem verde, destino final vermelho)
        for (var i = 0; i < paradas.Count; i++)
        {
            var p = P(paradas[i].Lat, paradas[i].Lon);
            var cor = i == 0 ? Color.FromRgb(0x16, 0xA3, 0x4A) : i == paradas.Count - 1 ? Color.FromRgb(0xDC, 0x26, 0x26) : Color.FromRgb(0x25, 0x63, 0xEB);
            ctx.DrawEllipse(new SolidColorBrush(cor), new Pen(Brushes.White, 2), p, 11, 11);
            var numero = new FormattedText((i + 1).ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                           new Typeface(FontFamily.Default, weight: FontWeight.Bold), 11, Brushes.White);
            ctx.DrawText(numero, new Point(p.X - numero.Width / 2, p.Y - numero.Height / 2));
            var nome = new FormattedText(paradas[i].Rotulo, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, weight: FontWeight.SemiBold),
                                         12, new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37)));
            var caixa = new Rect(p.X + 14, p.Y - nome.Height / 2 - 2, nome.Width + 8, nome.Height + 4);
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)), caixa, 3);
            ctx.DrawText(nome, new Point(caixa.X + 4, caixa.Y + 2));
        }
        if (paradas.Count == 0)
        {
            var dica = new FormattedText("Inclua as paradas ao lado para ver o roteiro no mapa.", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                         Typeface.Default, 14, new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)));
            ctx.DrawText(dica, new Point((area.Width - dica.Width) / 2, 16));
        }
    }
}
