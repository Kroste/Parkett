using FluentAssertions;
using Parkett.Domain;

namespace Parkett.Tests;

public class PortfolioTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    private static Fill Buy(string symbol, decimal qty, decimal price, decimal fee = 1m) =>
        new(Guid.NewGuid(), symbol, OrderSide.Buy, qty, price, fee, Now);

    private static Fill Sell(string symbol, decimal qty, decimal price, decimal fee = 1m) =>
        new(Guid.NewGuid(), symbol, OrderSide.Sell, qty, price, fee, Now);

    [Fact]
    public void Kauf_belastet_Kasse_inklusive_Gebuehr()
    {
        var portfolio = Portfolio.Open(10_000m).Apply(Buy("SAP", 10m, 100m, fee: 1m));

        portfolio.Cash.Should().Be(8_999m);
        portfolio.QuantityOf("SAP").Should().Be(10m);
        portfolio.TotalFees.Should().Be(1m);
    }

    [Fact]
    public void Verkauf_schreibt_abzueglich_Gebuehr_gut()
    {
        var portfolio = Portfolio.Open(10_000m)
            .Apply(Buy("SAP", 10m, 100m, fee: 1m))
            .Apply(Sell("SAP", 10m, 110m, fee: 1m));

        // 10.000 - 1.000 - 1 + 1.100 - 1
        portfolio.Cash.Should().Be(10_098m);
        portfolio.GetPosition("SAP").Should().BeNull("eine glattgestellte Position wird entfernt");
    }

    [Fact]
    public void Aufstocken_bildet_gewichteten_Einstandskurs()
    {
        var portfolio = Portfolio.Open(10_000m)
            .Apply(Buy("SAP", 10m, 100m, fee: 0m))
            .Apply(Buy("SAP", 10m, 120m, fee: 0m));

        portfolio.GetPosition("SAP")!.AveragePrice.Should().Be(110m);
        portfolio.GetPosition("SAP")!.Quantity.Should().Be(20m);
    }

    [Fact]
    public void Teilverkauf_laesst_Einstandskurs_unveraendert()
    {
        var portfolio = Portfolio.Open(10_000m)
            .Apply(Buy("SAP", 20m, 100m, fee: 0m))
            .Apply(Sell("SAP", 5m, 130m, fee: 0m));

        var position = portfolio.GetPosition("SAP")!;
        position.Quantity.Should().Be(15m);
        position.AveragePrice.Should().Be(100m);
    }

    [Fact]
    public void Realisierter_Gewinn_zieht_beide_Gebuehren_ab()
    {
        var portfolio = Portfolio.Open(10_000m)
            .Apply(Buy("SAP", 10m, 100m, fee: 5m))
            .Apply(Sell("SAP", 10m, 110m, fee: 5m));

        // Kursgewinn 100, minus 5 Kaufgebühr, minus 5 Verkaufsgebühr
        portfolio.RealizedPnL.Should().Be(90m);
    }

    [Fact]
    public void Equity_bewertet_Positionen_zum_uebergebenen_Kurs()
    {
        var portfolio = Portfolio.Open(10_000m).Apply(Buy("SAP", 10m, 100m, fee: 0m));

        portfolio.Equity(new Dictionary<string, decimal> { ["SAP"] = 120m }).Should().Be(10_200m);
    }

    [Fact]
    public void Equity_faellt_ohne_Kurs_auf_den_Einstand_zurueck()
    {
        var portfolio = Portfolio.Open(10_000m).Apply(Buy("SAP", 10m, 100m, fee: 0m));

        portfolio.Equity(new Dictionary<string, decimal>()).Should().Be(10_000m);
    }
}
