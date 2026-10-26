using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Vorticis.Application.Relatorios;
using Vorticis.Domain.Comum;

namespace Vorticis.Desktop.Comum.Views;

/// <summary>
/// Gráfico de barras (agrupadas por série) ou de linhas, desenhado direto no Avalonia: um eixo só, grade discreta, cores categóricas em ordem
/// fixa (validadas para daltonismo), barras com a ponta arredondada presa à linha do zero, linhas de 2 px com marcadores, legenda a partir de
/// duas séries e dica com os valores ao passar o mouse. A tabela com os números fica ao lado, na tela (as cores não são a única pista).
/// </summary>
public sealed class GraficoControl : Control
{
    public static readonly StyledProperty<DadosGrafico?> DadosProperty = AvaloniaProperty.Register<GraficoControl, DadosGrafico?>(nameof(Dados));

    /// <summary>Cores das séries 1 a 4, sempre nesta ordem (azul, laranja, água, amarelo).</summary>
    public static readonly IReadOnlyList<Color> Cores = [Color.Parse("#2a78d6"), Color.Parse("#eb6834"), Color.Parse("#1baf7a"), Color.Parse("#eda100")];

    private static readonly IBrush Superficie = new SolidColorBrush(Color.Parse("#fcfcfb"));
    private static readonly IBrush TextoPrimario = new SolidColorBrush(Color.Parse("#0b0b0b"));
    private static readonly IBrush TextoSecundario = new SolidColorBrush(Color.Parse("#52514e"));
    private static readonly IPen Grade = new Pen(new SolidColorBrush(Color.Parse("#e6e5e1")), 1);
    private static readonly IPen Base = new Pen(new SolidColorBrush(Color.Parse("#a3a29d")), 1);
    private static readonly IBrush Destaque = new SolidColorBrush(Color.Parse("#0b0b0b"), 0.05);
    private static readonly Typeface Fonte = new("Inter, Segoe UI, Arial");
    private int _hover = -1;

    static GraficoControl()
    {
        AffectsRender<GraficoControl>(DadosProperty);
        DadosProperty.Changed.AddClassHandler<GraficoControl>((g, _) => g._hover = -1);
    }

    public GraficoControl() => ClipToBounds = true;

    public DadosGrafico? Dados
    {
        get => GetValue(DadosProperty);
        set => SetValue(DadosProperty, value);
    }

    private static FormattedText Texto(string t, double tamanho, IBrush cor, FontWeight peso = FontWeight.Normal) =>
        new(t, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(Fonte.FontFamily, FontStyle.Normal, peso), tamanho, cor);

    private static string Curto(decimal v) => Math.Abs(v) switch
    {
        >= 1_000_000 => (v / 1_000_000m).ToString("0.#", Formato.PtBr) + " mi",
        >= 10_000 => (v / 1_000m).ToString("0.#", Formato.PtBr) + " mil",
        _ => v.ToString(v == Math.Truncate(v) ? "N0" : "N2", Formato.PtBr),
    };

    public static string Valor(decimal v, string unidade) => unidade == "R$" ? Formato.Moeda(v) : $"{v.ToString(v == Math.Truncate(v) ? "N0" : "N2", Formato.PtBr)} {unidade}";

    /// <summary>Área do gráfico (sem eixos, legenda e rótulos).</summary>
    private Rect Area(DadosGrafico d) => new(64, 32, Math.Max(10, Bounds.Width - 64 - 12), Math.Max(10, Bounds.Height - 32 - 44));

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(Superficie, new Rect(Bounds.Size));
        if (Dados is not { } d || d.Vazio)
        {
            var vazio = Texto(Dados is null ? "Escolha o período e clique em Gerar." : "Sem dados no período escolhido.", 13, TextoSecundario);
            ctx.DrawText(vazio, new Point((Bounds.Width - vazio.Width) / 2, (Bounds.Height - vazio.Height) / 2));
            return;
        }
        var area = Area(d);
        var todos = d.Series.SelectMany(s => s.Valores).ToList();
        var escala = MontagemGraficos.Escala(todos.Min(), todos.Max());
        var (min, max) = (escala[0], escala[^1]);
        double Y(decimal v) => area.Bottom - (double)((v - min) / (max - min)) * area.Height;

        // grade e rótulos do eixo vertical (discretos)
        foreach (var marca in escala)
        {
            var y = Y(marca);
            ctx.DrawLine(marca == 0 ? Base : Grade, new Point(area.Left, y), new Point(area.Right, y));
            var t = Texto(Curto(marca), 11, TextoSecundario);
            ctx.DrawText(t, new Point(area.Left - t.Width - 6, y - t.Height / 2));
        }
        var unidade = Texto(d.Unidade, 11, TextoSecundario);
        ctx.DrawText(unidade, new Point(Math.Max(4, area.Left - unidade.Width - 6), 15 - unidade.Height / 2));   // unidade do eixo, na altura da legenda

        var n = d.Categorias.Count;
        var largura = area.Width / n;
        if (_hover >= 0 && _hover < n) ctx.FillRectangle(Destaque, new Rect(area.Left + _hover * largura, area.Top, largura, area.Height));

