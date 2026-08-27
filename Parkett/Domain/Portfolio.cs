using System.Collections.Immutable;

namespace Parkett.Domain;

/// <summary>
/// Virtuelles Depot. Unveränderlich: <see cref="Apply"/> liefert ein neues Portfolio,
/// damit sich jeder Zwischenstand für die Equity-Kurve aufheben lässt.
/// </summary>
public sealed record Portfolio
{
    public required string Currency { get; init; }

    public required decimal Cash { get; init; }

    public ImmutableDictionary<string, Position> Positions { get; init; } =
        ImmutableDictionary<string, Position>.Empty;

    /// <summary>Summe aller bisher gezahlten Gebühren — die wichtigste Kennzahl für kleine Konten.</summary>
    public decimal TotalFees { get; init; }

    /// <summary>Realisierter Gewinn/Verlust aus geschlossenen Positionen, nach Gebühren.</summary>
    public decimal RealizedPnL { get; init; }

    public static Portfolio Open(decimal startingCash, string currency = "EUR")
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startingCash);
        return new Portfolio { Cash = startingCash, Currency = currency };
    }

    public Position? GetPosition(string symbol) =>
        Positions.TryGetValue(symbol, out var position) ? position : null;

    public decimal QuantityOf(string symbol) => GetPosition(symbol)?.Quantity ?? 0m;

    /// <summary>Gesamtwert = Kasse + Marktwert aller Positionen zu den übergebenen Kursen.</summary>
    public decimal Equity(IReadOnlyDictionary<string, decimal> prices)
    {
        var value = Cash;

        foreach (var position in Positions.Values)
        {
            if (prices.TryGetValue(position.Symbol, out var price))
            {
                value += position.MarketValue(price);
            }
            else
            {
                // Kein Kurs verfügbar: mit Einstand bewerten statt die Position stillschweigend zu verlieren.
                value += position.CostBasis;
            }
        }

        return value;
    }

    /// <summary>Verbucht eine Ausführung und liefert den neuen Depotstand.</summary>
    public Portfolio Apply(Fill fill)
    {
        var cash = Cash + fill.CashDelta;
        var fees = TotalFees + fill.Fee;
        var realized = RealizedPnL;
        var positions = Positions;

        var existing = GetPosition(fill.Symbol);
        var signedQuantity = fill.Side == OrderSide.Buy ? fill.Quantity : -fill.Quantity;

        if (existing is null)
        {
            positions = positions.SetItem(
                fill.Symbol,
                new Position(fill.Symbol, signedQuantity, fill.Price));

            // Eröffnende Order: die Gebühr ist sofort realisierter Verlust.
            realized -= fill.Fee;
        }
        else
        {
            var newQuantity = existing.Quantity + signedQuantity;
            var isReducing = Math.Sign(signedQuantity) != Math.Sign(existing.Quantity) && existing.Quantity != 0m;

            if (isReducing)
            {
                // Geschlossener Anteil realisiert Gewinn/Verlust gegen den Einstandskurs.
                var closedQuantity = Math.Min(Math.Abs(signedQuantity), Math.Abs(existing.Quantity));
                var direction = Math.Sign(existing.Quantity);
                realized += (fill.Price - existing.AveragePrice) * closedQuantity * direction;
            }

            realized -= fill.Fee;

            if (newQuantity == 0m)
            {
                positions = positions.Remove(fill.Symbol);
            }
            else if (isReducing && Math.Sign(newQuantity) == Math.Sign(existing.Quantity))
            {
                // Teilverkauf: Einstandskurs des Restbestands bleibt unverändert.
                positions = positions.SetItem(fill.Symbol, existing with { Quantity = newQuantity });
            }
            else if (isReducing)
            {
                // Durchgedreht in die Gegenrichtung: neuer Einstand ist der Ausführungskurs.
                positions = positions.SetItem(fill.Symbol, new Position(fill.Symbol, newQuantity, fill.Price));
            }
            else
            {
                // Aufstocken: gewichteter Durchschnittseinstand.
                var totalCost = (existing.AveragePrice * existing.Quantity) + (fill.Price * signedQuantity);
                positions = positions.SetItem(
                    fill.Symbol,
                    new Position(fill.Symbol, newQuantity, totalCost / newQuantity));
            }
        }

        return this with
        {
            Cash = cash,
            Positions = positions,
            TotalFees = fees,
            RealizedPnL = realized,
        };
    }
}
