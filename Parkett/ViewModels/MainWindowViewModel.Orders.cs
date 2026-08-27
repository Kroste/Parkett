using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;
using Parkett.Domain;
using Parkett.Localization;

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
            SetStatus("Status_OrderCancelled");
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
            SetStatus(side == OrderSide.Buy ? "Status_Bought" : "Status_Sold");
        }
        else if (result.RemainsOpen)
        {
            SetStatus("Status_OrderResting");
        }
        else
        {
            StatusText = result.Order.RejectReason ?? L.T("Status_OrderRejected");
            OrderLog.Warn("Order abgelehnt: {Reason}", result.Order.RejectReason);
        }

        RefreshOpenOrders();
        UpdatePortfolioTexts();
    }
}
