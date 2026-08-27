// WICHTIG (Avalonia 12): KEIN manuelles InitializeComponent() definieren —
// der NameGenerator emittiert es zusammen mit den x:Name-Feldern selbst.
using Avalonia.Interactivity;
using Parkett.ViewModels;

namespace Parkett.Views;

public partial class SettingsWindow : ChromeWindow
{
    // Parameterloser Ctor für den XAML-Designer.
    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(SettingsWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
