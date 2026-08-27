namespace Parkett.Licensing;

/// <summary>
/// Entscheidet, welche Funktion in welcher Ausbaustufe verfügbar ist. Eine einzige Tabelle,
/// damit sich das Angebot ändern lässt, ohne den halben Code zu durchsuchen.
/// </summary>
public sealed class FeatureGate(IEditionProvider editionProvider)
{
    private static readonly Dictionary<Feature, Edition> MinimumEdition = new()
    {
        [Feature.UnlimitedInstruments] = Edition.Full,
        [Feature.MultiplePortfolios] = Edition.Full,
        [Feature.CsvExport] = Edition.Full,
        [Feature.CustomDataProviders] = Edition.Pro,
        [Feature.StrategyReport] = Edition.Pro,
    };

    /// <summary>Anzahl handelbarer Instrumente in der kostenlosen Fassung.</summary>
    public const int FreeInstrumentLimit = 10;

    private readonly IEditionProvider _editionProvider =
        editionProvider ?? throw new ArgumentNullException(nameof(editionProvider));

    public Edition Current => _editionProvider.Current;

    public bool IsEnabled(Feature feature) =>
        !MinimumEdition.TryGetValue(feature, out var required) || Current >= required;

    public int InstrumentLimit =>
        IsEnabled(Feature.UnlimitedInstruments) ? int.MaxValue : FreeInstrumentLimit;

    /// <summary>Text für den Hinweis, wenn eine gesperrte Funktion angeklickt wird.</summary>
    public string UpgradeHint(Feature feature) =>
        MinimumEdition.TryGetValue(feature, out var required) && Current < required
            ? required switch
            {
                Edition.Pro => "Diese Funktion gehört zur Pro-Version.",
                Edition.Full => "Diese Funktion gehört zur Vollversion.",
                _ => string.Empty,
            }
            : string.Empty;
}
