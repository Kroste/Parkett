using FluentAssertions;
using Parkett.Domain;
using Parkett.ViewModels;

namespace Parkett.Tests;

/// <summary>
/// Der Bericht rechnet selbst nur eine Zahl — das Ergebnis ohne Gebühren — und
/// wählt daraus das Urteil. Genau diese beiden Stellen sind hier abgesichert;
/// die Kennzahlen selbst prüft <see cref="PerformanceCalculatorTests"/>.
/// </summary>
public class ReportViewModelTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    private static ReportWindowViewModel Bericht(
        decimal startEquity,
        decimal endEquity,
        decimal fees,
        int trades = 3,
        decimal? originalStartingCash = null)
    {
        var report = new PerformanceReport(
            StartEquity: startEquity,
            EndEquity: endEquity,
            TotalReturnPercent: startEquity == 0m ? 0m : (endEquity - startEquity) / startEquity * 100m,
            MaxDrawdownPercent: 0m,
            TotalFees: fees,
            TradeCount: trades,
            WinRatePercent: 50m);

        return new ReportWindowViewModel(
            report,
            [new EquityPoint(Start, startEquity), new EquityPoint(Start.AddDays(30), endEquity)],
            "DEMO",
            30,
            originalStartingCash ?? startEquity);
    }

    [Fact]
    public void Ergebnis_ohne_Gebuehren_rechnet_die_Gebuehren_heraus()
    {
        // 10.000 → 9.800 mit 400 Gebühren: ohne sie wären es 10.200, also +2 %.
        var vm = Bericht(startEquity: 10_000m, endEquity: 9_800m, fees: 400m);

        vm.ReturnWithoutFeesPercent.Should().Be(2m);
    }

    [Fact]
    public void Ohne_Gebuehren_bleibt_das_Ergebnis_unveraendert()
    {
        var vm = Bericht(startEquity: 10_000m, endEquity: 10_500m, fees: 0m);

        vm.ReturnWithoutFeesPercent.Should().Be(5m);
    }

    [Fact]
    public void Startkapital_null_kippt_die_Rechnung_nicht()
    {
        var vm = Bericht(startEquity: 0m, endEquity: 0m, fees: 0m);

        vm.ReturnWithoutFeesPercent.Should().Be(0m);
    }

    [Fact]
    public void Verlust_allein_durch_Gebuehren_bekommt_ein_eigenes_Urteil()
    {
        // Das ist der Fall, für den der ganze Bericht gebaut ist: die Strategie
        // war im Plus, die Handelshäufigkeit hat es aufgefressen.
        var durchGebuehren = Bericht(startEquity: 10_000m, endEquity: 9_800m, fees: 400m);
        var echterVerlust = Bericht(startEquity: 10_000m, endEquity: 9_000m, fees: 50m);

        durchGebuehren.Verdict.Should().NotBe(
            echterVerlust.Verdict,
            "ein Minus nur wegen der Gebühren ist eine andere Lehre als ein Minus am Markt");
    }

    [Fact]
    public void Gewinn_und_Verlust_werden_unterschieden()
    {
        Bericht(10_000m, 11_000m, 20m).IsGain.Should().BeTrue();
        Bericht(10_000m, 9_000m, 20m).IsGain.Should().BeFalse();

        Bericht(10_000m, 11_000m, 20m).Verdict.Should().NotBe(Bericht(10_000m, 9_000m, 20m).Verdict);
    }

    [Fact]
    public void Punktlandung_auf_dem_Startkapital_gilt_nicht_als_Verlust()
    {
        Bericht(10_000m, 10_000m, 0m).IsGain.Should().BeTrue();
    }

    [Fact]
    public void Punktlandung_bekommt_ein_eigenes_Urteil()
    {
        // "Du liegst über deinem Startkapital" wäre bei exakt 0,00 % schlicht falsch —
        // und genau dieser Fall tritt ein, wenn gar nicht gehandelt wurde.
        var unveraendert = Bericht(10_000m, 10_000m, 0m, trades: 0);
        var gewinn = Bericht(10_000m, 11_000m, 0m);

        unveraendert.Verdict.Should().NotBe(gewinn.Verdict);
    }

    [Fact]
    public void Ohne_Rundlauf_steht_ein_Strich_statt_null_Prozent()
    {
        // "Trefferquote 0 %" würde behaupten, alle Trades seien schiefgegangen.
        var vm = Bericht(10_000m, 10_000m, 0m, trades: 0);

        vm.HasTrades.Should().BeFalse();
        vm.WinRateText.Should().Be("—");
    }

    [Fact]
    public void Mit_Rundlaeufen_steht_eine_Quote()
    {
        var vm = Bericht(10_000m, 10_500m, 20m, trades: 4);

        vm.HasTrades.Should().BeTrue();
        vm.WinRateText.Should().NotBe("—");
    }

    [Fact]
    public void Fortgesetzte_Sitzung_weist_sich_als_Teilbericht_aus()
    {
        // Restore baut die Equity-Kurve bewusst nicht nach: der Bericht zeigt dann
        // nur den fortgesetzten Teil, und "Startkapital" ist der Wiedereinstiegswert.
        var vm = Bericht(startEquity: 11_240m, endEquity: 12_000m, fees: 30m, originalStartingCash: 10_000m);

        vm.IsPartialSession.Should().BeTrue();
        vm.PartialSessionHint.Should().Contain("10");
    }

    [Fact]
    public void Durchgehende_Sitzung_zeigt_keinen_Teilbericht_Hinweis()
    {
        var vm = Bericht(startEquity: 10_000m, endEquity: 12_000m, fees: 30m);

        vm.IsPartialSession.Should().BeFalse();
    }
}
