using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;
using Parkett.Domain;

namespace Parkett.ViewModels;

/// <summary>Orderbefehle des Hauptfensters — getrennt gehalten, damit die VM lesbar bleibt.</summary>
public sealed partial class MainWindowViewModel
{
    private static readonly Logger OrderLog = LogManager.GetCurrentClassLogger();

    [ObservableProperty]
    private decimal? _limitPrice;

    [ObservableProperty]
    private decimal? _stopPrice;

    private bool CanTrade => !IsBusy && IsSessionRunning && Quantity > 0m && _clock is not null;

    [RelayCommand(CanExecute = nameof(CanTrade))]
    private void Buy() => Submit(OrderSide.Buy);

    [RelayCommand(CanExecute = nameof(CanTrade))]
    private void Sell() => Submit(OrderSide.Sell);

    [RelayCommand]
    private void CancelOrder(Guid id)
    {
        if (_session.Cancel(id))
        {
            RefreshOpenOrders();
            StatusText = "Order storniert.";
        }
    }

    /// <summary>
    /// Baut die Order aus den gesetzten Feldern: Limit schlägt Stop, beides leer heißt Market.
    /// Ausgeführt wird gegen den Kurs der aktuellen Kerze — nie gegen einen zukünftigen.
    /// </summary>
    private void Submit(OrderSide side)
    {
        if (_clock is null)
        {
            return;
        }

        var quote = _clock.CurrentQuote;
        var now = _clock.Current.OpenTime;

        var order = (LimitPrice, StopPrice) switch
        {
            ({ } limit, _) => Order.Limit(quote.Symbol, side, Quantity, limit, now),
            (null, { } stop) => Order.Stop(quote.Symbol, side, Quantity, stop, now),
            _ => Order.Market(quote.Symbol, side, Quantity, now),
        };

        var result = _session.Submit(order, quote, now);

        if (result.Fill is { } fill)
        {
            Blotter.Insert(0, FormatFill(fill));
            RefreshMarkers();
            StatusText = side == OrderSide.Buy ? "Kauf ausgeführt." : "Verkauf ausgeführt.";
        }
        else if (result.RemainsOpen)
        {
            StatusText = "Order liegt im Buch und wartet auf den Kurs.";
        }
        else
        {
            StatusText = result.Order.RejectReason ?? "Order nicht ausgeführt.";
            OrderLog.Warn("Order abgelehnt: {Reason}", result.Order.RejectReason);
        }

        RefreshOpenOrders();
        UpdatePortfolioTexts();
    }
}
