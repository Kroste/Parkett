using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Parkett.Localization;
using Parkett.Simulation;

namespace Parkett.ViewModels;

/// <summary>
/// Zeitsteuerung: Play/Pause, Einzelschritt und das Nachziehen der Anzeige aus
/// der <see cref="SimulationClock"/>. Kern und Felder liegen in
/// <c>MainWindowViewModel.cs</c>.
/// </summary>
public sealed partial class MainWindowViewModel
{
    [RelayCommand(CanExecute = nameof(CanStep))]
    private void TogglePlay()
    {
        Speed = Speed == SimulationSpeed.Paused ? _pendingSpeed : SimulationSpeed.Paused;
    }

    [RelayCommand(CanExecute = nameof(CanStep))]
    private void Step()
    {
        if (_clock is null)
        {
            return;
        }

        var step = _clock.Advance();

        if (step is null)
        {
            FinishSession();
            return;
        }

        var fills = _session.OnQuote(step.Quote, step.Candle.OpenTime);

        foreach (var fill in fills)
        {
            Blotter.Insert(0, FormatFill(fill));
        }

        if (fills.Count > 0)
        {
            RefreshMarkers();
            RefreshOpenOrders();
        }

        RefreshFromClock();
    }

    partial void OnSpeedChanged(SimulationSpeed value)
    {
        OnPropertyChanged(nameof(PlayButtonText));

        if (value.Interval() is { } interval && IsSessionRunning)
        {
            _timer.Interval = interval;
            _timer.Start();
            Log.Debug("Ablaufgeschwindigkeit {Speed} ({Interval} ms).", value, interval.TotalMilliseconds);
        }
        else
        {
            _timer.Stop();
        }
    }

    private void RefreshFromClock()
    {
        if (_clock is null)
        {
            return;
        }

        ChartCandles = _clock.Visible;
        Progress = _clock.Total <= 1 ? 1d : (double)_clock.Index / (_clock.Total - 1);

        var quote = _clock.CurrentQuote;
        QuoteText = L.F(
            "Quote_Format",
            quote.Symbol,
            quote.Last.ToString("N2", CultureInfo.CurrentCulture),
            quote.Bid.ToString("N2", CultureInfo.CurrentCulture),
            quote.Ask.ToString("N2", CultureInfo.CurrentCulture));
        DateText = _clock.Current.OpenTime.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture);

        UpdatePortfolioTexts();
    }
}
