namespace Parkett.Services;

/// <summary>
/// Erzeugt Geld-/Briefkurse aus einem einzelnen Kurs. Nötig, weil EOD-Quellen nur einen
/// Schlusskurs liefern, die Ausführung aber einen Spread braucht — ohne ihn führt der
/// Simulator jeden Trade kostenlos aus und erzieht zu Überhandeln.
/// </summary>
public static class SyntheticSpread
{
    /// <summary>Halber Spread in Prozent, angelehnt an typische Xetra-Spreads liquider Werte.</summary>
    public const decimal DefaultHalfSpreadPercent = 0.0005m;

    public static (decimal Bid, decimal Ask) Around(decimal last, decimal halfSpreadPercent = DefaultHalfSpreadPercent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(halfSpreadPercent);

        var half = last * halfSpreadPercent;
        var bid = Math.Round(last - half, 4, MidpointRounding.AwayFromZero);
        var ask = Math.Round(last + half, 4, MidpointRounding.AwayFromZero);

        return (bid, ask);
    }
}
