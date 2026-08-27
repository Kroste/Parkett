using Parkett.Domain;

namespace Parkett.Services;

/// <summary>
/// Quelle für Kurse. Bewusst schmal gehalten, damit sich weitere Quellen (eigener Broker-Zugang,
/// andere Börsen) ohne Umbau ergänzen lassen — bei Bedarf später als Plugin nach Kroste-Muster.
/// </summary>
public interface IMarketDataProvider
{
    string Id { get; }

    string DisplayName { get; }

    MarketDataLicense License { get; }

    /// <summary>Ist der Provider einsatzbereit (Zugangsdaten vorhanden, Datei gefunden, ...)?</summary>
    bool IsAvailable { get; }

    Task<IReadOnlyList<Instrument>> SearchAsync(string query, CancellationToken cancellationToken = default);

    Task<Quote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Candle>> GetHistoryAsync(
        string symbol,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
