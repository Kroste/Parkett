using Parkett.Domain;

namespace Parkett.Persistence;

/// <summary>Ein Bestand im gespeicherten Stand.</summary>
public sealed record PositionSnapshot(string Symbol, decimal Quantity, decimal AveragePrice);

/// <summary>Eine Ausführung im gespeicherten Stand.</summary>
public sealed record FillSnapshot(
    Guid OrderId,
    string Symbol,
    OrderSide Side,
    decimal Quantity,
    decimal Price,
    decimal Fee,
    DateTimeOffset ExecutedAt);

/// <summary>
/// Vollständiger Stand einer unterbrochenen Sitzung. Bewusst ein eigenes DTO statt der
/// Domänentypen: das Speicherformat darf sich nicht ändern, nur weil die Domäne umgebaut wird.
/// </summary>
public sealed record SessionSnapshot
{
    /// <summary>Formatversion — erlaubt spätere Migration statt stillem Datenverlust.</summary>
    public int Version { get; init; } = 1;

    public required string Symbol { get; init; }

    public required int CandleIndex { get; init; }

    public required decimal StartingCash { get; init; }

    public required decimal Cash { get; init; }

    public string Currency { get; init; } = "EUR";

    public decimal TotalFees { get; init; }

    public decimal RealizedPnL { get; init; }

    public IReadOnlyList<PositionSnapshot> Positions { get; init; } = [];

    public IReadOnlyList<FillSnapshot> Fills { get; init; } = [];

    public DateTimeOffset SavedAt { get; init; }
}
