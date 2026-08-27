using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;
using Parkett.Domain;
using Parkett.Licensing;
using Parkett.Services;

namespace Parkett.ViewModels;

/// <summary>
/// Hauptfenster-VM. Bleibt bewusst dünn: alles Rechnende liegt in
/// <see cref="TradingSession"/> und ist dort ohne UI getestet.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly IMarketDataProvider _dataProvider;
    private readonly FeatureGate _features;
    private readonly TradingSession _session;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuyCommand))]
    [NotifyCanExecuteChangedFor(nameof(SellCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshQuoteCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuyCommand))]
    [NotifyCanExecuteChangedFor(nameof(SellCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshQuoteCommand))]
    private string _symbol = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuyCommand))]
    [NotifyCanExecuteChangedFor(nameof(SellCommand))]
    private decimal _quantity = 1m;

    [ObservableProperty]
    private string _statusText = "Bereit.";

    [ObservableProperty]
    private string _quoteText = "—";

    [ObservableProperty]
    private string _equityText = "—";

    [ObservableProperty]
    private string _cashText = "—";

    [ObservableProperty]
    private string _feeText = "—";

    [ObservableProperty]
    private string _returnText = "—";

    public MainWindowViewModel(IMarketDataProvider dataProvider, IFeeModel feeModel, FeatureGate features)
    {
        _dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
        _features = features ?? throw new ArgumentNullException(nameof(features));
        _session = new TradingSession(StartingCash, feeModel);

        DataSourceText = dataProvider.License.StatusText;
        EditionText = features.Current switch
        {
            Edition.Pro => "Pro",
            Edition.Full => "Vollversion",
            _ => "Kostenlose Fassung",
        };

        UpdatePortfolioTexts();
    }

    /// <summary>Startkapital der Übungssitzung. Bewusst klein — der Lerneffekt liegt in den Gebühren.</summary>
    public const decimal StartingCash = 10_000m;

    public ObservableCollection<string> Blotter { get; } = [];

    public string DataSourceText { get; }

    public string EditionText { get; }

    public string DisclaimerText =>
        "Virtuelles Geld. Keine Anlageberatung, keine Kauf- oder Verkaufsempfehlung.";

    private bool CanTrade => !IsBusy && !string.IsNullOrWhiteSpace(Symbol) && Quantity > 0m;

    [RelayCommand(CanExecute = nameof(CanTrade))]
    private Task BuyAsync() => TradeAsync(OrderSide.Buy);

    [RelayCommand(CanExecute = nameof(CanTrade))]
    private Task SellAsync() => TradeAsync(OrderSide.Sell);

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshQuoteAsync()
    {
        IsBusy = true;

        try
        {
            var quote = await _dataProvider.GetQuoteAsync(Symbol.Trim()).ConfigureAwait(true);

            if (quote is null)
            {
                QuoteText = "kein Kurs";
                StatusText = $"Für {Symbol} liegt kein Kurs vor.";
                return;
            }

            ShowQuote(quote);
            _session.OnQuote(quote, DateTimeOffset.UtcNow);
            UpdatePortfolioTexts();
            StatusText = $"Kurs aktualisiert ({_dataProvider.License.StatusText}).";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Kursabruf für {Symbol} fehlgeschlagen.", Symbol);
            StatusText = "Kursabruf fehlgeschlagen — Details im Log.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRefresh => !IsBusy && !string.IsNullOrWhiteSpace(Symbol);

    private async Task TradeAsync(OrderSide side)
    {
        IsBusy = true;

        try
        {
            var symbol = Symbol.Trim();
            var quote = await _dataProvider.GetQuoteAsync(symbol).ConfigureAwait(true);

            if (quote is null)
            {
                StatusText = $"Für {symbol} liegt kein Kurs vor — Order nicht ausgeführt.";
                return;
            }

            ShowQuote(quote);

            var now = DateTimeOffset.UtcNow;
            var order = Order.Market(symbol, side, Quantity, now);
            var result = _session.Submit(order, quote, now);

            // Async-Continuation: VM-Properties nur auf dem UI-Thread anfassen.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (result.Fill is { } fill)
                {
                    Blotter.Insert(0, FormatFill(fill));
                    StatusText = $"{(side == OrderSide.Buy ? "Kauf" : "Verkauf")} ausgeführt.";
                }
                else
                {
                    StatusText = result.Order.RejectReason ?? "Order nicht ausgeführt.";
                }

                UpdatePortfolioTexts();
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Order fehlgeschlagen: {Side} {Qty} {Symbol}", side, Quantity, Symbol);
            StatusText = "Order fehlgeschlagen — Details im Log.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ShowQuote(Quote quote) =>
        QuoteText = string.Create(
            CultureInfo.CurrentCulture,
            $"{quote.Symbol}  {quote.Last:N2}  (Geld {quote.Bid:N2} / Brief {quote.Ask:N2})");

    private static string FormatFill(Fill fill) =>
        string.Create(
            CultureInfo.CurrentCulture,
            $"{fill.ExecutedAt.LocalDateTime:HH:mm:ss}  {(fill.Side == OrderSide.Buy ? "Kauf" : "Verkauf")}  {fill.Quantity:N0} {fill.Symbol} @ {fill.Price:N2}  (Gebühr {fill.Fee:N2})");

    private void UpdatePortfolioTexts()
    {
        var portfolio = _session.Portfolio;

        EquityText = string.Create(CultureInfo.CurrentCulture, $"{_session.Equity:N2} {portfolio.Currency}");
        CashText = string.Create(CultureInfo.CurrentCulture, $"{portfolio.Cash:N2} {portfolio.Currency}");
        FeeText = string.Create(CultureInfo.CurrentCulture, $"{portfolio.TotalFees:N2} {portfolio.Currency}");
        ReturnText = string.Create(CultureInfo.CurrentCulture, $"{_session.TotalReturnPercent:+0.00;-0.00;0.00} %");
    }
}
