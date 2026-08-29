using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Parkett.Licensing;
using Parkett.Persistence;
using Parkett.Services;
using Parkett.Simulation;

namespace Parkett.ViewModels;

/// <summary>
/// Sitzungsablauf: starten, fortsetzen, beenden und der Stand beim Schließen.
/// Kern und Felder liegen in <c>MainWindowViewModel.cs</c>.
/// </summary>
public sealed partial class MainWindowViewModel
{
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartSessionAsync()
    {
        if (SelectedInstrument is not { } instrument)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var history = await _dataProvider
                .GetHistoryAsync(instrument.Symbol, DateTimeOffset.MinValue, DateTimeOffset.MaxValue)
                .ConfigureAwait(true);

            if (history.Count < 2)
            {
                SetStatus("Status_NotEnoughHistory", instrument.Symbol);
                return;
            }

            Stop();

            _clock = new SimulationClock(instrument.Symbol, history);
            _session = new TradingSession(StartingCash, _feeModel);

            Blotter.Clear();
            OpenOrders.Clear();
            ChartMarkers = [];
            IsSessionRunning = true;

            RefreshFromClock();
            SetStatus("Status_SessionRunning", instrument.Symbol, history.Count);
            Log.Info("Sitzung gestartet: {Symbol} mit {Count} Kerzen.", instrument.Symbol, history.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Sitzungsstart für {Symbol} fehlgeschlagen.", instrument.Symbol);
            SetStatus("Status_StartFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Setzt die zuletzt unterbrochene Sitzung an derselben Kerze fort.</summary>
    [RelayCommand(CanExecute = nameof(CanResume))]
    private async Task ResumeSessionAsync()
    {
        var snapshot = _sessionStore.Load();

        if (snapshot is null)
        {
            HasSavedSession = false;
            SetStatus("Status_NoSavedSession");
            return;
        }

        IsBusy = true;

        try
        {
            var history = await _dataProvider
                .GetHistoryAsync(snapshot.Symbol, DateTimeOffset.MinValue, DateTimeOffset.MaxValue)
                .ConfigureAwait(true);

            if (history.Count < 2 || snapshot.CandleIndex >= history.Count)
            {
                // Historie hat sich seit dem Speichern geändert — lieber ehrlich abbrechen
                // als an der falschen Kerze weiterzuspielen.
                SetStatus("Status_ResumeStale", snapshot.Symbol);
                _sessionStore.Clear();
                HasSavedSession = false;
                return;
            }

            Stop();

            _clock = new SimulationClock(snapshot.Symbol, history, snapshot.CandleIndex);
            _session = SessionSnapshotMapper.ToSession(snapshot, _feeModel);

            Blotter.Clear();

            foreach (var fill in _session.Fills.OrderByDescending(f => f.ExecutedAt))
            {
                Blotter.Add(FormatFill(fill));
            }

            OpenOrders.Clear();
            OnPropertyChanged(nameof(HasOpenOrders));
            IsSessionRunning = true;

            RefreshMarkers();
            RefreshFromClock();

            SelectedInstrument = Instruments.FirstOrDefault(i =>
                string.Equals(i.Symbol, snapshot.Symbol, StringComparison.OrdinalIgnoreCase));

            SetStatus("Status_SessionResumed", snapshot.Symbol, _clock.Current.OpenTime.ToString("d", CultureInfo.CurrentCulture));
            Log.Info("Sitzung fortgesetzt: {Symbol} bei Index {Index}.", snapshot.Symbol, snapshot.CandleIndex);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Sitzung konnte nicht fortgesetzt werden.");
            SetStatus("Status_ResumeFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Vom Exit-Hook gerufen: Einstellungen und laufende Sitzung sichern. Beendete Sitzungen
    /// werden verworfen, sonst bietet die App beim nächsten Start ein totes Fortsetzen an.
    /// </summary>
    public void PersistOnExit()
    {
        // Nur die drei Felder, die dem Hauptfenster gehören — Sprache, Gebührenmodell
        // und Lizenzschlüssel kommen frisch von der Platte. Vorher schrieb das hier
        // die beim Start geladene Kopie zurück und machte jede Änderung aus dem
        // Einstellungsfenster wieder rückgängig.
        _settings = _settingsService.Update(gespeichert => gespeichert with
        {
            LastSymbol = SelectedInstrument?.Symbol ?? gespeichert.LastSymbol,
            PreferredSpeed = _pendingSpeed,
            DefaultQuantity = Quantity,
        });

        if (IsSessionRunning && _clock is not null)
        {
            _sessionStore.Save(SessionSnapshotMapper.ToSnapshot(
                _session, _clock.Symbol, _clock.Index, DateTimeOffset.UtcNow));
        }
        else
        {
            _sessionStore.Clear();
        }
    }

    /// <summary>
    /// Meldet, dass die Sitzung durchgelaufen ist und der Abschlussbericht gezeigt
    /// werden soll. Das VM baut kein Fenster — das MainWindow hängt sich hier ein,
    /// wie es auch Einstellungen und Über-Fenster öffnet.
    /// </summary>
    public event EventHandler<ReportWindowViewModel>? SessionFinished;

    private void FinishSession()
    {
        Stop();
        IsSessionRunning = false;
        _sessionStore.Clear();
        HasSavedSession = false;

        var report = _session.Report();

        // Die Statuszeile bleibt als Kurzfassung stehen: sie ist noch da, wenn der
        // Bericht längst geschlossen ist.
        SetStatus(
            "Status_SessionFinished",
            report.TotalReturnPercent.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture) + " %",
            report.TradeCount,
            report.WinRatePercent.ToString("N0", CultureInfo.CurrentCulture) + " %",
            report.FeeDragPercent.ToString("N1", CultureInfo.CurrentCulture) + " %");

        Log.Info("Sitzung beendet: {Report}", report);

        SessionFinished?.Invoke(this, new ReportWindowViewModel(
            report,
            _session.EquityCurve,
            _clock?.Symbol ?? SelectedInstrument?.Symbol ?? "—",
            _clock?.Total ?? _session.EquityCurve.Count,
            _session.StartingCash,
            _session.Portfolio.Currency));
    }

    private void Stop()
    {
        _timer.Stop();
        Speed = SimulationSpeed.Paused;
    }

    private async Task LoadInstrumentsAsync()
    {
        try
        {
            var instruments = await _dataProvider.SearchAsync(string.Empty).ConfigureAwait(true);
            var limit = _features.InstrumentLimit;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Instruments.Clear();

                foreach (var instrument in instruments.Take(limit))
                {
                    Instruments.Add(instrument);
                }

                SelectedInstrument = Instruments.FirstOrDefault(i =>
                                         string.Equals(i.Symbol, _settings.LastSymbol, StringComparison.OrdinalIgnoreCase))
                                     ?? Instruments.FirstOrDefault();

                if (Instruments.Count == 0)
                {
                    SetStatus("Status_NoData");
                }
                else if (instruments.Count > limit)
                {
                    SetStatus("Status_InstrumentLimit", limit, instruments.Count, _features.UpgradeHint(Feature.UnlimitedInstruments));
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Instrumentenliste konnte nicht geladen werden.");
            SetStatus("Status_InstrumentsFailed");
        }
    }
}
