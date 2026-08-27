namespace Parkett.Domain;

/// <summary>
/// Eine Order im virtuellen Depot. Unveränderlich — Statuswechsel erzeugen eine neue Instanz,
/// damit der Order-Verlauf lückenlos protokollierbar bleibt.
/// </summary>
public sealed record Order
{
    public required Guid Id { get; init; }
    public required string Symbol { get; init; }
    public required OrderSide Side { get; init; }
    public required OrderType Type { get; init; }
    public required decimal Quantity { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Limitpreis — Pflicht bei <see cref="OrderType.Limit"/>.</summary>
    public decimal? LimitPrice { get; init; }

    /// <summary>Stoppreis — Pflicht bei <see cref="OrderType.Stop"/>.</summary>
    public decimal? StopPrice { get; init; }

    public OrderStatus Status { get; init; } = OrderStatus.New;

    /// <summary>Grund der Ablehnung, nur bei <see cref="OrderStatus.Rejected"/> gesetzt.</summary>
    public string? RejectReason { get; init; }

    public static Order Market(string symbol, OrderSide side, decimal quantity, DateTimeOffset now, Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Symbol = symbol,
            Side = side,
            Type = OrderType.Market,
            Quantity = quantity,
            CreatedAt = now,
        };

    public static Order Limit(string symbol, OrderSide side, decimal quantity, decimal limitPrice, DateTimeOffset now, Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Symbol = symbol,
            Side = side,
            Type = OrderType.Limit,
            Quantity = quantity,
            LimitPrice = limitPrice,
            CreatedAt = now,
        };

    public static Order Stop(string symbol, OrderSide side, decimal quantity, decimal stopPrice, DateTimeOffset now, Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Symbol = symbol,
            Side = side,
            Type = OrderType.Stop,
            Quantity = quantity,
            StopPrice = stopPrice,
            CreatedAt = now,
        };

    public Order Reject(string reason) => this with { Status = OrderStatus.Rejected, RejectReason = reason };

    public Order Fill() => this with { Status = OrderStatus.Filled };

    public Order Cancel() => this with { Status = OrderStatus.Cancelled };
}
