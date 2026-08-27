// WICHTIG (Avalonia 12): KEIN manuelles InitializeComponent() definieren —
// der NameGenerator emittiert es zusammen mit den x:Name-Feldern selbst.
// Sonst gewinnt die manuelle Version per Overload-Auflösung und die Felder
// bleiben null (siehe references/avalonia12.md).
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NLog;
using Parkett.Localization;
using Parkett.Services;

namespace Parkett.Views;

public partial class AboutWindow : ChromeWindow
{
    private const string GithubUrl = "https://github.com/Kroste/Parkett";
    private const string BmcUrl = "https://buymeacoffee.com/kroste";
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly UpdateService? _updateService;
    private string? _assetUrl;

    // Parameterloser Ctor für den XAML-Designer.
    public AboutWindow()
    {
        InitializeComponent();
    }

    public AboutWindow(UpdateService updateService) : this()
    {
        _updateService = updateService;
        VersionText.Text = L.F("About_Version", updateService.CurrentVersion);
        UpdateButton.Click += OnCheckUpdate;
        InstallButton.Click += OnInstallUpdate;
        GithubButton.Click += (_, _) => Launch(GithubUrl);
        BmcButton.Click += (_, _) => Launch(BmcUrl);
    }

    private async void OnCheckUpdate(object? sender, RoutedEventArgs e)
    {
        if (_updateService is null) return;
        UpdateButton.IsEnabled = false;
        UpdateResult.Text = L.T("About_Checking");
        try
        {
            var result = await _updateService.CheckForUpdateAsync();
            UpdateResult.Text = result.UpdateAvailable
                ? L.F("About_UpdateAvailable", result.LatestVersion)
                : result.LatestVersion is null
                    ? L.T("About_NoAccess")
                    : L.T("About_UpToDate");

            // Ohne Asset für diese Plattform gibt es nichts zu installieren —
            // dann bleibt es bei der Meldung.
            _assetUrl = result.UpdateAvailable ? result.AssetUrl : null;
            InstallButton.IsVisible = _assetUrl is not null;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Update-Prüfung im Über-Fenster fehlgeschlagen");
            UpdateResult.Text = L.T("About_CheckFailed");
        }
        finally
        {
            UpdateButton.IsEnabled = true;
        }
    }

    private async void OnInstallUpdate(object? sender, RoutedEventArgs e)
    {
        if (_updateService is null || _assetUrl is null) return;

        InstallButton.IsEnabled = false;
        UpdateButton.IsEnabled = false;
        UpdateProgress.IsVisible = true;

        // Der Fortschritt kommt vom Download-Thread — ohne Dispatcher sähen die
        // Bindings die Änderung nicht (Kroste-Standard: VM/UI-State auf dem UI-Thread).
        var progress = new Progress<double>(value => Dispatcher.UIThread.Post(() =>
        {
            UpdateProgress.Value = value;
            UpdateResult.Text = L.F("Update_Downloading", (int)(value * 100));
        }));

        var started = await _updateService.DownloadAndApplyAsync(_assetUrl, progress);

        if (!started)
        {
            UpdateResult.Text = L.T("Update_Failed");
            UpdateProgress.IsVisible = false;
            InstallButton.IsEnabled = true;
            UpdateButton.IsEnabled = true;
            return;
        }

        // PFLICHT: Das Installer-Skript wartet auf das Prozessende. Beenden wir uns
        // nicht selbst, wartet es ewig und die Anzeige bleibt bei 100 % stehen.
        UpdateResult.Text = L.T("Update_Restarting");
        UpdateService.TerminateForUpdate();
    }

    private void Launch(string url)
    {
        try
        {
            TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(new Uri(url));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Link konnte nicht geöffnet werden: {url}", url);
        }
    }
}
