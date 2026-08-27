// WICHTIG (Avalonia 12): KEIN manuelles InitializeComponent() definieren —
// der NameGenerator emittiert es zusammen mit den x:Name-Feldern selbst.
using Avalonia.Interactivity;
using Avalonia.Threading;
using Parkett.Localization;
using Parkett.Services;

namespace Parkett.Views;

/// <summary>
/// Zustimmung zum Self-Update beim Start (Kroste-Standard: Update-Check ist nicht
/// blockierend, die Installation passiert nie ungefragt). Zeigt den Fortschritt und
/// beendet die App, sobald der Installer läuft.
/// </summary>
public partial class UpdatePromptWindow : ChromeWindow
{
    private readonly UpdateService? _updateService;
    private readonly string? _assetUrl;

    // Parameterloser Ctor für den XAML-Designer.
    public UpdatePromptWindow()
    {
        InitializeComponent();
    }

    public UpdatePromptWindow(UpdateService updateService, string latestVersion, string assetUrl) : this()
    {
        _updateService = updateService;
        _assetUrl = assetUrl;

        Headline.Text = L.F("Update_Headline", latestVersion);
        Body.Text = L.F("Update_Body", updateService.CurrentVersion);

        LaterButton.Click += (_, _) => Close();
        InstallButton.Click += OnInstall;
    }

    private async void OnInstall(object? sender, RoutedEventArgs e)
    {
        if (_updateService is null || _assetUrl is null) return;

        InstallButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        Progress.IsVisible = true;
        Status.IsVisible = true;

        var progress = new Progress<double>(value => Dispatcher.UIThread.Post(() =>
        {
            Progress.Value = value;
            Status.Text = L.F("Update_Downloading", (int)(value * 100));
        }));

        var started = await _updateService.DownloadAndApplyAsync(_assetUrl, progress);

        if (!started)
        {
            Status.Text = L.T("Update_Failed");
            Progress.IsVisible = false;
            InstallButton.IsEnabled = true;
            LaterButton.IsEnabled = true;
            return;
        }

        // PFLICHT: Das Installer-Skript wartet auf das Prozessende — ohne dieses
        // Beenden hängt es und die Anzeige bleibt bei 100 % stehen.
        Status.Text = L.T("Update_Restarting");
        UpdateService.TerminateForUpdate();
    }
}
