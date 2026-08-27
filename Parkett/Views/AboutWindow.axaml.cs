// Kroste-Standard-Codebehind für das AboutWindow. Zusammen mit
// AboutWindow.axaml in Views/ ablegen. Beim Kopieren "Parkett" durch den
// echten Projektnamen ersetzen. Der UpdateService gehört zum Auto-Update-Muster
// (siehe references/autoupdate.md); GitHub-URL ans eigene Repo anpassen.
//
// WICHTIG (Avalonia 12): KEIN manuelles InitializeComponent() definieren —
// der Name-Generator emittiert es zusammen mit den x:Name-Feldern selbst.
// Sonst gewinnt die manuelle Version per Overload-Auflösung und die Felder
// bleiben null (siehe references/avalonia12.md).
using Avalonia.Controls;
using Avalonia.Interactivity;
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
