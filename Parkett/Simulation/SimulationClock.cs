using NLog;
using Parkett.Domain;
using Parkett.Services;

namespace Parkett.Simulation;

/// <summary>
/// Ein Schritt der Sitzung: der erreichte Zeitpunkt und die Kurse aller Instrumente,
/// die an diesem Zeitpunkt eine neue Kerze haben.
/// </summary>
/// <param name="Quotes">
/// Nur die Instrumente, die an diesem Zeitpunkt gehandelt wurden. Wer über einen
/// Feiertag hinweg keine Kerze hat, fehlt hier — sein letzter Kurs gilt weiter.
/// </param>
public sealed record SimulationStep(
    DateTimeOffset At,
    IReadOnlyList<Quote> Quotes,
    int Index,
    int Total)
{
    public bool IsLast => Index >= Total - 1;

    /// <summary>Fortschritt 0..1 — für die Fortschrittsanzeige der Transportleiste.</summary>
    public double Progress => Total <= 1 ? 1d : (double)Index / (Total - 1);
}

/// <summary>
/// Läuft Zeitpunkt für Zeitpunkt durch eine oder mehrere geladene Historien und deckt
/// sie schrittweise auf. Enthält KEINEN Timer — das Takten übernimmt die UI
/// (DispatcherTimer) bzw. der Test. Dadurch ist die gesamte Ablauflogik ohne UI und
/// ohne Warten testbar.
///
/// <b>Warum eine gemeinsame Zeitachse und nicht ein Index je Instrument:</b> zwei
/// Historien haben selten dieselben Handelstage — Feiertage unterscheiden sich je
/// Börse, und Broker-Exporte haben Lücken. Liefe jedes Instrument auf einem eigenen
/// Zähler, driftete das Depot auseinander: Instrument A stünde im April, während B
/// noch im März handelt. Die Uhr taktet deshalb auf der sortierten Vereinigung aller
/// Kerzenzeitpunkte; wer an einem Zeitpunkt keine Kerze hat, behält seinen letzten Kurs.
/// </summary>
public sealed class SimulationClock
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly List<SymbolTrack> _tracks = [];
    private readonly IReadOnlyList<DateTimeOffset> _timeline;

    /// <summary>Einzelnes Instrument — der häufige Fall und der Sonderfall mit einem Eintrag.</summary>
    public SimulationClock(string symbol, IReadOnlyList<Candle> candles, int warmupCandles = 60)
        : this([new SymbolHistory(symbol, candles)], warmupCandles)
    {
    }

    public SimulationClock(IReadOnlyList<SymbolHistory> histories, int warmupCandles = 60)
    {
        ArgumentNullException.ThrowIfNull(histories);

        if (histories.Count == 0)
        {
            throw new ArgumentException("Für eine Sitzung wird mindestens ein Instrument benötigt.", nameof(histories));
        }

        foreach (var history in histories)
        {
            if (history.Candles.Count < 2)
            {
                throw new ArgumentException(
                    $"Für eine Sitzung werden mindestens zwei Kerzen benötigt ({history.Symbol}).",
                    nameof(histories));
            }

            _tracks.Add(new SymbolTrack(history.Symbol, history.Candles));
        }

        _timeline = BuildTimeline(histories);

        ActiveSymbol = _tracks[0].Symbol;

        // Vorlauf: der Spieler soll einen Chart sehen, bevor er die erste Entscheidung trifft.
        SeekTo(Math.Clamp(warmupCandles, 1, _timeline.Count - 1));

        Log.Info(
            "Sitzungsuhr für {Symbols}: {Total} Zeitpunkte, Vorlauf bis Index {Index}.",
            string.Join(", ", _tracks.Select(t => t.Symbol)),
            _timeline.Count,
            Index);
    }

    /// <summary>
    /// Die gemeinsame Zeitachse: jeder Handelszeitpunkt genau einmal, aufsteigend.
    /// Öffentlich, weil ein gespeicherter Sitzungsstand seinen Index darauf bezieht —
    /// wer prüfen will, ob ein Stand noch passt, braucht die Länge, bevor er eine Uhr baut.
    /// </summary>
    public static IReadOnlyList<DateTimeOffset> BuildTimeline(IReadOnlyList<SymbolHistory> histories)
    {
        ArgumentNullException.ThrowIfNull(histories);

        return histories
            .SelectMany(h => h.Candles.Select(c => c.OpenTime))
            .Distinct()
            .OrderBy(t => t)
            .ToList();
    }

    /// <summary>
    /// Das Instrument, auf das sich <see cref="Visible"/>, <see cref="Current"/> und
    /// <see cref="CurrentQuote"/> beziehen — also das, was der Chart gerade zeigt.
    /// Umschalten ändert nur die Anzeige, nie den Ablauf.
    /// </summary>
    public string ActiveSymbol { get; private set; }

    public IReadOnlyList<string> Symbols => _tracks.Select(t => t.Symbol).ToList();

    /// <summary>Symbol des ersten Instruments — für Sitzungen, die nur eines führen.</summary>
    public string Symbol => _tracks[0].Symbol;

    /// <summary>Index auf der gemeinsamen Zeitachse.</summary>
    public int Index { get; private set; }

    public int Total => _timeline.Count;

    public DateTimeOffset At => _timeline[Index];

    public bool IsFinished => Index >= _timeline.Count - 1;

    /// <summary>Zuletzt aufgedeckte Kerze des angezeigten Instruments.</summary>
    public Candle Current => Track(ActiveSymbol).Current;

    /// <summary>Alle bisher aufgedeckten Kerzen des angezeigten Instruments.</summary>
    public IReadOnlyList<Candle> Visible => Track(ActiveSymbol).Visible;

    /// <summary>Kurs des angezeigten Instruments. Geld/Brief entstehen synthetisch um den Schlusskurs.</summary>
    public Quote CurrentQuote => Track(ActiveSymbol).Quote;

    /// <summary>Wechselt das angezeigte Instrument. Unbekannte Symbole werden ignoriert.</summary>
    public bool ShowSymbol(string symbol)
    {
        var track = _tracks.Find(t => string.Equals(t.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

        if (track is null)
        {
            return false;
        }

        ActiveSymbol = track.Symbol;
        return true;
    }

    public IReadOnlyList<Candle> VisibleFor(string symbol) => Track(symbol).Visible;

    public Quote QuoteFor(string symbol) => Track(symbol).Quote;

    /// <summary>
    /// Rückt einen Zeitpunkt vor. Liefert <c>null</c>, wenn die Historie zu Ende ist —
    /// der Aufrufer beendet dann die Sitzung, statt still stehenzubleiben.
    /// </summary>
    public SimulationStep? Advance()
    {
        if (IsFinished)
        {
            return null;
        }

        Index++;
        var at = _timeline[Index];
        var quotes = new List<Quote>();

        foreach (var track in _tracks)
        {
            if (track.AdvanceTo(at))
            {
                quotes.Add(track.Quote);
            }
        }

        return new SimulationStep(at, quotes, Index, Total);
    }

    /// <summary>Springt an den Anfang zurück (neue Runde auf derselben Historie).</summary>
    public void Reset(int warmupCandles = 60)
    {
        SeekTo(Math.Clamp(warmupCandles, 1, _timeline.Count - 1));
        Log.Info("Sitzungsuhr zurückgesetzt auf Index {Index}.", Index);
    }

    /// <summary>Setzt alle Instrumente auf den Stand des Zeitpunkts <paramref name="index"/>.</summary>
    private void SeekTo(int index)
    {
        Index = index;
        var at = _timeline[index];

        foreach (var track in _tracks)
        {
            track.SeekTo(at);
        }
    }

    private SymbolTrack Track(string symbol) =>
        _tracks.Find(t => string.Equals(t.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unbekanntes Instrument: {symbol}", nameof(symbol));

    /// <summary>
    /// Der Stand eines Instruments auf der gemeinsamen Zeitachse. Führt einen eigenen
    /// Zeiger mit, statt bei jedem Zugriff zu suchen — <see cref="Visible"/> wird bei
    /// jedem Takt für den Chart abgefragt.
    /// </summary>
    private sealed class SymbolTrack(string symbol, IReadOnlyList<Candle> candles)
    {
        private int _cursor;

        public string Symbol { get; } = symbol;

        public Candle Current => candles[_cursor];

        public IReadOnlyList<Candle> Visible => candles.Take(_cursor + 1).ToList();

        public Quote Quote
        {
            get
            {
                var (bid, ask) = SyntheticSpread.Around(Current.Close);
                return new Quote(Symbol, bid, ask, Current.Close, Current.OpenTime, DelayMinutes: 1440);
            }
        }

        /// <summary>
        /// Deckt alle Kerzen bis <paramref name="at"/> auf. True, wenn dabei mindestens
        /// eine neue dazukam — nur dann gibt es an diesem Zeitpunkt einen neuen Kurs.
        /// </summary>
        public bool AdvanceTo(DateTimeOffset at)
        {
            var before = _cursor;

            while (_cursor + 1 < candles.Count && candles[_cursor + 1].OpenTime <= at)
            {
                _cursor++;
            }

            return _cursor != before;
        }

        public void SeekTo(DateTimeOffset at)
        {
            _cursor = 0;
            AdvanceTo(at);
        }
    }
}
