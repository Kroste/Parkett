using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using NLog;
using Parkett.Services;

namespace Parkett;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Masking MUSS vor dem ersten Logger-Aufruf registriert sein, sonst
        // schluckt ${masked:…} das Ende jeder Nachricht.
        MaskingLayoutRenderer.Register();

        var log = LogManager.GetCurrentClassLogger();
        log.Info("Parkett startet (Version {Version}).", UpdateService.AppVersion);

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            log.Fatal(ex, "Unbehandelter Fehler beim Start.");
            throw;
        }
        finally
        {
            log.Info("Parkett beendet.");
            LogManager.Shutdown();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(EmojiFontOptions())
            .LogToTrace();

    /// <summary>
    /// Inter bringt keine Emoji-Glyphen mit. Ohne diesen Fallback rendern die
    /// Länderflaggen im Sprachumschalter und die Piktogramme der Transportleiste
    /// als Ersatzkästchen.
    ///
    /// FALLE: <c>WithInterFont()</c> setzt die Standardfamilie über dieselben
    /// Options. Wer sie ersetzt, muss <see cref="FontManagerOptions.DefaultFamilyName"/>
    /// erneut angeben — sonst fällt die ganze App auf die System-Schrift zurück.
    /// </summary>
    private static FontManagerOptions EmojiFontOptions()
    {
        var emojiFamily = OperatingSystem.IsWindows() ? "Segoe UI Emoji" : "Noto Color Emoji";

        return new FontManagerOptions
        {
            DefaultFamilyName = "fonts:Inter#Inter",
            FontFallbacks =
            [
                new FontFallback { FontFamily = new FontFamily(emojiFamily) },
            ],
        };
    }
}
