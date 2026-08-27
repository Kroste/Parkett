using Parkett.Domain;

namespace Parkett.Charting;

/// <summary>
/// Rechnet die Equity-Kurve in Pixelkoordinaten um — dieselbe Trennung wie bei
/// <see cref="ChartViewport"/>: die Skalierung liegt hier und ist testbar, das
/// Control zeichnet nur noch.
///
/// Ein Unterschied zum Kurschart, der die ganze Aussage des Berichts trägt: das
/// <b>Startkapital liegt immer im sichtbaren Bereich</b>. Eine Kurve, die
/// automatisch auf ihr eigenes Min/Max zoomt, sieht bei −40 % genauso aus wie bei
/// +40 % — erst die Referenzlinie macht sichtbar, ob die Sitzung Geld verdient
/// oder verloren hat.
/// </summary>
public sealed class EquityViewport
{
    /// <summary>Luft über und unter der Kurve, damit sie nicht am Rand klebt.</summary>
    private const double VerticalPaddingRatio = 0.08;

    private readonly double _valueSpan;

    public EquityViewport(IReadOnlyList<EquityPoint> curve, decimal startEquity, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(curve);

        Width = Math.Max(1d, width);
        Height = Math.Max(1d, height);
        Points = curve;
        StartEquity = startEquity;

        if (curve.Count == 0)
        {
            MinValue = startEquity == 0m ? 0m : startEquity * 0.99m;
            MaxValue = startEquity == 0m ? 1m : startEquity * 1.01m;
            _valueSpan = (double)(MaxValue - MinValue);
            return;
        }

        // Startkapital bewusst in die Spanne einbeziehen — siehe Klassenkommentar.
        var low = Math.Min(curve.Min(p => p.Equity), startEquity);
        var high = Math.Max(curve.Max(p => p.Equity), startEquity);

        if (high == low)
        {
            // Sitzung ohne jede Bewegung: künstliche Spanne statt Division durch null.
            low -= 1m;
            high += 1m;
        }

        var padding = (high - low) * (decimal)VerticalPaddingRatio;
        MinValue = low - padding;
        MaxValue = high + padding;
        _valueSpan = (double)(MaxValue - MinValue);
    }

    public double Width { get; }

    public double Height { get; }

    public IReadOnlyList<EquityPoint> Points { get; }

    public decimal StartEquity { get; }

    public decimal MinValue { get; }

    public decimal MaxValue { get; }

    public int Count => Points.Count;

    /// <summary>Waagerechte Position eines Punktes. Ein einzelner Punkt sitzt links.</summary>
    public double X(int index)
    {
        if (Count <= 1)
        {
            return 0d;
        }

        return (double)index / (Count - 1) * Width;
    }

    /// <summary>Senkrechte Position eines Depotwerts. 0 ist oben — Bildschirmkoordinaten.</summary>
    public double Y(decimal value)
    {
        if (_valueSpan <= 0d)
        {
            return Height / 2d;
        }

        var ratio = (double)(MaxValue - value) / _valueSpan;
        return ratio * Height;
    }

    /// <summary>Senkrechte Position der Startkapital-Linie.</summary>
    public double StartLineY => Y(StartEquity);

    /// <summary>
    /// Waagerechte Rasterlinien auf runden Werten. Nutzt dieselbe 1/2/5-Rundung wie
    /// der Kurschart, damit beide Charts dasselbe Raster-Gefühl haben.
    /// </summary>
    public IReadOnlyList<decimal> ValueGridLines(int targetCount = 4)
    {
        if (targetCount < 2)
        {
            return [];
        }

        var step = ChartViewport.NiceStep((MaxValue - MinValue) / targetCount);

        if (step <= 0m)
        {
            return [];
        }

        var first = Math.Ceiling(MinValue / step) * step;
        var lines = new List<decimal>();

        for (var value = first; value <= MaxValue; value += step)
        {
            lines.Add(value);
        }

        return lines;
    }

    /// <summary>
    /// Der tiefste Punkt gemessen am bisherigen Höchststand — der Moment, den
    /// <see cref="PerformanceReport.MaxDrawdownPercent"/> als Zahl ausweist. Der
    /// Bericht markiert ihn, damit die Kennzahl im Verlauf wiederzufinden ist.
    /// Liefert <c>null</c>, solange es keinen Rückgang gab.
    /// </summary>
    public int? MaxDrawdownIndex()
    {
        var peak = decimal.MinValue;
        var worst = 0m;
        int? index = null;

        for (var i = 0; i < Points.Count; i++)
        {
            var equity = Points[i].Equity;

            if (equity > peak)
            {
                peak = equity;
            }

            if (peak <= 0m)
            {
                continue;
            }

            var drawdown = (peak - equity) / peak * 100m;

            if (drawdown > worst)
            {
                worst = drawdown;
                index = i;
            }
        }

        return index;
    }
}
