using Parkett.Domain;

namespace Parkett.Charting;

/// <summary>
/// Rechnet Kurse und Kerzen-Indizes in Pixelkoordinaten um. Bewusst als eigene Klasse
/// ohne Avalonia-Bezug: Skalierungsfehler sind die häufigste Ursache für "der Chart
/// sieht falsch aus", und hier lassen sie sich direkt testen statt per Screenshot raten.
/// </summary>
public sealed class ChartViewport
{
    /// <summary>Luft über und unter dem Kursband, damit Kerzen nicht am Rand kleben.</summary>
    private const double VerticalPaddingRatio = 0.08;

    private readonly double _priceSpan;

    public ChartViewport(IReadOnlyList<Candle> candles, double width, double height, int maxVisibleCandles = 120)
    {
        ArgumentNullException.ThrowIfNull(candles);

        Width = Math.Max(1d, width);
        Height = Math.Max(1d, height);

        VisibleCandles = candles.Count > maxVisibleCandles
            ? candles.Skip(candles.Count - maxVisibleCandles).ToList()
            : candles;

        if (VisibleCandles.Count == 0)
        {
            MinPrice = 0m;
            MaxPrice = 1m;
            _priceSpan = 1d;
            return;
        }

        var low = VisibleCandles.Min(c => c.Low);
        var high = VisibleCandles.Max(c => c.High);

        if (high == low)
        {
            // Waagerechte Historie: künstliche Spanne, sonst Division durch null.
            high = low + 1m;
        }

        var padding = (high - low) * (decimal)VerticalPaddingRatio;
        MinPrice = low - padding;
        MaxPrice = high + padding;
        _priceSpan = (double)(MaxPrice - MinPrice);
    }

    public double Width { get; }

    public double Height { get; }

    public IReadOnlyList<Candle> VisibleCandles { get; }

    public decimal MinPrice { get; }

    public decimal MaxPrice { get; }

    public int Count => VisibleCandles.Count;

    /// <summary>Breite eines Kerzen-Slots inklusive Abstand.</summary>
    public double SlotWidth => Count == 0 ? Width : Width / Count;

    /// <summary>Breite des gezeichneten Kerzenkörpers — 70 % des Slots, mindestens 1 Pixel.</summary>
    public double BodyWidth => Math.Max(1d, SlotWidth * 0.7d);

    /// <summary>Waagerechte Mitte des Kerzen-Slots.</summary>
    public double XCenter(int visibleIndex) => (visibleIndex + 0.5d) * SlotWidth;

    /// <summary>Senkrechte Position eines Kurses. 0 ist oben — Bildschirmkoordinaten.</summary>
    public double Y(decimal price)
    {
        if (_priceSpan <= 0d)
        {
            return Height / 2d;
        }

        var ratio = (double)(MaxPrice - price) / _priceSpan;
        return ratio * Height;
    }

    /// <summary>Umkehrung von <see cref="Y"/> — für das Fadenkreuz.</summary>
    public decimal PriceAt(double y)
    {
        var ratio = Math.Clamp(y / Height, 0d, 1d);
        return MaxPrice - ((decimal)ratio * (MaxPrice - MinPrice));
    }

    /// <summary>Index der Kerze unter einer x-Position, oder <c>null</c> außerhalb.</summary>
    public int? IndexAt(double x)
    {
        if (Count == 0 || x < 0d || x > Width)
        {
            return null;
        }

        var index = (int)(x / SlotWidth);
        return Math.Clamp(index, 0, Count - 1);
    }

    /// <summary>
    /// Preislinien auf runden Werten. Ein Raster auf krummen Zahlen ist unlesbar —
    /// deshalb wird der Schritt auf 1/2/5 × Zehnerpotenz gerundet.
    /// </summary>
    public IReadOnlyList<decimal> PriceGridLines(int targetCount = 5)
    {
        if (Count == 0 || targetCount < 2)
        {
            return [];
        }

        var step = NiceStep((MaxPrice - MinPrice) / targetCount);

        if (step <= 0m)
        {
            return [];
        }

        var first = Math.Ceiling(MinPrice / step) * step;
        var lines = new List<decimal>();

        for (var value = first; value <= MaxPrice; value += step)
        {
            lines.Add(value);
        }

        return lines;
    }

    /// <summary>Rundet einen Rohschritt auf den nächsten "schönen" Wert (1, 2, 5 × 10ⁿ).</summary>
    public static decimal NiceStep(decimal rawStep)
    {
        if (rawStep <= 0m)
        {
            return 0m;
        }

        var exponent = (int)Math.Floor(Math.Log10((double)rawStep));
        var magnitude = (decimal)Math.Pow(10, exponent);
        var normalised = rawStep / magnitude;

        var factor = normalised switch
        {
            <= 1m => 1m,
            <= 2m => 2m,
            <= 5m => 5m,
            _ => 10m,
        };

        return factor * magnitude;
    }

    /// <summary>
    /// Positionen für Datumsbeschriftungen: gleichmäßig verteilt, aber immer auf echte
    /// Kerzen gesetzt, damit die Beschriftung zum Gitter passt.
    /// </summary>
    public IReadOnlyList<(int Index, DateTimeOffset At)> TimeGridLines(int targetCount = 5)
    {
        if (Count == 0)
        {
            return [];
        }

        var count = Math.Min(targetCount, Count);
        var stride = Math.Max(1, Count / count);
        var result = new List<(int, DateTimeOffset)>();

        for (var i = 0; i < Count; i += stride)
        {
            result.Add((i, VisibleCandles[i].OpenTime));
        }

        return result;
    }
}
