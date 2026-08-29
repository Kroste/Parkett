using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using Parkett.Domain;
using Parkett.Licensing;
using Parkett.Localization;
using Parkett.Persistence;
using Parkett.Services;
using Parkett.ViewModels;
using Parkett.Views;

namespace Parkett;

public partial class App : Application
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Öffentlicher Schlüssel zur Lizenzprüfung (ECDSA P-256, SubjectPublicKeyInfo, Base64).
    /// Wird beim Einrichten des Direktverkaufs eingetragen — der PRIVATE Schlüssel
    /// gehört niemals ins Repository.
    /// </summary>
    private const string LicensePublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEZpvDmBUI8JoqC9BuzaPqgD2HutXfaaqGHj/jhkKs6cfw6dlIjvOcimT5dZYm5wD3lwcbIWUQdjDMb9kVUQHVrQ==";

    // GC-Referenz: ohne Feld sammelt der GC den TrayController ein und das Icon verschwindet.
    private TrayController? _tray;

    private ServiceProvider? _services;

    /// <summary>Container für Fenster, die erst auf Klick entstehen (Einstellungen, Über).</summary>
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        GlobalExceptionHandler.Install();

        _services = BuildServices();
        Services = _services;

        // Sprache setzen, BEVOR das erste Fenster gebaut wird — sonst flackert es.
        var settings = _services.GetRequiredService<SettingsService>().Load();

        if (!string.IsNullOrWhiteSpace(settings.UiCulture))
        {
            LocalizationService.Instance.SetCulture(settings.UiCulture);
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Werkzeug-Modus: Bericht rendern und beenden, ohne Hauptfenster.
            if (ReportPreview.IsRequested(desktop.Args ?? []))
            {
                ReportPreview.Run(desktop.Args ?? []);
                return;
            }

            var window = new MainWindow
            {
                DataContext = _services.GetRequiredService<MainWindowViewModel>(),
            };

            desktop.MainWindow = window;

            _tray = new TrayController(this, window);
            _tray.Install();

            window.Opened += OnMainWindowOpened;

            // Zuverlässigster "App wird beendet"-Hook: hier den Sitzungsstand sichern.
            desktop.Exit += (_, _) =>
            {
                Log.Info("Desktop-Lifetime beendet — Stand wird gesichert.");

                try
                {
                    (window.DataContext as MainWindowViewModel)?.PersistOnExit();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Sichern beim Beenden fehlgeschlagen.");
                }

                _services?.Dispose();
                LogManager.Shutdown();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Update-Check beim Start — im Hintergrund, damit ein hängender Proxy nie das
    /// Hauptfenster aufhält, und nur mit Zustimmung des Nutzers vor der Installation.
    /// </summary>
    private async void OnMainWindowOpened(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        // Einmal pro Start reicht — sonst poppt der Dialog nach jedem Minimieren erneut auf.
        window.Opened -= OnMainWindowOpened;

        var updateService = _services?.GetService<UpdateService>();

        if (updateService is null)
        {
            return;
        }

        try
        {
            var result = await updateService.CheckForUpdateAsync().ConfigureAwait(true);

            // Ohne Asset für diese Plattform gäbe es nichts zu installieren.
            if (!result.UpdateAvailable || result.AssetUrl is null || result.LatestVersion is null)
            {
                return;
            }

            Log.Info("Update {Version} verfügbar — Nutzer wird gefragt.", result.LatestVersion);

            var prompt = new UpdatePromptWindow(updateService, result.LatestVersion, result.AssetUrl);
            await prompt.ShowDialog(window);
        }
        catch (Exception ex)
        {
            // Ein fehlgeschlagener Check darf die App nie stören.
            Log.Warn(ex, "Update-Check beim Start fehlgeschlagen.");
        }
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        var dataDirectory = SettingsService.DefaultDirectory;

        services.AddSingleton<UpdateService>();
        services.AddSingleton(new LicenseVerifier(LicensePublicKey));
        services.AddSingleton<ISecretProtector>(_ => new SecretProtector(dataDirectory));
        services.AddSingleton(sp => new SettingsService(dataDirectory, sp.GetRequiredService<ISecretProtector>()));
        services.AddSingleton(_ => new SessionStore(dataDirectory));

        services.AddSingleton<IEditionProvider>(sp =>
        {
            // Steam-Builds setzen die Stufe fest (dort ist der App-Besitz die Lizenz).
            // Der Direktverkauf liest stattdessen den gespeicherten Schlüssel.
            var verifier = sp.GetRequiredService<LicenseVerifier>();

            if (!verifier.IsConfigured)
            {
                return new FixedEditionProvider(Edition.Free, "Entwicklungsbuild");
            }

            var settings = sp.GetRequiredService<SettingsService>().Load();
            return new LicenseKeyEditionProvider(verifier, settings.LicenseKey, DateTimeOffset.UtcNow);
        });

        services.AddSingleton<FeatureGate>();

        services.AddSingleton<IMarketDataProvider>(_ =>
            new CsvHistoryProvider(Path.Combine(AppContext.BaseDirectory, "Data")));

        services.AddSingleton<IFeeModel>(sp =>
        {
            var model = sp.GetRequiredService<SettingsService>().Load().FeeModel;

            return model switch
            {
                "Free" => TieredFeeModel.Free,
                "Hausbank" => TieredFeeModel.Hausbank,
                _ => TieredFeeModel.Neobroker,
            };
        });

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SettingsWindowViewModel>();

        return services.BuildServiceProvider();
    }

}
