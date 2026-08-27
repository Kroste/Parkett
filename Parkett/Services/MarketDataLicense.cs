using Parkett.Localization;

namespace Parkett.Services;

/// <summary>
/// Rechtlicher Rahmen einer Datenquelle. Steht bewusst als eigener Typ im Code und nicht nur
/// in der Doku: Ob eine Quelle in einem verkauften Produkt ausgeliefert werden darf, entscheidet
/// über die Wirtschaftlichkeit des ganzen Produkts. Jeder Provider MUSS das deklarieren, und die
/// UI zeigt es an.
/// </summary>
public enum DataRedistributionRight
{
    /// <summary>
    /// Daten dürfen mit dem Produkt ausgeliefert werden. Gilt für historische Kurse, die mindestens
    /// einen vollen Handelstag alt sind — für die braucht es keine Börsenlizenz.
    /// </summary>
    Redistributable,

    /// <summary>
    /// Daten holt der Nutzer mit SEINEM eigenen Zugang (Bring your own key). Das Produkt verbreitet
    /// nichts weiter und braucht daher keine eigene Vertriebslizenz.
    /// </summary>
    UserSuppliedCredentials,

    /// <summary>
    /// Weitergabe nur mit eigenem Vertrag mit dem Datenanbieter bzw. der Börse.
    /// Der Provider bleibt deaktiviert, solange <see cref="MarketDataLicense.AgreementReference"/> leer ist.
    /// </summary>
    RequiresAgreement,
}

/// <summary>Lizenz- und Verzögerungsangaben einer Datenquelle.</summary>
public sealed record MarketDataLicense(
    string SourceName,
    DataRedistributionRight Redistribution,
    int DelayMinutes,
    string AttributionText,
    string? AgreementReference = null)
{
    /// <summary>
    /// Darf diese Quelle in einem verkauften Build aktiv sein? Quellen mit
    /// <see cref="DataRedistributionRight.RequiresAgreement"/> erst, wenn der Vertrag hinterlegt ist.
    /// </summary>
    public bool IsUsableInPaidBuild => Redistribution switch
    {
        DataRedistributionRight.Redistributable => true,
        DataRedistributionRight.UserSuppliedCredentials => true,
        DataRedistributionRight.RequiresAgreement => !string.IsNullOrWhiteSpace(AgreementReference),
        _ => false,
    };

    /// <summary>
    /// Text für die Statuszeile, z. B. "Xetra · 15 Min. verzögert". Die Verzögerung sichtbar
    /// zu machen ist Auflage praktisch jeder Datenlizenz — deshalb steht sie dauerhaft in der UI.
    /// </summary>
    public string StatusText => DelayMinutes switch
    {
        0 => L.F("Data_Realtime", SourceName),
        < 60 => L.F("Data_Delayed", SourceName, DelayMinutes),
        _ => L.F("Data_PreviousClose", SourceName),
    };
}
