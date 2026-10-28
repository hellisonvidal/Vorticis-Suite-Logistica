using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Vorticis.Frota.Desktop.Views;

/// <summary>Ponto do mapa: latitude, longitude e o rótulo (cidade).</summary>
public sealed record PontoMapa(double Lat, double Lon, string Rotulo);

/// <summary>
/// Mapa do roteirizador. Com internet, o fundo é o mapa de ruas e estradas do OpenStreetMap (<see cref="ProvedorBlocosMapa"/>); sem internet,
/// as cidades da região aparecem como pontos claros. Por cima: o caminho (pela estrada, quando consultado; senão em linha reta), as paradas
/// numeradas (origem verde, destino vermelho), o km de cada trecho e um quadro com os totais. Roda do mouse aproxima/afasta, arrastar move,
/// clique duplo volta a enquadrar o roteiro.
/// </summary>
public sealed class MapaRoteiro : Control
{
    public static readonly StyledProperty<IReadOnlyList<PontoMapa>?> ParadasProperty = AvaloniaProperty.Register<MapaRoteiro, IReadOnlyList<PontoMapa>?>(nameof(Paradas));
    public static readonly StyledProperty<IReadOnlyList<(double Lat, double Lon)>?> CaminhoProperty =
        AvaloniaProperty.Register<MapaRoteiro, IReadOnlyList<(double Lat, double Lon)>?>(nameof(Caminho));
    /// <summary>Rótulo de cada trecho (ex.: "107,6 km"), na ordem das paradas (com a volta à origem, se houver).</summary>
    public static readonly StyledProperty<IReadOnlyList<string>?> RotulosTrechosProperty = AvaloniaProperty.Register<MapaRoteiro, IReadOnlyList<string>?>(nameof(RotulosTrechos));
    /// <summary>Linhas do quadro de totais (km, tempo, custo), no canto do mapa.</summary>
    public static readonly StyledProperty<IReadOnlyList<string>?> LegendaProperty = AvaloniaProperty.Register<MapaRoteiro, IReadOnlyList<string>?>(nameof(Legenda));
    public static readonly StyledProperty<bool> VoltaOrigemProperty = AvaloniaProperty.Register<MapaRoteiro, bool>(nameof(VoltaOrigem));

    static MapaRoteiro()
    {
        AffectsRender<MapaRoteiro>(ParadasProperty, CaminhoProperty, RotulosTrechosProperty, LegendaProperty, VoltaOrigemProperty);
        ParadasProperty.Changed.AddClassHandler<MapaRoteiro>((m, _) => m.Enquadrar());
        CaminhoProperty.Changed.AddClassHandler<MapaRoteiro>((m, _) => m.Enquadrar());
    }

    public MapaRoteiro()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public IReadOnlyList<PontoMapa>? Paradas { get => GetValue(ParadasProperty); set => SetValue(ParadasProperty, value); }
    public IReadOnlyList<(double Lat, double Lon)>? Caminho { get => GetValue(CaminhoProperty); set => SetValue(CaminhoProperty, value); }
    public IReadOnlyList<string>? RotulosTrechos { get => GetValue(RotulosTrechosProperty); set => SetValue(RotulosTrechosProperty, value); }
    public IReadOnlyList<string>? Legenda { get => GetValue(LegendaProperty); set => SetValue(LegendaProperty, value); }
    public bool VoltaOrigem { get => GetValue(VoltaOrigemProperty); set => SetValue(VoltaOrigemProperty, value); }

