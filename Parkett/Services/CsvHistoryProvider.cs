using System.Globalization;
using NLog;
using Parkett.Domain;
using Parkett.Localization;

namespace Parkett.Services;

/// <summary>
/// Liest EOD-Kurse aus mitgelieferten CSV-Dateien (eine Datei je Symbol, Format
/// <c>Date,Open,High,Low,Close,Volume</c>). Das ist die Datenquelle der Steam-Version:
/// historische Kurse ab einem vollen Handelstag Alter sind ohne Börsenlizenz auslieferbar.
/// </summary>
public sealed class CsvHistoryProvider : IMarketDataProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly string _dataDirectory;
    private readonly Dictionary<string, IReadOnlyList<Candle>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public CsvHistoryProvider(string dataDirectory)
    {
        _dataDirectory = dataDirectory ?? throw new ArgumentNullException(nameof(dataDirectory));
    }

    public string Id => "csv-history";

    public string DisplayName => L.T("Data_BundledHistoryName");

    // Property statt Feld: ein einmal initialisiertes Feld würde die Sprache einfrieren,
    // die beim App-Start galt, und den Live-Wechsel aushebeln.
    public MarketDataLicense License => new(
        SourceName: L.T("Data_BundledHistory"),
        Redistribution: DataRedistributionRight.Redistributable,
        DelayMinutes: 1440,
        AttributionText: L.T("Data_BundledHistoryNote"));

    public bool IsAvailable => Directory.Exists(_dataDirectory);

    public Task<IReadOnlyList<Instrument>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return Task.FromResult<IReadOnlyList<Instrument>>([]);
        }

        var matches = Directory.EnumerateFiles(_dataDirectory, "*.csv")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(symbol => !string.IsNullOrEmpty(symbol))
            .Where(symbol => string.IsNullOrWhiteSpace(query)
                             || symbol!.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(symbol => new Instrument(symbol!, symbol!, "EUR", "HIST"))
            .OrderBy(i => i.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Ohne diese Zeile lässt sich nach einem Import nicht nachvollziehen, ob die
        // Datei überhaupt gefunden wurde — der Dateiname entscheidet über das Symbol.
        Log.Info("{Count} Instrumente in {Path}: {Symbols}",
            matches.Count, _dataDirectory, string.Join(", ", matches.Select(m => m.Symbol)));

        return Task.FromResult<IReadOnlyList<Instrument>>(matches);
    }

    public async Task<Quote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var candles = await LoadAsync(symbol, cancellationToken).ConfigureAwait(false);

        if (candles.Count == 0)
        {
            return null;
        }

        var last = candles[^1];
        var (bid, ask) = SyntheticSpread.Around(last.Close);

        return new Quote(symbol, bid, ask, last.Close, last.OpenTime, License.DelayMinutes);
    }

    public async Task<IReadOnlyList<Candle>> GetHistoryAsync(
        string symbol,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var candles = await LoadAsync(symbol, cancellationToken).ConfigureAwait(false);
        return candles.Where(c => c.OpenTime >= from && c.OpenTime <= to).ToList();
    }

    private async Task<IReadOnlyList<Candle>> LoadAsync(string symbol, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(symbol, out var cached))
        {
            return cached;
        }

        var path = Path.Combine(_dataDirectory, $"{symbol}.csv");

        if (!File.Exists(path))
        {
            Log.Debug("Keine Kursdatei für {Symbol} unter {Path}", symbol, path);
            return [];
        }

        var candles = new List<Candle>();
        var lineNumber = 0;

        foreach (var line in await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false))
        {
            lineNumber++;

            if (lineNumber == 1 && line.StartsWith("Date", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryParseCandle(line, out var candle))
            {
                candles.Add(candle);
            }
            else if (!string.IsNullOrWhiteSpace(line))
            {
                Log.Warn("Kursdatei {Path}, Zeile {Line} nicht lesbar — übersprungen.", path, lineNumber);
            }
        }

        candles.Sort((a, b) => a.OpenTime.CompareTo(b.OpenTime));
        _cache[symbol] = candles;

        Log.Info("Kurshistorie {Symbol} geladen: {Count} Kerzen.", symbol, candles.Count);
        return candles;
    }

    /// <summary>Parst eine CSV-Zeile. Öffentlich, damit die Formatlogik direkt testbar ist.</summary>
    public static bool TryParseCandle(string line, out Candle candle)
    {
        candle = null!;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var parts = line.Split(',');

        if (parts.Length < 5)
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
        {
            return false;
        }

        if (!TryDecimal(parts[1], out var open) ||
            !TryDecimal(parts[2], out var high) ||
            !TryDecimal(parts[3], out var low) ||
            !TryDecimal(parts[4], out var close))
        {
            return false;
        }

        var volume = 0L;
        if (parts.Length > 5)
        {
            _ = long.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out volume);
        }

        candle = new Candle(date, open, high, low, close, volume);
        return true;
    }

    private static bool TryDecimal(string value, out decimal result) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
}
