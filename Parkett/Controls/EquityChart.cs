using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Parkett.Charting;
using Parkett.Domain;

namespace Parkett.Controls;

/// <summary>
/// Zeichnet die Equity-Kurve des Abschlussberichts: Verlauf, Startkapital-Linie,
/// Wertraster und eine Markierung am größten Rückgang.
///
/// Die Skalierung liegt in <see cref="EquityViewport"/> und ist dort getestet —
/// hier steht nur noch das Zeichnen. Farben kommen als StyledProperty von außen
/// (im XAML per DynamicResource), damit im Control keine Farbliterale stehen.
/// </summary>
public sealed class EquityChart : Control
{
    /// <summary>Platz rechts für die Wertbeschriftung.</summary>
    private const double ValueAxisWidth = 68d;

    private const double LabelFontSize = 11d;

    public static readonly StyledProperty<IReadOnlyList<EquityPoint>?> CurveProperty =
        AvaloniaProperty.Register<EquityChart, IReadOnlyList<EquityPoint>?>(nameof(Curve));

    public static readonly StyledProperty<decimal> StartEquityProperty =
        AvaloniaProperty.Register<EquityChart, decimal>(nameof(StartEquity));

    public static readonly StyledProperty<IBrush?> GainBrushProperty =
        AvaloniaProperty.Register<EquityChart, IBrush?>(nameof(GainBrush));

    public static readonly StyledProperty<IBrush?> LossBrushProperty =
        AvaloniaProperty.Register<EquityChart, IBrush?>(nameof(LossBrush));

    public static readonly StyledProperty<IBrush?> BaselineBrushProperty =
        AvaloniaProperty.Register<EquityChart, IBrush?>(nameof(BaselineBrush));

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<EquityChart, IBrush?>(nameof(GridBrush));

    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<EquityChart, IBrush?>(nameof(LabelBrush));

    static EquityChart()
    {
        // Ohne diese Registrierung bleibt der Chart nach einem Datenwechsel stehen.
        AffectsRender<EquityChart>(
            CurveProperty,
            StartEquityProperty,
            GainBrushProperty,
            LossBrushProperty,
            BaselineBrushProperty,
            GridBrushProperty,
            LabelBrushProperty);
    }

    public IReadOnlyList<EquityPoint>? Curve
    {
        get => GetValue(CurveProperty);
        set => SetValue(CurveProperty, value);
    }

    public decimal StartEquity
    {
        get => GetValue(StartEquityProperty);
        set => SetValue(StartEquityProperty, value);
    }

    public IBrush? GainBrush
    {
        get => GetValue(GainBrushProperty);
        set => SetValue(GainBrushProperty, value);
    }

    public IBrush? LossBrush
    {
        get => GetValue(LossBrushProperty);
        set => SetValue(LossBrushProperty, value);
    }

    public IBrush? BaselineBrush
    {
        get => GetValue(BaselineBrushProperty);
        set => SetValue(BaselineBrushProperty, value);
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

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var plotWidth = Math.Max(1d, Bounds.Width - ValueAxisWidth);
        var plotHeight = Math.Max(1d, Bounds.Height);

        var curve = Curve ?? [];
        var viewport = new EquityViewport(curve, StartEquity, plotWidth, plotHeight);

        DrawGrid(context, viewport, plotWidth);
        DrawBaseline(context, viewport, plotWidth);

        if (viewport.Count < 2)
        {
            // Eine Sitzung ohne zwei Punkte hat keinen Verlauf — Raster und
            // Startlinie stehen trotzdem, sonst wirkt das Fenster kaputt.
            return;
        }

        DrawCurve(context, viewport);
        DrawDrawdownMarker(context, viewport);
    }

    private void DrawGrid(DrawingContext context, EquityViewport viewport, double plotWidth)
    {
        if (GridBrush is not { } grid)
        {
            return;
        }

        var pen = new Pen(grid, 1d);
        var typeface = new Typeface(FontFamily.Default);

        foreach (var value in viewport.ValueGridLines())
        {
            var y = viewport.Y(value);
            context.DrawLine(pen, new Point(0, y), new Point(plotWidth, y));

            if (LabelBrush is not { } label)
            {
                continue;
            }

            var text = new FormattedText(
                value.ToString("N0", CultureInfo.CurrentCulture),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                LabelFontSize,
                label);

            context.DrawText(text, new Point(plotWidth + 8d, y - (text.Height / 2d)));
        }
    }

