namespace Parkett.Domain;

/// <summary>Handelbares Instrument. Symbol ist der eindeutige Schlüssel innerhalb einer Datenquelle.</summary>
public sealed record Instrument(
    string Symbol,
    string Name,
    string Currency,
    string Exchange)
{
    public override string ToString() => $"{Symbol} ({Exchange})";
}
