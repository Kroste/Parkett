using Parkett.Domain;

namespace Parkett.Charting;

/// <summary>Eine eigene Ausführung als Markierung im Chart.</summary>
public sealed record ChartMarker(DateTimeOffset At, decimal Price, OrderSide Side);
