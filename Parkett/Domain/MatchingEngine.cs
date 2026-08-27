namespace Parkett.Domain;

/// <summary>Ergebnis eines Ausführungsversuchs: entweder ein Fill, eine Ablehnung, oder "bleibt offen".</summary>
public sealed record MatchResult(Order Order, Fill? Fill, bool RemainsOpen)
{
    public bool IsFilled => Fill is not null;
}

/// <summary>
/// Führt Orders gegen einen Kurs aus. Bewusst konservativ: Käufe zum Brief, Verkäufe zum Geld.
/// Ein Simulator, der zur Mitte ausführt, schönt jede Strategie um den halben Spread pro Trade —
/// bei engen Konten ist genau das der Unterschied zwischen "funktioniert" und "funktioniert nicht".
/// </summary>
public sealed class MatchingEngine(IFeeModel feeModel)
{
    private readonly IFeeModel _feeModel = feeModel ?? throw new ArgumentNullException(nameof(feeModel));

    public MatchResult TryExecute(Order order, Quote quote, Portfolio portfolio, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(quote);
        ArgumentNullException.ThrowIfNull(portfolio);

        if (order.Status != OrderStatus.New)
        {
            return new MatchResult(order, null, RemainsOpen: false);
        }

        if (order.Quantity <= 0m)
        {
            return new MatchResult(order.Reject("Stückzahl muss größer als null sein."), null, false);
        }

        if (!string.Equals(order.Symbol, quote.Symbol, StringComparison.OrdinalIgnoreCase))
        {
            return new MatchResult(order.Reject("Kurs gehört zu einem anderen Symbol."), null, false);
        }

        var executionPrice = DetermineExecutionPrice(order, quote);

        if (executionPrice is null)
        {
            // Limit/Stop nicht erreicht — Order bleibt im Buch.
            return new MatchResult(order, null, RemainsOpen: true);
        }

        var price = executionPrice.Value;
        var fee = _feeModel.CalculateFee(order.Side, order.Quantity, price);

        if (order.Side == OrderSide.Buy)
        {
            var required = (order.Quantity * price) + fee;
            if (required > portfolio.Cash)
            {
                return new MatchResult(
                    order.Reject($"Nicht genug Kapital: {required:N2} {portfolio.Currency} benötigt, {portfolio.Cash:N2} verfügbar."),
                    null,
                    false);
            }
        }
        else
        {
            // Leerverkäufe sind bewusst nicht erlaubt — das ist ein Lernsimulator, kein Margin-Konto.
            if (portfolio.QuantityOf(order.Symbol) < order.Quantity)
            {
                return new MatchResult(
                    order.Reject("Leerverkauf nicht möglich: Bestand reicht nicht aus."),
                    null,
                    false);
            }
        }

        var fill = new Fill(order.Id, order.Symbol, order.Side, order.Quantity, price, fee, now);
        return new MatchResult(order.Fill(), fill, RemainsOpen: false);
    }

    private static decimal? DetermineExecutionPrice(Order order, Quote quote)
    {
        var buyPrice = quote.Ask;
        var sellPrice = quote.Bid;

        return order.Type switch
        {
            OrderType.Market => order.Side == OrderSide.Buy ? buyPrice : sellPrice,

            OrderType.Limit when order.Side == OrderSide.Buy =>
                buyPrice <= order.LimitPrice ? buyPrice : null,

            OrderType.Limit =>
                sellPrice >= order.LimitPrice ? sellPrice : null,

            // Stop-Buy löst aus, wenn der Kurs den Stop erreicht ODER überschreitet.
            OrderType.Stop when order.Side == OrderSide.Buy =>
                buyPrice >= order.StopPrice ? buyPrice : null,

            // Stop-Loss löst aus, wenn der Kurs auf oder unter den Stop fällt.
            OrderType.Stop =>
                sellPrice <= order.StopPrice ? sellPrice : null,

            _ => null,
        };
    }
}
