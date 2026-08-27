using NLog;
using Parkett.Domain;

namespace Parkett.Services;

/// <summary>
/// Eine laufende Handelssitzung: Depot, offene Orders, Ausführungen und Equity-Kurve.
/// Enthält die gesamte Geschäftslogik und ist damit vollständig ohne UI testbar —
/// das ViewModel delegiert nur.
/// </summary>
public sealed class TradingSession
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly MatchingEngine _engine;
    private readonly List<Order> _openOrders = [];
    private readonly List<Fill> _fills = [];
    private readonly List<EquityPoint> _equityCurve = [];
    private readonly Dictionary<string, decimal> _lastPrices = new(StringComparer.OrdinalIgnoreCase);

    public TradingSession(decimal startingCash, IFeeModel feeModel, string currency = "EUR")
    {
        ArgumentNullException.ThrowIfNull(feeModel);

        Portfolio = Portfolio.Open(startingCash, currency);
        StartingCash = startingCash;
        _engine = new MatchingEngine(feeModel);

        Log.Info("Handelssitzung eröffnet: {Cash} {Currency}", startingCash, currency);
    }

    /// <summary>
    /// Stellt eine gespeicherte Sitzung wieder her. Die Equity-Kurve wird bewusst NICHT
    /// rekonstruiert — sie hinge von Kursen ab, die zum Speicherzeitpunkt galten. Der
    /// Verlauf startet neu, die Kennzahlen beziehen sich danach auf den fortgesetzten Teil.
    /// </summary>
    public static TradingSession Restore(
        decimal startingCash,
        IFeeModel feeModel,
        Portfolio portfolio,
        IEnumerable<Fill> fills)
    {
        ArgumentNullException.ThrowIfNull(portfolio);
        ArgumentNullException.ThrowIfNull(fills);

        var session = new TradingSession(startingCash, feeModel, portfolio.Currency)
        {
            Portfolio = portfolio,
        };

        session._fills.AddRange(fills);

        Log.Info("Sitzung wiederhergestellt: {Cash} Kasse, {Positions} Positionen, {Fills} Ausführungen.",
            portfolio.Cash, portfolio.Positions.Count, session._fills.Count);

        return session;
    }

    public Portfolio Portfolio { get; private set; }

    public decimal StartingCash { get; }

    public IReadOnlyList<Order> OpenOrders => _openOrders;

    public IReadOnlyList<Fill> Fills => _fills;

    public IReadOnlyList<EquityPoint> EquityCurve => _equityCurve;

    /// <summary>Gesamtwert des Depots zu den zuletzt bekannten Kursen.</summary>
    public decimal Equity => Portfolio.Equity(_lastPrices);

    /// <summary>Gewinn/Verlust gegenüber dem Startkapital, in Prozent.</summary>
    public decimal TotalReturnPercent =>
        StartingCash == 0m ? 0m : (Equity - StartingCash) / StartingCash * 100m;

    /// <summary>
    /// Gibt eine Order auf. Nicht sofort ausführbare Limit-/Stop-Orders bleiben offen und
    /// werden bei jedem <see cref="OnQuote"/> erneut geprüft.
    /// </summary>
    public MatchResult Submit(Order order, Quote quote, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(quote);

        RememberPrice(quote);

        var result = _engine.TryExecute(order, quote, Portfolio, now);
        ApplyResult(result, now);

        if (result.RemainsOpen)
        {
            _openOrders.Add(result.Order);
            Log.Info("Order {Id} bleibt offen: {Side} {Qty} {Symbol} ({Type})",
                order.Id, order.Side, order.Quantity, order.Symbol, order.Type);
        }

        return result;
    }

    /// <summary>Neuer Kurs: bewertet das Depot neu und prüft offene Orders.</summary>
    public IReadOnlyList<Fill> OnQuote(Quote quote, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(quote);

        RememberPrice(quote);

        var newFills = new List<Fill>();
        var stillOpen = new List<Order>();

        foreach (var order in _openOrders)
        {
            if (!string.Equals(order.Symbol, quote.Symbol, StringComparison.OrdinalIgnoreCase))
            {
                stillOpen.Add(order);
                continue;
            }

            var result = _engine.TryExecute(order, quote, Portfolio, now);
            ApplyResult(result, now);

            if (result.Fill is { } fill)
            {
                newFills.Add(fill);
            }
            else if (result.RemainsOpen)
            {
                stillOpen.Add(result.Order);
            }
        }

        _openOrders.Clear();
        _openOrders.AddRange(stillOpen);

        RecordEquity(now);
        return newFills;
    }

    public bool Cancel(Guid orderId)
    {
        var index = _openOrders.FindIndex(o => o.Id == orderId);

        if (index < 0)
        {
            return false;
        }

        Log.Info("Order {Id} storniert.", orderId);
        _openOrders.RemoveAt(index);
        return true;
    }

    public PerformanceReport Report() => PerformanceCalculator.Analyse(_equityCurve, _fills);

    private void ApplyResult(MatchResult result, DateTimeOffset now)
    {
        if (result.Fill is not { } fill)
        {
            if (result.Order.Status == OrderStatus.Rejected)
            {
                Log.Warn("Order {Id} abgelehnt: {Reason}", result.Order.Id, result.Order.RejectReason);
            }

            return;
        }

        Portfolio = Portfolio.Apply(fill);
        _fills.Add(fill);

        Log.Info("Ausgeführt: {Side} {Qty} {Symbol} zu {Price} (Gebühr {Fee})",
            fill.Side, fill.Quantity, fill.Symbol, fill.Price, fill.Fee);

        RecordEquity(now);
    }

    private void RememberPrice(Quote quote) => _lastPrices[quote.Symbol] = quote.Last;

    private void RecordEquity(DateTimeOffset now)
    {
        var equity = Equity;

        // Denselben Zeitpunkt nicht doppelt aufnehmen — sonst verzerrt sich der Drawdown.
        if (_equityCurve.Count > 0 && _equityCurve[^1].At == now)
        {
            _equityCurve[^1] = new EquityPoint(now, equity);
            return;
        }

        _equityCurve.Add(new EquityPoint(now, equity));
    }
}
