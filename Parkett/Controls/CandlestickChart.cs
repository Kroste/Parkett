using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Parkett.Charting;
using Parkett.Domain;

namespace Parkett.Controls;

/// <summary>
/// Zeichnet Kerzen, Preisraster, Zeitachse, eigene Ausführungen und ein Fadenkreuz.
/// Die gesamte Skalierung liegt in <see cref="ChartViewport"/> und ist dort getestet —
/// hier steht nur noch das Zeichnen.
///
/// Farben kommen als StyledProperty von außen (im XAML per DynamicResource gesetzt),
/// damit im Control keine Farbliterale stehen und das Theme greift.
/// </summary>
public sealed class CandlestickChart : Control
{
    /// <summary>Platz rechts für die Preisbeschriftung.</summary>
    private const double PriceAxisWidth = 62d;

    /// <summary>Platz unten für die Datumsbeschriftung.</summary>
    private const double TimeAxisHeight = 22d;

    private const double LabelFontSize = 11d;

    public static readonly StyledProperty<IReadOnlyList<Candle>?> CandlesProperty =
        AvaloniaProperty.Register<CandlestickChart, IReadOnlyList<Candle>?>(nameof(Candles));

    public static readonly StyledProperty<IReadOnlyList<ChartMarker>?> MarkersProperty =
        AvaloniaProperty.Register<CandlestickChart, IReadOnlyList<ChartMarker>?>(nameof(Markers));

    public static readonly StyledProperty<IBrush?> UpBrushProperty =
        AvaloniaProperty.Register<CandlestickChart, IBrush?>(nameof(UpBrush));

    public static readonly StyledProperty<IBrush?> DownBrushProperty =
        AvaloniaProperty.Register<CandlestickChart, IBrush?>(nameof(DownBrush));

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<CandlestickChart, IBrush?>(nameof(GridBrush));

    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<CandlestickChart, IBrush?>(nameof(LabelBrush));

    public static readonly StyledProperty<IBrush?> CrosshairBrushProperty =
        AvaloniaProperty.Register<CandlestickChart, IBrush?>(nameof(CrosshairBrush));

    static CandlestickChart()
    {
        // Ohne diese Registrierung bleibt der Chart nach einem Datenwechsel stehen.
        AffectsRender<CandlestickChart>(
            CandlesProperty,
            MarkersProperty,
            UpBrushProperty,
            DownBrushProperty,
            GridBrushProperty,
            LabelBrushProperty,
            CrosshairBrushProperty);
    }

    private Point? _pointer;

    public IReadOnlyList<Candle>? Candles
    {
        get => GetValue(CandlesProperty);
        set => SetValue(CandlesProperty, value);
    }

    public IReadOnlyList<ChartMarker>? Markers
    {
        get => GetValue(MarkersProperty);
        set => SetValue(MarkersProperty, value);
    }

    public IBrush? UpBrush
    {
        get => GetValue(UpBrushProperty);
        set => SetValue(UpBrushProperty, value);
    }

