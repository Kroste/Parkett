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

    /// <summary>
    /// Das Instrument, das beim Speichern im Chart stand. Bleibt erhalten, damit ein
    /// Stand aus Version 1 (eine Sitzung = ein Instrument) unverändert lesbar ist.
    /// </summary>
    public required string Symbol { get; init; }

    /// <summary>
    /// Alle Instrumente der Sitzung. Leer bei Ständen aus Version 1 — dann gilt
    /// <see cref="Symbol"/> allein. Ohne diese Liste ließen sich Positionen in
    /// anderen Werten nach dem Fortsetzen nicht mehr bewerten: ihre Historie wäre
    /// gar nicht geladen, und das Depot zeigte sie mit dem Einstandskurs.
    /// </summary>
    public IReadOnlyList<string> Symbols { get; init; } = [];

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
