using System.Text.Json;
using NLog;

namespace Parkett.Persistence;

/// <summary>
/// Atomares Laden und Speichern einer JSON-Datei. Kapselt die drei Pflichten des
/// Kroste-Persistenzmusters: tmp+move statt Direktschreiben, defekte Datei als
/// <c>.broken</c> sichern statt zu überschreiben, und niemals wegen kaputter
/// Nutzerdaten abstürzen.
/// </summary>
public sealed class JsonStore(string filePath)
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));

    public string FilePath => _filePath;

    public bool Exists => File.Exists(_filePath);

    /// <summary>Lädt die Datei. Liefert <c>null</c>, wenn sie fehlt oder unlesbar ist.</summary>
    public T? Load<T>() where T : class
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Defekte Datei NICHT überschreiben — sichern, damit sie später zu retten ist.
            RescueBroken(ex);
            return null;
        }
    }

    /// <summary>Schreibt atomar: erst in <c>.tmp</c>, dann per Move über die Zieldatei.</summary>
    public bool Save<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var temp = _filePath + ".tmp";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
            File.Move(temp, _filePath, overwrite: true);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Error(ex, "Speichern fehlgeschlagen: {Path}", _filePath);
            TryDelete(temp);

            return false;
        }
    }

    private void RescueBroken(Exception cause)
    {
        var broken = _filePath + ".broken";

        try
        {
            File.Move(_filePath, broken, overwrite: true);
            Log.Error(cause, "Datei {Path} nicht lesbar — als {Broken} gesichert, es wird leer weitergestartet.",
                _filePath, broken);
        }
        catch (Exception moveFailure) when (moveFailure is IOException or UnauthorizedAccessException)
        {
            Log.Error(moveFailure, "Defekte Datei {Path} konnte nicht gesichert werden.", _filePath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn(ex, "Temporäre Datei {Path} konnte nicht entfernt werden.", path);
        }
    }
}
