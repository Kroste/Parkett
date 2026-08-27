using FluentAssertions;
using Parkett.Domain;
using Parkett.Services;

namespace Parkett.Tests;

public class TradingSessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    private static Quote QuoteAt(decimal price, DateTimeOffset at, string symbol = "SAP")
    {
        var (bid, ask) = SyntheticSpread.Around(price, 0m);
        return new Quote(symbol, bid, ask, price, at, 15);
    }

    [Fact]
    public void Sitzung_startet_mit_dem_Startkapital()
    {
        var session = new TradingSession(10_000m, TieredFeeModel.Free);

        session.Equity.Should().Be(10_000m);
        session.TotalReturnPercent.Should().Be(0m);
    }

    [Fact]
    public void Kauf_und_Kursanstieg_erhoehen_den_Depotwert()
    {
        var session = new TradingSession(10_000m, TieredFeeModel.Free);

        session.Submit(Order.Market("SAP", OrderSide.Buy, 10m, Start), QuoteAt(100m, Start), Start);
        session.OnQuote(QuoteAt(120m, Start.AddDays(1)), Start.AddDays(1));

        session.Equity.Should().Be(10_200m);
        session.TotalReturnPercent.Should().Be(2m);
    }

    [Fact]
    public void Nicht_ausfuehrbare_Limitorder_landet_im_Orderbuch()
    {
        var session = new TradingSession(10_000m, TieredFeeModel.Free);

        session.Submit(
            Order.Limit("SAP", OrderSide.Buy, 10m, limitPrice: 90m, Start),
            QuoteAt(100m, Start),
            Start);

        session.OpenOrders.Should().HaveCount(1);
        session.Fills.Should().BeEmpty();
    }

    [Fact]
    public void Offene_Limitorder_wird_beim_passenden_Kurs_ausgefuehrt()
    {
        var session = new TradingSession(10_000m, TieredFeeModel.Free);
        session.Submit(Order.Limit("SAP", OrderSide.Buy, 10m, 90m, Start), QuoteAt(100m, Start), Start);

        var fills = session.OnQuote(QuoteAt(85m, Start.AddDays(1)), Start.AddDays(1));

        fills.Should().HaveCount(1);
        session.OpenOrders.Should().BeEmpty();
        session.Portfolio.QuantityOf("SAP").Should().Be(10m);
    }

    [Fact]
    public void Kurs_eines_anderen_Symbols_laesst_offene_Orders_unberuehrt()
    {
        var session = new TradingSession(10_000m, TieredFeeModel.Free);
        session.Submit(Order.Limit("SAP", OrderSide.Buy, 10m, 90m, Start), QuoteAt(100m, Start), Start);

        session.OnQuote(QuoteAt(10m, Start.AddDays(1), symbol: "BMW"), Start.AddDays(1));

        session.OpenOrders.Should().HaveCount(1);
    }

    [Fact]
    public void Stornierte_Order_verschwindet_aus_dem_Orderbuch()
    {
        var session = new TradingSession(10_000m, TieredFeeModel.Free);
        var order = Order.Limit("SAP", OrderSide.Buy, 10m, 90m, Start);
        session.Submit(order, QuoteAt(100m, Start), Start);

        session.Cancel(order.Id).Should().BeTrue();
        session.OpenOrders.Should().BeEmpty();
        session.Cancel(order.Id).Should().BeFalse("eine bereits stornierte Order gibt es nicht mehr");
    }

    [Fact]
    public void Equity_Kurve_nimmt_denselben_Zeitpunkt_nicht_doppelt_auf()
    {
        var session = new TradingSession(10_000m, TieredFeeModel.Free);
        var at = Start.AddDays(1);

        session.OnQuote(QuoteAt(100m, at), at);
        session.OnQuote(QuoteAt(101m, at), at);

        session.EquityCurve.Should().HaveCount(1);
    }

    [Fact]
    public void Haeufiges_Handeln_verbrennt_ein_kleines_Konto_ueber_Gebuehren()
    {
        // 500 € Kapital, 20 Roundtrips zum unveränderten Kurs, 1 € je Order.
        var session = new TradingSession(500m, TieredFeeModel.Neobroker);
        var at = Start;

        for (var i = 0; i < 20; i++)
        {
            at = at.AddDays(1);
            session.Submit(Order.Market("SAP", OrderSide.Buy, 1m, at), QuoteAt(100m, at), at);

            at = at.AddDays(1);
            session.Submit(Order.Market("SAP", OrderSide.Sell, 1m, at), QuoteAt(100m, at), at);
        }

        session.Portfolio.TotalFees.Should().Be(40m);
        session.Equity.Should().Be(460m);
        session.TotalReturnPercent.Should().Be(-8m, "40 € Gebühren auf 500 € sind 8 %, ohne dass sich der Kurs bewegt hat");
    }

    [Fact]
    public void Bericht_fasst_Rundlaeufe_und_Gebuehren_zusammen()
    {
        var session = new TradingSession(10_000m, TieredFeeModel.Neobroker);

        session.Submit(Order.Market("SAP", OrderSide.Buy, 10m, Start), QuoteAt(100m, Start), Start);
        var later = Start.AddDays(5);
        session.Submit(Order.Market("SAP", OrderSide.Sell, 10m, later), QuoteAt(110m, later), later);

        var report = session.Report();

        report.TradeCount.Should().Be(1);
        report.WinRatePercent.Should().Be(100m);
        report.TotalFees.Should().Be(2m);
    }
}
