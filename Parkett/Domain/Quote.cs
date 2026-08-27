namespace Parkett.Domain;

/// <summary>
/// Momentaufnahme eines Kurses. <paramref name="DelayMinutes"/> dokumentiert die
/// Verzögerung der Quelle — 0 = Echtzeit, 15 = typische kostenfreie Verzögerung,
/// 1440+ = Vortagsschluss. Die UI MUSS die Verzögerung sichtbar anzeigen
/// (Auflage praktisch aller Datenlizenzen).
/// </summary>
public sealed record Quote(
    string Symbol,
    decimal Bid,
    decimal Ask,
    decimal Last,
    DateTimeOffset AsOf,
    int DelayMinutes)
{
    public decimal Mid => (Bid + Ask) / 2m;
    public decimal Spread => Ask - Bid;
    public bool IsRealtime => DelayMinutes == 0;
}
