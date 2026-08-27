using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NLog;
using Parkett.Domain;
using Parkett.ViewModels;

namespace Parkett.Views;

/// <summary>
/// Werkzeug-Modus: rendert den Abschlussbericht mit erfundenen Zahlen in eine PNG
/// und beendet die App wieder. Aufruf:
///
/// <code>Parkett.exe --report-preview &lt;datei.png&gt; [gewinn|verlust|gebuehren|leer]</code>
///
/// <b>Warum das im Produktivcode steht:</b> eine Layout-Änderung lässt sich sonst nur
/// prüfen, indem man eine Sitzung von Hand bis zum Ende durchspielt. UI-Fernsteuerung
/// von außen (SetForegroundWindow, PrintWindow) ist nach Kroste-Standard tabu — sie
/// blockiert Verhaltens-AV und ist ohnehin fragil. Der Weg führt über den eigenen
/// Prozess, und das ist die kleinste Fassung davon.
/// </summary>
internal static class ReportPreview
{
    public const string Switch = "--report-preview";

    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly DateTimeOffset Start = new(2026, 1, 6, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Erkennt den Werkzeug-Modus, ohne die normale Kommandozeile zu stören.</summary>
    public static bool IsRequested(string[] args) =>
        args.Any(a => string.Equals(a, Switch, StringComparison.OrdinalIgnoreCase));

    public static void Run(string[] args)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, Switch, StringComparison.OrdinalIgnoreCase));
        var target = index >= 0 && index + 1 < args.Length ? args[index + 1] : "report-preview.png";
        var variant = index >= 0 && index + 2 < args.Length ? args[index + 2] : "gebuehren";

        var window = new ReportWindow(BuildViewModel(variant));

        window.Opened += (_, _) => Dispatcher.UIThread.Post(
            () => Capture(window, target),
            DispatcherPriority.Background);

        window.Show();
    }

    private static void Capture(Window window, string target)
    {
        try
        {
            var size = new Size(window.Bounds.Width, window.Bounds.Height);

            if (size.Width < 1 || size.Height < 1)
            {
                Log.Error("Fenster hat keine Größe — nichts zu rendern.");
                return;
            }

            using var bitmap = new RenderTargetBitmap(
                new PixelSize((int)size.Width, (int)size.Height),
                new Vector(96, 96));

            bitmap.Render(window);

            var full = Path.GetFullPath(target);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            // Avalonia 12: Save(string, int?) ist veraltet, die Optionen sind Pflicht.
            bitmap.Save(full, new PngBitmapEncoderOptions());

            Log.Info("Vorschau geschrieben: {Path} ({Width}x{Height})", full, (int)size.Width, (int)size.Height);
            Console.WriteLine($"geschrieben: {full}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Vorschau konnte nicht gerendert werden.");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Vier Fälle, die im Layout unterschiedlich aussehen — und genau die, bei denen
    /// eine Änderung am Bericht am ehesten etwas zerlegt.
    /// </summary>
    private static ReportWindowViewModel BuildViewModel(string variant) => variant.ToLowerInvariant() switch
    {
        "gewinn" => Build(Kurve(10_000m, 10_400m, 10_150m, 11_200m, 11_800m), 11_800m, 120m, 6, 66.67m),
        "verlust" => Build(Kurve(10_000m, 9_600m, 9_900m, 8_800m, 8_500m), 8_500m, 180m, 7, 28.57m),
        "leer" => Build(Kurve(10_000m, 10_000m), 10_000m, 0m, 0, 0m),
        // Der Fall, für den der Bericht gebaut ist: ohne Gebühren wäre es ein Plus.
        _ => Build(Kurve(10_000m, 10_500m, 10_200m, 10_800m, 9_850m), 9_850m, 620m, 31, 45.16m),
    };

    private static ReportWindowViewModel Build(
        IReadOnlyList<EquityPoint> kurve,
        decimal end,
        decimal fees,
        int trades,
        decimal winRate)
    {
        var report = new PerformanceReport(
            StartEquity: kurve[0].Equity,
            EndEquity: end,
            TotalReturnPercent: Math.Round((end - kurve[0].Equity) / kurve[0].Equity * 100m, 2),
            MaxDrawdownPercent: PerformanceCalculator.CalculateMaxDrawdownPercent(kurve),
            TotalFees: fees,
            TradeCount: trades,
            WinRatePercent: winRate);

        return new ReportWindowViewModel(report, kurve, "DEMO", kurve.Count, kurve[0].Equity);
    }

    /// <summary>Aus Stützstellen eine Kurve mit Zwischenschritten — sonst wirkt sie eckig.</summary>
    private static IReadOnlyList<EquityPoint> Kurve(params decimal[] stuetzstellen)
    {
        var punkte = new List<EquityPoint>();
        const int ProSegment = 12;

        for (var i = 0; i < stuetzstellen.Length - 1; i++)
        {
            for (var s = 0; s < ProSegment; s++)
            {
                var anteil = (decimal)s / ProSegment;
                var wert = stuetzstellen[i] + ((stuetzstellen[i + 1] - stuetzstellen[i]) * anteil);

                // Etwas Zappeln, damit die Kurve nicht wie ein Polygonzug aussieht.
                var zappel = (decimal)Math.Sin((i * ProSegment) + s) * 18m;

                punkte.Add(new EquityPoint(Start.AddDays(punkte.Count), Math.Round(wert + zappel, 2)));
            }
        }

        punkte.Add(new EquityPoint(Start.AddDays(punkte.Count), stuetzstellen[^1]));

        return punkte;
    }

    /// <summary>Formatiert nichts — hier nur, damit die Konsole eine Rückmeldung hat.</summary>
    public static string Describe(string[] args) =>
        string.Join(' ', args.Select(a => a.ToString(CultureInfo.InvariantCulture)));
}