    /// <summary>
    /// Das Startkapital als gestrichelte Linie — die Bezugsgröße, ohne die der
    /// Verlauf keine Aussage hat.
    /// </summary>
    private void DrawBaseline(DrawingContext context, EquityViewport viewport, double plotWidth)
    {
        if (BaselineBrush is not { } baseline)
        {
            return;
        }

        var pen = new Pen(baseline, 1.5d, new DashStyle([4d, 4d], 0d));
        var y = viewport.StartLineY;

        context.DrawLine(pen, new Point(0, y), new Point(plotWidth, y));

        if (LabelBrush is null)
        {
            return;
        }

        var text = new FormattedText(
            viewport.StartEquity.ToString("N0", CultureInfo.CurrentCulture),
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default),
            LabelFontSize,
            baseline);

        context.DrawText(text, new Point(plotWidth + 8d, y - (text.Height / 2d)));
    }

    /// <summary>
    /// Verlauf als Linie plus schwach gefüllte Fläche bis zur Startkapital-Linie.
    ///
    /// Die Fläche ist <b>zweifarbig</b>, geteilt an der Startlinie: die Abschnitte
    /// über dem Startkapital in der Gewinnfarbe, die darunter in der Verlustfarbe.
    /// Eine durchgehend eingefärbte Fläche wäre irreführend — ein Verlauf, der lange
    /// im Plus lag und erst am Ende abrutscht, wäre sonst vollständig rot.
    /// </summary>
    private void DrawCurve(DrawingContext context, EquityViewport viewport)
    {
        var flaeche = new StreamGeometry();

        using (var zeichner = flaeche.Open())
        {
            var basisY = viewport.StartLineY;

            zeichner.BeginFigure(new Point(viewport.X(0), basisY), isFilled: true);

            for (var i = 0; i < viewport.Count; i++)
            {
                zeichner.LineTo(Punkt(viewport, i));
            }

            zeichner.LineTo(new Point(viewport.X(viewport.Count - 1), basisY));
            zeichner.EndFigure(isClosed: true);
        }

        FillAbove(context, viewport, flaeche, GainBrush);
        FillBelow(context, viewport, flaeche, LossBrush);

        var linie = new StreamGeometry();

        using (var zeichner = linie.Open())
        {
            zeichner.BeginFigure(Punkt(viewport, 0), isFilled: false);

            for (var i = 1; i < viewport.Count; i++)
            {
                zeichner.LineTo(Punkt(viewport, i));
            }

            zeichner.EndFigure(isClosed: false);
        }

        // Die Linie folgt dem Endstand — sie beantwortet "wie ist es ausgegangen".
        var end = viewport.Points[^1].Equity;
        var lineBrush = end >= viewport.StartEquity ? GainBrush : LossBrush;

        if (lineBrush is not null)
        {
            context.DrawGeometry(null, new Pen(lineBrush, 2d), linie);
        }
    }

    private void FillAbove(DrawingContext context, EquityViewport viewport, Geometry flaeche, IBrush? brush)
    {
        if (brush is null)
        {
            return;
        }

        var hoehe = Math.Max(0d, viewport.StartLineY);
        using var _ = context.PushClip(new Rect(0, 0, viewport.Width, hoehe));
        context.DrawGeometry(new SolidColorBrush(Farbe(brush), 0.16d), null, flaeche);
    }

    private void FillBelow(DrawingContext context, EquityViewport viewport, Geometry flaeche, IBrush? brush)
    {
        if (brush is null)
        {
            return;
        }

        var oben = Math.Max(0d, viewport.StartLineY);
        var hoehe = Math.Max(0d, viewport.Height - oben);
        using var _ = context.PushClip(new Rect(0, oben, viewport.Width, hoehe));
        context.DrawGeometry(new SolidColorBrush(Farbe(brush), 0.16d), null, flaeche);
    }

    /// <summary>
    /// Markiert den tiefsten Punkt gemessen am bisherigen Höchststand. Ohne ihn
    /// bleibt "maximaler Rückgang: 12,4 %" eine Zahl ohne Ort im Verlauf.
    /// </summary>
    private void DrawDrawdownMarker(DrawingContext context, EquityViewport viewport)
    {
        if (viewport.MaxDrawdownIndex() is not { } index || LossBrush is not { } loss)
        {
            return;
        }

        var punkt = Punkt(viewport, index);

        context.DrawEllipse(null, new Pen(loss, 2d), punkt, 4d, 4d);
    }

    private static Point Punkt(EquityViewport viewport, int index) =>
        new(viewport.X(index), viewport.Y(viewport.Points[index].Equity));

    /// <summary>
    /// Die Füllfläche braucht dieselbe Farbe mit weniger Deckkraft. Kommt der Pinsel
    /// nicht als <see cref="ISolidColorBrush"/> (Verlauf, Bildpinsel), wird nicht
    /// gefüllt statt eine falsche Farbe zu raten.
    /// </summary>
    private static Color Farbe(IBrush brush) =>
        brush is ISolidColorBrush solid ? solid.Color : Colors.Transparent;
}
