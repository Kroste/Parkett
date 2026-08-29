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
            // Die Sitzung läuft über ALLE geladenen Instrumente, nicht nur das gewählte:
            // ein Depot, das nur einen Wert halten darf, ist kein Depot. Die Auswahl
            // bestimmt ab jetzt nur noch, was der Chart zeigt und worauf sich eine
            // neue Order bezieht.
            var histories = await LoadHistoriesAsync().ConfigureAwait(true);

            if (histories.Count == 0)
            {
                SetStatus("Status_NotEnoughHistory", instrument.Symbol);
                return;
            }

            Stop();

            _clock = new SimulationClock(histories);
            _clock.ShowSymbol(instrument.Symbol);
            _session = new TradingSession(StartingCash, _feeModel);

            Blotter.Clear();
            OpenOrders.Clear();
            ChartMarkers = [];
            IsSessionRunning = true;

            RefreshFromClock();

            if (histories.Count == 1)
            {
                SetStatus("Status_SessionRunning", histories[0].Symbol, _clock.Total);
            }
            else
            {
                SetStatus("Status_SessionRunningMulti", histories.Count, _clock.Total);
            }

            Log.Info(
                "Sitzung gestartet: {Count} Instrumente ({Symbols}), {Steps} Zeitpunkte.",
                histories.Count,
                string.Join(", ", histories.Select(h => h.Symbol)),
                _clock.Total);
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
            // Stände aus Version 1 kennen nur ein Symbol; neuere führen die ganze Liste.
            var symbols = snapshot.Symbols.Count > 0 ? snapshot.Symbols : [snapshot.Symbol];
            var histories = await LoadHistoriesAsync(symbols).ConfigureAwait(true);

            if (histories.Count == 0 ||
                snapshot.CandleIndex >= SimulationClock.BuildTimeline(histories).Count)
            {
                // Historie hat sich seit dem Speichern geändert — lieber ehrlich abbrechen
                // als an der falschen Kerze weiterzuspielen.
                SetStatus("Status_ResumeStale", snapshot.Symbol);
                _sessionStore.Clear();
                HasSavedSession = false;
                return;
            }

            Stop();

            _clock = new SimulationClock(histories, snapshot.CandleIndex);
            _clock.ShowSymbol(snapshot.Symbol);
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
                _session,
                _clock.ActiveSymbol,
                _clock.Index,
                DateTimeOffset.UtcNow,
                _clock.Symbols));
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
            DescribeInstruments(),
            _clock?.Total ?? _session.EquityCurve.Count,
            _session.StartingCash,
            _session.Portfolio.Currency));
    }

    /// <summary>
    /// Was im Bericht über der Sitzung steht. Bei einem Instrument sein Symbol, bei
    /// mehreren die ersten drei plus Zähler — die Kopfzeile des Berichts ist einzeilig,
    /// zehn ausgeschriebene Symbole würden rechts abgeschnitten.
    /// </summary>
    private string DescribeInstruments()
    {
        var symbols = _clock?.Symbols ?? [];

        return symbols.Count switch
        {
            0 => SelectedInstrument?.Symbol ?? "—",
            <= 3 => string.Join(", ", symbols),
            _ => string.Join(", ", symbols.Take(3)) + $" +{symbols.Count - 3}",
        };
    }

    private void Stop()
    {
        _timer.Stop();
        Speed = SimulationSpeed.Paused;
    }

    /// <summary>
    /// Lädt die Historien aller angebotenen Instrumente. Wer zu wenig Kerzen hat,
    /// fällt still heraus statt die ganze Sitzung zu verhindern — eine unbrauchbare
    /// Datei unter zehn guten soll niemanden am Spielen hindern. Das gewählte
    /// Instrument steht vorn, damit der Chart ohne Umweg darauf steht.
    /// </summary>
    private Task<IReadOnlyList<SymbolHistory>> LoadHistoriesAsync() =>
        LoadHistoriesAsync(Instruments
            .OrderByDescending(i => string.Equals(i.Symbol, SelectedInstrument?.Symbol, StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Symbol)
            .ToList());

    private async Task<IReadOnlyList<SymbolHistory>> LoadHistoriesAsync(IReadOnlyList<string> symbols)
    {
        var histories = new List<SymbolHistory>();

        foreach (var symbol in symbols)
        {
            try
            {
                var history = await _dataProvider
                    .GetHistoryAsync(symbol, DateTimeOffset.MinValue, DateTimeOffset.MaxValue)
                    .ConfigureAwait(true);

                if (history.Count < 2)
                {
                    Log.Warn("{Symbol} übersprungen: nur {Count} Kerzen.", symbol, history.Count);
                    continue;
                }

                histories.Add(new SymbolHistory(symbol, history));
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Historie für {Symbol} nicht ladbar — Instrument wird übersprungen.", symbol);
            }
        }

        return histories;
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
