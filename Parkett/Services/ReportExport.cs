using System.Globalization;
using System.Text;

namespace Parkett.Services;

/// <summary>
/// Der Teil des Bericht-Exports, der ohne Avalonia auskommt: der Vorschlag für den
/// Dateinamen. Das Rendern selbst steht in <see cref="Views.ReportImage"/> — hier
/// liegt nur, was sich ohne Fenster prüfen lässt.
///
/// <b>Warum das überhaupt eigene Logik ist:</b> das Symbol kommt aus einer CSV, die
/// der Nutzer selbst besorgt hat. Broker-Exporte tragen dort Dinge wie
/// <c>DE0007236101/SIE.DE</c> — ein Schrägstrich im Dateinamen ist unter Linux ein
/// Verzeichniswechsel und unter Windows schlicht ungültig.
/// </summary>
public static class ReportExport
{
    public const string Extension = ".png";

    /// <summary>Steht im Dateinamen, wenn das Symbol nach dem Filtern nichts übrig lässt.</summary>
    private const string Fallback = "Sitzung";

    /// <summary>
    /// Schlägt einen Dateinamen vor: <c>Parkett-DEMO-2026-02-23.png</c>. Das Datum ist
    /// das Sitzungsende und bewusst ISO — so sortieren mehrere Berichte im Ordner
    /// chronologisch, unabhängig von der Kultur des Systems.
    /// </summary>
    public static string SuggestFileName(string? symbol, DateTimeOffset sessionEnd)
    {
        var clean = Sanitize(symbol);
        var date = sessionEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return $"Parkett-{clean}-{date}{Extension}";
    }

    /// <summary>
    /// Lässt nur stehen, was in jedem Dateisystem unstrittig ist. Kein Blacklisting
    /// über <see cref="Path.GetInvalidFileNameChars"/>: das ist unter Linux fast leer
    /// und würde dort Namen durchlassen, die auf einem gemounteten Windows-Laufwerk
    /// scheitern.
    /// </summary>
    private static string Sanitize(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return Fallback;
        }

        var builder = new StringBuilder(symbol.Length);

        foreach (var c in symbol.Trim())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (c is '.' or '-' or '_')
            {
                builder.Append('-');
            }
            // Alles andere fällt weg — inklusive Leerzeichen, Schrägstrichen und Umlauten.
        }

        // Führende und mehrfache Trenner sehen nach Fehler aus, obwohl der Name gültig wäre.
        var result = builder.ToString().Trim('-');

        while (result.Contains("--", StringComparison.Ordinal))
        {
            result = result.Replace("--", "-", StringComparison.Ordinal);
        }

        return result.Length == 0 ? Fallback : result;
    }
}
