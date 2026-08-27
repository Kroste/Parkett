// WICHTIG (Avalonia 12): KEIN manuelles InitializeComponent() definieren —
// der NameGenerator emittiert es zusammen mit den x:Name-Feldern selbst.
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using Parkett.Services;
using Parkett.ViewModels;

namespace Parkett.Views;

public partial class MainWindow : ChromeWindow
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Der DataContext wird erst in <c>App.OnFrameworkInitializationCompleted</c>
    /// gesetzt, nicht im Konstruktor — deshalb hier abonnieren und nicht dort.
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // C# 14 erlaubt die bedingte Zuweisung — vor .NET 10 wäre hier ein
        // if-Block nötig gewesen.
        _viewModel?.SessionFinished -= OnSessionFinished;

        _viewModel = DataContext as MainWindowViewModel;

        _viewModel?.SessionFinished += OnSessionFinished;
    }

    private async void OnSessionFinished(object? sender, ReportWindowViewModel report)
    {
        try
        {
            await new ReportWindow(report).ShowDialog(this);
        }
        catch (Exception ex)
        {
            // Ein fehlender Bericht darf die Sitzung nicht mitreißen — die
            // Statuszeile trägt die Kurzfassung ohnehin.
            Log.Error(ex, "Abschlussbericht konnte nicht geöffnet werden.");
        }
    }

    private async void OnOpenSettings(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (App.Services?.GetRequiredService<SettingsWindowViewModel>() is not { } viewModel)
            {
                return;
            }

            await new SettingsWindow(viewModel).ShowDialog(this);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Einstellungen-Fenster konnte nicht geöffnet werden.");
        }
    }

    private async void OnOpenAbout(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (App.Services?.GetRequiredService<UpdateService>() is not { } updateService)
            {
                return;
            }

            await new AboutWindow(updateService).ShowDialog(this);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Über-Fenster konnte nicht geöffnet werden.");
        }
    }
}
