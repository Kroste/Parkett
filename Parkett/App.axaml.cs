using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using Parkett.Domain;
using Parkett.Licensing;
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
    private const string LicensePublicKey = "";

    // GC-Referenz: ohne Feld sammelt der GC den TrayController ein und das Icon verschwindet.
    private TrayController? _tray;

    private ServiceProvider? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        GlobalExceptionHandler.Install();

        _services = BuildServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow
            {
                DataContext = _services.GetRequiredService<MainWindowViewModel>(),
            };

            desktop.MainWindow = window;

            _tray = new TrayController(this, window);
            _tray.Install();

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

        services.AddSingleton<IFeeModel>(_ => TieredFeeModel.Neobroker);

        services.AddTransient<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }

}
