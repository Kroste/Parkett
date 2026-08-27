namespace Parkett.Licensing;

/// <summary>Ausbaustufe aus einem gespeicherten Lizenzschlüssel — der Weg für den Direktverkauf.</summary>
public sealed class LicenseKeyEditionProvider : IEditionProvider
{
    public LicenseKeyEditionProvider(LicenseVerifier verifier, string? storedKey, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(verifier);

        if (string.IsNullOrWhiteSpace(storedKey))
        {
            Current = Edition.Free;
            SourceDescription = "Kostenlose Fassung";
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
    }

    public Edition Current { get; }

    public string SourceDescription { get; }
}
