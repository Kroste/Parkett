using NLog;

namespace Parkett.Persistence;

/// <summary>
/// Lädt und speichert <see cref="AppSettings"/>. Der Lizenzschlüssel wird beim Laden
/// entschlüsselt und beim Speichern wieder verschlüsselt — der Rest der App sieht nur Klartext.
/// </summary>
public sealed class SettingsService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly JsonStore _store;
    private readonly ISecretProtector _protector;

    public SettingsService(string dataDirectory, ISecretProtector protector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _store = new JsonStore(Path.Combine(dataDirectory, "settings.json"));
    }

    /// <summary>Standard-Datenverzeichnis: %AppData%\Parkett bzw. ~/.config/Parkett.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Parkett");

    public AppSettings Load()
    {
        var stored = _store.Load<AppSettings>();

        if (stored is null)
        {
            Log.Info("Keine Einstellungen gefunden — Standardwerte werden verwendet.");
            return AppSettings.Default;
        }

        return stored with { LicenseKey = _protector.Unprotect(stored.LicenseKey) };
    }

    /// <summary>
    /// Ändert die gespeicherten Einstellungen, ohne die Felder anderer Fenster zu
    /// verlieren: erst frisch laden, dann <paramref name="change"/> darauf anwenden,
    /// dann schreiben. Liefert den geschriebenen Stand zurück.
    ///
    /// <b>Warum das nötig ist:</b> Hauptfenster und Einstellungen halten je eine
    /// eigene Kopie und schreiben die ganze Datei. Wer sie blind speichert, macht
    /// jede Änderung des anderen rückgängig — der Lizenzschlüssel überlebte so das
    /// Beenden nicht, weil das Hauptfenster seine beim Start geladene Kopie
    /// zurückschrieb.
    /// </summary>
    public AppSettings Update(Func<AppSettings, AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var updated = change(Load());
        Save(updated);

        return updated;
    }

    public bool Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var toStore = settings with
        {
            LicenseKey = string.IsNullOrWhiteSpace(settings.LicenseKey)
                ? null
                : _protector.Protect(settings.LicenseKey),
        };

        return _store.Save(toStore);
    }
}
