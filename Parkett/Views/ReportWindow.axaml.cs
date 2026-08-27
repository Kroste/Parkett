// WICHTIG (Avalonia 12): KEIN manuelles InitializeComponent() definieren —
// der NameGenerator emittiert es zusammen mit den x:Name-Feldern selbst.
using Avalonia.Interactivity;
using Parkett.ViewModels;

namespace Parkett.Views;

/// <summary>
/// Abschlussbericht einer beendeten Sitzung. Ersetzt die frühere Statuszeile —
/// der Moment, in dem der Simulator seine Lehre erteilt, braucht mehr Platz als
/// eine Zeile am unteren Fensterrand.
/// </summary>
public partial class ReportWindow : ChromeWindow
{
    // Parameterloser Ctor für den XAML-Designer.
    public ReportWindow()
    {
        InitializeComponent();
    }

    public ReportWindow(ReportWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
