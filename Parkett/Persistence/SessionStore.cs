using NLog;

namespace Parkett.Persistence;

/// <summary>Speichert und lädt den Stand der unterbrochenen Sitzung.</summary>
public sealed class SessionStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly JsonStore _store;

    public SessionStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _store = new JsonStore(Path.Combine(dataDirectory, "session.json"));
    }

    public bool HasSavedSession => _store.Exists;

    public SessionSnapshot? Load()
    {
        var snapshot = _store.Load<SessionSnapshot>();

        if (snapshot is null)
        {
            return null;
        }

        if (snapshot.Version != 1)
        {
            Log.Warn("Gespeicherte Sitzung hat Formatversion {Version} — wird ignoriert.", snapshot.Version);
            return null;
        }

        Log.Info("Gespeicherte Sitzung gefunden: {Symbol} bei Kerze {Index}, gesichert {SavedAt}.",
            snapshot.Symbol, snapshot.CandleIndex, snapshot.SavedAt);

        return snapshot;
    }

    public bool Save(SessionSnapshot snapshot) => _store.Save(snapshot);

    /// <summary>
    /// Verwirft den Stand nach einer beendeten Sitzung. Verschiebt statt zu löschen —
    /// Verhaltens-AV wertet Move harmloser als Delete, und der letzte Stand bleibt zur Diagnose.
    /// </summary>
    public void Clear()
    {
        if (!_store.Exists)
        {
            return;
        }

        try
        {
            File.Move(_store.FilePath, _store.FilePath + ".last", overwrite: true);
            Log.Info("Sitzungsstand abgelegt als {Path}.last", _store.FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn(ex, "Sitzungsstand konnte nicht abgelegt werden.");
        }
    }
}