    public IBrush? DownBrush
    {
        get => GetValue(DownBrushProperty);
        set => SetValue(DownBrushProperty, value);
    }

    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public IBrush? LabelBrush
    {
        get => GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public IBrush? CrosshairBrush
    {
        get => GetValue(CrosshairBrushProperty);
        set => SetValue(CrosshairBrushProperty, value);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _pointer = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pointer = null;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var candles = Candles;

        if (candles is null || candles.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var plotWidth = Math.Max(1d, Bounds.Width - PriceAxisWidth);
        var plotHeight = Math.Max(1d, Bounds.Height - TimeAxisHeight);
        var viewport = new ChartViewport(candles, plotWidth, plotHeight);

        DrawGrid(context, viewport, plotWidth);
        DrawCandles(context, viewport);
        DrawMarkers(context, viewport);
        DrawTimeAxis(context, viewport, plotHeight);
        DrawCrosshair(context, viewport, plotWidth, plotHeight);
    }

    private void DrawGrid(DrawingContext context, ChartViewport viewport, double plotWidth)
    {
        var gridPen = new Pen(GridBrush ?? Brushes.Gray, 1d);
        var typeface = new Typeface(FontFamily.Default);

        foreach (var price in viewport.PriceGridLines())
        {
            var y = viewport.Y(price);

            context.DrawLine(gridPen, new Point(0, y), new Point(plotWidth, y));

            var text = new FormattedText(
                price.ToString("N2", CultureInfo.CurrentCulture),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                LabelFontSize,
                LabelBrush ?? Brushes.Gray);

            context.DrawText(text, new Point(plotWidth + 6d, y - (text.Height / 2d)));
        }
    }

    private void DrawCandles(DrawingContext context, ChartViewport viewport)
    {
        var up = UpBrush ?? Brushes.Green;
        var down = DownBrush ?? Brushes.Red;

        for (var i = 0; i < viewport.Count; i++)
        {
            var candle = viewport.VisibleCandles[i];
            var brush = candle.IsBullish ? up : down;
            var x = viewport.XCenter(i);

            // Docht
            var wickPen = new Pen(brush, Math.Max(1d, viewport.BodyWidth * 0.12d));
            context.DrawLine(wickPen, new Point(x, viewport.Y(candle.High)), new Point(x, viewport.Y(candle.Low)));

            // Körper — bei Open == Close bleibt eine sichtbare Linie stehen
            var top = viewport.Y(Math.Max(candle.Open, candle.Close));
            var bottom = viewport.Y(Math.Min(candle.Open, candle.Close));
            var height = Math.Max(1d, bottom - top);

            context.FillRectangle(
                brush,
                new Rect(x - (viewport.BodyWidth / 2d), top, viewport.BodyWidth, height));
        }
    }

    private void DrawMarkers(DrawingContext context, ChartViewport viewport)
    {
        var markers = Markers;

        if (markers is null || markers.Count == 0 || viewport.Count == 0)
        {
            return;
        }

        var first = viewport.VisibleCandles[0].OpenTime;
        var size = Math.Clamp(viewport.SlotWidth * 0.6d, 4d, 10d);

        foreach (var marker in markers)
        {
            if (marker.At < first)
            {
                continue;
            }

            // Index über den Zeitstempel suchen — Ausführungen kennen keinen Kerzenindex.
            var index = FindIndex(viewport, marker.At);

            if (index is null)
            {
                continue;
            }

            var x = viewport.XCenter(index.Value);
            var y = viewport.Y(marker.Price);
            var brush = marker.Side == OrderSide.Buy ? UpBrush ?? Brushes.Green : DownBrush ?? Brushes.Red;

            // Kauf: Dreieck unter dem Kurs, Spitze nach oben. Verkauf: darüber, Spitze nach unten.
            var offset = marker.Side == OrderSide.Buy ? size * 1.6d : -size * 1.6d;
            var tip = new Point(x, y + (marker.Side == OrderSide.Buy ? size * 0.6d : -size * 0.6d));

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(tip, isFilled: true);
                ctx.LineTo(new Point(x - size, y + offset));
                ctx.LineTo(new Point(x + size, y + offset));
                ctx.EndFigure(true);
            }

            context.DrawGeometry(brush, null, geometry);
        }
    }

    private static int? FindIndex(ChartViewport viewport, DateTimeOffset at)
    {
        for (var i = 0; i < viewport.Count; i++)
        {
            if (viewport.VisibleCandles[i].OpenTime == at)
            {
                return i;
            }
        }

        return null;
    }

    private void DrawTimeAxis(DrawingContext context, ChartViewport viewport, double plotHeight)
    {
        var typeface = new Typeface(FontFamily.Default);
        var pattern = ChartViewport.AxisDatePattern(CultureInfo.CurrentCulture);

        foreach (var (index, at) in viewport.TimeGridLines())
        {
            var text = new FormattedText(
                at.ToString(pattern, CultureInfo.CurrentCulture),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                LabelFontSize,
                LabelBrush ?? Brushes.Gray);

            var x = Math.Max(0d, viewport.XCenter(index) - (text.Width / 2d));
            context.DrawText(text, new Point(x, plotHeight + 4d));
        }
    }

    private void DrawCrosshair(DrawingContext context, ChartViewport viewport, double plotWidth, double plotHeight)
    {
        if (_pointer is not { } pointer || pointer.X > plotWidth || pointer.Y > plotHeight)
        {
            return;
        }

        var pen = new Pen(CrosshairBrush ?? Brushes.LightGray, 1d, new DashStyle([3, 3], 0));

        context.DrawLine(pen, new Point(0, pointer.Y), new Point(plotWidth, pointer.Y));
        context.DrawLine(pen, new Point(pointer.X, 0), new Point(pointer.X, plotHeight));

        var price = viewport.PriceAt(pointer.Y);
        var text = new FormattedText(
            price.ToString("N2", CultureInfo.CurrentCulture),
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default),
            LabelFontSize,
            CrosshairBrush ?? Brushes.LightGray);

        context.DrawText(text, new Point(plotWidth + 6d, pointer.Y - (text.Height / 2d)));
    }
}
