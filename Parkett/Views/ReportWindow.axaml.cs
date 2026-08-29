// WICHTIG (Avalonia 12): KEIN manuelles InitializeComponent() definieren —
// der NameGenerator emittiert es zusammen mit den x:Name-Feldern selbst.
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NLog;
using Parkett.Localization;
using Parkett.ViewModels;

namespace Parkett.Views;

/// <summary>
/// Abschlussbericht einer beendeten Sitzung. Ersetzt die frühere Statuszeile —
/// der Moment, in dem der Simulator seine Lehre erteilt, braucht mehr Platz als
/// eine Zeile am unteren Fensterrand.
/// </summary>
public partial class ReportWindow : ChromeWindow
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    // Parameterloser Ctor für den XAML-Designer.
    public ReportWindow()
    {
        InitializeComponent();
    }

    public ReportWindow(ReportWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    /// <summary>
    /// Rendert den Bericht als PNG. Das ist der Punkt, an dem eine Sitzung
    /// vergleichbar wird: eine Zahlentabelle im Fenster ist nach dem Schließen weg,
    /// ein Bild lässt sich neben den nächsten Bericht legen.
    /// </summary>
    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        // async void ist bei einem Click-Handler unvermeidbar — dann muss hier aber
        // auch alles gefangen werden, sonst reißt eine Exception den Prozess mit.
        var viewModel = DataContext as ReportWindowViewModel;

        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = L.T("Report_SaveDialogTitle"),
                SuggestedFileName = viewModel?.SuggestedFileName,
                DefaultExtension = "png",
                FileTypeChoices =
                [
                    new FilePickerFileType(L.T("Report_SaveFileType"))
                    {
                        Patterns = ["*.png"],
                        MimeTypes = ["image/png"],
                        AppleUniformTypeIdentifiers = ["public.png"],
                    },
                ],
            });

            if (file is null)
            {
                // Abgebrochen ist keine Fehlermeldung wert — die Statuszeile bleibt, wie sie war.
                return;
            }

            await using (var stream = await file.OpenWriteAsync())
            {
                RenderSurface(() => ReportImage.Render(ReportSurface, stream));
            }

            Log.Info("Bericht gespeichert: {Name}", file.Name);
            viewModel?.ReportExportSucceeded(file.TryGetLocalPath() ?? file.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Der Bericht konnte nicht gespeichert werden.");
            viewModel?.ReportExportFailed(ex.Message);
        }
    }

    /// <summary>
    /// Schreibt den Bericht direkt in eine Datei. Einstieg für den Werkzeug-Modus
    /// <c>--report-preview</c> — er läuft über dieselbe Fläche und dieselbe Mechanik
    /// wie der Knopf, damit die Vorschau nicht etwas anderes zeigt als der Export.
    /// </summary>
    internal string SaveReportImage(string path) =>
        RenderSurface(() => ReportImage.Save(ReportSurface, path));

    /// <summary>
    /// Rendert mit zurückgestelltem Scroll-Offset. <b>Ohne das</b> zeichnet Avalonia
    /// den Inhalt um den Offset verschoben: oben fehlte der Kopf des Berichts, unten
    /// stünde eine leere Fläche. Der Sprung bleibt unsichtbar, weil zwischen
    /// Zurücksetzen und Wiederherstellen kein Frame liegt.
    /// </summary>
    private T RenderSurface<T>(Func<T> render)
    {
        var offset = Scroller.Offset;
        Scroller.Offset = default;
        Scroller.UpdateLayout();

        try
        {
            return render();
        }
        finally
        {
            Scroller.Offset = offset;
        }
    }

    private void RenderSurface(Action render) => RenderSurface(() =>
    {
        render();
        return true;
    });

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
