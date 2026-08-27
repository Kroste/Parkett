using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
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
/// Hauptfenster-VM. Bewusst über mehrere partial-Dateien verteilt, damit keine
/// davon zum unlesbaren Sammelbecken wird:
///
/// <list type="bullet">
///   <item><c>MainWindowViewModel.cs</c> — Felder, Konstruktor, gebundener Zustand.</item>
///   <item><c>.Session.cs</c> — starten, fortsetzen, beenden, Stand sichern.</item>
///   <item><c>.Transport.cs</c> — Play/Pause, Einzelschritt, Tempo.</item>
///   <item><c>.Orders.cs</c> — Kaufen, Verkaufen, Stornieren.</item>
///   <item><c>.Presentation.cs</c> — Anzeigetexte und Sprachwechsel.</item>
/// </list>
///
/// Das Rechnende liegt in <see cref="TradingSession"/> und
/// <see cref="SimulationClock"/> — beide ohne UI getestet.
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

    public void Dispose() => _timer.Stop();
}
