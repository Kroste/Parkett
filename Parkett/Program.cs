using Avalonia;
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
            .LogToTrace();
}
