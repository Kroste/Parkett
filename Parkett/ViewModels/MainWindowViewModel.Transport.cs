using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Parkett.Domain;
using Parkett.Services;
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

        // Jeder Kurs dieses Zeitpunkts geht einzeln in die Sitzung: das Depot wird neu
        // bewertet, und offene Orders prüft die Sitzung gegen ihr eigenes Symbol.
        var fills = new List<Fill>();

        foreach (var quote in step.Quotes)
        {
            fills.AddRange(_session.OnQuote(quote, step.At));
        }

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

    /// <summary>
    /// Instrumentenwechsel während der Sitzung: schaltet nur die Anzeige um. Der Ablauf
    /// läuft für alle Instrumente weiter, offene Orders und Positionen bleiben, wie sie
    /// sind — der Chart springt lediglich auf einen anderen Wert desselben Depots.
    /// </summary>
    partial void OnSelectedInstrumentChanged(Instrument? value)
    {
        if (value is null || _clock is null || !IsSessionRunning)
        {
            return;
        }

        if (_clock.ShowSymbol(value.Symbol))
        {
            RefreshMarkers();
            RefreshFromClock();
            Log.Debug("Chart zeigt jetzt {Symbol}.", value.Symbol);
        }
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
