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
        VersionText.Text = $"Version {updateService.CurrentVersion}";
        UpdateButton.Click += OnCheckUpdate;
        GithubButton.Click += (_, _) => Launch(GithubUrl);
        BmcButton.Click += (_, _) => Launch(BmcUrl);
    }

    private async void OnCheckUpdate(object? sender, RoutedEventArgs e)
    {
        if (_updateService is null) return;
        UpdateButton.IsEnabled = false;
        UpdateResult.Text = "Prüfe …";
        try
        {
            var result = await _updateService.CheckForUpdateAsync();
            UpdateResult.Text = result.UpdateAvailable
                ? $"Version {result.LatestVersion} verfügbar!"
                : result.LatestVersion is null
                    ? "Kein Zugriff auf GitHub."
                    : "Du hast die aktuelle Version.";
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Update-Prüfung im Über-Fenster fehlgeschlagen");
            UpdateResult.Text = "Prüfung fehlgeschlagen.";
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
