using System.Globalization;
using Parkett.Domain;
using Parkett.Localization;

namespace Parkett.ViewModels;

/// <summary>Anzeigezeile für eine offene Order — hält das Domänenmodell aus der UI heraus.</summary>
public sealed record OpenOrderRow(Guid Id, string Symbol, string Side, string Type, string Quantity, string Trigger)
{
    public static OpenOrderRow From(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        var trigger = order.Type switch
        {
            OrderType.Limit => order.LimitPrice?.ToString("N2", CultureInfo.CurrentCulture) ?? "—",
            OrderType.Stop => order.StopPrice?.ToString("N2", CultureInfo.CurrentCulture) ?? "—",
            _ => "—",
        };

        return new OpenOrderRow(
            order.Id,
            order.Symbol,
            order.Side == OrderSide.Buy ? L.T("Order_SideBuy") : L.T("Order_SideSell"),
            order.Type.ToString(),
            order.Quantity.ToString("N0", CultureInfo.CurrentCulture),
            trigger);
    }
}
