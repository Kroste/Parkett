namespace Parkett.Domain;

/// <summary>Eine OHLCV-Kerze. <paramref name="OpenTime"/> ist der Beginn des Intervalls (UTC).</summary>
public sealed record Candle(
    DateTimeOffset OpenTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume)
{
    public bool IsBullish => Close >= Open;
    public decimal Range => High - Low;
}
