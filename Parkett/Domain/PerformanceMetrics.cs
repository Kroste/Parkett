namespace Parkett.Domain;

/// <summary>Ein Punkt der Equity-Kurve.</summary>
public sealed record EquityPoint(DateTimeOffset At, decimal Equity);

/// <summary>Ausgewertete Kennzahlen eines Depotverlaufs.</summary>
public sealed record PerformanceReport(
    decimal StartEquity,
    decimal EndEquity,
    decimal TotalReturnPercent,
    decimal MaxDrawdownPercent,
    decimal TotalFees,
    int TradeCount,
    decimal WinRatePercent)
{
    /// <summary>
    /// Anteil der Gebühren am Startkapital. Ab etwa 10 % pro Jahr ist eine Strategie
    /// rechnerisch kaum noch zu retten — deshalb steht die Zahl im Bericht ganz oben.
    /// </summary>
    public decimal FeeDragPercent => StartEquity == 0m ? 0m : TotalFees / StartEquity * 100m;
}

/// <summary>Berechnet Kennzahlen aus Equity-Kurve und Ausführungen. Reine Funktion, komplett testbar.</summary>
public static class PerformanceCalculator
{
    public static PerformanceReport Analyse(
        IReadOnlyList<EquityPoint> equityCurve,
        IReadOnlyList<Fill> fills)
    {
        ArgumentNullException.ThrowIfNull(equityCurve);
        ArgumentNullException.ThrowIfNull(fills);

        if (equityCurve.Count == 0)
        {
            return new PerformanceReport(0m, 0m, 0m, 0m, 0m, 0, 0m);
        }

        var start = equityCurve[0].Equity;
        var end = equityCurve[^1].Equity;

        var totalReturn = start == 0m ? 0m : (end - start) / start * 100m;
        var maxDrawdown = CalculateMaxDrawdownPercent(equityCurve);
        var totalFees = fills.Sum(f => f.Fee);

        var (tradeCount, winRate) = CalculateRoundTrips(fills);

        return new PerformanceReport(
            start,
            end,
            Math.Round(totalReturn, 2, MidpointRounding.AwayFromZero),
            Math.Round(maxDrawdown, 2, MidpointRounding.AwayFromZero),
            totalFees,
            tradeCount,
            Math.Round(winRate, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>Maximaler prozentualer Rückgang vom bisherigen Höchststand.</summary>
    public static decimal CalculateMaxDrawdownPercent(IReadOnlyList<EquityPoint> equityCurve)
    {
        ArgumentNullException.ThrowIfNull(equityCurve);

        var peak = decimal.MinValue;
        var maxDrawdown = 0m;

        foreach (var point in equityCurve)
        {
            if (point.Equity > peak)
            {
                peak = point.Equity;
            }

            if (peak > 0m)
            {
                var drawdown = (peak - point.Equity) / peak * 100m;
                if (drawdown > maxDrawdown)
                {
                    maxDrawdown = drawdown;
                }
            }
        }

        return maxDrawdown;
    }

    /// <summary>
    /// Zählt abgeschlossene Rundläufe (Kauf → Verkauf je Symbol, FIFO) und den Anteil der Gewinner.
    /// Gebühren beider Seiten fließen ein, sonst sieht die Trefferquote besser aus als sie ist.
    /// </summary>
    private static (int TradeCount, decimal WinRatePercent) CalculateRoundTrips(IReadOnlyList<Fill> fills)
    {
        var open = new Dictionary<string, Queue<(decimal Quantity, decimal Price, decimal FeePerUnit)>>(StringComparer.OrdinalIgnoreCase);
        var closed = new List<decimal>();

        foreach (var fill in fills.OrderBy(f => f.ExecutedAt))
        {
            if (!open.TryGetValue(fill.Symbol, out var queue))
            {
                queue = new Queue<(decimal, decimal, decimal)>();
                open[fill.Symbol] = queue;
            }

            var feePerUnit = fill.Quantity == 0m ? 0m : fill.Fee / fill.Quantity;

            if (fill.Side == OrderSide.Buy)
            {
                queue.Enqueue((fill.Quantity, fill.Price, feePerUnit));
                continue;
            }

            var remaining = fill.Quantity;

            while (remaining > 0m && queue.Count > 0)
            {
                var lot = queue.Dequeue();
                var matched = Math.Min(remaining, lot.Quantity);

                var profit = ((fill.Price - lot.Price) * matched)
                             - (feePerUnit * matched)
                             - (lot.FeePerUnit * matched);

                closed.Add(profit);
                remaining -= matched;

                if (lot.Quantity > matched)
                {
                    queue.Enqueue((lot.Quantity - matched, lot.Price, lot.FeePerUnit));
                }
            }
        }

        if (closed.Count == 0)
        {
            return (0, 0m);
        }

        var winners = closed.Count(p => p > 0m);
        return (closed.Count, (decimal)winners / closed.Count * 100m);
    }
}
