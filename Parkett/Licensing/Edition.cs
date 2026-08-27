namespace Parkett.Licensing;

/// <summary>
/// Ausbaustufe der Anwendung. Wichtig: Auf Steam gibt es KEINE Lizenzschlüssel — dort ist der
/// Besitz der App-ID die Lizenz, und die kostenlose Fassung ist eine eigene Demo-App.
/// Der Schlüssel-Mechanismus greift nur beim Direktverkauf.
/// </summary>
public enum Edition
{
    /// <summary>Kostenlose Fassung: ein Depot, begrenzte Instrumente, keine eigenen Datenquellen.</summary>
    Free,

    /// <summary>Vollversion (Steam-Kauf oder Direktkauf): alle Instrumente, mehrere Depots.</summary>
    Full,

    /// <summary>Pro (nur Direktverkauf): eigene Datenzugänge, Strategie-Auswertung, Export.</summary>
    Pro,
}

/// <summary>Einzeln schaltbare Funktionen. Ein Enum statt verstreuter if-Abfragen im ganzen Code.</summary>
public enum Feature
{
    MultiplePortfolios,
    CustomDataProviders,
    StrategyReport,
    CsvExport,
    UnlimitedInstruments,
}
