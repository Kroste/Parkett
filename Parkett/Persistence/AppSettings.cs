using Parkett.Simulation;

namespace Parkett.Persistence;

/// <summary>
/// Gespeicherte Einstellungen. <see cref="LicenseKey"/> liegt inline verschlüsselt
/// in der Datei — der Rest bleibt lesbares JSON.
/// </summary>
public sealed record AppSettings
{
    public string? LastSymbol { get; init; }

    public SimulationSpeed PreferredSpeed { get; init; } = SimulationSpeed.Normal;

    public string FeeModel { get; init; } = "Neobroker";

    public decimal DefaultQuantity { get; init; } = 10m;

    /// <summary>ISO-Code der UI-Sprache. Nullable für Rückwärtskompatibilität.</summary>
    public string? UiCulture { get; init; }

    /// <summary>Verschlüsselter Lizenzschlüssel (Präfix ENC1:), oder null.</summary>
    public string? LicenseKey { get; init; }

    public static AppSettings Default { get; } = new();
}
