using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;
using Parkett.Charting;
using Parkett.Domain;
using Parkett.Licensing;
using Parkett.Localization;
using Parkett.Persistence;
using Parkett.Services;
using Parkett.Simulation;

namespace Parkett.ViewModels;

/// <summary>
/// Hauptfenster-VM: Sitzungsablauf, Chart und Kennzahlen. Die Orderbefehle liegen in
/// <c>MainWindowViewModel.Orders.cs</c>, das Rechnende in <see cref="TradingSession"/>
/// und <see cref="SimulationClock"/> — beide ohne UI getestet.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    /// <summary>Startkapital der Übungssitzung. Klein genug, dass Gebühren spürbar sind.</summary>
    public const decimal StartingCash = 10_000m;

    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly IMarketDataProvider _dataProvider;
    private readonly IFeeModel _feeModel;
    private readonly FeatureGate _features;
    private readonly SettingsService _settingsService;
    private readonly SessionStore _sessionStore;
    private readonly DispatcherTimer _timer;

    private AppSettings _settings = AppSettings.Default;

    private TradingSession _session;
    private SimulationClock? _clock;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuyCommand))]
    [NotifyCanExecuteChangedFor(nameof(SellCommand))]
    [NotifyCanExecuteChangedFor(nameof(StepCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartSessionCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartSessionCommand))]
    private Instrument? _selectedInstrument;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuyCommand))]
    [NotifyCanExecuteChangedFor(nameof(SellCommand))]
    private decimal _quantity = 10m;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuyCommand))]
    [NotifyCanExecuteChangedFor(nameof(SellCommand))]
    [NotifyCanExecuteChangedFor(nameof(StepCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePlayCommand))]
    private bool _isSessionRunning;

    [ObservableProperty]
    private SimulationSpeed _speed = SimulationSpeed.Paused;

    /// <summary>Zuletzt gewählte Stufe — merkt sich das Tempo über eine Pause hinweg.</summary>
    private SimulationSpeed _pendingSpeed = SimulationSpeed.Normal;

    [ObservableProperty]
    private IReadOnlyList<Candle> _chartCandles = [];

    [ObservableProperty]
    private IReadOnlyList<ChartMarker> _chartMarkers = [];

    [ObservableProperty]
    private string _statusText = L.T("Status_ChooseInstrument");

    /// <summary>
    /// Letzte Statusmeldung als Key plus Argumente. Ohne das lässt sich eine bereits
    /// gesetzte Meldung beim Sprachwechsel nicht neu übersetzen — sie bliebe für immer
    /// in der Sprache stehen, die beim Auslösen galt.
    /// </summary>
    private (string Key, object?[] Args) _status = ("Status_ChooseInstrument", []);

    [ObservableProperty]
    private string _quoteText = "—";

    [ObservableProperty]
    private string _dateText = "—";

    [ObservableProperty]
    private string _equityText = "—";

    [ObservableProperty]
    private string _cashText = "—";

    [ObservableProperty]
    private string _positionText = "—";

    [ObservableProperty]
    private string _feeText = "—";

    [ObservableProperty]
    private string _returnText = "—";

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResumeSessionCommand))]
    private bool _hasSavedSession;

    public MainWindowViewModel(
        IMarketDataProvider dataProvider,
        IFeeModel feeModel,
        FeatureGate features,
        SettingsService settingsService,
        SessionStore sessionStore)
    {
        _dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
        _feeModel = feeModel ?? throw new ArgumentNullException(nameof(feeModel));
        _features = features ?? throw new ArgumentNullException(nameof(features));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _session = new TradingSession(StartingCash, feeModel);

        _settings = _settingsService.Load();
        _pendingSpeed = _settings.PreferredSpeed;
        Quantity = _settings.DefaultQuantity;
        HasSavedSession = _sessionStore.HasSavedSession;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Step();


        // Sprachwechsel wirkt live: alle abgeleiteten Texte neu melden und neu rendern.
        LocalizationService.Instance.PropertyChanged += (_, _) => OnLanguageChanged();

        UpdatePortfolioTexts();
        _ = LoadInstrumentsAsync();
    }

    public ObservableCollection<Instrument> Instruments { get; } = [];

    public ObservableCollection<string> Blotter { get; } = [];

    public ObservableCollection<OpenOrderRow> OpenOrders { get; } = [];

    public IReadOnlyList<SpeedOption> Speeds { get; } =
    [
        SpeedOption.For(SimulationSpeed.Slow),
        SpeedOption.For(SimulationSpeed.Normal),
        SpeedOption.For(SimulationSpeed.Fast),
        SpeedOption.For(SimulationSpeed.VeryFast),
    ];

    /// <summary>Auswahl der ComboBox. Setzt <see cref="Speed"/>, sobald die Sitzung läuft.</summary>
    public SpeedOption? SelectedSpeed
    {
        get => Speeds.FirstOrDefault(s => s.Value == _pendingSpeed);
        set
        {
            if (value is null || value.Value == _pendingSpeed)
            {
                return;
            }

            _pendingSpeed = value.Value;
            OnPropertyChanged();

            // Tempo umstellen wirkt nur, wenn gerade gespielt wird — sonst bleibt die
            // Pause bestehen und die Stufe greift beim nächsten Start.
            if (Speed != SimulationSpeed.Paused)
            {
                Speed = _pendingSpeed;
            }
        }
    }

    /// <summary>True, wenn mindestens eine Order im Buch liegt — steuert die Sichtbarkeit der Karte.</summary>
    public bool HasOpenOrders => OpenOrders.Count > 0;

    /// <summary>Wird bei jedem Zugriff neu erzeugt, damit der Sprachwechsel durchschlägt.</summary>
    public string DataSourceText => _dataProvider.License.StatusText;

    public string EditionText => _features.Current switch
    {
        Edition.Pro => L.T("Edition_Pro"),
        Edition.Full => L.T("Edition_Full"),
        _ => L.T("Edition_Free"),
    };

    public string DisclaimerText => L.T("Main_Disclaimer");

    public string PlayButtonText =>
        Speed == SimulationSpeed.Paused ? L.T("Transport_Play") : L.T("Transport_Pause");

    private bool CanStart => !IsBusy && SelectedInstrument is not null;

    private bool CanResume => !IsBusy && HasSavedSession;

    private bool CanStep => !IsBusy && IsSessionRunning;

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
        _settings = _settings with
        {
            LastSymbol = SelectedInstrument?.Symbol ?? _settings.LastSymbol,
            PreferredSpeed = _pendingSpeed,
            DefaultQuantity = Quantity,
        };

        _settingsService.Save(_settings);

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

    private void FinishSession()
    {
        Stop();
        IsSessionRunning = false;
        _sessionStore.Clear();
        HasSavedSession = false;

        var report = _session.Report();
        SetStatus(
            "Status_SessionFinished",
            report.TotalReturnPercent.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture) + " %",
            report.TradeCount,
            report.WinRatePercent.ToString("N0", CultureInfo.CurrentCulture) + " %",
            report.FeeDragPercent.ToString("N1", CultureInfo.CurrentCulture) + " %");

        Log.Info("Sitzung beendet: {Report}", report);
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

    /// <summary>Setzt die Statusmeldung und merkt sie sich übersetzbar.</summary>
    private void SetStatus(string key, params object?[] args)
    {
        _status = (key, args);
        StatusText = L.F(key, args);
    }

    /// <summary>
    /// Der Sprachwechsel läuft über LocalizedString für alle {loc:Tr}-Bindings — VM-Properties
    /// erreicht er nicht von selbst. Hier werden sie nachgezogen.
    /// </summary>
    private void OnLanguageChanged()
    {
        StatusText = L.F(_status.Key, _status.Args);

        OnPropertyChanged(nameof(DisclaimerText));
        OnPropertyChanged(nameof(DataSourceText));
        OnPropertyChanged(nameof(EditionText));
        OnPropertyChanged(nameof(PlayButtonText));

        UpdatePortfolioTexts();
        RefreshOpenOrders();
        RebuildBlotter();

        // Die Kursanzeige entsteht in RefreshFromClock — ohne diesen Aufruf bliebe
        // "Geld/Brief" in der alten Sprache stehen.
        RefreshFromClock();
    }

    private void RebuildBlotter()
    {
        Blotter.Clear();

        foreach (var fill in _session.Fills.OrderByDescending(f => f.ExecutedAt))
        {
            Blotter.Add(FormatFill(fill));
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

    private void RefreshMarkers() =>
        ChartMarkers = _session.Fills
            .Select(f => new ChartMarker(f.ExecutedAt, f.Price, f.Side))
            .ToList();

    private void RefreshOpenOrders()
    {
        // Clear+Add ist hier richtig: die Liste ändert sich nur durch Nutzeraktionen
        // bzw. Ausführungen, nicht in einer schnellen Schleife.
        OpenOrders.Clear();

        foreach (var order in _session.OpenOrders)
        {
            OpenOrders.Add(OpenOrderRow.From(order));
        }

        OnPropertyChanged(nameof(HasOpenOrders));
    }

    private void UpdatePortfolioTexts()
    {
        var portfolio = _session.Portfolio;
        var currency = portfolio.Currency;

        EquityText = string.Create(CultureInfo.CurrentCulture, $"{_session.Equity:N2} {currency}");
        CashText = string.Create(CultureInfo.CurrentCulture, $"{portfolio.Cash:N2} {currency}");
        FeeText = string.Create(CultureInfo.CurrentCulture, $"{portfolio.TotalFees:N2} {currency}");
        ReturnText = string.Create(CultureInfo.CurrentCulture, $"{_session.TotalReturnPercent:+0.00;-0.00;0.00} %");

        var symbol = _clock?.Symbol;
        var quantity = symbol is null ? 0m : portfolio.QuantityOf(symbol);

        PositionText = quantity == 0m
            ? L.T("Portfolio_NoPosition")
            : L.F(
                "Portfolio_PositionFormat",
                quantity.ToString("N0", CultureInfo.CurrentCulture),
                portfolio.GetPosition(symbol!)!.AveragePrice.ToString("N2", CultureInfo.CurrentCulture));
    }

    private static string FormatFill(Fill fill) =>
        string.Create(
            CultureInfo.CurrentCulture,
            $"{fill.ExecutedAt.ToString("d", CultureInfo.CurrentCulture)}  {(fill.Side == OrderSide.Buy ? L.T("Order_SideBuy") : L.T("Order_SideSell"))}  {fill.Quantity:N0} {fill.Symbol} @ {fill.Price:N2}  ({fill.Fee:N2})");

    public void Dispose() => _timer.Stop();
}
