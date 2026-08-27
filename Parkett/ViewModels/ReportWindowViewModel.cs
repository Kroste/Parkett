using System.Globalization;
using Parkett.Charting;
using Parkett.Domain;
using Parkett.Localization;

namespace Parkett.ViewModels;

/// <summary>
/// Abschlussbericht einer Sitzung. Rechnet nichts selbst — alle Zahlen kommen aus
/// <see cref="PerformanceReport"/> — sondern übersetzt sie in Anzeigetexte und in
/// das Urteil, das der Simulator erteilen soll.
///
/// Reihenfolge im Fenster ist Absicht: die Gebührenlast steht ganz oben, noch vor
/// dem Ergebnis. Sie ist der Teil, den der Nutzer selbst steuert.
/// </summary>
public sealed class ReportWindowViewModel : ViewModelBase
{
    private readonly PerformanceReport _report;
    private readonly string _currency;

    public ReportWindowViewModel(
        PerformanceReport report,
        IReadOnlyList<EquityPoint> equityCurve,
        string symbol,
        int tradingDays,
        decimal originalStartingCash,
        string currency = "EUR")
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _currency = currency;
        EquityCurve = equityCurve ?? throw new ArgumentNullException(nameof(equityCurve));
        Symbol = symbol;
        TradingDays = tradingDays;
        OriginalStartingCash = originalStartingCash;

        LocalizationService.Instance.PropertyChanged += (_, _) => OnLanguageChanged();
    }

    /// <summary>
    /// Das Kapital, mit dem die Sitzung ursprünglich begonnen hat — nicht zwingend
    /// der erste Punkt der Kurve: <see cref="Services.TradingSession.Restore"/> baut
    /// die Equity-Kurve bewusst nicht nach.
    /// </summary>
    public decimal OriginalStartingCash { get; }

    /// <summary>
    /// True, wenn der Bericht nur den fortgesetzten Teil einer Sitzung zeigt. Dann
    /// ist "Startkapital" der Depotwert beim Wiedereinstieg, und die Prozentzahlen
    /// beziehen sich auf diesen Teil — ohne Hinweis läse man sie als Gesamtergebnis.
    /// </summary>
    public bool IsPartialSession => _report.StartEquity != OriginalStartingCash;

    public string PartialSessionHint => L.F("Report_PartialHint", Money(OriginalStartingCash));

    public IReadOnlyList<EquityPoint> EquityCurve { get; }

    public string Symbol { get; }

    public int TradingDays { get; }

    public decimal StartEquity => _report.StartEquity;

    /// <summary>True, wenn die Sitzung über dem Startkapital endete — steuert die Kurvenfarbe.</summary>
    public bool IsGain => _report.EndEquity >= _report.StartEquity;

    /// <summary>
    /// Ergebnis, das ohne jede Gebühr herausgekommen wäre. Der Vergleich mit dem
    /// tatsächlichen Ergebnis ist die eigentliche Lehre des Berichts.
    /// </summary>
    public decimal ReturnWithoutFeesPercent =>
        _report.StartEquity == 0m
            ? 0m
            : Math.Round(
                (_report.EndEquity + _report.TotalFees - _report.StartEquity) / _report.StartEquity * 100m,
                2,
                MidpointRounding.AwayFromZero);

    /// <summary>
    /// Der Satz unter den Kennzahlen. Der interessanteste Fall steht zuerst: eine
    /// Sitzung, die nur durch die Gebühren ins Minus gerutscht ist.
    /// </summary>
    public string Verdict =>
        _report.TotalReturnPercent < 0m && ReturnWithoutFeesPercent > 0m
            ? L.T("Report_VerdictFeesAtePlus")
            : _report.TotalReturnPercent == 0m
                // Punktlandung: "über deinem Startkapital" wäre hier schlicht falsch,
                // und genau dieser Fall tritt ein, wenn gar nicht gehandelt wurde.
                ? L.T("Report_VerdictFlat")
                : IsGain
                    ? L.T("Report_VerdictGain")
                    : L.T("Report_VerdictLoss");

    public string SubHeadline => L.F(
        "Report_Subline",
        Symbol,
        TradingDays,
        First(EquityCurve),
        Last(EquityCurve));

    public string FeeDragText => Percent(_report.FeeDragPercent);

    public string FeesPaidText => Money(_report.TotalFees);

    public string ReturnWithoutFeesText => SignedPercent(ReturnWithoutFeesPercent);

    public string StartEquityText => Money(_report.StartEquity);

    public string EndEquityText => Money(_report.EndEquity);

    public string TotalReturnText => SignedPercent(_report.TotalReturnPercent);

    public string MaxDrawdownText => Percent(_report.MaxDrawdownPercent);

    public string TradeCountText => _report.TradeCount.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Ohne abgeschlossenen Rundlauf ist eine Trefferquote von "0 %" irreführend.</summary>
    public string WinRateText =>
        _report.TradeCount == 0 ? "—" : Percent(_report.WinRatePercent);

    /// <summary>Sichtbar statt einer Trefferquote, die es nicht gibt.</summary>
    public bool HasTrades => _report.TradeCount > 0;

    public string NoTradesText => L.T("Report_NoTrades");

    /// <summary>
    /// Abgeleitete Texte sind fertige Strings und folgen dem Sprachwechsel nicht von
    /// selbst — dieselbe Regel wie im Hauptfenster.
    /// </summary>
    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(SubHeadline));
        OnPropertyChanged(nameof(Verdict));
        OnPropertyChanged(nameof(NoTradesText));
        OnPropertyChanged(nameof(PartialSessionHint));

        // Zahlen folgen der OS-Kultur, aber das Vorzeichen-Format und "—" nicht:
        // sicherheitshalber alle Textfelder neu melden.
        OnPropertyChanged(nameof(FeeDragText));
        OnPropertyChanged(nameof(FeesPaidText));
        OnPropertyChanged(nameof(ReturnWithoutFeesText));
        OnPropertyChanged(nameof(StartEquityText));
        OnPropertyChanged(nameof(EndEquityText));
        OnPropertyChanged(nameof(TotalReturnText));
        OnPropertyChanged(nameof(MaxDrawdownText));
        OnPropertyChanged(nameof(TradeCountText));
        OnPropertyChanged(nameof(WinRateText));
    }

    private string Money(decimal value) =>
        string.Create(CultureInfo.CurrentCulture, $"{value:N2} {_currency}");

    private static string Percent(decimal value) =>
        string.Create(CultureInfo.CurrentCulture, $"{value:N2} %");

    private static string SignedPercent(decimal value) =>
        string.Create(CultureInfo.CurrentCulture, $"{value:+0.00;-0.00;0.00} %");

    private static string First(IReadOnlyList<EquityPoint> curve) =>
        curve.Count == 0 ? "—" : curve[0].At.ToString("d", CultureInfo.CurrentCulture);

    private static string Last(IReadOnlyList<EquityPoint> curve) =>
        curve.Count == 0 ? "—" : curve[^1].At.ToString("d", CultureInfo.CurrentCulture);
}
