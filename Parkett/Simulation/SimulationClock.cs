using NLog;
using Parkett.Domain;
using Parkett.Services;

namespace Parkett.Simulation;

/// <summary>Ein Schritt der Sitzung: die neu aufgedeckte Kerze samt daraus abgeleitetem Kurs.</summary>
public sealed record SimulationStep(Candle Candle, Quote Quote, int Index, int Total)
{
    public bool IsLast => Index >= Total - 1;

    /// <summary>Fortschritt 0..1 — für die Fortschrittsanzeige der Transportleiste.</summary>
    public double Progress => Total <= 1 ? 1d : (double)Index / (Total - 1);
}

/// <summary>
/// Läuft Kerze für Kerze durch eine geladene Historie und deckt sie schrittweise auf.
/// Enthält KEINEN Timer — das Takten übernimmt die UI (DispatcherTimer) bzw. der Test.
/// Dadurch ist die gesamte Ablauflogik ohne UI und ohne Warten testbar.
/// </summary>
public sealed class SimulationClock
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly IReadOnlyList<Candle> _candles;

    public SimulationClock(string symbol, IReadOnlyList<Candle> candles, int warmupCandles = 60)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(candles);

        if (candles.Count < 2)
        {
            throw new ArgumentException("Für eine Sitzung werden mindestens zwei Kerzen benötigt.", nameof(candles));
        }

        Symbol = symbol;
        _candles = candles;

        // Vorlauf: der Spieler soll einen Chart sehen, bevor er die erste Entscheidung trifft.
        Index = Math.Clamp(warmupCandles, 1, candles.Count - 1);

        Log.Info("Sitzungsuhr für {Symbol}: {Total} Kerzen, Vorlauf bis Index {Index}.",
            symbol, candles.Count, Index);
    }

    public string Symbol { get; }

    /// <summary>Index der zuletzt aufgedeckten Kerze.</summary>
    public int Index { get; private set; }

    public int Total => _candles.Count;

    public bool IsFinished => Index >= _candles.Count - 1;

    public Candle Current => _candles[Index];

    /// <summary>Alle bisher aufgedeckten Kerzen — genau das, was der Chart zeigen darf.</summary>
    public IReadOnlyList<Candle> Visible => _candles.Take(Index + 1).ToList();

    /// <summary>Kurs zur aktuellen Kerze. Geld/Brief entstehen synthetisch um den Schlusskurs.</summary>
    public Quote CurrentQuote => ToQuote(Current);

    /// <summary>
    /// Deckt die nächste Kerze auf. Liefert <c>null</c>, wenn die Historie zu Ende ist —
    /// der Aufrufer beendet dann die Sitzung, statt still stehenzubleiben.
    /// </summary>
    public SimulationStep? Advance()
    {
        if (IsFinished)
        {
            return null;
        }

        Index++;
        var candle = _candles[Index];

        return new SimulationStep(candle, ToQuote(candle), Index, Total);
    }

    /// <summary>Springt an den Anfang zurück (neue Runde auf derselben Historie).</summary>
    public void Reset(int warmupCandles = 60)
    {
        Index = Math.Clamp(warmupCandles, 1, _candles.Count - 1);
        Log.Info("Sitzungsuhr zurückgesetzt auf Index {Index}.", Index);
    }

    private Quote ToQuote(Candle candle)
    {
        var (bid, ask) = SyntheticSpread.Around(candle.Close);
        return new Quote(Symbol, bid, ask, candle.Close, candle.OpenTime, DelayMinutes: 1440);
    }
}