    /// <summary>Zoom do mapa (nível dos blocos, 3 a 17) e o centro em coordenadas de mundo (pixels no zoom 0 × 256).</summary>
    public double Zoom { get; private set; } = 4;
    private (double X, double Y) _centro = Mundo(-15, -52);
    private bool _enquadrado;
    private Point? _arrasto;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ProvedorBlocosMapa.BlocosChegaram += InvalidateVisual;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ProvedorBlocosMapa.BlocosChegaram -= InvalidateVisual;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (!_enquadrado) Enquadrar();
    }

    /// <summary>Projeção de Mercator (a dos mapas da web): latitude e longitude → coordenadas de mundo no zoom 0 (0 a 256).</summary>
    private static (double X, double Y) Mundo(double lat, double lon)
    {
        var s = Math.Sin(Math.Clamp(lat, -85, 85) * Math.PI / 180);
        return ((lon + 180) / 360 * 256, (0.5 - Math.Log((1 + s) / (1 - s)) / (4 * Math.PI)) * 256);
    }

    private Point Tela(double lat, double lon)
    {
        var (x, y) = Mundo(lat, lon);
        var f = Math.Pow(2, Zoom);
        return new Point((x - _centro.X) * f + Bounds.Width / 2, (y - _centro.Y) * f + Bounds.Height / 2);
    }

    /// <summary>Enquadra todas as paradas e o caminho, com folga para os rótulos.</summary>
    public void Enquadrar()
    {
        var pontos = (Paradas ?? []).Select(p => (p.Lat, p.Lon)).Concat(Caminho ?? []).ToList();
        if (Bounds.Width < 10 || Bounds.Height < 10) { _enquadrado = false; InvalidateVisual(); return; }
        _enquadrado = true;
        if (pontos.Count == 0) { (_centro, Zoom) = (Mundo(-15, -52), 4); InvalidateVisual(); return; }
        var mundo = pontos.Select(p => Mundo(p.Lat, p.Lon)).ToList();
        var (x0, x1, y0, y1) = (mundo.Min(p => p.X), mundo.Max(p => p.X), mundo.Min(p => p.Y), mundo.Max(p => p.Y));
        _centro = ((x0 + x1) / 2, (y0 + y1) / 2);
        var largura = Math.Max(x1 - x0, 1e-6);
        var altura = Math.Max(y1 - y0, 1e-6);
        var z = Math.Log2(Math.Min((Bounds.Width - 260) / largura, (Bounds.Height - 140) / altura));
        Zoom = Math.Clamp(Math.Floor(z * 2) / 2, 3, 13);
        InvalidateVisual();
    }

    /// <summary>Aproxima (+1) ou afasta (−1) mantendo o ponto sob o mouse no lugar.</summary>
    public void Aproximar(double passos, Point? sobre = null)
    {
        var p = sobre ?? new Point(Bounds.Width / 2, Bounds.Height / 2);
        var f = Math.Pow(2, Zoom);
        var alvo = (X: _centro.X + (p.X - Bounds.Width / 2) / f, Y: _centro.Y + (p.Y - Bounds.Height / 2) / f);
        Zoom = Math.Clamp(Zoom + passos, 3, 17);
        var g = Math.Pow(2, Zoom);
        _centro = (alvo.X - (p.X - Bounds.Width / 2) / g, alvo.Y - (p.Y - Bounds.Height / 2) / g);
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        Aproximar(e.Delta.Y > 0 ? 0.5 : -0.5, e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2) { Enquadrar(); e.Handled = true; return; }
        _arrasto = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_arrasto is not { } antes) return;
        var agora = e.GetPosition(this);
        var f = Math.Pow(2, Zoom);
        _centro = (_centro.X - (agora.X - antes.X) / f, _centro.Y - (agora.Y - antes.Y) / f);
        _arrasto = agora;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _arrasto = null;
        e.Pointer.Capture(null);
    }

    private static readonly IReadOnlyList<(double Lat, double Lon)> Cidades = [.. Vorticis.Frota.BaseMunicipios.Todos.Select(m => (m.Latitude, m.Longitude))];

    public override void Render(DrawingContext ctx)
    {
        var area = new Rect(Bounds.Size);
        ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xF3)), area);
        var paradas = Paradas ?? [];
        DesenharFundo(ctx, area);

        // caminho: pela estrada (consultado) ou em linha reta entre as paradas
        var reta = paradas.Select(p => (p.Lat, p.Lon)).ToList();
        if (VoltaOrigem && reta.Count > 1) reta.Add(reta[0]);
        var caminho = Caminho is { Count: > 1 } c ? c : reta;
        if (caminho.Count > 1)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(Tela(caminho[0].Lat, caminho[0].Lon), false);
                foreach (var (lat, lon) in caminho.Skip(1)) g.LineTo(Tela(lat, lon));
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, new Pen(Brushes.White, 10, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geo);
            var tracejado = Caminho is { Count: > 1 } ? null : new DashStyle([2, 1.5], 0);
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(0x0F, 0x76, 0x6E)), 5, tracejado, PenLineCap.Round, PenLineJoin.Round), geo);
        }

        // km de cada trecho, no meio dele (sobre o caminho da estrada, quando houver)
        var rotulos = RotulosTrechos ?? [];
        for (var i = 0; i + 1 < reta.Count && i < rotulos.Count; i++)
        {
            var a = Tela(reta[i].Lat, reta[i].Lon);
            var b = Tela(reta[i + 1].Lat, reta[i + 1].Lon);
            var meio = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            if (Caminho is { Count: > 1 })
                meio = Caminho.Select(p => Tela(p.Lat, p.Lon)).MinBy(p => (p.X - meio.X) * (p.X - meio.X) + (p.Y - meio.Y) * (p.Y - meio.Y));
            Etiqueta(ctx, meio, rotulos[i], Color.FromRgb(0x0F, 0x76, 0x6E), Colors.White, 12, centralizada: true);
        }

        // paradas numeradas: origem verde, intermediárias azuis, destino vermelho
        for (var i = 0; i < paradas.Count; i++)
        {
            var p = Tela(paradas[i].Lat, paradas[i].Lon);
            var cor = i == 0 ? Color.FromRgb(0x16, 0xA3, 0x4A) : i == paradas.Count - 1 && !VoltaOrigem ? Color.FromRgb(0xDC, 0x26, 0x26) : Color.FromRgb(0x25, 0x63, 0xEB);
            ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)), null, new Point(p.X + 1, p.Y + 2), 15, 15);
            ctx.DrawEllipse(new SolidColorBrush(cor), new Pen(Brushes.White, 3), p, 14, 14);
            var numero = Texto((i + 1).ToString(CultureInfo.InvariantCulture), 13, FontWeight.Bold, Colors.White);
            ctx.DrawText(numero, new Point(p.X - numero.Width / 2, p.Y - numero.Height / 2));
            Etiqueta(ctx, new Point(p.X + 19, p.Y), paradas[i].Rotulo, Colors.White, Color.FromRgb(0x0F, 0x17, 0x2A), 13, centralizada: false);
        }

        // quadro dos totais
        if (Legenda is { Count: > 0 } legenda)
        {
            var linhas = legenda.Select((l, k) => Texto(l, k == 0 ? 14 : 12.5, k == 0 ? FontWeight.Bold : FontWeight.Normal, Color.FromRgb(0x0F, 0x17, 0x2A))).ToList();
            var caixa = new Rect(12, 12, linhas.Max(l => l.Width) + 24, linhas.Sum(l => l.Height + 3) + 16);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)), new Pen(new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)), 1), caixa, 8, 8);
            var y = caixa.Y + 8;
            foreach (var l in linhas) { ctx.DrawText(l, new Point(caixa.X + 12, y)); y += l.Height + 3; }
        }
        if (paradas.Count == 0)
            Etiqueta(ctx, new Point(area.Width / 2, 40), "Inclua as paradas ao lado para ver o roteiro no mapa.", Colors.White, Color.FromRgb(0x33, 0x41, 0x55), 14, centralizada: true);
    }

    /// <summary>Mapa de ruas e estradas (blocos do OpenStreetMap) ou, sem eles, as cidades em pontos.</summary>
    private void DesenharFundo(DrawingContext ctx, Rect area)
    {
        var z = (int)Math.Round(Zoom);
        var escala = Math.Pow(2, Zoom - z);                               // zoom fracionário: os blocos são ampliados um pouco
        var tam = 256 * escala;
        var f = Math.Pow(2, z);
        var (cx, cy) = (_centro.X * f, _centro.Y * f);                   // centro em pixels do zoom inteiro
        var x0 = (int)Math.Floor((cx - area.Width / 2 / escala) / 256);
        var y0 = (int)Math.Floor((cy - area.Height / 2 / escala) / 256);
        var x1 = (int)Math.Floor((cx + area.Width / 2 / escala) / 256);
        var y1 = (int)Math.Floor((cy + area.Height / 2 / escala) / 256);
        var faltaram = false;
        var desenhou = false;
        for (var tx = x0; tx <= x1; tx++)
            for (var ty = y0; ty <= y1; ty++)
            {
                var destino = new Rect(area.Width / 2 + (tx * 256 - cx) * escala, area.Height / 2 + (ty * 256 - cy) * escala, tam + 0.5, tam + 0.5);
                if (ProvedorBlocosMapa.Obter(z, tx, ty) is { } bloco) { ctx.DrawImage(bloco, new Rect(bloco.Size), destino); desenhou = true; }
                else faltaram = true;
            }
        if (faltaram && !desenhou)
        {
            // sem internet: as cidades da região dão a forma do mapa
            var ponto = new SolidColorBrush(Color.FromRgb(0xA9, 0xBB, 0xC9));
            var raio = Zoom >= 7 ? 1.8 : 1.1;
            foreach (var (lat, lon) in Cidades)
            {
                var p = Tela(lat, lon);
                if (p.X >= -2 && p.Y >= -2 && p.X <= area.Width + 2 && p.Y <= area.Height + 2) ctx.DrawEllipse(ponto, null, p, raio, raio);
            }
        }
        if (desenhou)
        {
            var credito = Texto("© colaboradores do OpenStreetMap", 10, FontWeight.Normal, Color.FromRgb(0x33, 0x41, 0x55));
            var r = new Rect(area.Width - credito.Width - 10, area.Height - credito.Height - 4, credito.Width + 8, credito.Height + 2);
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), r);
            ctx.DrawText(credito, new Point(r.X + 4, r.Y + 1));
        }
    }

    private static FormattedText Texto(string texto, double tamanho, FontWeight peso, Color cor) =>
        new(texto, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, weight: peso), tamanho, new SolidColorBrush(cor));

    private static void Etiqueta(DrawingContext ctx, Point p, string texto, Color fundo, Color letra, double tamanho, bool centralizada)
    {
        var t = Texto(texto, tamanho, FontWeight.SemiBold, letra);
        var caixa = new Rect(centralizada ? p.X - t.Width / 2 - 6 : p.X, p.Y - t.Height / 2 - 3, t.Width + 12, t.Height + 6);
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), null, caixa.Translate(new Vector(1, 1.5)), 5, 5);
        ctx.DrawRectangle(new SolidColorBrush(fundo), new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0x0F, 0x17, 0x2A)), 0.8), caixa, 5, 5);
        ctx.DrawText(t, new Point(caixa.X + 6, caixa.Y + 3));
    }
}
