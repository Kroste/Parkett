using System.Globalization;
using Parkett.Charting;
using Parkett.Domain;
using Parkett.Localization;

namespace Parkett.ViewModels;

/// <summary>
/// Alles, was aus dem Sitzungszustand Anzeigetexte macht — Statuszeile,
/// Depot-Kennzahlen, Ausführungsliste, Orderbuch — plus das Nachziehen beim
/// Sprachwechsel. Kern und Felder liegen in <c>MainWindowViewModel.cs</c>.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Setzt die Statusmeldung und merkt sie sich übersetzbar.</summary>
    private void SetStatus(string key, params object?[] args)
    {
        _status = (key, args);
        StatusText = L.F(key, args);
    }

    /// <summary>
    /// Der Sprachwechsel läuft über LocalizedString für alle {loc:Tr}-Bindings — VM-Properties
    /// erreicht er nicht von selbst. Hier werden sie nachgezogen.
    /// </summary>
    private void OnLanguageChanged()
    {
        StatusText = L.F(_status.Key, _status.Args);

        OnPropertyChanged(nameof(DisclaimerText));
        OnPropertyChanged(nameof(DataSourceText));
        OnPropertyChanged(nameof(EditionText));
        OnPropertyChanged(nameof(PlayButtonText));

        UpdatePortfolioTexts();
        RefreshOpenOrders();
        RebuildBlotter();

        // Die Kursanzeige entsteht in RefreshFromClock — ohne diesen Aufruf bliebe
        // "Geld/Brief" in der alten Sprache stehen.
        RefreshFromClock();
    }

    private void RebuildBlotter()
    {
        Blotter.Clear();

        foreach (var fill in _session.Fills.OrderByDescending(f => f.ExecutedAt))
        {
            Blotter.Add(FormatFill(fill));
        }
    }

    private void RefreshMarkers() =>
        ChartMarkers = _session.Fills
            .Select(f => new ChartMarker(f.ExecutedAt, f.Price, f.Side))
            .ToList();

    private void RefreshOpenOrders()
    {
        // Clear+Add ist hier richtig: die Liste ändert sich nur durch Nutzeraktionen
        // bzw. Ausführungen, nicht in einer schnellen Schleife.
        OpenOrders.Clear();

        foreach (var order in _session.OpenOrders)
        {
            OpenOrders.Add(OpenOrderRow.From(order));
        }

        OnPropertyChanged(nameof(HasOpenOrders));
    }

    private void UpdatePortfolioTexts()
    {
        var portfolio = _session.Portfolio;
        var currency = portfolio.Currency;

        EquityText = string.Create(CultureInfo.CurrentCulture, $"{_session.Equity:N2} {currency}");
        CashText = string.Create(CultureInfo.CurrentCulture, $"{portfolio.Cash:N2} {currency}");
        FeeText = string.Create(CultureInfo.CurrentCulture, $"{portfolio.TotalFees:N2} {currency}");
        ReturnText = string.Create(CultureInfo.CurrentCulture, $"{_session.TotalReturnPercent:+0.00;-0.00;0.00} %");

        var symbol = _clock?.Symbol;
        var quantity = symbol is null ? 0m : portfolio.QuantityOf(symbol);

        PositionText = quantity == 0m
            ? L.T("Portfolio_NoPosition")
            : L.F(
                "Portfolio_PositionFormat",
                quantity.ToString("N0", CultureInfo.CurrentCulture),
                portfolio.GetPosition(symbol!)!.AveragePrice.ToString("N2", CultureInfo.CurrentCulture));
    }

    private static string FormatFill(Fill fill) =>
        string.Create(
            CultureInfo.CurrentCulture,
            $"{fill.ExecutedAt.ToString("d", CultureInfo.CurrentCulture)}  {(fill.Side == OrderSide.Buy ? L.T("Order_SideBuy") : L.T("Order_SideSell"))}  {fill.Quantity:N0} {fill.Symbol} @ {fill.Price:N2}  ({fill.Fee:N2})");
}
