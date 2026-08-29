using NLog;

namespace Parkett.Licensing;

/// <summary>Ausbaustufe aus einem gespeicherten Lizenzschlüssel — der Weg für den Direktverkauf.</summary>
public sealed class LicenseKeyEditionProvider : IEditionProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public LicenseKeyEditionProvider(LicenseVerifier verifier, string? storedKey, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(verifier);

        if (string.IsNullOrWhiteSpace(storedKey))
        {
            Current = Edition.Free;
            SourceDescription = "Kostenlose Fassung";
            LogEdition();
            return;
        }

        var result = verifier.Check(storedKey, now);

        if (result.IsValid)
        {
            Current = result.License!.Edition;
            SourceDescription = $"Lizenziert für {result.License.LicensedTo}";
        }
        else
        {
            Current = Edition.Free;
            SourceDescription = result.Status switch
            {
                LicenseStatus.Expired => "Lizenz abgelaufen — kostenlose Fassung aktiv",
                LicenseStatus.SignatureInvalid => "Lizenzschlüssel ungültig",
                _ => "Lizenzschlüssel nicht lesbar",
            };
        }

        LogEdition();
    }

    /// <summary>
    /// Schreibt die erkannte Ausbaustufe ins Log. <b>Ohne das ist der Erfolgsfall
    /// stumm:</b> ein gültiger Schlüssel erzeugt weder hier noch im Verifier eine
    /// Zeile, ein fehlender ebenso wenig. Bei der Frage „warum steht da kostenlose
    /// Fassung?" hilft das Log dann überhaupt nicht weiter — die Antwort stand nur
    /// in der settings.json. Der Schlüssel selbst wird nie geloggt, nur das Ergebnis.
    /// </summary>
    private void LogEdition() => Log.Info("Ausbaustufe: {Edition} ({Source})", Current, SourceDescription);

    public Edition Current { get; }

    public string SourceDescription { get; }
}
