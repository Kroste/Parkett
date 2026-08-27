using NLog;

namespace Parkett.Services;

/// <summary>
/// Fängt unbehandelte Ausnahmen aus allen drei Quellen ab und protokolliert sie,
/// statt die App still abstürzen zu lassen.
/// </summary>
public static class GlobalExceptionHandler
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Wird gesetzt, sobald die UI steht — zeigt dem Nutzer eine Meldung.</summary>
    public static Action<Exception>? OnUnhandled { get; set; }

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Handle(e.ExceptionObject as Exception, "AppDomain");

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Handle(e.Exception, "TaskScheduler");
            e.SetObserved();
        };
    }

    public static void Handle(Exception? ex, string source)
    {
        if (ex is null)
        {
            Log.Error("Unbehandelter Fehler aus {Source} ohne Exception-Objekt.", source);
            return;
        }

        Log.Error(ex, "Unbehandelter Fehler aus {Source}.", source);

        try
        {
            OnUnhandled?.Invoke(ex);
        }
        catch (Exception handlerFailure)
        {
            Log.Error(handlerFailure, "Fehleranzeige selbst fehlgeschlagen.");
        }
    }
}
