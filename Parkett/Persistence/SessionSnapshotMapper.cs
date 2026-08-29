using System.Collections.Immutable;
using Parkett.Domain;
using Parkett.Services;

namespace Parkett.Persistence;

/// <summary>Übersetzt zwischen laufender Sitzung und Speicherformat.</summary>
public static class SessionSnapshotMapper
{
    /// <param name="symbol">Das Instrument, das im Chart stand — es wird beim Fortsetzen wieder gezeigt.</param>
    /// <param name="symbols">
    /// Alle Instrumente der Sitzung. <c>null</c> bedeutet: nur <paramref name="symbol"/>.
    /// </param>
    public static SessionSnapshot ToSnapshot(
        TradingSession session,
        string symbol,
        int candleIndex,
        DateTimeOffset savedAt,
        IReadOnlyList<string>? symbols = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        var portfolio = session.Portfolio;

        return new SessionSnapshot
        {
            Symbol = symbol,
            Symbols = symbols ?? [symbol],
            CandleIndex = candleIndex,
            StartingCash = session.StartingCash,
            Cash = portfolio.Cash,
            Currency = portfolio.Currency,
            TotalFees = portfolio.TotalFees,
            RealizedPnL = portfolio.RealizedPnL,
            Positions = portfolio.Positions.Values
                .Select(p => new PositionSnapshot(p.Symbol, p.Quantity, p.AveragePrice))
                .ToList(),
            Fills = session.Fills
                .Select(f => new FillSnapshot(f.OrderId, f.Symbol, f.Side, f.Quantity, f.Price, f.Fee, f.ExecutedAt))
                .ToList(),
            SavedAt = savedAt,
        };
    }

    public static TradingSession ToSession(SessionSnapshot snapshot, IFeeModel feeModel)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var positions = snapshot.Positions.ToImmutableDictionary(
            p => p.Symbol,
            p => new Position(p.Symbol, p.Quantity, p.AveragePrice),
            StringComparer.OrdinalIgnoreCase);

        var portfolio = new Portfolio
        {
            Currency = snapshot.Currency,
            Cash = snapshot.Cash,
            Positions = positions,
            TotalFees = snapshot.TotalFees,
            RealizedPnL = snapshot.RealizedPnL,
        };

        var fills = snapshot.Fills
            .Select(f => new Fill(f.OrderId, f.Symbol, f.Side, f.Quantity, f.Price, f.Fee, f.ExecutedAt))
            .ToList();

        return TradingSession.Restore(snapshot.StartingCash, feeModel, portfolio, fills);
    }
}
