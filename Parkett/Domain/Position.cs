namespace Parkett.Domain;

/// <summary>
/// Bestand in einem Instrument. <see cref="Position.AveragePrice"/> ist der gewichtete Einstandskurs
/// OHNE Gebühren — Gebühren laufen über den realisierten G/V, nicht über den Einstand.
/// </summary>
public sealed record Position(string Symbol, decimal Quantity, decimal AveragePrice)
{
    public bool IsFlat => Quantity == 0m;

    public decimal CostBasis => Quantity * AveragePrice;

    public decimal MarketValue(decimal price) => Quantity * price;

    public decimal UnrealizedPnL(decimal price) => (price - AveragePrice) * Quantity;
}
