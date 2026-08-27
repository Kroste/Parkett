using FluentAssertions;
using Parkett.Domain;

namespace Parkett.Tests;

public class PerformanceCalculatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 2, 9, 0, 0, TimeSpan.Zero);

    private static EquityPoint Point(int day, decimal equity) =>
        new(Start.AddDays(day), equity);

    [Fact]
    public void Leere_Kurve_liefert_Nullbericht()
    {
        var report = PerformanceCalculator.Analyse([], []);

        report.EndEquity.Should().Be(0m);
        report.TradeCount.Should().Be(0);
    }

    [Fact]
    public void Gesamtrendite_wird_aus_Anfang_und_Ende_berechnet()
    {
        var report = PerformanceCalculator.Analyse(
            [Point(0, 10_000m), Point(1, 11_500m)],
            []);

        report.TotalReturnPercent.Should().Be(15m);
    }

    [Fact]
    public void Maximaler_Rueckgang_misst_vom_Hoechststand()
    {
        // Hoch 12.000, Tief danach 9.000 → 25 %
        var drawdown = PerformanceCalculator.CalculateMaxDrawdownPercent(
        [
            Point(0, 10_000m),
            Point(1, 12_000m),
            Point(2, 9_000m),
            Point(3, 11_000m),
        ]);

        drawdown.Should().Be(25m);
    }

    [Fact]
    public void Steigende_Kurve_hat_keinen_Rueckgang()
    {
        PerformanceCalculator
            .CalculateMaxDrawdownPercent([Point(0, 100m), Point(1, 110m), Point(2, 120m)])
            .Should().Be(0m);
    }

    [Fact]
    public void Trefferquote_zaehlt_abgeschlossene_Rundlaeufe()
    {
        var fills = new List<Fill>
        {
            new(Guid.NewGuid(), "SAP", OrderSide.Buy, 10m, 100m, 0m, Start),
            new(Guid.NewGuid(), "SAP", OrderSide.Sell, 10m, 110m, 0m, Start.AddDays(1)),
            new(Guid.NewGuid(), "BMW", OrderSide.Buy, 10m, 100m, 0m, Start.AddDays(2)),
            new(Guid.NewGuid(), "BMW", OrderSide.Sell, 10m, 90m, 0m, Start.AddDays(3)),
        };

        var report = PerformanceCalculator.Analyse([Point(0, 10_000m), Point(4, 10_000m)], fills);

        report.TradeCount.Should().Be(2);
        report.WinRatePercent.Should().Be(50m);
    }

    [Fact]
    public void Gebuehren_koennen_einen_Gewinntrade_zum_Verlust_machen()
    {
        // 1 € Kursgewinn, aber 5 € Gebühr je Seite
        var fills = new List<Fill>
        {
            new(Guid.NewGuid(), "SAP", OrderSide.Buy, 1m, 100m, 5m, Start),
            new(Guid.NewGuid(), "SAP", OrderSide.Sell, 1m, 101m, 5m, Start.AddDays(1)),
        };

        var report = PerformanceCalculator.Analyse([Point(0, 10_000m), Point(2, 9_991m)], fills);

        report.TradeCount.Should().Be(1);
        report.WinRatePercent.Should().Be(0m, "nach Gebühren war der Trade ein Verlust");
    }

    [Fact]
    public void Gebuehrenlast_wird_am_Startkapital_gemessen()
    {
        var fills = new List<Fill>
        {
            new(Guid.NewGuid(), "SAP", OrderSide.Buy, 1m, 100m, 50m, Start),
        };

        var report = PerformanceCalculator.Analyse([Point(0, 500m), Point(1, 450m)], fills);

        report.FeeDragPercent.Should().Be(10m);
    }
}
