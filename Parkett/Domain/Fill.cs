namespace Parkett.Domain;

/// <summary>Eine Ausführung. Gebühren werden getrennt geführt, damit die Kostenwirkung sichtbar bleibt.</summary>
public sealed record Fill(
    Guid OrderId,
    string Symbol,
    OrderSide Side,
    decimal Quantity,
    decimal Price,
    decimal Fee,
    DateTimeOffset ExecutedAt)
{
    /// <summary>Bruttowert ohne Gebühr.</summary>
    public decimal GrossValue => Quantity * Price;

    /// <summary>Wirkung auf den Kassenbestand: Kauf belastet inkl. Gebühr, Verkauf schreibt abzüglich Gebühr gut.</summary>
    public decimal CashDelta => Side == OrderSide.Buy
        ? -(GrossValue + Fee)
        : GrossValue - Fee;
}