        // rótulos das categorias: um a cada k, para não encavalar
        var passo = Math.Max(1, (int)Math.Ceiling(n * 80 / Math.Max(area.Width, 1)));
        for (var i = 0; i < n; i += passo)
        {
            var nome = d.Categorias[i].Length > 16 ? d.Categorias[i][..15] + "…" : d.Categorias[i];
            var t = Texto(nome, 11, TextoSecundario);
            ctx.DrawText(t, new Point(area.Left + i * largura + (largura - t.Width) / 2, area.Bottom + 6));
        }

        if (d.Forma == FormaGrafico.Barras) DesenharBarras(ctx, d, area, largura, Y);
        else DesenharLinhas(ctx, d, area, largura, Y);

        // legenda (a partir de duas séries): quadrado da cor + nome em texto neutro
        if (d.Series.Count > 1)
        {
            var x = area.Left;
            for (var s = 0; s < d.Series.Count; s++)
            {
                ctx.DrawRectangle(new SolidColorBrush(Cores[s]), null, new RoundedRect(new Rect(x, 9, 12, 12), 3));
                var t = Texto(d.Series[s].Nome, 12, TextoPrimario);
                ctx.DrawText(t, new Point(x + 18, 15 - t.Height / 2));
                x += 18 + t.Width + 18;
            }
        }
    }

    private static void DesenharBarras(DrawingContext ctx, DadosGrafico d, Rect area, double largura, Func<decimal, double> Y)
    {
        var series = d.Series.Count;
        var grupo = Math.Min(largura * 0.72, 56 * series + 2 * (series - 1));   // barras finas: no máximo 56 px cada
        var barra = Math.Max(2, (grupo - 2 * (series - 1)) / series);   // 2 px de superfície entre barras vizinhas
        var zero = Y(0);
        for (var i = 0; i < d.Categorias.Count; i++)
            for (var s = 0; s < series; s++)
            {
                var v = d.Series[s].Valores[i];
                if (v == 0) continue;
                var x = area.Left + i * largura + (largura - grupo) / 2 + s * (barra + 2);
                var (topo, fundo) = v > 0 ? (Y(v), zero) : (zero, Y(v));
                var cor = new SolidColorBrush(Cores[s]);
                var r = new Rect(x, topo, barra, Math.Max(1, fundo - topo));
                ctx.DrawRectangle(cor, null, new RoundedRect(r, Math.Min(4, barra / 2)));
                // a ponta do lado do zero fica reta (presa à linha de base)
                var reto = Math.Min(4, r.Height / 2);
                ctx.FillRectangle(cor, v > 0 ? new Rect(r.X, r.Bottom - reto, r.Width, reto) : new Rect(r.X, r.Y, r.Width, reto));
            }
        // um rótulo só: o maior valor (rótulo seletivo, nunca em todas as barras)
        if (series == 1 && d.Series[0].Valores.Count > 0)
        {
            var valores = d.Series[0].Valores;
            var i = valores.ToList().IndexOf(valores.Max());
            var t = Texto(Valor(valores[i], d.Unidade), 11, TextoPrimario, FontWeight.SemiBold);
            ctx.DrawText(t, new Point(Math.Clamp(area.Left + i * largura + (largura - t.Width) / 2, area.Left, area.Right - t.Width), Math.Max(area.Top, Y(valores[i]) - t.Height - 2)));
        }
    }

    private static void DesenharLinhas(DrawingContext ctx, DadosGrafico d, Rect area, double largura, Func<decimal, double> Y)
    {
        for (var s = 0; s < d.Series.Count; s++)
        {
            var cor = new SolidColorBrush(Cores[s]);
            var pontos = d.Series[s].Valores.Select((v, i) => new Point(area.Left + i * largura + largura / 2, Y(v))).ToList();
            var caneta = new Pen(cor, 2, lineJoin: PenLineJoin.Round, lineCap: PenLineCap.Round);
            for (var i = 1; i < pontos.Count; i++) ctx.DrawLine(caneta, pontos[i - 1], pontos[i]);
            foreach (var p in pontos) ctx.DrawEllipse(cor, new Pen(Superficie, 2), p, 4, 4);   // marcador de 8 px com anel da superfície
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Dados is not { } d || d.Vazio) return;
        var area = Area(d);
        var p = e.GetPosition(this);
        var i = p.X < area.Left || p.X > area.Right ? -1 : Math.Clamp((int)((p.X - area.Left) / (area.Width / d.Categorias.Count)), 0, d.Categorias.Count - 1);
        if (i == _hover) return;
        _hover = i;
        InvalidateVisual();
        if (i < 0) { ToolTip.SetIsOpen(this, false); return; }
        ToolTip.SetTip(this, d.Categorias[i] + "\n" + string.Join("\n", d.Series.Select(s => $"{s.Nome}: {Valor(s.Valores[i], d.Unidade)}")));
        ToolTip.SetIsOpen(this, true);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = -1;
        ToolTip.SetIsOpen(this, false);
        InvalidateVisual();
    }
}
