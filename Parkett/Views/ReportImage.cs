using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using NLog;

namespace Parkett.Views;

/// <summary>
/// Rendert den Berichtsinhalt in eine PNG. Einziger Renderpfad für beide Einstiege —
/// den Knopf im Bericht und den Werkzeug-Modus <c>--report-preview</c>. Dadurch prüft
/// die Vorschau denselben Code, den der Nutzer auslöst; zwei getrennte Fassungen
/// hätten sich sonst auseinandergelebt.
/// </summary>
internal static class ReportImage
{
    /// <summary>
    /// Doppelte Auflösung. Ein Bericht wird angeschaut und weitergegeben, nicht nur
    /// abgelegt — bei 96 dpi sind die 11-px-Beschriftungen im Bild kaum lesbar.
    /// </summary>
    public const double Scale = 2.0;

    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Schreibt <paramref name="content"/> in seiner arrangierten Größe nach
    /// <paramref name="path"/> und liefert den vollen Pfad zurück.
    /// </summary>
    public static string Save(Control content, string path)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using (var file = File.Create(full))
        {
            Render(content, file);
        }

        Log.Info("Bericht geschrieben: {Path}", full);

        return full;
    }

    /// <summary>
    /// Rendert in einen offenen Strom. Das ist der Weg für den Speichern-Dialog: unter
    /// Linux liefert das Datei-Portal nicht immer einen lokalen Pfad, sondern nur ein
    /// beschreibbares Ziel. Wer hier auf einen Pfad besteht, scheitert im Flatpak.
    ///
    /// Wirft bei Fehlern — der Aufrufer entscheidet, ob daraus eine Statuszeile oder
    /// ein Log-Eintrag wird.
    /// </summary>
    public static void Render(Control content, Stream target)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(target);

        // Die arrangierte Größe, nicht die sichtbare: im ScrollViewer misst Avalonia
        // den Inhalt mit unbegrenzter Höhe, deshalb steht hier der ganze Bericht —
        // auch der Teil, den der Nutzer erst herunterscrollen müsste.
        var size = content.Bounds.Size;

        if (size.Width < 1 || size.Height < 1)
        {
            throw new InvalidOperationException(
                $"Der Bericht hat keine darstellbare Größe ({size.Width}x{size.Height}).");
        }

        using var bitmap = new RenderTargetBitmap(
            new PixelSize(
                (int)Math.Ceiling(size.Width * Scale),
                (int)Math.Ceiling(size.Height * Scale)),
            new Vector(96 * Scale, 96 * Scale));

        bitmap.Render(content);

        // Avalonia 12: Save(Stream, int?) ist veraltet, die Optionen sind Pflicht.
        bitmap.Save(target, new PngBitmapEncoderOptions());

        Log.Debug(
            "Bericht gerendert: {Width}x{Height} px",
            (int)(size.Width * Scale),
            (int)(size.Height * Scale));
    }
}
